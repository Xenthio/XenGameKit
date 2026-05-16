// Base class for all NPCs in XenGameKit.
//
// Closely mirrors the sandbox gamemode's Npc base, adapted for the XenGameKit
// player/damage system.
//
// Networking: NPCs are owned by whoever spawned them (host by default).
// Simulation (schedule ticking, damage handling) runs only on the owner.
// All clients receive synced state via [Sync] and see ragdolls via [Rpc.Broadcast].
//
// Source SDK notes consulted:
//   - CAI_Senses separates Look (LoS) from Listen (sound ents) — we mirror that.
//   - CAI_BaseNPC.GetSchedule() returning null = idle, same as Source.
//   - Relationships: D_HT/D_LI/D_FR/D_NU → NpcDisposition.Hate/Like/Fear/Ignore.
//   - Sound stimuli (CSoundEnt) are world-registered; we use NpcStimulusSystem instead.
//
// To create a new NPC:
//   1. Subclass BaseNpc. Add [Property] fields.
//   2. Register relationships in OnStart via NpcRelationships.Set<T>(tag, disposition).
//   3. Override GetSchedule() to drive behaviour.
//   4. Override reaction virtuals (OnSighted, OnHeardSound, etc.) to interrupt schedules.
//   5. NetworkSpawn(null) the GameObject — OwnerTransfer.Fixed keeps ownership on host.
//   6. Override OnDie() for loot drops, sounds, etc.

public abstract class BaseNpc : Component, Component.IDamageable
{
	[Property, Group( "Stats" )] public float MaxHealth  { get; set; } = 100f;
	[Property, Group( "Stats" )] public float Health     { get; set; } = 100f;
	[Property, Group( "Info"  )] public string NpcName   { get; set; } = "NPC";
	[Property, Group( "Debug" )] public bool ShowDebug   { get; set; } = false;

	[Property] public SkinnedModelRenderer Renderer { get; set; }

	// Sub-layers — added automatically via [RequireComponent]
	[RequireComponent] public NpcSenses     Senses     { get; private set; }
	[RequireComponent] public NpcNavigation Navigation { get; private set; }
	[RequireComponent] public NpcAnimation  Animation  { get; private set; }

	public bool IsDead { get; private set; }

	// Unique id for relationship lookups. Stable for this instance's lifetime.
	public Guid NpcId { get; } = Guid.NewGuid();

	// True on the peer that owns and simulates this NPC (host by default).
	// Like IsProxy but semantically clearer — use this instead of !IsProxy in NPC code.
	bool IsOwner => !IsProxy;

	// Host-only: the currently running schedule
	ScheduleBase  _schedule;
	readonly Dictionary<Type, ScheduleBase> _scheduleCache = new();

	// ─── Spawn helper ────────────────────────────────────────────────────────────

	/// <summary>
	/// Spawn and network an NPC at a given transform.
	/// Ownership is given to <paramref name="owner"/> (default null = host).
	/// Call this instead of raw NetworkSpawn so ownership is always set correctly.
	/// </summary>
	public static T Spawn<T>( Scene scene, Transform transform, Connection owner = null ) where T : BaseNpc, new()
	{
		var go = new GameObject( true, typeof(T).Name );
		go.WorldTransform = transform;
		var npc = go.Components.Create<T>();
		go.NetworkSpawn( owner );
		go.Network.SetOwnerTransfer( OwnerTransfer.Fixed );
		return npc;
	}

	// ─── Lifecycle ─────────────────────────────────────────────────────────────

	protected override void OnStart()
	{
		Tags.Add( "npc" );
		Health = MaxHealth;
	}

	protected override void OnUpdate()
	{
		if ( !IsOwner || IsDead ) return;

		TickSchedule();

		if ( ShowDebug ) DrawDebug();
	}

	// ─── Schedule system ──────────────────────────────────────────────────────

	// Override this to return which schedule should run right now.
	// Called every frame when no schedule is active. Return null for idle.
	public virtual ScheduleBase GetSchedule() => null;

	// Get (or create) a cached schedule instance of type T.
	protected T GetSchedule<T>() where T : ScheduleBase, new()
	{
		if ( !_scheduleCache.TryGetValue( typeof(T), out var s ) )
			_scheduleCache[typeof(T)] = s = new T();
		return (T)s;
	}

	void TickSchedule()
	{
		if ( _schedule is not null )
		{
			var status = _schedule.Tick( this );
			if ( status != NpcTaskStatus.Running )
			{
				_schedule.End( this );
				_schedule = null;
			}
			return;
		}

		var next = GetSchedule();
		if ( next is null ) return;

		_schedule = next;
		_schedule.Start( this );
	}

	// Interrupt the current schedule (e.g. when hit).
	protected void InterruptSchedule()
	{
		if ( _schedule is null ) return;
		_schedule.End( this );
		_schedule = null;
	}

	// ─── Damage / death ───────────────────────────────────────────────────────

	void Component.IDamageable.OnDamage( in Sandbox.DamageInfo rawDamage )
	{
		var damage = rawDamage as DamageInfo ?? new DamageInfo( rawDamage.Damage, rawDamage.Attacker, rawDamage.Weapon );
		if ( !IsOwner || IsDead ) return;

		Health -= damage.Damage;
		OnHurt( damage );

		if ( Health <= 0f )
		{
			IsDead = true;
			InterruptSchedule();
			OnDie( damage );
		}
	}

	// Override to react to being hit (pain sounds, flinch, interrupt schedule, etc.)
	protected virtual void OnHurt( in DamageInfo damage ) { }

	// ─── Senses reactions ────────────────────────────────────────────────────
	//
	// These fire when NpcSenses detects a change — first time seeing something,
	// losing sight of it, hearing it, smelling it. Override to react.
	// All run host-only. Check GetDisposition(obj) if you want to filter by faction.

	/// <summary>A new object just entered line of sight.</summary>
	public virtual void OnSighted( GameObject obj ) { }

	/// <summary>An object we were watching has left line of sight.</summary>
	public virtual void OnLostSight( GameObject obj ) { }

	/// <summary>A new object just entered hearing range (no LoS required).</summary>
	public virtual void OnHeard( GameObject obj ) { }

	/// <summary>An object we could hear has gone silent / left range.</summary>
	public virtual void OnLostHearing( GameObject obj ) { }

	/// <summary>
	/// A sound stimulus was emitted nearby via NpcStimulusSystem.EmitSound.
	/// e.g. gunshot, footstep, explosion — anything that makes noise in the world.
	/// </summary>
	public virtual void OnHeardSound( NpcSoundStimulus stimulus ) { }

	/// <summary>
	/// A smell stimulus was emitted nearby via NpcStimulusSystem.EmitSmell.
	/// e.g. blood, food, chemicals, a player's scent trail.
	/// </summary>
	public virtual void OnSmelled( NpcSmellStimulus stimulus ) { }

	// ─── Relationships ───────────────────────────────────────────────────────

	/// <summary>
	/// How does this NPC feel about a given GameObject?
	/// Checks personal overrides first, then class-level defaults in NpcRelationships.
	/// Returns Ignore for anything not registered.
	/// </summary>
	public NpcDisposition GetDisposition( GameObject target )
		=> NpcRelationships.Get( NpcId, GetType(), target );

	/// <summary>
	/// Override how this specific NPC instance feels about a specific target.
	/// Good for grudges, betrayals, or a player aggro-ing a neutral NPC.
	/// </summary>
	public void SetDisposition( GameObject target, NpcDisposition disposition )
		=> NpcRelationships.SetPersonal( NpcId, target.Id, disposition );

	/// <summary>Clear a personal disposition override, reverting to the class default.</summary>
	public void ClearDisposition( GameObject target )
		=> NpcRelationships.ClearPersonal( NpcId, target.Id );

	// Override for custom death behaviour (loot drops, sounds, etc.).
	// Default: kill feed entry + ragdoll + destroy.
	protected virtual void OnDie( in DamageInfo damage )
	{
		// Tell the kill feed about this death
		var killer = damage.Attacker?.GetComponent<Player>()?.PlayerData?.DisplayName ?? damage.Attacker?.Name ?? "World";
		KillfeedData.Add( killer, NpcName, damage.Weapon?.Name ?? "" );

		SpawnRagdoll( GetDeathVelocity( damage ), damage.Origin );
		GameObject.Destroy();
	}

	Vector3 GetDeathVelocity( in DamageInfo damage )
	{
		if ( damage.Tags.Contains( DamageTags.Explosion ) && damage.Origin != Vector3.Zero )
		{
			float dist     = (WorldPosition - damage.Origin).Length;
			float strength = MathX.Remap( dist, 0, 512, 500, 1500 ).Clamp( 500, 1500 );
			return (WorldPosition - damage.Origin + Vector3.Up).Normal * strength;
		}

		return damage.Attacker?.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SpawnRagdoll( Vector3 velocity, Vector3 origin )
	{
		if ( !Renderer.IsValid() ) return;

		var go = new GameObject( true, $"{NpcName} Ragdoll" );
		go.Tags.Add( "ragdoll" );
		go.WorldTransform = WorldTransform;

		var rend = go.Components.Create<SkinnedModelRenderer>();
		rend.CopyFrom( Renderer );
		rend.UseAnimGraph = false;

		// Copy clothing children
		foreach ( var child in Renderer.GameObject.Children
			.SelectMany( c => c.Components.GetAll<SkinnedModelRenderer>() ) )
		{
			var childGo   = new GameObject( true, child.GameObject.Name );
			childGo.Parent = go;
			var childRend = childGo.Components.Create<SkinnedModelRenderer>();
			childRend.CopyFrom( child );
			childRend.BoneMergeTarget = rend;
		}

		var physics  = go.Components.Create<ModelPhysics>();
		physics.Model    = rend.Model;
		physics.Renderer = rend;
		physics.CopyBonesFrom( Renderer, true );

		ApplyRagdollForce( physics, velocity, origin );

		rend.Invoke( 30f, go.Destroy );
	}

	async void ApplyRagdollForce( ModelPhysics physics, Vector3 velocity, Vector3 origin )
	{
		await GameTask.Delay( 10 );
		if ( !physics.IsValid() ) return;

		foreach ( var body in physics.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;
			rb.Velocity = velocity * 0.5f;
			if ( origin != Vector3.Zero && velocity.Length > 10f )
				rb.ApplyImpulse( Vector3.Direction( origin, rb.WorldPosition ) * velocity.Length * rb.Mass * 0.3f );
		}
	}

	// ─── Debug ────────────────────────────────────────────────────────────────

	void DrawDebug()
	{
		var pos  = Scene.Camera.PointToScreenPixels( WorldPosition + Vector3.Up * 80f, out var behind );
		if ( behind ) return;

		var info = new System.Text.StringBuilder();
		info.AppendLine( $"{NpcName} [{Health:F0}/{MaxHealth:F0}]" );
		if ( _schedule is not null ) info.AppendLine( $"Schedule: {_schedule.GetType().Name}" );
		info.Append( Senses.GetDebugString() );

		var text = TextRendering.Scope.Default;
		text.Text      = info.ToString();
		text.FontSize  = 13;
		text.FontName  = "Poppins";
		text.TextColor = Color.Yellow;
		text.Outline   = new TextRendering.Outline { Color = Color.Black, Size = 4, Enabled = true };

		DebugOverlay.ScreenText( pos, text, TextFlag.Center );
	}
}
