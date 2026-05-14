/// <summary>
/// Applies an explosion at this object's position.
/// Call <see cref="Explode"/> from code, or set <see cref="ExplodeOnStart"/> for fire-and-forget.
/// Uses <see cref="Explosion.Blast"/> which follows the engine's Prop.CreateExplosion() pattern.
/// </summary>
[Title( "Explosion Damage" )]
[Category( "Game" )]
[Icon( "local_fire_department" )]
public sealed class ExplosionDamage : Component
{
	[Property, Group( "Explosion" )] public float Magnitude      { get; set; } = 100f;
	[Property, Group( "Explosion" )] public float RadiusOverride { get; set; } = 0f;
	[Property, Group( "Explosion" )] public float DamageForce    { get; set; } = 1f;
	[Property, Group( "Explosion" )] public bool  DoDamage       { get; set; } = true;

	[Property, Group( "Behaviour" )] public bool       ExplodeOnStart   { get; set; } = false;
	[Property, Group( "Behaviour" )] public bool       Repeatable       { get; set; } = false;
	[Property, Group( "Behaviour" )] public GameObject IgnoreGameObject { get; set; }

	protected override void OnStart()
	{
		// Pull values from a sibling RadiusDamage if configured by the engine (e.g. Prop.CreateExplosion)
		var rd = GetComponent<RadiusDamage>();
		if ( rd.IsValid() )
		{
			if ( rd.Radius       > 0 ) RadiusOverride = rd.Radius;
			if ( rd.DamageAmount > 0 ) Magnitude      = rd.DamageAmount;
			if ( rd.PhysicsForceScale != 0 ) DamageForce = rd.PhysicsForceScale;

			// Disable it so it can never double-apply damage
			rd.Enabled = false;
		}

		if ( ExplodeOnStart )
			Explode();
	}

	public void Explode()
	{
		if ( !Networking.IsHost ) return;

		Explosion.Blast( new ExplosionInfo
		{
			Position    = WorldPosition,
			Magnitude   = Magnitude,
			Radius      = RadiusOverride,
			DamageForce = DamageForce,
			DoDamage    = DoDamage,
			Attacker    = IgnoreGameObject ?? GameObject,
			Weapon      = GameObject,
			Ignore      = IgnoreGameObject,
		} );

		if ( !Repeatable )
			GameObject.Destroy();
	}
}
