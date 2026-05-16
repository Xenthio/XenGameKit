// NpcNavigation — NavMeshAgent wrapper.
// Schedules call MoveTo() and poll GetStatus(). That's the whole API.
public class NpcNavigation : Component
{
	[Property] public float WalkSpeed { get; set; } = 120f;
	[Property] public float RunSpeed  { get; set; } = 250f;
	[Property] public float StopDist  { get; set; } = 24f;

	public bool IsRunning { get; set; } = false;

	NavMeshAgent _agent;
	BaseNpc      _npc;
	Vector3?     _target;
	float        _stopDist;

	protected override void OnStart()
	{
		_npc   = GetComponent<BaseNpc>();
		_agent = GetComponent<NavMeshAgent>();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || _agent is null ) return;
		_agent.MaxSpeed = IsRunning ? RunSpeed : WalkSpeed;
		_npc?.Animation?.SetMove( _agent.Velocity, _agent.WorldRotation );
	}

	public void MoveTo( Vector3 target, float stopDistance = -1f )
	{
		_target   = target;
		_stopDist = stopDistance < 0 ? StopDist : stopDistance;
		_agent?.MoveTo( target );
	}

	public void Stop()
	{
		_target = null;
		_agent?.Stop();
	}

	public NpcTaskStatus GetStatus()
	{
		if ( _target is null ) return NpcTaskStatus.Success;

		float dist = WorldPosition.Distance( _target.Value );
		if ( dist <= _stopDist ) return NpcTaskStatus.Success;
		if ( _agent is not null && !_agent.IsNavigating ) return NpcTaskStatus.Failed;
		return NpcTaskStatus.Running;
	}
}
