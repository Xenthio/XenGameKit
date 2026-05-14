/// <summary>
/// Data passed to <see cref="Explosion.Blast"/>.
/// Magnitude mirrors Source's env_explosion "iMagnitude" convention.
/// </summary>
public struct ExplosionInfo
{
	public Vector3    Position    { get; set; }
	public float      Magnitude   { get; set; }
	public float      Radius      { get; set; }
	public float      DamageForce { get; set; }
	public bool       DoDamage    { get; set; }
	public GameObject Attacker    { get; set; }
	public GameObject Weapon      { get; set; }
	public GameObject Ignore      { get; set; }
	public TagSet     DamageTags  { get; set; }
}

/// <summary>
/// Explosion utility. Clones explosion_med.prefab for visuals, then uses
/// <see cref="Damage.Radius"/> for HL2-style LOS-aware damage with proper kill credit.
/// </summary>
public static class Explosion
{
	/// <summary>
	/// Trigger an explosion. Host-only.
	/// Spawns explosion_med.prefab for effects, then applies radius damage via <see cref="Damage.Radius"/>.
	/// </summary>
	public static void Blast( ExplosionInfo info )
	{
		if ( !Networking.IsHost ) return;

		var magnitude = MathF.Max( 0f, info.Magnitude );
		var radius    = info.Radius > 0f ? info.Radius : magnitude * 2.5f;

		// Visuals: clone explosion_med.prefab (particles + sound) on all clients
		SpawnEffect( info.Position );

		if ( !info.DoDamage || radius <= 0f || magnitude <= 0f )
			return;

		var tags = info.DamageTags ?? new TagSet();
		if ( !tags.Contains( DamageTags.Explosion ) )
			tags.Add( DamageTags.Explosion );

		var instigator = info.Attacker?.GetComponentInParent<Player>( true );

		// Damage: HL2-style LOS-aware radius damage with proper InstigatorId + weapon credit
		Damage.Radius(
			point:        info.Position,
			radius:       radius,
			baseDamage:   magnitude,
			tags:         tags,
			source:       info.Attacker,
			weapon:       info.Weapon,
			instigatorId: instigator?.PlayerId ?? Guid.Empty,
			ignore:       info.Ignore,
			extraForce:   MathF.Max( 0f, info.DamageForce )
		);
	}

	[Rpc.Broadcast]
	static void SpawnEffect( Vector3 position )
	{
		if ( Application.IsDedicatedServer ) return;

		var prefab = ResourceLibrary.Get<PrefabFile>( "prefabs/engine/explosion_med.prefab" );
		if ( prefab is null ) return;

		// Clone disabled, strip damage before enabling so OnStart can’t double-fire
		var go = GameObject.Clone( prefab, new CloneConfig
		{
			Transform    = new Transform( position, Rotation.Identity ),
			StartEnabled = false,
		} );

		if ( !go.IsValid() ) return;

		// Synchronously remove before any OnStart can run
		foreach ( var ed in go.GetComponentsInChildren<ExplosionDamage>( true ) )
			ed.Destroy();
		foreach ( var rd in go.GetComponentsInChildren<RadiusDamage>( true ) )
			rd.Destroy();

		go.Enabled = true;
	}
}
