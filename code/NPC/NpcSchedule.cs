// NpcTaskStatus — what a schedule or task reports back each tick.
// Mirrors the sandbox gamemode's TaskStatus, renamed to avoid namespace collision.
public enum NpcTaskStatus
{
	Running,     // Keep going
	Success,     // Done, move to next task / pick new schedule
	Failed,      // Something went wrong, bail out
	Interrupted, // External event cancelled us
}

// ─── Schedule ─────────────────────────────────────────────────────────────────

// A schedule is an ordered list of NpcTasks. The NPC runs them sequentially.
// When all tasks succeed the schedule is done and GetSchedule() picks the next one.
//
// Use case: "EngageSchedule" = MoveTo(target) → Fire(target) → Reposition
//
// To create a schedule:
//   1. Subclass ScheduleBase.
//   2. Override OnStart() and call AddTask() to queue your tasks.
//   3. Override ShouldInterrupt() if an external condition should abort it early.
public abstract class ScheduleBase
{
	readonly List<NpcTask> _tasks = new();
	int _index;

	// Queue a task. Call from OnStart().
	protected void AddTask( NpcTask task ) => _tasks.Add( task );

	// Override to decide whether this schedule should abort this frame.
	// Return true to interrupt (e.g. target died, health too low).
	protected virtual bool ShouldInterrupt( BaseNpc npc ) => false;

	// Override to build your task list.
	protected virtual void OnStart( BaseNpc npc ) { }

	// Override for cleanup (stop agent, clear look target, etc.).
	protected virtual void OnEnd( BaseNpc npc ) { }

	internal void Start( BaseNpc npc )
	{
		_tasks.Clear();
		_index = 0;
		OnStart( npc );
		_tasks.FirstOrDefault()?.Start( npc );
	}

	internal NpcTaskStatus Tick( BaseNpc npc )
	{
		if ( _tasks.Count == 0 ) return NpcTaskStatus.Success;
		if ( _index >= _tasks.Count ) return NpcTaskStatus.Success;

		if ( ShouldInterrupt( npc ) ) return NpcTaskStatus.Interrupted;

		var status = _tasks[_index].Tick( npc );

		if ( status == NpcTaskStatus.Running ) return NpcTaskStatus.Running;

		_tasks[_index].End( npc );

		if ( status == NpcTaskStatus.Success )
		{
			_index++;
			if ( _index < _tasks.Count )
			{
				_tasks[_index].Start( npc );
				return NpcTaskStatus.Running;
			}

			return NpcTaskStatus.Success;
		}

		return status; // Failed or Interrupted
	}

	internal void End( BaseNpc npc )
	{
		if ( _index < _tasks.Count )
			_tasks[_index].End( npc );
		OnEnd( npc );
	}

	public string GetDebugString()
	{
		if ( _index >= _tasks.Count ) return GetType().Name;
		return $"{GetType().Name} / {_tasks[_index].GetType().Name}";
	}
}

// ─── Task ─────────────────────────────────────────────────────────────────────

// A single step inside a schedule. Override Start/Tick/End.
// Return Success from Tick() when done, Running while working, Failed to bail.
public abstract class NpcTask
{
	protected virtual void OnStart( BaseNpc npc ) { }
	protected virtual void OnEnd( BaseNpc npc )   { }

	// Return the status this frame.
	protected abstract NpcTaskStatus OnTick( BaseNpc npc );

	internal void Start( BaseNpc npc )  => OnStart( npc );
	internal void End( BaseNpc npc )    => OnEnd( npc );
	internal NpcTaskStatus Tick( BaseNpc npc ) => OnTick( npc );
}
