// Base class for driveable vehicles. Source-engine inspired.
//
// Covers the essentials: enter/exit, camera, input forwarding.
// Physics (arcade vs simulation) is left to subclasses — add your own
// Rigidbody drive logic in OnDrive().
//
// To make a new vehicle:
//   1. Subclass BaseVehicle.
//   2. Override OnDrive() to apply forces to your Rigidbody.
//   3. Override GetCameraTransform() if the default chase cam doesn't suit.
//   4. Place your vehicle prefab in the scene. Players press E to enter.
//
// The vehicle sets the camera and disables the player's own movement while occupied.
// On exit, everything is restored.

public abstract class BaseVehicle : Component, IPressable
{
	[Property, Group( "Seats"   )] public Transform DriverSeat     { get; set; }
	[Property, Group( "Seats"   )] public Transform ExitPoint      { get; set; }
	[Property, Group( "Camera"  )] public float     CameraDistance { get; set; } = 300f;
	[Property, Group( "Camera"  )] public float     CameraHeight   { get; set; } = 80f;
	[Property, Group( "Camera"  )] public float     CameraSmooth   { get; set; } = 8f;

	// The player currently driving, null if unoccupied. Synced so all clients know.
	[Sync] public Player Driver { get; private set; }

	public bool IsOccupied => Driver.IsValid();

	Angles _cameraAngles;

	// ─── Enter / exit ─────────────────────────────────────────────────────────

	bool IPressable.Press( IPressable.Event e )
	{
		// Use key from any player who isn't already in a vehicle
		var presser = (e.Source as Component)?.GetComponent<Player>( FindMode.InSelf | FindMode.InParent );
		if ( !presser.IsValid() ) return false;

		if ( IsOccupied )
		{
			// Only the driver can exit by pressing E again
			if ( presser == Driver ) Exit();
			return false;
		}

		Enter( presser );
		return true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	public void Enter( Player player )
	{
		if ( IsOccupied ) return;

		Driver = player;
		OnDriverEntered( player );

		// Disable the player's own movement — vehicle drives them now
		if ( player.IsLocalPlayer )
			player.WalkController.Enabled = false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	public void Exit()
	{
		if ( !IsOccupied ) return;

		var driver = Driver;
		Driver = null;

		// Teleport driver to exit point
		if ( driver.IsValid() )
		{
			driver.WorldPosition = ExitPoint.Position != Vector3.Zero
				? WorldTransform.PointToWorld( ExitPoint.Position )
				: WorldPosition + WorldRotation.Left * 60f + Vector3.Up * 40f;

			if ( driver.IsLocalPlayer )
				driver.WalkController.Enabled = true;
		}

		OnDriverExited( driver );
	}

	// ─── Per-frame ────────────────────────────────────────────────────────────

	protected override void OnUpdate()
	{
		if ( !IsOccupied ) return;

		// Stick the driver to the seat
		if ( Driver.IsValid() )
		{
			Driver.WorldPosition = WorldTransform.PointToWorld( DriverSeat.Position );
			Driver.WorldRotation = WorldTransform.RotationToWorld( DriverSeat.Rotation );
		}

		// Only the driver sends input
		if ( !Driver.IsLocalPlayer ) return;

		UpdateCamera();

		var input = new VehicleInput
		{
			Throttle  = Input.AnalogMove.x,
			Steering  = -Input.AnalogMove.y,
			Brake     = Input.Down( "Run" ) ? 1f : 0f,
			Handbrake = Input.Down( "Duck" ),
		};

		Drive( input );

		if ( Input.Pressed( "Use" ) ) Exit();
	}

	[Rpc.Broadcast]
	void Drive( VehicleInput input )
	{
		if ( IsProxy ) return;
		OnDrive( input );
	}

	// ─── Camera ──────────────────────────────────────────────────────────────

	void UpdateCamera()
	{
		_cameraAngles += Input.AnalogLook;
		_cameraAngles.pitch = _cameraAngles.pitch.Clamp( -30f, 60f );
		_cameraAngles.roll  = 0f;

		var cam = Scene.GetAll<CameraComponent>().FirstOrDefault( c => c.IsMainCamera );
		if ( cam is null ) return;

		var target = GetCameraTransform();
		cam.WorldPosition = Vector3.Lerp( cam.WorldPosition, target.Position, CameraSmooth * Time.Delta );
		cam.WorldRotation = Rotation.Slerp( cam.WorldRotation, target.Rotation, CameraSmooth * Time.Delta );
	}

	// Returns the desired camera world-space transform. Override for first-person or cinematic modes.
	protected virtual Transform GetCameraTransform()
	{
		var rot = _cameraAngles.ToRotation();
		var pos = WorldPosition
		          + Vector3.Up * CameraHeight
		          - rot.Forward * CameraDistance;

		return new Transform( pos, Rotation.LookAt( WorldPosition + Vector3.Up * 40f - pos ) );
	}

	// ─── Virtuals ─────────────────────────────────────────────────────────────

	// Apply throttle/steering/brake forces here. Called every frame while driven.
	protected abstract void OnDrive( VehicleInput input );

	// Called on all clients when a driver enters.
	protected virtual void OnDriverEntered( Player driver ) { }

	// Called on all clients when the driver exits.
	protected virtual void OnDriverExited( Player driver ) { }
}

// Input snapshot passed to OnDrive each frame.
public struct VehicleInput
{
	public float Throttle;   // -1 to 1 (forward/back)
	public float Steering;   // -1 to 1 (left/right)
	public float Brake;      //  0 to 1
	public bool  Handbrake;
}
