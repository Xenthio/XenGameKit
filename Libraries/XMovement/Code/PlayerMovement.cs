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
	/// Can we control our movement in the air?
	/// </summary>
	[Property, Group( "Config" )] public float AirControl { get; set; } = 30f;

	/// <summary>
	/// Maximum wish speed used for the dot-product / addspeed check in air.
	/// Equivalent to Source's GetAirSpeedCap() (hardcoded 30 in HL2/CS).
	/// Keeping this low is what makes strafing feel like Source — you can always
	/// add velocity sideways because the capped component against your current
	/// velocity direction stays small.
	/// </summary>
	[Property, Group( "Config" )] public float AirSpeedCap { get; set; } = 30f;

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
				// Source-correct air acceleration: full wish velocity passed in,
				// AirAccelerate internally caps wishspd to AirSpeedCap for the
				// dot-product check while using full speed for the accel formula.
				AirAccelerate( WishVelocity, AirAcceleration );
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

		ClipVelocityToOutOfBounds();

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

		HandleOutOfBounds();

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
