/// <summary>
/// Source-like env_explosion wrapper.
/// Place on a GameObject and call Explode() from game logic or map scripts.
/// </summary>
public sealed class EnvExplosion : Component
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
