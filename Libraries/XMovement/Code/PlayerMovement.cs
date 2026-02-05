using Sandbox;

namespace XMovement;

public partial class PlayerMovement : Component
{
	/// <summary>
	/// The current gravity.
	/// </summary>
	[Property, Group( "Config" )] public Vector3 Gravity { get; set; } = new Vector3( 0, 0, 800 );

	/// <summary>
	/// How much friction does the player have?
	/// </summary>
	[Property, Group( "Friction" )] public float BaseFriction { get; set; } = 4.0f;

	/// <summary>
	/// The speed at which we fully come to a stop.
	/// </summary>
	[Property, Group( "Friction" )] public float StopSpeed { get; set; } = 100.0f;

	/// <summary>
	/// Maximum speed cap for air movement calculations (similar to Source Engine's GetAirSpeedCap).
	/// This limits how much speed can be gained per frame in the air, while still allowing
	/// full acceleration rate. Default is 30, matching Source Engine.
	/// </summary>
	[Property, Group( "Config" )] public float AirControl { get; set; } = 30f;

	[Property, Group( "Acceleration" )] public float AirAcceleration { get; set; } = 10f;

	[Property, Group( "Acceleration" )] public float BaseAcceleration { get; set; } = 10;
	[Property] public MovementFrequencyMode MovementFrequency { get; set; } = MovementFrequencyMode.PerFixedUpdate;
	public enum MovementFrequencyMode
	{
		PerFixedUpdate,
		PerUpdate
	}
	[Sync] public Vector3 WishVelocity { get; set; }
	protected override void OnStart()
	{
		base.OnStart();
		Tags.Add( "player" );
		CreateShadowObjects();
	}

	public void PrepareMovement()
	{
		UpdateFromSimulatedShadow();
	}

	/// <summary>
	/// Move a character, with this velocity
	/// </summary>
	public void Move( bool withWishVelocity = true, bool withGravity = true, float frictionOverride = 0 )
	{
		RestoreGroundPos();
		ApplyAcceleration();

		// Start gravity
		if ( !IsOnGround && withGravity )
			Velocity -= Gravity * Time.Delta * 0.5f;

		if ( withWishVelocity )
		{
			if ( IsOnGround )
			{
				Accelerate( WishVelocity, BaseAcceleration );
			}
			else
			{
				// Air movement: Apply speed cap to the gain limit, not the acceleration rate
				ApplyAirMovement( WishVelocity, AirAcceleration, AirControl );
			}
		}

		if ( frictionOverride > 0 )
		{
			Velocity = ApplyFriction( Velocity, frictionOverride, StopSpeed );
		}
		else if ( IsOnGround )
		{
			Velocity = ApplyFriction( Velocity, GetFriction(), StopSpeed );
		}


		if ( TryUnstuck() )
			return;

		if ( IsOnGround )
		{
			Move( true );
		}
		else
		{
			Move( false );
		}

		if ( IsOnGround ) StayOnGround();

		CategorizePosition();

		// Finish gravity
		if ( !IsOnGround && withGravity )
			Velocity -= Gravity * Time.Delta * 0.5f;

		ResetSimulatedShadow();
		SaveGroundPos();
		PreviousPosition = WorldPosition;
	}

	private void ApplyAcceleration()
	{
		if ( !IsOnGround ) Acceleration = AirAcceleration;
		else Acceleration = BaseAcceleration;
	}

	/// <summary>
	/// Get the current friction.
	/// </summary>
	/// <returns></returns>
	private float GetFriction()
	{
		return BaseFriction;
	}
}
