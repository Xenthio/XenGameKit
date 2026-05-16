// npc_zombie — shambling headcrab victim. Slow, durable, melee only.
//
// Source reference (npc_zombie.cpp / CNPC_BaseZombie):
//   - SCHED_ZOMBIE_CHASE_ENEMY \u2014 slow relentless pursuit
//   - SCHED_ZOMBIE_MELEE_ATTACK1 \u2014 swing when in range
//   - AlertSound/IdleSound/PainSound \u2014 moaning behaviour
//   - TakeDamage \u2014 flinch threshold, gore checks (we skip torso split)
//   - GatherConditions \u2014 checks COND_CAN_MELEE_ATTACK1
//
// Simplified for XenGameKit:
//   - Wander idle, moan occasionally
//   - Endless chase when enemy sighted
//   - Melee swing when in SwingRange
//   - React to gunshots (turn to investigate)

public class npc_zombie : BaseNpc
{
	[Property, Group( "Combat" )] public float SwingRange    { get; set; } = 72f;
	[Property, Group( "Combat" )] public float SwingDamage   { get; set; } = 25f;
	[Property, Group( "Combat" )] public float SwingCooldown { get; set; } = 2f;
	[Property, Group( "Combat" )] public float WanderRadius  { get; set; } = 300f;

	TimeSince _lastSwing;
	Vector3?  _alertPosition;

	protected override void OnStart()
	{
		base.OnStart();
		NpcName   = "Zombie";
		MaxHealth = 50f;
		Health    = MaxHealth;

		Tags.Add( "hostile_npc" );

		NpcRelationships.Set<npc_zombie>( "player",       NpcDisposition.Hate );
		NpcRelationships.Set<npc_zombie>( "friendly_npc", NpcDisposition.Hate );
		NpcRelationships.Set<npc_zombie>( "hostile_npc",  NpcDisposition.Ignore );

		Senses.ScanTags   = new() { "player", "npc" };
		Senses.SightRange = 768f;

		// Zombies are slow
		Navigation.WalkSpeed = 70f;
		Navigation.RunSpeed  = 120f;
	}

	public override ScheduleBase GetSchedule()
	{
		var target = Senses.NearestVisible;

		if ( target.IsValid() && GetDisposition( target ) == NpcDisposition.Hate )
		{
			float dist = WorldPosition.Distance( target.WorldPosition );

			// In swing range — attack
			if ( dist <= SwingRange && _lastSwing >= SwingCooldown )
				return GetSchedule<ZombieSwingSchedule>();

			// Chase
			var chase = GetSchedule<ZombieChaseSchedule>();
			chase.Target = target;
			return chase;
		}

		// Heard something — shuffle over to investigate
		if ( _alertPosition.HasValue )
		{
			var investigate = GetSchedule<ZombieInvestigateSchedule>();
			investigate.Destination = _alertPosition.Value;
			_alertPosition = null;
			return investigate;
		}

		return GetSchedule<ZombieIdleSchedule>();
	}

	public override void OnSighted( GameObject obj )
	{
		if ( GetDisposition( obj ) == NpcDisposition.Hate )
			InterruptSchedule();
	}

	public override void OnHeardSound( NpcSoundStimulus s )
	{
		if ( s.SoundType is "gunshot" or "explosion" or "footstep" && !Senses.NearestVisible.IsValid() )
			_alertPosition = s.Origin;
	}

	public void TrySwing()
	{
		if ( _lastSwing < SwingCooldown ) return;
		var target = Senses.NearestVisible;
		if ( !target.IsValid() ) return;
		if ( WorldPosition.Distance( target.WorldPosition ) > SwingRange ) return;

		_lastSwing = 0;
		Animation.TriggerAttack();
		Animation.LookAt( target.WorldPosition );

		var damageable = target.GetComponentInParent<Component.IDamageable>();
		damageable?.OnDamage( new DamageInfo( SwingDamage, GameObject, null ) );

		// Blood at hit point
		BloodSystem.Splat( target.WorldPosition, (target.WorldPosition - WorldPosition).Normal, target );
	}

	protected override void OnHurt( in DamageInfo damage )
	{
		// Zombies flinch only on heavy hits (>15 damage in Source)
		if ( damage.Damage >= 15f )
			InterruptSchedule();
	}

	// ─── Schedules ────────────────────────────────────────────────────────────

	class ZombieChaseSchedule : ScheduleBase
	{
		public GameObject Target;
		protected override void OnStart( BaseNpc npc )
		{
			npc.Navigation.IsRunning = true;
			AddTask( new ChaseTask( Target, stopDistance: 60f, updateInterval: 0.3f ) );
		}
		protected override void OnEnd( BaseNpc npc ) => npc.Navigation.IsRunning = false;
		protected override bool ShouldInterrupt( BaseNpc npc ) => !Target.IsValid();
	}

	class ZombieSwingSchedule : ScheduleBase
	{
		protected override void OnStart( BaseNpc npc )
		{
			AddTask( new ZombieSwingTask() );
			AddTask( new WaitTask( 0.6f ) ); // recovery
		}
	}

	class ZombieSwingTask : NpcTask
	{
		protected override void OnStart( BaseNpc npc ) => (npc as npc_zombie)?.TrySwing();
		protected override NpcTaskStatus OnTick( BaseNpc npc ) => NpcTaskStatus.Success;
	}

	class ZombieInvestigateSchedule : ScheduleBase
	{
		public Vector3 Destination;
		protected override void OnStart( BaseNpc npc )
		{
			AddTask( new MoveToTask( Destination, stopDistance: 80f ) );
			AddTask( new WaitTask( Random.Shared.Float( 2f, 4f ) ) );
		}
	}

	class ZombieIdleSchedule : ScheduleBase
	{
		protected override void OnStart( BaseNpc npc )
		{
			AddTask( new WanderTask( (npc as npc_zombie)?.WanderRadius ?? 300f ) );
			AddTask( new WaitTask( Random.Shared.Float( 2f, 5f ) ) );
		}
	}
}
