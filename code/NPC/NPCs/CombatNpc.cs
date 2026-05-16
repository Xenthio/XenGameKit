// Example combat NPC. Searches for players, chases them, and attacks.
// Shows the full pattern for making a functional NPC with XenGameKit.
//
// To make your own NPC, copy this file, rename everything, and override GetSchedule().

public class CombatNpc : BaseNpc
{
	[Property, Group( "Combat" )] public float AttackRange    { get; set; } = 128f;
	[Property, Group( "Combat" )] public float AttackDamage   { get; set; } = 10f;
	[Property, Group( "Combat" )] public float AttackCooldown { get; set; } = 1.5f;
	[Property, Group( "Combat" )] public float WanderRadius   { get; set; } = 400f;

	TimeSince _lastAttack;

	protected override void OnStart()
	{
		base.OnStart();
		Senses.TargetTags = new() { "player" };
		NpcName = "Grunt";
	}

	public override ScheduleBase GetSchedule()
	{
		var target = Senses.NearestVisible;

		if ( target.IsValid() )
		{
			// In attack range? Swing at them.
			float dist = WorldPosition.Distance( target.WorldPosition );
			if ( dist <= AttackRange ) return GetSchedule<AttackSchedule>();

			// Close in.
			var chase = GetSchedule<ChaseSchedule>();
			chase.Target = target;
			return chase;
		}

		// Nothing visible — wander around.
		return GetSchedule<PatrolSchedule>();
	}

	protected override void OnHurt( in DamageInfo damage )
	{
		InterruptSchedule();
	}

	// Try to melee attack the nearest visible target.
	public void TryAttack()
	{
		if ( _lastAttack < AttackCooldown ) return;
		var target = Senses.NearestVisible;
		if ( !target.IsValid() ) return;
		if ( WorldPosition.Distance( target.WorldPosition ) > AttackRange ) return;

		_lastAttack = 0;

		// Face the target first
		Animation.LookAt( target.WorldPosition );

		var damageable = target.GetComponentInParent<Component.IDamageable>();
		damageable?.OnDamage( new DamageInfo( AttackDamage, GameObject, null ) );
	}

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

	class AttackSchedule : ScheduleBase
	{
		protected override void OnStart( BaseNpc npc )
		{
			AddTask( new AttackTask() );
			AddTask( new WaitTask( 0.5f ) );
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

	class AttackTask : NpcTask
	{
		protected override NpcTaskStatus OnTick( BaseNpc npc )
		{
			(npc as CombatNpc)?.TryAttack();
			return NpcTaskStatus.Success;
		}
	}
}
