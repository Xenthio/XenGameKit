/// <summary>
/// Data used to execute an explosion event.
/// Magnitude mirrors Source's env_explosion "iMagnitude" convention.
/// </summary>
public struct ExplosionInfo
{
	public Vector3 Position { get; set; }
	public float Magnitude { get; set; }
	public float Radius { get; set; }
	public float DamageForce { get; set; }
	public bool DoDamage { get; set; }
	public GameObject Attacker { get; set; }
	public GameObject Weapon { get; set; }
	public GameObject Ignore { get; set; }
	public SoundEvent ExplosionSound { get; set; }
	public GameObject ExplosionEffectPrefab { get; set; }
	public TagSet DamageTags { get; set; }
}

/// <summary>
/// Static explosion utility, similar in spirit to Source's ExplosionCreate/env_explosion flow.
/// Use from grenades, scripted entities, traps, and map logic.
/// </summary>
public static class Explosion
{
	public static void Blast( ExplosionInfo info )
	{
		if ( !Networking.IsHost )
			return;

		var magnitude = MathF.Max( 0f, info.Magnitude );
		var radius = info.Radius > 0f ? info.Radius : magnitude * 2.5f;

		BroadcastEffects( info.Position, info.ExplosionSound, info.ExplosionEffectPrefab );

		if ( !info.DoDamage || radius <= 0f || magnitude <= 0f )
			return;

		var tags = info.DamageTags ?? new TagSet();
		if ( !tags.Contains( DamageTags.Explosion ) )
			tags.Add( DamageTags.Explosion );

		var instigator = info.Attacker?.GetComponentInParent<Player>( true );

		Damage.Radius(
			point: info.Position,
			radius: radius,
			baseDamage: magnitude,
			tags: tags,
			source: info.Attacker,
			weapon: info.Weapon,
			instigatorId: instigator?.PlayerId ?? Guid.Empty,
			ignore: info.Ignore,
			extraForce: MathF.Max( 0f, info.DamageForce )
		);
	}

	[Rpc.Broadcast]
	static void BroadcastEffects( Vector3 position, SoundEvent explosionSound, GameObject effectPrefab )
	{
		if ( Application.IsDedicatedServer )
			return;

		if ( explosionSound.IsValid() )
			Sound.Play( explosionSound, position );

		if ( !effectPrefab.IsValid() )
			return;

		effectPrefab.Clone( new CloneConfig
		{
			Transform = new Transform( position, Rotation.Identity ),
			StartEnabled = true
		} );
	}
}
