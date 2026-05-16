// Built-in NpcTask implementations — the vocabulary of schedule building.
// Import these into any schedule with AddTask( new WaitTask( 2f ) ) etc.

// ─── Wait ─────────────────────────────────────────────────────────────────────

/// <summary>Idles for a fixed duration then succeeds.</summary>
public class WaitTask : NpcTask
{
	readonly float _seconds;
	TimeSince _elapsed;

	public WaitTask( float seconds ) => _seconds = seconds;

	protected override void OnStart( BaseNpc npc ) => _elapsed = 0;
	protected override NpcTaskStatus OnTick( BaseNpc npc ) =>
		_elapsed >= _seconds ? NpcTaskStatus.Success : NpcTaskStatus.Running;
}

// ─── Move to position ─────────────────────────────────────────────────────────

/// <summary>Navigates to a world-space position. Fails if the agent can't path there.</summary>
public class MoveToTask : NpcTask
{
	readonly Vector3 _target;
	readonly float   _stopDist;

	public MoveToTask( Vector3 target, float stopDistance = 24f )
	{
		_target   = target;
		_stopDist = stopDistance;
	}

	protected override void OnStart( BaseNpc npc )
	{
		npc.Navigation.MoveTo( _target, _stopDist );
	}

	protected override NpcTaskStatus OnTick( BaseNpc npc )
	{
		return npc.Navigation.GetStatus();
	}

	protected override void OnEnd( BaseNpc npc )
	{
		npc.Navigation.Stop();
	}
}

// ─── Move to and track a GameObject ──────────────────────────────────────────

/// <summary>Chases a target GameObject until within stopDistance.</summary>
public class ChaseTask : NpcTask
{
	readonly GameObject _target;
	readonly float      _stopDist;
	readonly float      _updateInterval;
	TimeSince           _lastUpdate;

	public ChaseTask( GameObject target, float stopDistance = 80f, float updateInterval = 0.2f )
	{
		_target         = target;
		_stopDist       = stopDistance;
		_updateInterval = updateInterval;
	}

	protected override void OnStart( BaseNpc npc )
	{
		if ( _target.IsValid() )
			npc.Navigation.MoveTo( _target.WorldPosition, _stopDist );
		_lastUpdate = 0;
	}

	protected override NpcTaskStatus OnTick( BaseNpc npc )
	{
		if ( !_target.IsValid() ) return NpcTaskStatus.Failed;

		// Re-path periodically so we track moving targets
		if ( _lastUpdate > _updateInterval )
		{
			npc.Navigation.MoveTo( _target.WorldPosition, _stopDist );
			_lastUpdate = 0;
		}

		return npc.Navigation.GetStatus();
	}

	protected override void OnEnd( BaseNpc npc ) => npc.Navigation.Stop();
}

// ─── Look at ─────────────────────────────────────────────────────────────────

/// <summary>Points the NPC's head/eyes at a world-space position for a fixed duration.</summary>
public class LookAtTask : NpcTask
{
	readonly Vector3 _target;
	readonly float   _duration;
	TimeSince        _elapsed;

	public LookAtTask( Vector3 target, float duration = 1f )
	{
		_target   = target;
		_duration = duration;
	}

	protected override void OnStart( BaseNpc npc )
	{
		npc.Animation.LookAt( _target );
		_elapsed = 0;
	}

	protected override NpcTaskStatus OnTick( BaseNpc npc ) =>
		_elapsed >= _duration ? NpcTaskStatus.Success : NpcTaskStatus.Running;

	protected override void OnEnd( BaseNpc npc ) => npc.Animation.StopLooking();
}

// ─── Wander ───────────────────────────────────────────────────────────────────

/// <summary>Picks a random point within radius and walks there.</summary>
public class WanderTask : NpcTask
{
	readonly float _radius;
	Vector3        _origin;
	MoveToTask     _inner;

	public WanderTask( float radius = 300f ) => _radius = radius;

	protected override void OnStart( BaseNpc npc )
	{
		_origin = npc.WorldPosition;
		var offset = new Vector3(
			Random.Shared.Float( -_radius, _radius ),
			Random.Shared.Float( -_radius, _radius ),
			0 );

		_inner = new MoveToTask( _origin + offset );
		_inner.Start( npc );
	}

	protected override NpcTaskStatus OnTick( BaseNpc npc ) => _inner.Tick( npc );
	protected override void OnEnd( BaseNpc npc ) => _inner.End( npc );
}
