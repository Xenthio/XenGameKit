using Sandbox;
using Sandbox.Engine.Utility.RayTrace;
using System;
using System.Diagnostics;

namespace XMovement;

public partial class PlayerMovement : Component
{
	[Property, FeatureEnabled( "Out of Bounds" )] public bool OutOfBoundsDetectionEnabled { get; set; } = true;

	[Property, Feature( "Out of Bounds" )] public bool OutOfBoundsIsSolid { get; set; } = true;

	/// <summary>
	/// How far down to trace when checking for a floor below the player (DownwardsTrace mode).
	/// </summary>
	[Property, Feature( "Out of Bounds" ), ShowIf( "DetectionMode", OutOfBoundsDetectionMode.DownwardsTrace )] public float OutOfBoundsTraceDistance { get; set; } = 4096f;

	[Property, Feature( "Out of Bounds" ), ShowIf( "DetectionMode", OutOfBoundsDetectionMode.DownwardsTrace ), ShowIf( "DetectionMode", OutOfBoundsDetectionMode.BidirectionalTrace )]
	public bool AccurateDownwardsTraceSampling { get; set; } = true;

	[Property, Feature( "Out of Bounds" ), ShowIf( "DetectionMode", OutOfBoundsDetectionMode.BidirectionalTrace )]
	public bool AccurateBidirectionalSampling { get; set; } = false;

	[Property, Feature( "Out of Bounds" )] public OutOfBoundsDetectionMode DetectionMode { get; set; } = OutOfBoundsDetectionMode.DownwardsTrace;

	[ConVar] public static bool debug_playermovement_oob { get; set; } = false;
	[ConVar] public static bool debug_playermovement_oob_timing { get; set; } = false;

	public enum OutOfBoundsDetectionMode
	{
		/// <summary>
		/// Simple void check. This will only check if there is a floor below the player.
		/// </summary>
		DownwardsTrace,
		/// <summary>
		/// Casts rays in all axis-aligned directions. If every ray hits a surface, the player
		/// is inside the sealed map hull. If any ray escapes to infinity, the player is outside.
		/// More reliable than RayParityTest for maps with internal geometry.
		/// Requires a fully sealed map — all 6 directions must be enclosed.
		/// </summary>
		AllDirectionsTrace,
		/// <summary>
		/// For each direction, fires an outward ray to find the nearest surface, then fires a
		/// return ray inward from the far end of the trace back toward the player. If the return
		/// ray hits a closer surface before reaching the player, there is a hull wall between
		/// the player and that surface — meaning the player is outside it. Works without a fully
		/// sealed map and is more robust to internal geometry than AllDirectionsTrace.
		/// </summary>
		BidirectionalTrace,
		/// <summary>
		/// Checks whether the player overlaps any collider tagged with <see cref="OutOfBoundsTag"/>.
		/// Works with any map type — the level designer places trigger volumes around OOB areas.
		/// </summary>
		OobVolume,
		/// <summary>
		/// Check if the player is outside of the navmesh. This requires the navmesh to be set up correctly and may cause performance issues if the navmesh is very large or complex.
		/// </summary>
		NavMeshTest,
		/// <summary>
		/// DIY.
		/// </summary>
		Custom
	}

	/// <summary>
	/// True if the player was out of bounds on the previous tick.
	/// </summary>
	public bool WasOutOfBounds { get; private set; }

	/// <summary>
	/// True if the player is currently out of bounds.
	/// </summary>
	public bool IsCurrentlyOutOfBounds { get; private set; }

	public bool IsOutOfBounds() => IsOutOfBounds( WorldPosition );

	/// <summary>
	/// Returns true if the given world position is considered out of bounds.
	/// For point-based modes, samples at feet/mid/head so gaps above walls are caught.
	/// BidirectionalTrace uses a bbox sweep internally so it only needs one sample.
	/// </summary>
	public bool IsOutOfBounds( Vector3 position )
	{
		if ( !OutOfBoundsDetectionEnabled ) return false;

		var timingEnabled = debug_playermovement_oob_timing;
		long timingStart = 0;
		if ( timingEnabled )
			timingStart = Stopwatch.GetTimestamp();

		var isOutOfBounds = false;

		// Always check feet position for all modes.
		if ( CheckOutOfBoundsAt( position ) )
		{
			isOutOfBounds = true;
		}
		// For non-downwards modes also probe the head position so a player standing in a
		// sliver above a wall (feet technically inside, head outside) is caught.
		else if ( DetectionMode != OutOfBoundsDetectionMode.DownwardsTrace )
		{
			var headPos = position + Vector3.Up * (Height * WorldScale.z * 0.9f);
			isOutOfBounds = CheckOutOfBoundsAt( headPos );
		}

		if ( timingEnabled )
			RecordOutOfBoundsTiming( Stopwatch.GetElapsedTime( timingStart ) );

		return isOutOfBounds;
	}

	private bool CheckOutOfBoundsAt( Vector3 position )
	{
		return DetectionMode switch
		{
			OutOfBoundsDetectionMode.DownwardsTrace => DownwardsTraceOutOfBoundsCheck( position ),
			OutOfBoundsDetectionMode.AllDirectionsTrace => AllDirectionsOutOfBoundsCheck( position ),
			OutOfBoundsDetectionMode.BidirectionalTrace => BidirectionalOutOfBoundsCheck( position ),
			OutOfBoundsDetectionMode.OobVolume => OobVolumeCheck( position ),
			OutOfBoundsDetectionMode.NavMeshTest => NavMeshOutOfBoundsCheck( position ),
			OutOfBoundsDetectionMode.Custom => CustomOutOfBoundsCheck?.Invoke() ?? false,
			_ => false
		};
	}

	private bool DownwardsTraceOutOfBoundsCheck( Vector3 position )
	{
		// Sample a 3x3 grid across the player's footprint so floor void checks line up more
		// closely with the actual collision area rather than just the center point.
		var sampleRadius = Radius * MathF.Max( WorldScale.x, WorldScale.y );

		foreach ( var normalizedSampleOffset in GetFootprintSampleOffsets( AccurateDownwardsTraceSampling ) )
		{
			var sampleOffset = normalizedSampleOffset * sampleRadius;
			var samplePosition = position + new Vector3( sampleOffset.x, sampleOffset.y, 0.0f );
			var tr = BuildProbeTrace( samplePosition, samplePosition + Vector3.Down * OutOfBoundsTraceDistance ).Run();
			if ( !tr.Hit )
				return true;

			if ( debug_playermovement_oob )
			{
				DebugOverlaySystem.Current.Line( samplePosition, tr.EndPosition, tr.Hit ? Color.Green : Color.Red, 0 );
				DebugOverlaySystem.Current.Sphere( new Sphere( tr.EndPosition, 4f ), Color.Green, 0, overlay: true );
			}
		}

		return false;
	}

	/// <summary>
	/// How far each directional ray travels (AllDirectionsTrace mode).
	/// Should be large enough to always reach the map hull from any interior point.
	/// </summary>
	[Property, Feature( "Out of Bounds" ), ShowIf( "DetectionMode", OutOfBoundsDetectionMode.AllDirectionsTrace )]
	public float AllDirectionsTraceDistance { get; set; } = 8192f;

	private static readonly Vector3[] AllDirectionAxes = new[]
	{
		Vector3.Up, Vector3.Down,
		Vector3.Forward, Vector3.Backward,
		Vector3.Left, Vector3.Right,
	};

	private static readonly Vector2[] AccurateFootprintSampleOffsets =
	{
		new( -0.75f, -0.75f ),
		new( 0.0f, -0.75f ),
		new( 0.75f, -0.75f ),
		new( -0.75f, 0.0f ),
		new( 0.0f, 0.0f ),
		new( 0.75f, 0.0f ),
		new( -0.75f, 0.75f ),
		new( 0.0f, 0.75f ),
		new( 0.75f, 0.75f ),
	};

	private static readonly Vector2[] SingleFootprintSampleOffset =
	{
		new( 0.0f, 0.0f )
	};

	private double _oobTimingTotalMs;
	private double _oobTimingMaxMs;
	private int _oobTimingSampleCount;

	private ReadOnlySpan<Vector2> GetFootprintSampleOffsets( bool accurateSampling )
	{
		return accurateSampling ? AccurateFootprintSampleOffsets : SingleFootprintSampleOffset;
	}

	private void RecordOutOfBoundsTiming( TimeSpan elapsed )
	{
		_oobTimingSampleCount++;
		_oobTimingTotalMs += elapsed.TotalMilliseconds;
		_oobTimingMaxMs = Math.Max( _oobTimingMaxMs, elapsed.TotalMilliseconds );

		if ( _oobTimingSampleCount < 30 )
			return;

		Log.Info( $"PlayerMovement OOB avg {_oobTimingTotalMs / _oobTimingSampleCount:0.###}ms max {_oobTimingMaxMs:0.###}ms over {_oobTimingSampleCount} checks" );
		_oobTimingSampleCount = 0;
		_oobTimingTotalMs = 0;
		_oobTimingMaxMs = 0;
	}

	private bool AllDirectionsOutOfBoundsCheck( Vector3 position )
	{
		// Cast a ray in each axis-aligned direction. Inside a fully sealed map every ray
		// hits a surface. If even one escapes (no hit), we must be outside the hull.
		foreach ( var dir in AllDirectionAxes )
		{
			if ( !BuildProbeTrace( position, position + dir * AllDirectionsTraceDistance ).Run().Hit )
				return true;
		}
		return false;
	}

	/// <summary>
	/// How far each bidirectional ray travels. Should be large enough to always reach the
	/// map hull from any interior point.
	/// </summary>
	[Property, Feature( "Out of Bounds" ), ShowIf( "DetectionMode", OutOfBoundsDetectionMode.BidirectionalTrace )]
	public float BidirectionalTraceDistance { get; set; } = 8192f;

	/// <summary>
	/// How many axis directions must report OOB before triggering. Lower values are more
	/// sensitive; higher values reduce false positives from one-sided geometry holes.
	/// Default 2 means any two directions reporting OOB is enough.
	/// </summary>
	[Property, Feature( "Out of Bounds" ), ShowIf( "DetectionMode", OutOfBoundsDetectionMode.BidirectionalTrace )]
	public int BidirectionalTraceThreshold { get; set; } = 2;

	private bool BidirectionalOutOfBoundsCheck( Vector3 position )
	{
		// Always check for a floor first — if there's nothing below, we're in the void
		// regardless of what the wall geometry says.
		if ( DownwardsTraceOutOfBoundsCheck( position ) ) return true;

		int oobCount = 0;
		var sampleRadius = Radius * MathF.Max( WorldScale.x, WorldScale.y );

		foreach ( var dir in AllDirectionAxes )
		{
			var directionIsOob = false;

			foreach ( var normalizedSampleOffset in GetFootprintSampleOffsets( AccurateBidirectionalSampling ) )
			{
				var sampleOffset = normalizedSampleOffset * sampleRadius;
				var originOffset = new Vector3( sampleOffset.x, sampleOffset.y, 0.0f );
				var startPoint = position + originOffset + (Vector3.Up * 0.5f);
				var farPoint = startPoint + dir * BidirectionalTraceDistance;

				// Point ray outward from multiple points across the collision footprint so
				// thin slivers the center point can fit into are caught by the outer samples.
				var outward = BuildProbeTrace( startPoint, farPoint ).UseHitPosition().Run();

				if ( !outward.Hit )
				{
					// Up: open sky is expected, skip.
					if ( dir == Vector3.Up ) continue;
					// Horizontal/down: open space, skip (sides being open is fine).
					continue;
				}

				// Fire the return ray from just past the outward hit, back toward this sample.
				// If it hits something before reaching us, there's a hull wall between the
				// hit surface and this point in the player footprint.
				var returnStart = outward.HitPosition + dir * -0.1f;
				var inward = BuildProbeTrace( returnStart, startPoint ).IgnoreKeyframed().IgnoreDynamic().Run();

				// Start and end position are not slightly off due to precision issues.
				var isclose = inward.HitPosition.Distance( returnStart ) < 0.1f;

				if ( inward.Hit && !inward.StartedSolid && !isclose )
				{
					if ( debug_playermovement_oob )
					{
						DebugOverlaySystem.Current.Line( returnStart, inward.HitPosition, Color.Red, 2 );
						DebugOverlaySystem.Current.Sphere( new Sphere( inward.StartPosition, 4f ), Color.Red, 2, overlay: true );
					}

					directionIsOob = true;
					break;
				}
			}

			if ( directionIsOob && ++oobCount >= BidirectionalTraceThreshold )
				return true;
		}

		return false;
	}

	/// <summary>
	/// The tag used to identify out-of-bounds trigger volumes in the scene (OobVolume mode).
	/// </summary>
	[Property, Feature( "Out of Bounds" ), ShowIf( "DetectionMode", OutOfBoundsDetectionMode.OobVolume )]
	public string OutOfBoundsTag { get; set; } = "oob";

	private bool OobVolumeCheck( Vector3 position )
	{
		// Overlap the player's bbox against any collider carrying the OOB tag.
		// This is a zero-length bbox trace — it returns a hit if we're already inside a volume.
		var box = BoundingBox;
		box.Mins *= WorldScale;
		box.Maxs *= WorldScale;
		var tr = Scene.Trace.Ray( position, position )
			.Size( box )
			.WithTag( OutOfBoundsTag )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		return tr.Hit;
	}
	/// <summary>
	/// Builds a point (ray) trace with the same tag/collision-rule filtering as BuildTrace.
	/// </summary>
	private SceneTrace BuildProbeTrace( Vector3 from, Vector3 to )
	{
		var trace = Scene.Trace.Ray( from, to ).IgnoreGameObjectHierarchy( GameObject );
		return UseCollisionRules ? trace.WithCollisionRules( Tags ) : trace.WithoutTags( IgnoreLayers );
	}

	private bool NavMeshOutOfBoundsCheck( Vector3 position )
	{
		// TODO
		throw new NotImplementedException( "NavMesh out-of-bounds check is not yet implemented sorry lol" );
	}

	/// <summary>
	/// When OutOfBoundsIsSolid is true, binary-searches along the current velocity vector to find
	/// the last in-bounds position, then estimates the boundary wall normal and clips velocity
	/// against it — preserving the tangential (sliding) component like normal wall collision.
	/// Call this before Move().
	/// </summary>
	internal void ClipVelocityToOutOfBounds()
	{
		if ( !OutOfBoundsDetectionEnabled || !OutOfBoundsIsSolid ) return;

		var desiredDelta = Velocity * Time.Delta;
		if ( desiredDelta.LengthSquared < 0.001f ) return;

		var start = WorldPosition;
		var end = start + desiredDelta;

		// Only clip if the destination is OOB but the start is not.
		if ( !IsOutOfBounds( end ) ) return;
		if ( IsOutOfBounds( start ) ) return;

		// Binary search for the boundary fraction.
		float lo = 0f, hi = 1f;
		for ( int i = 0; i < 16; i++ )
		{
			float mid = (lo + hi) * 0.5f;
			if ( IsOutOfBounds( start + desiredDelta * mid ) )
				hi = mid;
			else
				lo = mid;
		}

		var boundaryPos = start + desiredDelta * lo;

		// Estimate the boundary wall normal: sample each cardinal direction from the boundary
		// point. Directions that are OOB contribute negatively — the normal ends up pointing
		// away from the void (into the in-bounds area), just like a wall normal.
		var oobNormal = Vector3.Zero;
		var probeDistance = MathF.Max( Radius * WorldScale.x, 2f );
		var cardinals = new Vector3[] { Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right };
		foreach ( var dir in cardinals )
		{
			if ( IsOutOfBounds( boundaryPos + dir * probeDistance ) )
				oobNormal -= dir;
		}

		if ( oobNormal.LengthSquared > 0.001f )
		{
			oobNormal = oobNormal.Normal;

			// Slide: remove only the velocity component going into the OOB boundary,
			// exactly as CharacterControllerHelper does for solid walls.
			var velIntoWall = Velocity.Dot( oobNormal );
			if ( velIntoWall < 0 )
				Velocity -= oobNormal * velIntoWall;
		}
		else
		{
			// Couldn't determine a clean normal (e.g. surrounded on all sides) — stop dead.
			var safeDir = desiredDelta.Normal;
			var safeSpeed = (desiredDelta * lo).Length / Time.Delta;
			var dot = Velocity.Dot( safeDir );
			if ( dot > safeSpeed )
				Velocity -= safeDir * (dot - safeSpeed);
		}

		OnTouchOutOfBounds?.Invoke();
	}

	/// <summary>
	/// Fires enter/leave events based on OOB state transitions.
	/// When OutOfBoundsIsSolid is true, the boundary is enforced by ClipVelocityToOutOfBounds()
	/// so only touch events are fired here if the player still ends up OOB.
	/// </summary>
	internal void HandleOutOfBounds()
	{
		if ( !OutOfBoundsDetectionEnabled ) return;

		WasOutOfBounds = IsCurrentlyOutOfBounds;
		IsCurrentlyOutOfBounds = IsOutOfBounds();

		if ( OutOfBoundsIsSolid )
		{
			if ( IsCurrentlyOutOfBounds )
				OnTouchOutOfBounds?.Invoke();
		}
		else
		{
			if ( IsCurrentlyOutOfBounds && !WasOutOfBounds )
				OnEnterOutOfBounds?.Invoke();
			else if ( !IsCurrentlyOutOfBounds && WasOutOfBounds )
				OnLeaveOutOfBounds?.Invoke();
		}
	}

	[Property, Feature( "Out of Bounds" ), ShowIf( "DetectionMode", OutOfBoundsDetectionMode.Custom )] public Func<bool> CustomOutOfBoundsCheck { get; set; }

	/// <summary>
	/// Event fired when the XMovement object enters the out of bounds area (Only works when OutOfBoundsIsSolid is false)
	/// </summary>
	[Property, Feature( "Out of Bounds" )] public event Action OnEnterOutOfBounds;

	/// <summary>
	/// Event fired when this XMovement object leaves the out of bounds area (Only works when OutOfBoundsIsSolid is false)
	/// </summary>
	[Property, Feature( "Out of Bounds" )] public event Action OnLeaveOutOfBounds;

	/// <summary>
	/// Event fired when the XMovement object collides with the out of bounds area (Only works when OutOfBoundsIsSolid is true)
	/// </summary>
	[Property, Feature( "Out of Bounds" )] public event Action OnTouchOutOfBounds;
}
