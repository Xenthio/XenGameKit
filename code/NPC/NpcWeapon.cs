// NPC weapon system.
//
// NPCs don't use the player inventory or input pipeline. Instead they get an NpcWeapon —
// a plain component that handles aim, world model attachment, and firing.
//
// The visual (worldmodel) reuses BaseCarryable.CreateWorldModel() so weapon prefabs look
// identical on NPCs and players. The firing logic is fully separate and driven by NPC code.
//
// To give an NPC a weapon:
//   1. Add any NpcWeapon subclass to your NPC prefab (or add it in OnStart).
//   2. Call npc.Weapon.TryFire(target) from a schedule task.
//
// To make a new NPC weapon type:
//   1. Subclass NpcWeaponBase.
//   2. Implement OnFire(origin, direction, target) — do whatever: Bullet.Fire, spawn
//      a grenade, call Explosion.Blast, launch a projectile. No restrictions.
//   3. That's it.
//
// Built-in types:
//   NpcBulletWeapon    — reads stats from a BaseBulletWeapon prefab, fires hitscan bullets
//   NpcGrenadeWeapon   — lobs a grenade prefab at the target with arc calculation
//   NpcMeleeNpcWeapon  — melee swing (complement to BaseNpc.TrySwing for weapon-wielding NPCs)

public abstract class NpcWeaponBase : Component
{
	[Property] public GameObject WeaponPrefab { get; set; }
	[Property] public float      FireRate     { get; set; } = 1f; // shots per second

	TimeSince _lastFire;
	bool      _worldModelCreated;

	public bool CanFire => _lastFire >= 1f / FireRate;

	// ─── World model ──────────────────────────────────────────────────────────

	// Call once when the NPC spawns or equips this weapon.
	// Attaches the weapon worldmodel to the NPC's hold_r bone.
	public void AttachWorldModel( SkinnedModelRenderer npcRenderer )
	{
		if ( _worldModelCreated || WeaponPrefab is null || npcRenderer is null ) return;
		_worldModelCreated = true;

		// Clone the weapon prefab as a non-networked cosmetic child
		var bone    = npcRenderer.GetBoneObject( "hold_r" ) ?? npcRenderer.GameObject;
		var worldGo = WeaponPrefab.Clone( new CloneConfig
		{
			Parent       = bone,
			StartEnabled = true,
			Transform    = global::Transform.Zero,
		} );

		worldGo.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;

		// Pull holdtype from the weapon prefab so NpcAnimation poses correctly
		var carryable = WeaponPrefab.Components.Get<BaseCarryable>( true );
		if ( carryable.IsValid() )
		{
			var npcAnim = GetComponentInParent<NpcAnimation>();
			npcAnim?.SetHoldType( (int)carryable.HoldType );
		}
	}

	// ─── Fire ─────────────────────────────────────────────────────────────────

	/// <summary>
	/// Try to fire at a target. Returns true if the weapon actually fired.
	/// Handles the fire rate cooldown internally.
	/// </summary>
	public bool TryFire( Vector3 origin, Vector3 direction, GameObject target = null )
	{
		if ( !CanFire ) return false;
		_lastFire = 0;
		OnFire( origin, direction, target );
		return true;
	}

	/// <summary>
	/// Implement the actual firing logic here. No restrictions on what you do.
	/// Fire a bullet, spawn a projectile, trigger an explosion — anything goes.
	/// </summary>
	protected abstract void OnFire( Vector3 origin, Vector3 direction, GameObject target );
}

// ─── Bullet weapon ────────────────────────────────────────────────────────────

/// <summary>
/// Hitscan bullet weapon for NPCs. Set WeaponPrefab to any BaseBulletWeapon prefab —
/// damage, spread, range, and sound are pulled from it automatically.
/// </summary>
public class NpcBulletWeapon : NpcWeaponBase
{
	[Property, Group( "Override" )] public float DamageOverride   { get; set; } = 0f;  // 0 = use prefab value
	[Property, Group( "Override" )] public float SpreadOverride   { get; set; } = 0f;  // extra spread for NPCs
	[Property, Group( "Override" )] public int   BurstCount       { get; set; } = 1;

	protected override void OnFire( Vector3 origin, Vector3 direction, GameObject target )
	{
		// Pull stats from the weapon prefab if available; fall back to safe defaults
		var weapon  = WeaponPrefab?.Components.Get<BaseBulletWeapon>( true );
		var damage  = DamageOverride > 0f ? DamageOverride : weapon?.Damage ?? 10f;
		var range   = weapon?.Range ?? 2048f;
		var radius  = weapon?.BulletRadius ?? 1f;
		var force   = weapon?.ShootForce ?? 50f;
		var spread  = (weapon?.AimConeBase.x ?? 2f) + SpreadOverride;
		var sound   = weapon?.ShootSound;
		var attacker = GetComponentInParent<BaseNpc>();

		Bullet.Fire( new BulletInfo
		{
			Origin    = origin,
			Direction = direction,
			Damage    = damage,
			Radius    = radius,
			Range     = range,
			Force     = force,
			Spread    = spread,
			Count     = BurstCount,
			Attacker  = attacker?.GameObject,
			Weapon    = WeaponPrefab,
			ShootSound = sound,
		} );
	}
}

// ─── Grenade weapon ───────────────────────────────────────────────────────────

/// <summary>
/// Lobs a grenade prefab at a target with a basic arc.
/// The grenade prefab should have a Grenade component on it (or any self-destructing exploding thing).
/// </summary>
public class NpcGrenadeWeapon : NpcWeaponBase
{
	[Property] public float ThrowForce    { get; set; } = 800f;
	[Property] public float ArcHeight     { get; set; } = 200f; // extra upward kick

	protected override void OnFire( Vector3 origin, Vector3 direction, GameObject target )
	{
		if ( WeaponPrefab is null ) return;

		var attacker = GetComponentInParent<BaseNpc>();

		// Calculate a lobbed arc direction toward the target
		var aimDir = direction;
		if ( target.IsValid() )
		{
			var toTarget = (target.WorldPosition - origin);
			var flat     = toTarget.WithZ( 0 ).Normal;
			aimDir       = (flat + Vector3.Up * (ArcHeight / MathF.Max( toTarget.Length, 1f ))).Normal;
		}

		var go = WeaponPrefab.Clone( new CloneConfig
		{
			Transform    = new Transform( origin, Rotation.LookAt( aimDir ) ),
			StartEnabled = true,
		} );
		go.NetworkSpawn();

		var rb = go.GetComponent<Rigidbody>();
		rb?.ApplyImpulse( aimDir * ThrowForce * (rb.Mass) );

		// Note: Grenade is now a static type in s&box — attacker attribution not available here.
	}
}

// ─── Helper NpcTask ───────────────────────────────────────────────────────────

/// <summary>
/// Schedule task that fires an NPC's weapon at its nearest visible target.
/// Succeeds immediately after firing (or if no target / no weapon).
/// Pair with a WaitTask for burst control.
/// </summary>
public class FireWeaponTask : NpcTask
{
	protected override NpcTaskStatus OnTick( BaseNpc npc )
	{
		var weapon = npc.Components.Get<NpcWeaponBase>( FindMode.EnabledInSelfAndDescendants );
		var target = npc.Senses.NearestVisible;

		if ( weapon is null || !target.IsValid() )
			return NpcTaskStatus.Success;

		// Aim at the target's center mass with a slight random offset
		var aimPos = target.WorldPosition + Vector3.Up * 40f
		             + new Vector3(
		                 Random.Shared.Float( -12f, 12f ),
		                 Random.Shared.Float( -12f, 12f ),
		                 Random.Shared.Float( -8f,  8f ) );

		var origin    = npc.WorldPosition + Vector3.Up * 64f;
		var direction = (aimPos - origin).Normal;

		npc.Animation.LookAt( aimPos );
		weapon.TryFire( origin, direction, target );

		return NpcTaskStatus.Success;
	}
}
