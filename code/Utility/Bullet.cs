using Sandbox.Rendering;

/// <summary>
/// All the information needed to fire a single bullet.
/// Fill this out from anywhere — weapons, NPCs, turrets, explosions — and pass to <see cref="Bullet.Fire"/>.
/// Mirrors Source SDK's FireBulletsInfo_t.
/// </summary>
public struct BulletInfo
{
	/// <summary>World position the bullet originates from.</summary>
	public Vector3 Origin { get; set; }

	/// <summary>Direction the bullet travels (normalised).</summary>
	public Vector3 Direction { get; set; }

	/// <summary>Damage on a clean hit.</summary>
	public float Damage { get; set; }

	/// <summary>Sphere radius used for the trace (larger = more forgiving hits).</summary>
	public float Radius { get; set; }

	/// <summary>Maximum trace distance in world units.</summary>
	public float Range { get; set; }

	/// <summary>Impulse force applied to physics objects on hit.</summary>
	public float Force { get; set; }

	/// <summary>Number of pellets (1 for a regular bullet, higher for shotguns).</summary>
	public int Count { get; set; }

	/// <summary>Spread cone half-angle in degrees. 0 = perfectly accurate.</summary>
	public float Spread { get; set; }

	/// <summary>The GameObject responsible for firing (used for trace ignore + attacker attribution).</summary>
	public GameObject Attacker { get; set; }

	/// <summary>The weapon that fired (used for damage attribution and tracer origin).</summary>
	public GameObject Weapon { get; set; }

	/// <summary>Sound to play at the muzzle. Optional.</summary>
	public SoundEvent ShootSound { get; set; }

	/// <summary>Impact particle prefab override. If null, falls back to per-surface impact prefabs.</summary>
	public GameObject ImpactEffectOverride { get; set; }

	/// <summary>Tags added to the DamageInfo produced by this bullet.</summary>
	public TagSet DamageTags { get; set; }
}

/// <summary>
/// Fires bullets. A static utility usable from weapons, NPCs, turrets, or anything else.
/// All logic — trace, damage, impact effects, physics push — lives here, not in weapon classes.
/// </summary>
public static class Bullet
{
	/// <summary>
	/// Fire one or more bullets described by <paramref name="info"/>.
	/// Must be called on the host for damage; effects are broadcast to all clients.
	/// </summary>
	public static void Fire( BulletInfo info )
	{
		var count = Math.Max( 1, info.Count );
		for ( int i = 0; i < count; i++ )
			FireOne( info );
	}

	static void FireOne( in BulletInfo info )
	{
		var direction = info.Spread > 0
			? info.Direction.WithAimCone( info.Spread )
			: info.Direction.Normal;

		var scene = Game.ActiveScene;
		var tr = scene.Trace
			.Ray( info.Origin, info.Origin + direction * info.Range )
			.IgnoreGameObjectHierarchy( info.Attacker )
			.IgnoreGameObjectHierarchy( info.Weapon )
			.WithoutTags( "playercontroller" )
			.Radius( info.Radius )
			.UseHitboxes()
			.Run();

		// Effects — broadcast to all clients
		BroadcastEffects( info.Weapon, tr.StartPosition, tr.EndPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface, info.ShootSound, info.ImpactEffectOverride );

		if ( !Networking.IsHost ) return;

		// Damage
		if ( tr.Hit && tr.GameObject.IsValid() )
		{
			var damageable = tr.GameObject.GetComponentInParent<Component.IDamageable>();
			if ( damageable is not null )
			{
				var dmg = new DamageInfo( info.Damage, info.Attacker, info.Weapon )
				{
					Position = tr.HitPosition,
					Origin   = info.Origin,
					Tags     = info.DamageTags ?? new TagSet(),
					Hitbox   = tr.Hitbox,
				};
				damageable.Damage( dmg );
			}
		}

		// Physics push
		if ( tr.Body.IsValid() && info.Force > 0f )
			tr.Body.ApplyImpulseAt( tr.HitPosition, direction * info.Force * tr.Body.Mass );

		// Emit a gunshot sound stimulus so nearby NPCs react
		NpcStimulusSystem.EmitSound( info.Origin, "gunshot", volume: 1f, source: info.Attacker );

		// If we hit something biological, emit a blood splat
		if ( tr.Hit && tr.GameObject.IsValid() && tr.GameObject.Tags.HasAny( "npc", "player" ) )
			BloodSystem.Splat( tr.HitPosition, tr.Normal, tr.GameObject );
	}

	/// <summary>
	/// Broadcast an impact effect at a hit point. Use this from melee weapons, explosions,
	/// or anything else that needs surface decals/particles without firing a bullet.
	/// </summary>
	[Rpc.Broadcast]
	public static void SpawnImpactEffect(
		Vector3 hitPoint,
		Vector3 normal,
		GameObject hitObject,
		Surface hitSurface,
		GameObject impactOverride = null )
	{
		if ( Application.IsDedicatedServer ) return;
		if ( !hitObject.IsValid() ) return;

		var prefab = impactOverride;
		if ( !prefab.IsValid() && hitSurface.IsValid() )
		{
			prefab = hitSurface.PrefabCollection.BulletImpact
				?? hitSurface.GetBaseSurface()?.PrefabCollection.BulletImpact;
		}

		if ( !prefab.IsValid() ) return;

		var rot = Rotation.LookAt( normal * -1f, Vector3.Random );
		var impact = prefab.Clone( new CloneConfig
		{
			Transform    = new Transform( hitPoint, rot ),
			StartEnabled = true,
		} );
		impact.SetParent( hitObject, true );
	}

	[Rpc.Broadcast]
	static void BroadcastEffects(
		GameObject weapon,
		Vector3 origin,
		Vector3 hitPoint,
		bool hit,
		Vector3 normal,
		GameObject hitObject,
		Surface hitSurface,
		SoundEvent shootSound,
		GameObject impactOverride )
	{
		if ( Application.IsDedicatedServer ) return;

		// Cache owner for re-use below
		var ownerPlayer = weapon.IsValid() ? weapon.GetComponentInParent<Player>( true ) : null;

		// Weapon model effects — RunEvent broadcasts to ALL WeaponModel components (hits both ViewModel and WorldModel)
		if ( weapon.IsValid() )
		{
			var weaponModel = weapon.GetComponentInChildren<WeaponModel>();
			if ( weaponModel.IsValid() )
			{
				weaponModel.GameObject.RunEvent<WeaponModel>( x => x.OnAttack() );
				weaponModel.GameObject.RunEvent<WeaponModel>( x => x.CreateRangedEffects( weapon.GetComponent<BaseWeapon>(), hitPoint, origin ) );
			}

			// Drive the player body's attack animation (3rd person anim + muzzleflash timing)
			ownerPlayer?.WalkController?.BodyModelRenderer?.Set( "b_attack", true );
		}

		// Shoot sound
		if ( shootSound.IsValid() && weapon.IsValid() )
		{
			var snd = weapon.PlaySound( shootSound );
			// De-spatialize for the local shooter
			if ( ownerPlayer.IsValid() && ownerPlayer.IsLocalPlayer && snd.IsValid() )
				snd.SpacialBlend = 0;
		}

		if ( !hit || !hitObject.IsValid() ) return;

		// Impact effect — override, then surface lookup
		var prefab = impactOverride;
		if ( !prefab.IsValid() && hitSurface.IsValid() )
		{
			prefab = hitSurface.PrefabCollection.BulletImpact
				?? hitSurface.GetBaseSurface()?.PrefabCollection.BulletImpact;
		}

		if ( !prefab.IsValid() ) return;

		var rot = Rotation.LookAt( normal * -1f, Vector3.Random );
		var impact = prefab.Clone( new CloneConfig
		{
			Transform    = new Transform( hitPoint, rot ),
			StartEnabled = true
		} );
		impact.SetParent( hitObject, true );
	}
}
