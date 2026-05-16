// Simple arcade car. Add this alongside a Rigidbody on a vehicle GameObject.
// No real wheel simulation — forces are applied directly to the body,
// giving that classic GoldSrc/early-Source car feel.
//
// For a proper wheeled vehicle (suspensions, grip curves), subclass BaseVehicle
// and use s&box's WheelCollider when Facepunch ships it.

public class ArcadeCar : BaseVehicle
{
	[Property, Group( "Driving" )] public float EngineForce    { get; set; } = 800f;
	[Property, Group( "Driving" )] public float TurnSpeed      { get; set; } = 90f;  // degrees/sec
	[Property, Group( "Driving" )] public float BrakeForce     { get; set; } = 600f;
	[Property, Group( "Driving" )] public float MaxSpeed        { get; set; } = 900f;  // units/sec
	[Property, Group( "Driving" )] public float AirControl     { get; set; } = 0.2f;
	[Property, Group( "Driving" )] public float DownForce      { get; set; } = 600f;

	Rigidbody _body;

	protected override void OnStart()
	{
		base.OnStart();
		_body = GetComponent<Rigidbody>();
	}

	protected override void OnDrive( VehicleInput input )
	{
		if ( !_body.IsValid() ) return;

		var vel       = _body.Velocity;
		var forward   = WorldRotation.Forward;
		bool grounded = IsGrounded();

		// Downforce — keeps us on the road
		if ( grounded )
			_body.ApplyForce( Vector3.Down * DownForce * _body.Mass );

		// Throttle — only effective below max speed
		if ( grounded && MathF.Abs( input.Throttle ) > 0.01f )
		{
			float speed = vel.Dot( forward );
			if ( MathF.Abs( speed ) < MaxSpeed )
				_body.ApplyForce( forward * input.Throttle * EngineForce * _body.Mass );
		}

		// Braking
		if ( input.Brake > 0.01f && grounded )
		{
			var flatVel = vel.WithZ( 0 );
			_body.ApplyForce( -flatVel.Normal * input.Brake * BrakeForce * _body.Mass );
		}

		// Handbrake — kills lateral velocity
		if ( input.Handbrake && grounded )
		{
			var right   = WorldRotation.Right;
			var lateral = vel.Dot( right );
			_body.ApplyForce( -right * lateral * BrakeForce * _body.Mass );
		}

		// Steering — rotate the body when moving
		if ( grounded && MathF.Abs( input.Steering ) > 0.01f )
		{
			float speedFactor = MathX.Clamp01( vel.WithZ( 0 ).Length / 100f );
			float turn        = input.Steering * TurnSpeed * speedFactor * Time.Delta;

			// Flip turn direction when reversing
			if ( vel.Dot( forward ) < 0 ) turn = -turn;

			WorldRotation = WorldRotation * Rotation.FromYaw( turn );
		}
	}

	bool IsGrounded()
	{
		var tr = Scene.Trace
			.Ray( WorldPosition, WorldPosition + Vector3.Down * 24f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "player" )
			.Run();
		return tr.Hit;
	}
}
