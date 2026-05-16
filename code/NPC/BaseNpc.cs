// Base class for all NPCs in XenGameKit.
//
// Closely mirrors the sandbox gamemode's Npc base, adapted for the XenGameKit
// player/damage system. Runs only on the host — proxies receive synced state.
//
// Architecture (same as sandbox gamemode):
//   BaseNpc        — core: health, damage, death, ragdoll
//   NpcSenses      — sight/hearing scans, target detection
//   NpcNavigation  — NavMeshAgent wrapper, move commands
//   NpcAnimation   — animator parameters, look-at, holdtype
//   ScheduleBase   — ordered list of NpcTasks (see NpcSchedule.cs)
//   NpcTask        — single step in a schedule (move to, wait, fire, etc.)
//
// To create a new NPC:
//   1. Subclass BaseNpc. Add [Property] fields for tunable values.
//   2. Override GetSchedule() to return which schedule to run.
//   3. Add your NPC Component to a GameObject with a SkinnedModelRenderer,
//      NavMeshAgent, and Rigidbody. The required sub-layers add themselves
//      via [RequireComponent].
//   4. Override OnDie() for custom death logic (loot drops, sound, etc).

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

	// Host-only: the currently running schedule
	ScheduleBase  _schedule;
	readonly Dictionary<Type, ScheduleBase> _scheduleCache = new();

	// ─── Lifecycle ─────────────────────────────────────────────────────────────

	protected override void OnStart()
	{
		Tags.Add( "npc" );
		Health = MaxHealth;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || IsDead ) return;

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
		if ( IsProxy || IsDead ) return;

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
