// NpcSenses — environmental awareness layer.
// Scans nearby GameObjects by tag, tests line-of-sight, and exposes
// VisibleTargets / AudibleTargets for schedules to query.
// Runs only on the host; proxies don't need awareness data.
public class NpcSenses : Component
{
	[Property] public float SightRange   { get; set; } = 1024f;
	[Property] public float HearingRange { get; set; } = 512f;
	[Property] public float ScanInterval { get; set; } = 0.15f;

	// Tags that count as hostile targets (visible/audible lists are filtered to these).
	// Override in your NPC subclass's OnStart to customise.
	public TagSet TargetTags { get; set; } = new() { "player" };

	// All tags to scan for. Superset of TargetTags.
	public TagSet ScanTags   { get; set; } = new() { "player" };

	public IReadOnlyList<GameObject> VisibleTargets => _visible;
	public IReadOnlyList<GameObject> AudibleTargets => _audible;

	public GameObject NearestVisible { get; private set; }

	readonly List<GameObject> _visible = new();
	readonly List<GameObject> _audible = new();
	TimeSince _lastScan;

	BaseNpc _npc;

	protected override void OnStart()
	{
		_npc = GetComponent<BaseNpc>();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || _npc is null ) return;
		if ( _lastScan < ScanInterval ) return;
		_lastScan = 0;
		Scan();
	}

	void Scan()
	{
		_visible.Clear();
		_audible.Clear();
		NearestVisible = null;
		float nearestDist = float.MaxValue;

		foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, HearingRange ) ) )
		{
			if ( !obj.Tags.HasAny( ScanTags ) ) continue;

			float dist      = WorldPosition.Distance( obj.WorldPosition );
			bool  audible   = dist <= HearingRange;
			bool  visible   = dist <= SightRange && HasLos( obj );
			bool  isTarget  = obj.Tags.HasAny( TargetTags );

			if ( audible && isTarget ) _audible.Add( obj );
			if ( visible && isTarget )
			{
				_visible.Add( obj );
				if ( dist < nearestDist ) { nearestDist = dist; NearestVisible = obj; }
			}
		}
	}

	bool HasLos( GameObject target )
	{
		var eye    = WorldPosition + Vector3.Up * 64f;
		var tgt    = target.WorldPosition + Vector3.Up * 32f;
		var tr     = Scene.Trace.Ray( eye, tgt )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger" )
			.Run();
		return !tr.Hit || tr.GameObject == target || target.IsDescendant( tr.GameObject );
	}

	public string GetDebugString()
	{
		if ( _visible.Count == 0 && _audible.Count == 0 ) return "";
		return $"Senses: {_visible.Count} visible, {_audible.Count} audible";
	}
}
