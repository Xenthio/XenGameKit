// monster_scientist (HL1) / npc_citizen (HL2) — friendly NPC, flees danger.
//
// Source reference (hl1/monster_scientist):
//   - SCHED_FEAR \u2014 flee from enemies
//   - SCHED_TARGET_FACE \u2014 face player when idle
//   - AlertSound \u2014 "Help me!" / "Don't shoot!"
//   - GatherConditions \u2014 COND_SEE_ENEMY triggers fear schedule
//
// Simplified for XenGameKit:
//   - Wanders when nothing happens
//   - Flees hostile NPCs and enemies by running away
//   - Says nothing (no speech system yet \u2014 hook in SpeechLayer when added)
//   - Can optionally follow a player (Escort = true)
//
// Note: monster_scientist is HL1 era but behaviour maps cleanly.

public class monster_scientist : BaseNpc
{
	[Property, Group( "Behaviour" )] public bool  Escort       { get; set; } = false;
	[Property, Group( "Behaviour" )] public float FleeDistance { get; set; } = 600f;
	[Property, Group( "Behaviour" )] public float WanderRadius { get; set; } = 200f;

	protected override void OnStart()
	{
		base.OnStart();
		NpcName   = "Scientist";
		MaxHealth = 20f;
		Health    = MaxHealth;

		Tags.Add( "friendly_npc" );

		// Friendly to players, afraid of hostile NPCs, indifferent to other friendlies
		NpcRelationships.Set<monster_scientist>( "player",       NpcDisposition.Like   );
		NpcRelationships.Set<monster_scientist>( "hostile_npc",  NpcDisposition.Fear   );
		NpcRelationships.Set<monster_scientist>( "friendly_npc", NpcDisposition.Ignore );

		Senses.ScanTags   = new() { "player", "npc" };
		Senses.SightRange = 800f;
	}

	public override ScheduleBase GetSchedule()
	{
		// Feared enemy visible \u2014 run
		var feared = Senses.VisibleTargets.FirstOrDefault( t => GetDisposition( t ) == NpcDisposition.Fear );
		if ( feared.IsValid() )
		{
			var flee = GetSchedule<FleeSchedule>();
			flee.ThreatPosition = feared.WorldPosition;
			return flee;
		}

		// Escort mode \u2014 follow the nearest liked player
		if ( Escort )
		{
			var leader = Senses.VisibleTargets.FirstOrDefault( t => GetDisposition( t ) == NpcDisposition.Like );
			if ( leader.IsValid() )
			{
				var follow = GetSchedule<FollowSchedule>();
				follow.Target = leader;
				return follow;
			}
		}

		return GetSchedule<ScientistWanderSchedule>();
	}

	public override void OnSighted( GameObject obj )
	{
		// Saw something scary \u2014 interrupt current schedule immediately
		if ( GetDisposition( obj ) == NpcDisposition.Fear )
			InterruptSchedule();
	}

	public override void OnHeardSound( NpcSoundStimulus s )
	{
		// Heard a gunshot nearby \u2014 get scared even without LoS
		if ( s.SoundType is "gunshot" or "explosion" )
			InterruptSchedule();
	}

	// Flee away from a threat position
	Vector3 FleeTo( Vector3 threatPos )
	{
		var awayDir = (WorldPosition - threatPos).Normal;
		return WorldPosition + awayDir * FleeDistance;
	}

	// ─── Schedules ────────────────────────────────────────────────────────────

	class FleeSchedule : ScheduleBase
	{
		public Vector3 ThreatPosition;

		protected override void OnStart( BaseNpc npc )
		{
			npc.Navigation.IsRunning = true;
			var dest = (npc as monster_scientist)?.FleeTo( ThreatPosition ) ?? npc.WorldPosition;
			AddTask( new MoveToTask( dest, stopDistance: 64f ) );
			AddTask( new WaitTask( 1f ) );
		}

		protected override void OnEnd( BaseNpc npc ) => npc.Navigation.IsRunning = false;

		// Keep fleeing as long as we can still see the threat
		protected override bool ShouldInterrupt( BaseNpc npc )
		{
			return !npc.Senses.VisibleTargets.Any( t => npc.GetDisposition( t ) == NpcDisposition.Fear );
		}
	}

	class FollowSchedule : ScheduleBase
	{
		public GameObject Target;

		protected override void OnStart( BaseNpc npc )
		{
			AddTask( new ChaseTask( Target, stopDistance: 120f ) );
		}

		protected override bool ShouldInterrupt( BaseNpc npc ) => !Target.IsValid();
	}

	class ScientistWanderSchedule : ScheduleBase
	{
		protected override void OnStart( BaseNpc npc )
		{
			AddTask( new WanderTask( (npc as monster_scientist)?.WanderRadius ?? 200f ) );
			AddTask( new WaitTask( Random.Shared.Float( 2f, 6f ) ) );
		}
	}
}
