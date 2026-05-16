// NpcSenses — environmental awareness layer.
//
// Detects nearby GameObjects by sight, hearing, and smell. Tracks what the
// NPC was previously aware of and fires reaction callbacks on BaseNpc when
// things enter or leave awareness. Schedules poll the current lists; the
// reaction virtuals handle the "I just noticed something" moment.
//
// Sight:   line-of-sight trace within SightRange
// Hearing: proximity within HearingRange (no LoS needed)
// Smell:   reacts to NpcStimulusSystem.SmellStimuli emitted by other things
//          (corpses, food, chemicals) — no real-time scan, event driven
//
// Runs only on the host — proxies don't need awareness data.
public class NpcSenses : Component
{
	[Property, Group( "Ranges"   )] public float SightRange   { get; set; } = 1024f;
	[Property, Group( "Ranges"   )] public float HearingRange { get; set; } = 512f;
	[Property, Group( "Ranges"   )] public float SmellRange   { get; set; } = 256f;
	[Property, Group( "Scanning" )] public float ScanInterval { get; set; } = 0.15f;

	// Tags this NPC cares about — anything not in ScanTags is invisible to it.
	// Populated by your NPC subclass in OnStart based on its faction/role.
	public TagSet ScanTags { get; set; } = new() { "player", "npc" };

	public IReadOnlyList<GameObject> VisibleTargets => _visible;
	public IReadOnlyList<GameObject> AudibleTargets => _audible;

	public GameObject NearestVisible { get; private set; }
	public GameObject NearestAudible { get; private set; }

	readonly List<GameObject> _visible   = new();
	readonly List<GameObject> _audible   = new();
	readonly HashSet<GameObject> _prevVisible = new();
	readonly HashSet<GameObject> _prevAudible = new();

	TimeSince _lastScan;
	BaseNpc   _npc;

	protected override void OnStart()
	{
		_npc = GetComponent<BaseNpc>();

		// Listen for nearby sound stimuli
		NpcStimulusSystem.OnSoundEmitted += OnSoundStimulus;
		NpcStimulusSystem.OnSmellEmitted += OnSmellStimulus;
	}

	protected override void OnDestroy()
	{
		NpcStimulusSystem.OnSoundEmitted -= OnSoundStimulus;
		NpcStimulusSystem.OnSmellEmitted -= OnSmellStimulus;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || _npc is null || _npc.IsDead ) return;
		if ( _lastScan < ScanInterval ) return;
		_lastScan = 0;
		Scan();
	}

	void Scan()
	{
		var prevVisibleSnapshot = new HashSet<GameObject>( _prevVisible );
		var prevAudibleSnapshot = new HashSet<GameObject>( _prevAudible );

		_visible.Clear();
		_audible.Clear();
		NearestVisible = null;
		NearestAudible = null;

		float nearestVisDist = float.MaxValue;
		float nearestAudDist = float.MaxValue;

		foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, HearingRange ) ) )
		{
			if ( !obj.Tags.HasAny( ScanTags ) ) continue;
			if ( obj == GameObject ) continue;

			// Check relationship — ignore things we're indifferent to
			if ( _npc.GetDisposition( obj ) == NpcDisposition.Ignore ) continue;

			float dist    = WorldPosition.Distance( obj.WorldPosition );
			bool  audible = dist <= HearingRange;
			bool  visible = dist <= SightRange && HasLos( obj );

			if ( audible )
			{
				_audible.Add( obj );
				if ( dist < nearestAudDist ) { nearestAudDist = dist; NearestAudible = obj; }
			}

			if ( visible )
			{
				_visible.Add( obj );
				if ( dist < nearestVisDist ) { nearestVisDist = dist; NearestVisible = obj; }
			}
		}

		// Fire enter/exit events by diffing against previous frame
		foreach ( var obj in _visible )
			if ( !prevVisibleSnapshot.Contains( obj ) ) _npc.OnSighted( obj );

		foreach ( var obj in prevVisibleSnapshot )
			if ( !_visible.Contains( obj ) ) _npc.OnLostSight( obj );

		foreach ( var obj in _audible )
			if ( !prevAudibleSnapshot.Contains( obj ) ) _npc.OnHeard( obj );

		foreach ( var obj in prevAudibleSnapshot )
			if ( !_audible.Contains( obj ) ) _npc.OnLostHearing( obj );

		_prevVisible.Clear();
		foreach ( var obj in _visible ) _prevVisible.Add( obj );

		_prevAudible.Clear();
		foreach ( var obj in _audible ) _prevAudible.Add( obj );
	}

	// Fires when NpcStimulusSystem.EmitSound is called near us
	void OnSoundStimulus( NpcSoundStimulus s )
	{
		if ( IsProxy || _npc is null || _npc.IsDead ) return;
		if ( WorldPosition.Distance( s.Origin ) > HearingRange ) return;
		_npc.OnHeardSound( s );
	}

	// Fires when NpcStimulusSystem.EmitSmell is called near us
	void OnSmellStimulus( NpcSmellStimulus s )
	{
		if ( IsProxy || _npc is null || _npc.IsDead ) return;
		if ( WorldPosition.Distance( s.Origin ) > SmellRange ) return;
		_npc.OnSmelled( s );
	}

	bool HasLos( GameObject target )
	{
		var eye = WorldPosition + Vector3.Up * 64f;
		var tgt = target.WorldPosition + Vector3.Up * 32f;
		var tr  = Scene.Trace.Ray( eye, tgt )
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
