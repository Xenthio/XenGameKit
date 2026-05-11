/// <summary>
/// Applies explosion damage and physics force in a radius.
/// Place on a GameObject and call Explode() — or set ExplodeOnStart = true for fire-and-forget.
/// Used by explosion_med.prefab to replace the engine's RadiusDamage with our HL2-style system.
/// </summary>
[Title( "Explosion Damage" )]
[Category( "Game" )]
[Icon( "explosion" )]
public sealed class ExplosionDamage : Component
{
	[Property, Group( "Explosion" )] public float Magnitude { get; set; } = 100f;
	[Property, Group( "Explosion" )] public float RadiusOverride { get; set; } = 0f;
	[Property, Group( "Explosion" )] public float DamageForce { get; set; } = 0f;
	[Property, Group( "Explosion" )] public bool DoDamage { get; set; } = true;

	[Property, Group( "Effects" )] public SoundEvent ExplosionSound { get; set; }
	[Property, Group( "Effects" )] public GameObject ExplosionEffectPrefab { get; set; }

	[Property, Group( "Behaviour" )] public bool ExplodeOnStart { get; set; } = false;
	[Property, Group( "Behaviour" )] public bool Repeatable { get; set; } = false;
	[Property, Group( "Behaviour" )] public GameObject IgnoreGameObject { get; set; }

	protected override void OnStart()
	{
		// If a sibling RadiusDamage exists (set by Prop.CreateExplosion() via RunEvent),
		// pull its values so the prop's model data (radius, damage, force, attacker) flows through.
		var rd = GetComponent<RadiusDamage>();
		if ( rd.IsValid() )
		{
			if ( rd.Radius > 0 ) RadiusOverride = rd.Radius;
			if ( rd.DamageAmount > 0 ) Magnitude = rd.DamageAmount;
			if ( rd.PhysicsForceScale != 0 ) DamageForce = rd.PhysicsForceScale;
		}

		if ( ExplodeOnStart )
			Explode();
	}

	public void Explode()
	{
		if ( !Networking.IsHost )
			return;

		Explosion.Blast( new ExplosionInfo
		{
			Position = WorldPosition,
			Magnitude = Magnitude,
			Radius = RadiusOverride,
			DamageForce = DamageForce,
			DoDamage = DoDamage,
			Attacker = GameObject,
			Weapon = GameObject,
			Ignore = IgnoreGameObject,
			ExplosionSound = ExplosionSound,
			ExplosionEffectPrefab = ExplosionEffectPrefab,
		} );

		if ( !Repeatable )
			GameObject.Destroy();
	}
}
