/// <summary>
/// Data used when spawning and throwing a grenade prefab.
/// </summary>
public struct GrenadeThrowInfo
{
	public GameObject GrenadePrefab { get; set; }
	public Vector3 Position { get; set; }
	public Rotation Rotation { get; set; }
	public Vector3 Velocity { get; set; }
	public Vector3 AngularVelocity { get; set; }
	public float FuseTimeOverride { get; set; }
	public GameObject Attacker { get; set; }
	public GameObject Weapon { get; set; }
}

/// <summary>
/// Static grenade helper. Spawn/throw from weapons, NPCs, or scripted events.
/// </summary>
public static class Grenade
{
	public static GrenadeProjectile Throw( GrenadeThrowInfo info )
	{
		if ( !Networking.IsHost )
			return null;

		if ( !info.GrenadePrefab.IsValid() )
			return null;

		var grenadeGo = info.GrenadePrefab.Clone( new CloneConfig
		{
			Transform = new Transform( info.Position, info.Rotation ),
			StartEnabled = true
		} );

		if ( !grenadeGo.IsValid() )
			return null;

		var grenade = grenadeGo.GetComponentInChildren<GrenadeProjectile>( true );
		if ( !grenade.IsValid() )
		{
			grenadeGo.Destroy();
			return null;
		}

		grenade.Arm( info.Attacker, info.Weapon, info.FuseTimeOverride );

		var rb = grenade.GetComponent<Rigidbody>();
		if ( rb.IsValid() )
		{
			rb.Velocity = info.Velocity;
			rb.AngularVelocity = info.AngularVelocity;
		}

		return grenade;
	}
}

/// <summary>
/// Basic timed grenade projectile. Designed to be attached to a grenade prefab.
/// </summary>
public sealed class GrenadeProjectile : Component, Component.ICollisionListener
{
	[RequireComponent] public Rigidbody Body { get; set; }

	[Property, Group( "Grenade" )] public float FuseTime { get; set; } = 2.5f;
	[Property, Group( "Grenade" )] public bool AutoArmOnStart { get; set; } = true;
	[Property, Group( "Grenade" )] public bool DetonateOnContact { get; set; } = false;
	[Property, Group( "Grenade" )] public float ContactDetonateSpeed { get; set; } = 450f;

	[Property, Group( "Explosion" )] public float Magnitude { get; set; } = 100f;
	[Property, Group( "Explosion" )] public float Radius { get; set; } = 0f;
	[Property, Group( "Explosion" )] public float DamageForce { get; set; } = 0f;

	TimeSince _timeSinceArmed;
	bool _isArmed;
	bool _hasExploded;
	GameObject _attacker;
	GameObject _weapon;

	protected override void OnStart()
	{
		if ( AutoArmOnStart )
			Arm( null, null, FuseTime );
	}

	public void Arm( GameObject attacker, GameObject weapon, float fuseTimeOverride = 0f )
	{
		if ( fuseTimeOverride > 0f )
			FuseTime = fuseTimeOverride;

		_attacker = attacker;
		_weapon = weapon;
		_isArmed = true;
		_timeSinceArmed = 0f;
	}

	protected override void OnUpdate()
	{
		if ( !_isArmed || _hasExploded ) return;
		if ( !Networking.IsHost ) return;

		if ( _timeSinceArmed >= FuseTime )
			Explode();
	}

	void Component.ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( !_isArmed || _hasExploded ) return;
		if ( !Networking.IsHost ) return;
		if ( !DetonateOnContact ) return;

		if ( MathF.Abs( collision.Contact.NormalSpeed ) >= ContactDetonateSpeed )
			Explode();
	}

	public void Explode()
	{
		if ( _hasExploded ) return;
		if ( !Networking.IsHost ) return;

		_hasExploded = true;

		Explosion.Blast( new ExplosionInfo
		{
			Position    = WorldPosition,
			Magnitude   = Magnitude,
			Radius      = Radius,
			DamageForce = DamageForce,
			DoDamage    = true,
			Attacker    = _attacker ?? GameObject,
			Weapon      = _weapon,
			Ignore      = GameObject,
		} );

		GameObject.Destroy();
	}
}
