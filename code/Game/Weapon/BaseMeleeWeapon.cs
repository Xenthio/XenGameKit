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

	protected TimeUntil TimeUntilNextSwingAllowed;

	public override void OnControl( Player player )
	{
		if ( Input.Pressed( "attack1" ) && CanSwing() )
			Swing();
	}

	public bool CanSwing() => TimeUntilNextSwingAllowed <= 0;

	/// <summary>
	/// Perform a melee swing. Called on both host and client — damage only applied on host.
	/// </summary>
	public virtual void Swing()
	{
		TimeUntilNextSwingAllowed = SwingRate;

		// Fire the animation event on all clients
		TriggerSwingAnimation();

		if ( !Networking.IsHost ) return;

		var tr = DoTrace();
		if ( tr.Hit && tr.GameObject.IsValid() )
			OnHit( tr );
		else
			OnMiss();
	}

	protected virtual SceneTraceResult DoTrace()
	{
		var ray = Owner?.EyeTransform.ForwardRay ?? new Ray( WorldPosition, WorldRotation.Forward );
		return Scene.Trace
			.Ray( ray.Position, ray.Position + ray.Forward * Range )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "playercontroller" )
			.Radius( TraceRadius )
			.UseHitboxes()
			.Run();
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

		// Effects
		BroadcastHitEffects( tr.EndPosition, tr.Normal, tr.GameObject, tr.Surface );
	}

	protected virtual void OnMiss()
	{
		BroadcastMissEffects();
	}

	[Rpc.Broadcast]
	void TriggerSwingAnimation()
	{
		if ( Application.IsDedicatedServer ) return;

		// Tell the WeaponModel to play attack
		var model = GetComponentInChildren<WeaponModel>();
		if ( model.IsValid() )
			model.GameObject.RunEvent<WeaponModel>( x => x.OnAttack() );
	}

	[Rpc.Broadcast]
	void BroadcastHitEffects( Vector3 hitPoint, Vector3 normal, GameObject hitObject, Surface hitSurface )
	{
		if ( Application.IsDedicatedServer ) return;

		if ( HitSound.IsValid() )
			Sound.Play( HitSound, hitPoint );

		// Impact effect — override, then surface lookup
		var prefab = ImpactEffectOverride;
		if ( !prefab.IsValid() && hitSurface.IsValid() )
		{
			prefab = hitSurface.PrefabCollection.BulletImpact
				?? hitSurface.GetBaseSurface()?.PrefabCollection.BulletImpact;
		}

		if ( !prefab.IsValid() || !hitObject.IsValid() ) return;

		var rot = Rotation.LookAt( normal * -1f, Vector3.Random );
		var impact = prefab.Clone( new CloneConfig
		{
			Transform    = new Transform( hitPoint, rot ),
			StartEnabled = true,
		} );
		impact.SetParent( hitObject, true );
	}

	[Rpc.Broadcast]
	void BroadcastMissEffects()
	{
		if ( Application.IsDedicatedServer ) return;

		if ( MissSound.IsValid() )
			Sound.Play( MissSound, WorldPosition );
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
