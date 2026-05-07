using Sandbox.Rendering;

/// <summary>
/// Base class for all melee weapons — crowbars, pipes, knives, etc.
/// Does a short-range trace attack on primary fire. No ammo, no reloading.
/// All stats are configurable via Properties so most melee weapons need no C# subclass.
/// </summary>
public class BaseMeleeWeapon : BaseCarryable
{
	/// <summary>Damage per swing.</summary>
	[Property, Group( "Melee" )] public float Damage { get; set; } = 25f;

	/// <summary>Reach of the swing in world units. Crowbar in HL2 = 72 u.</summary>
	[Property, Group( "Melee" )] public float Range { get; set; } = 72f;

	/// <summary>Radius of the swing trace sphere. Larger = more forgiving.</summary>
	[Property, Group( "Melee" )] public float TraceRadius { get; set; } = 8f;

	/// <summary>Impulse force applied to physics objects on hit.</summary>
	[Property, Group( "Melee" )] public float HitForce { get; set; } = 300f;

	/// <summary>Seconds between swings.</summary>
	[Property, Group( "Melee" )] public float SwingRate { get; set; } = 0.5f;

	/// <summary>Sound played when the swing hits something.</summary>
	[Property, Group( "Melee" )] public SoundEvent HitSound { get; set; }

	/// <summary>Sound played when the swing misses (whiff).</summary>
	[Property, Group( "Melee" )] public SoundEvent MissSound { get; set; }

	/// <summary>
	/// Impact effect prefab on hit. Null = use per-surface defaults (same as bullet impacts).
	/// </summary>
	[Property, Group( "Melee" )] public GameObject ImpactEffectOverride { get; set; }

	/// <summary>
	/// View punch on a successful hit (pitch, yaw). Matches HL2 crowbar feel.
	/// Set to zero to disable.
	/// </summary>
	[Property, Group( "Melee" )] public Vector2 HitViewPunch { get; set; } = new Vector2( 1.5f, -1.5f );

	protected TimeUntil TimeUntilNextSwingAllowed;

	public override void OnControl( Player player )
	{
		// Hold to autoswing — same as Source's ItemPostFrame IN_ATTACK check.
		// SwingRate throttles the rate; no need for a separate press-only mode.
		if ( Input.Down( "attack1" ) && CanSwing() )
			Swing();
	}

	public bool CanSwing() => TimeUntilNextSwingAllowed <= 0;

	/// <summary>
	/// Perform a melee swing. Called on both host and client — damage only applied on host.
	/// </summary>
	public virtual void Swing()
	{
		TimeUntilNextSwingAllowed = SwingRate;

		if ( !Networking.IsHost ) return;

		var tr = DoTrace();
		bool hit = tr.Hit && tr.GameObject.IsValid();

		// Broadcast animation with hit result so the animgraph can pick the right variant
		BroadcastSwingAnimation( hit );

		if ( hit )
			OnHit( tr );
		else
			OnMiss();
	}

	/// <summary>
	/// Source-accurate two-pass melee trace, matching CBaseHLBludgeonWeapon::Swing.
	///
	/// Pass 1: line trace from eye along aim direction for Range units.
	/// Pass 2 (if pass 1 misses): hull trace (box, HullSize in each axis) along
	///   a shortened distance, with a dot-product facing check to reject grazes.
	///
	/// This is why the HL2 crowbar feels precise on close targets but still
	/// catches things slightly off-centre — the hull fallback covers the gap.
	/// </summary>
	[Property, Group( "Melee" )] public float HullSize { get; set; } = 16f;  // BLUDGEON_HULL_DIM

	protected virtual SceneTraceResult DoTrace()
	{
		var eye = Owner?.EyeTransform ?? new Transform( WorldPosition, WorldRotation );
		var start = eye.Position;
		var dir   = eye.Rotation.Forward;
		var end   = start + dir * Range;

		// Pass 1: line trace
		var tr = Scene.Trace
			.Ray( start, end )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "playercontroller" )
			.UseHitboxes()
			.Run();

		if ( tr.Hit ) return tr;

		// Pass 2: hull trace fallback (misses on the line trace).
		// Source backs the end off by the hull diagonal (sqrt(3)*HullSize) so the
		// hull tip stays at roughly the same world reach as the line trace.
		var hullRadius = 1.732f * HullSize;
		var hullEnd   = start + dir * MathF.Max( Range - hullRadius, 0f );
		var hullMins  = Vector3.One * -HullSize;
		var hullMaxs  = Vector3.One *  HullSize;

		var hullTr = Scene.Trace
			.Box( new BBox( hullMins, hullMaxs ), start, hullEnd )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "playercontroller" )
			.UseHitboxes()
			.Run();

		if ( !hullTr.Hit || !hullTr.GameObject.IsValid() ) return hullTr;

		// Dot-product check: must be at least roughly facing the hit object
		// (Source uses 0.70721f ≈ cos(45°))
		var toTarget = (hullTr.GameObject.WorldPosition - start).Normal;
		if ( toTarget.Dot( dir ) < 0.70721f )
		{
			// Force a miss result
			return tr;
		}

		return hullTr;
	}

	protected virtual void OnHit( SceneTraceResult tr )
	{
		// Damage
		var damageable = tr.GameObject.GetComponentInParent<Component.IDamageable>();
		if ( damageable is not null )
		{
			damageable.Damage( new DamageInfo( Damage, Owner?.GameObject ?? GameObject, GameObject )
			{
				Position = tr.HitPosition,
				Origin   = tr.StartPosition,
				Tags     = new TagSet( new[] { DamageTags.Melee } ),
			} );
		}

		// Physics push
		if ( tr.Body.IsValid() && HitForce > 0f )
			tr.Body.ApplyImpulseAt( tr.HitPosition, tr.Direction * HitForce * tr.Body.Mass );

		// Impact decal + sound — reuse the bullet effect system
		Bullet.SpawnImpactEffect( tr.HitPosition, tr.Normal, tr.GameObject, tr.Surface, ImpactEffectOverride );
		if ( HitSound.IsValid() ) BroadcastSound( HitSound, tr.HitPosition );
	}

	protected virtual void OnMiss()
	{
		if ( MissSound.IsValid() ) BroadcastSound( MissSound, WorldPosition );
	}

	[Rpc.Broadcast]
	void BroadcastSwingAnimation( bool hasHit )
	{
		if ( Application.IsDedicatedServer ) return;
		var model = GetComponentInChildren<WeaponModel>();
		if ( model.IsValid() )
			model.GameObject.RunEvent<WeaponModel>( x => x.OnMeleeAttack( hasHit ) );
	}

	[Rpc.Broadcast]
	void BroadcastSound( SoundEvent sound, Vector3 position )
	{
		if ( Application.IsDedicatedServer ) return;
		Sound.Play( sound, position );
	}

	public override void DrawHud( HudPainter painter, Vector2 crosshair )
	{
		// Simple static crosshair — melee has no aim cone
		var color = CanSwing() ? Color.White : Color.Red;
		var gap = 12f;
		var len = 8f;
		var w = 2f;
		painter.DrawLine( crosshair + Vector2.Left  * (len + gap), crosshair + Vector2.Left  * gap, w, color );
		painter.DrawLine( crosshair - Vector2.Left  * (len + gap), crosshair - Vector2.Left  * gap, w, color );
		painter.DrawLine( crosshair + Vector2.Up    * (len + gap), crosshair + Vector2.Up    * gap, w, color );
		painter.DrawLine( crosshair - Vector2.Up    * (len + gap), crosshair - Vector2.Up    * gap, w, color );
	}

	public override bool ShouldAvoid => false;  // Never auto-avoid melee
}
