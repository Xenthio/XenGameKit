// Example combat NPC. Armed with a bullet weapon, chases and shoots players.
// Shows the full pattern for making a functional armed NPC with XenGameKit.
//
// Set WeaponPrefab on the NpcBulletWeapon component to any BaseBulletWeapon prefab
// and the NPC will shoot with that weapon's stats and play its worldmodel.
//
// To make your own NPC, copy this file, rename everything, and override GetSchedule().

[RequireComponent] public NpcBulletWeapon Weapon { get; private set; }

public class CombatNpc : BaseNpc
{
	[Property, Group( "Combat" )] public float AttackRange    { get; set; } = 512f;
	[Property, Group( "Combat" )] public float WanderRadius   { get; set; } = 400f;

	Vector3? _lastKnownPosition;

	protected override void OnStart()
	{
		base.OnStart();

		NpcRelationships.Set<CombatNpc>( "player",       NpcDisposition.Hate );
		NpcRelationships.Set<CombatNpc>( "hostile_npc",  NpcDisposition.Hate );
		NpcRelationships.Set<CombatNpc>( "friendly_npc", NpcDisposition.Like );

		Senses.ScanTags = new() { "player", "npc" };
		NpcName = "Grunt";

		// Attach weapon worldmodel to our hold_r bone
		if ( Renderer.IsValid() )
			Weapon.AttachWorldModel( Renderer );
	}

	// Interrupt our current plan when we first sight an enemy.
	public override void OnSighted( GameObject obj )
	{
		if ( GetDisposition( obj ) == NpcDisposition.Hate )
			InterruptSchedule();
	}

	// Heard a gunshot or explosion nearby — go investigate.
	public override void OnHeardSound( NpcSoundStimulus stimulus )
	{
		if ( stimulus.SoundType is "gunshot" or "explosion" )
			_lastKnownPosition = stimulus.Origin;
	}

	public override ScheduleBase GetSchedule()
	{
		var target = Senses.NearestVisible;

		if ( target.IsValid() && GetDisposition( target ) == NpcDisposition.Hate )
		{
			float dist = WorldPosition.Distance( target.WorldPosition );

			// In attack range and weapon ready — shoot
			if ( dist <= AttackRange && Weapon.CanFire )
			{
				var shoot = GetSchedule<ShootSchedule>();
				return shoot;
			}

			// Close in
			var chase = GetSchedule<ChaseSchedule>();
			chase.Target = target;
			return chase;
		}

		// Nothing hostile visible — wander around.
		return GetSchedule<PatrolSchedule>();
	}

	protected override void OnHurt( in DamageInfo damage ) => InterruptSchedule();

	// ─── Schedules ────────────────────────────────────────────────────────────

	class ChaseSchedule : ScheduleBase
	{
		public GameObject Target;

		protected override void OnStart( BaseNpc npc )
		{
			npc.Navigation.IsRunning = true;
			AddTask( new ChaseTask( Target, stopDistance: 100f ) );
		}

		protected override void OnEnd( BaseNpc npc )
		{
			npc.Navigation.IsRunning = false;
		}

		protected override bool ShouldInterrupt( BaseNpc npc ) => !Target.IsValid();
	}

	class ShootSchedule : ScheduleBase
	{
		protected override void OnStart( BaseNpc npc )
		{
			AddTask( new FireWeaponTask() );
			AddTask( new WaitTask( 0.3f ) ); // brief pause between bursts
		}
	}

	class PatrolSchedule : ScheduleBase
	{
		protected override void OnStart( BaseNpc npc )
		{
			AddTask( new WanderTask( (npc as CombatNpc)?.WanderRadius ?? 300f ) );
			AddTask( new WaitTask( Random.Shared.Float( 1f, 3f ) ) );
		}
	}

}
