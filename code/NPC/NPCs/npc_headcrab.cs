// npc_headcrab — tiny alien parasite. Idles, wanders, leaps at players.
//
// Source reference (npc_headcrab.cpp):
//   - SCHED_RANGE_ATTACK1 → jump at enemy
//   - TASK_HEADCRAB_FIND_JUMP_POSITION → validate jump arc
//   - GatherConditions → COND_CAN_RANGE_ATTACK1 when in leap distance
//
// Simplified for XenGameKit:
//   - Idle: wander within WanderRadius
//   - Alert: heard something — look around
//   - Combat: chase until LeapRange, then leap via Rigidbody impulse
//   - Collision damage: dealt immediately on the leap frame (no touch needed)

public class npc_headcrab : BaseNpc
{
	[Property, Group( "Combat" )] public float LeapRange    { get; set; } = 160f;
	[Property, Group( "Combat" )] public float LeapForce    { get; set; } = 600f;
	[Property, Group( "Combat" )] public float LeapDamage   { get; set; } = 10f;
	[Property, Group( "Combat" )] public float LeapCooldown { get; set; } = 2.5f;
	[Property, Group( "Combat" )] public float WanderRadius { get; set; } = 250f;

	TimeSince _lastLeap;
	Vector3?  _alertPosition;

	protected override void OnStart()
	{
		base.OnStart();
		NpcName = "Headcrab";
		Tags.Add( "hostile_npc" );

		NpcRelationships.Set<npc_headcrab>( "player",       NpcDisposition.Hate   );
		NpcRelationships.Set<npc_headcrab>( "friendly_npc", NpcDisposition.Hate   );
		NpcRelationships.Set<npc_headcrab>( "hostile_npc",  NpcDisposition.Ignore );

		Senses.ScanTags   = new() { "player", "npc" };
		Senses.SightRange = 512f;

		Navigation.WalkSpeed = 100f;
		Navigation.RunSpeed  = 200f;
	}

	public override ScheduleBase GetSchedule()
	{
		var target = Senses.NearestVisible;

		if ( target.IsValid() && GetDisposition( target ) == NpcDisposition.Hate )
		{
			float dist = WorldPosition.Distance( target.WorldPosition );

			if ( dist <= LeapRange && _lastLeap >= LeapCooldown )
				return GetSchedule<LeapSchedule>();

			var chase = GetSchedule<HeadcrabChaseSchedule>();
			chase.Target = target;
			return chase;
		}

		if ( _alertPosition.HasValue )
		{
			var investigate = GetSchedule<InvestigateSchedule>();
			investigate.Destination = _alertPosition.Value;
			_alertPosition = null;
			return investigate;
		}

		return GetSchedule<HeadcrabWanderSchedule>();
	}

	public override void OnSighted( GameObject obj )
	{
		if ( GetDisposition( obj ) == NpcDisposition.Hate )
			InterruptSchedule();
	}

	public override void OnHeardSound( NpcSoundStimulus s )
	{
		if ( s.SoundType is "gunshot" or "explosion" && !Senses.NearestVisible.IsValid() )
			_alertPosition = s.Origin;
	}

	// Called by LeapTask — applies impulse and immediately deals damage if in range
	public void DoLeap()
	{
		var target = Senses.NearestVisible;
		if ( !target.IsValid() ) return;

		_lastLeap = 0;

		var dir = (target.WorldPosition - WorldPosition + Vector3.Up * 0.4f).Normal;
		var rb  = GetComponent<Rigidbody>();
		rb?.ApplyImpulse( dir * LeapForce * (rb.Mass) );

		Animation.TriggerAttack();

		// Deal damage if already in melee range (Source also checks touch-on-landing,
		// but we simplify: damage on leap initiation when already close)
		var damageable = target.GetComponentInParent<Component.IDamageable>();
		if ( damageable is not null && WorldPosition.Distance( target.WorldPosition ) <= LeapRange )
			damageable.OnDamage( new DamageInfo( LeapDamage, GameObject, null ) );
	}

	protected override void OnHurt( in DamageInfo damage ) => InterruptSchedule();

	// ─── Schedules ────────────────────────────────────────────────────────────

	class HeadcrabChaseSchedule : ScheduleBase
	{
		public GameObject Target;
		protected override void OnStart( BaseNpc npc )
		{
			npc.Navigation.IsRunning = true;
			AddTask( new ChaseTask( Target, stopDistance: 120f ) );
		}
		protected override void OnEnd( BaseNpc npc ) => npc.Navigation.IsRunning = false;
		protected override bool ShouldInterrupt( BaseNpc npc ) => !Target.IsValid();
	}

	class LeapSchedule : ScheduleBase
	{
		protected override void OnStart( BaseNpc npc ) => AddTask( new LeapTask() );
	}

	class LeapTask : NpcTask
	{
		protected override void OnStart( BaseNpc npc ) => (npc as npc_headcrab)?.DoLeap();
		protected override NpcTaskStatus OnTick( BaseNpc npc ) => NpcTaskStatus.Success;
	}

	class InvestigateSchedule : ScheduleBase
	{
		public Vector3 Destination;
		protected override void OnStart( BaseNpc npc )
		{
			AddTask( new MoveToTask( Destination, stopDistance: 64f ) );
			AddTask( new WaitTask( Random.Shared.Float( 1f, 2f ) ) );
		}
	}

	class HeadcrabWanderSchedule : ScheduleBase
	{
		protected override void OnStart( BaseNpc npc )
		{
			AddTask( new WanderTask( (npc as npc_headcrab)?.WanderRadius ?? 250f ) );
			AddTask( new WaitTask( Random.Shared.Float( 1f, 3f ) ) );
		}
	}
}
