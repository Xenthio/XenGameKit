using Sandbox;
using System;
namespace XMovement;

public partial class PlayerWalkControllerComplex : Component
{
	/// <summary>
	/// How quickly does the player move by default?
	/// </summary>
	[Property, Group( "Config" )] public float DefaultSpeed { get; set; } = 180.0f;

	[Property, FeatureEnabled( "Running" )] public bool EnableRunning { get; set; } = true;
	/// <summary>
	/// The Input Action that the alternate speed mode is triggered by.
	/// </summary>
	[Property, InputAction, Feature( "Running" )] public string RunAction { get; set; } = "Run";
	/// <summary>
	/// If the player should run by default, pressing the run action will swap to default speed
	/// </summary>
	[Property, Feature( "Running" )] public bool RunByDefault { get; set; } = false;
	/// <summary>
	/// The speed the player moves at while in the alternate speed mode.
	/// </summary>
	[Property, Feature( "Running" )] public float RunSpeed { get; set; } = 320.0f;
	[Sync] public bool IsRunning { get; set; }

	[Property, FeatureEnabled( "Walking" )] public bool EnableWalking { get; set; } = true;
	/// <summary>
	/// The Input Action that the alternate speed mode is triggered by.
	/// </summary>
	[Property, InputAction, Feature( "Walking" )] public string WalkAction { get; set; } = "Walk";
	/// <summary>
	/// The speed the player moves at while in the alternate speed mode.
	/// </summary>
	[Property, Feature( "Walking" )] public float WalkSpeed { get; set; } = 120.0f;
	[Sync] public bool IsWalking { get; set; }

	[Property, FeatureEnabled( "Crouching" )] public bool EnableCrouching { get; set; } = true;

	/// <summary>
	/// The Input Action that crouching is triggered by.
	/// </summary>
	[Property, InputAction, Feature( "Crouching" )] public string CrouchAction { get; set; } = "Duck";
	/// <summary>
	/// The speed the player moves at while crouching.
	/// </summary>
	[Property, Feature( "Crouching" )] public float CrouchSpeed { get; set; } = 80.0f;
	[Sync] public bool IsCrouching { get; set; }

	[Property, FeatureEnabled( "Jumping" )] public bool EnableJumping { get; set; } = true;
	/// <summary>
	/// The Input Action that jumping is triggered by.
	/// </summary>
	[Property, InputAction, Feature( "Jumping" )] public string JumpAction { get; set; } = "Jump";
	/// <summary>
	/// Can the player hold down jump to repeatedly jump?
	/// </summary>
	[Property, Feature( "Jumping" )] public bool AllowPogosticking { get; set; } = false;
	/// <summary>
	/// How powerful is the player's jump?
	/// </summary>
	[Property, Feature( "Jumping" )] public float JumpPower { get; set; } = 268.3281572999747f;

	/// <summary>
	/// Do we want to jump next movement update?
	/// </summary>
	public bool WantsJump { get; set; }
	public Vector3 WishMove { get; private set; }

	public virtual void DoMovement()
	{
		Controller.PrepareMovement();

		BuildWishVelocity();
		BuildInput();

		if ( Controller.IsOnGround && WantsJump && CanJump() ) Jump();

		CheckWater();
		CheckLadder();
		CheckNoclip();

		if ( IsNoclipping )
		{
			DoNoclipMove();
		}
		else if ( IsTouchingLadder )
		{
			LadderMove();
		}
		else if ( IsSwimming )
		{
			SwimMove();
		}
		else
		{
			Controller.Move();
		}

		ResetFrameInput();

		LadderLatchCheck();

		Animate();
	}

	public void Jump()
	{
		Controller.LaunchUpwards( JumpPower );
		BroadcastPlayerJumped();
	}

	/// <summary>
	/// Fired (broadcast to all clients) when this player jumps.
	/// </summary>
	public event Action OnJumped;

	/// <summary>
	/// A network message that lets other users that we've triggered a jump.
	/// </summary>
	[Rpc.Broadcast]
	public void BroadcastPlayerJumped()
	{
		AnimationHelper?.TriggerJump();
		OnJumped?.Invoke();
	}

	private void BuildFrameInput()
	{
		if ( AllowPogosticking && Input.Down( JumpAction ) || (IsInVR && Input.VR.RightHand.ButtonA.IsPressed) ) WantsJump = true;
		else if ( Input.Pressed( JumpAction ) || (IsInVR && Input.VR.RightHand.ButtonA.Delta) ) WantsJump = true;
	}
	private void ResetFrameInput()
	{
		WantsJump = false;
	}
	private void BuildInput()
	{
		var run = Input.Down( RunAction ) || (IsInVR && Input.VR.LeftHand.ButtonA);
		var walk = Input.Down( WalkAction );
		var crouch = Input.Down( CrouchAction ) || (IsInVR && Input.VR.LeftHand.JoystickPress.IsPressed);

		if ( RunByDefault )
			IsRunning = !run && EnableRunning;
		else
			IsRunning = run && EnableRunning;

		IsWalking = walk && EnableWalking;
		IsCrouching = crouch || !CanUncrouch();
	}

	public virtual float GetWishSpeed()
	{
		if ( IsCrouching ) return CrouchSpeed;
		if ( IsRunning ) return RunSpeed;
		if ( IsWalking ) return WalkSpeed;
		return DefaultSpeed;
	}

	public void BuildWishVelocity()
	{
		WishMove = Input.AnalogMove;

		if ( IsInVR ) WishMove = new Vector3( Input.VR.LeftHand.Joystick.Value.y, -Input.VR.LeftHand.Joystick.Value.x );

		var rot = EyeAngles.WithPitch( 0f ).ToRotation();

		var wishDirection = WishMove.Normal * rot;
		wishDirection = wishDirection.WithZ( 0 );

		Controller.WishVelocity = wishDirection * GetWishSpeed();
	}

	public bool CanJump()
	{
		if ( !EnableJumping ) return false;
		if ( IsSwimming ) return false;
		return true;
	}
	public bool CanUncrouch()
	{
		var b = Controller.Height;
		if ( !IsCrouching ) return true;
		Controller.Height = 72;
		var tr = Controller.TraceDirection( Vector3.Zero );
		Controller.Height = b;
		return !tr.Hit;
	}
}
