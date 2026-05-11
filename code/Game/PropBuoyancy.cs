/// <summary>
/// Per-prop component attached by <see cref="BuoyancyPropExtension.OnPropCreated"/>.
/// Each fixed update, checks if the prop overlaps any water-tagged collider and applies
/// <see cref="Rigidbody.ApplyBuoyancy"/> accordingly. Also extinguishes fire when
/// more than half submerged.
/// </summary>
[Title( "Prop Buoyancy" )]
[Category( "Physics" )]
[Icon( "water" )]
public sealed class PropBuoyancy : Component
{
	/// <summary>Tag used to identify water volumes.</summary>
	[Property] public string WaterTag { get; set; } = "water";

	/// <summary>
	/// Fraction of the prop's height that must be submerged before fire is extinguished.
	/// 0.5 = half submerged (HL2 behaviour).
	/// </summary>
	[Property, Range( 0f, 1f )] public float ExtinguishSubmergedFraction { get; set; } = 0.5f;

	/// <summary>Show debug gizmos (water surface plane, submersion fraction).</summary>
	[Property] public bool Debug { get; set; } = false;

	Rigidbody _rb;

	protected override void OnStart()
	{
		_rb = GetComponent<Rigidbody>( true );
	}

	protected override void OnFixedUpdate()
	{
		if ( !_rb.IsValid() ) return;

		var waterPlane = FindWaterSurface();
		if ( !waterPlane.HasValue ) return;

		_rb.ApplyBuoyancy( waterPlane.Value, Time.Delta );

		// Fire extinguish check
		var bounds = GameObject.GetBounds();
		var bottom = bounds.Mins.z;
		var height = bounds.Maxs.z - bottom;
		if ( height <= 0f ) return;

		var submerged   = Math.Clamp( waterPlane.Value.Position.z - bottom, 0f, height );
		var subFraction = submerged / height;

		if ( subFraction < ExtinguishSubmergedFraction ) return;

		var fire = GetComponent<FireComponent>( true );
		if ( fire.IsValid() && fire.IsBurning )
			fire.Extinguish( fire.MaxHeat );
	}

	Plane? FindWaterSurface()
	{
		var bounds = GameObject.GetBounds();
		var bottom = bounds.Mins.z;
		var top    = bounds.Maxs.z;
		var center = new Vector3( bounds.Center.x, bounds.Center.y, top + 1f ); // trace from above

		// Trace straight down from above the prop through the full height.
		// .HitTriggers() is required to detect mesh/trigger water volumes.
		var pm = Scene.Trace.Ray( center, center + Vector3.Down * (top - bottom + 64f) )
			.WithTag( WaterTag )
			.HitTriggers()
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( !pm.Hit ) return null;

		// Hit point is where we entered the water surface from above
		var surfacePos = pm.HitPosition;
		return new Plane( surfacePos, Vector3.Up );
	}

	protected override void DrawGizmos()
	{
		if ( !Debug ) return;

		var plane = FindWaterSurface();
		if ( !plane.HasValue )
		{
			Gizmo.Draw.Color = Color.Red.WithAlpha( 0.5f );
			Gizmo.Draw.Text( "No water", new global::Transform( WorldPosition + Vector3.Up * 16f ) );
			return;
		}

		var bounds = GameObject.GetBounds();
		var bottom = bounds.Mins.z;
		var height = bounds.Maxs.z - bottom;
		var submerged   = height > 0f ? Math.Clamp( plane.Value.Position.z - bottom, 0f, height ) / height : 0f;

		Gizmo.Draw.Color = Color.Cyan.WithAlpha( 0.4f );
		Gizmo.Draw.Line( plane.Value.Position - Vector3.Right * 32f, plane.Value.Position + Vector3.Right * 32f );
		Gizmo.Draw.Line( plane.Value.Position - Vector3.Forward * 32f, plane.Value.Position + Vector3.Forward * 32f );

		Gizmo.Draw.Color = Color.White;
		Gizmo.Draw.Text( $"Submerged: {submerged:P0}", new global::Transform( WorldPosition + Vector3.Up * 16f ) );
	}
}
