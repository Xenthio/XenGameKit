using Sandbox;
using System;
namespace XMovement;

public partial class PlayerWalkControllerComplex : Component
{
	[Property, FeatureEnabled( "Swimming" )] public bool EnableSwimming { get; set; } = true;
	[Property, Feature( "Swimming" )] public string WaterTag { get; set; } = "water";
	[Property, Feature( "Swimming" )] public float SwimmingSpeedScale { get; set; } = 0.8f;
	[Property, Feature( "Swimming" )] public float SwimmingFriction { get; set; } = 4;
	[Property, InputAction, Feature( "Swimming" )] public string SwimUpAction { get; set; } = "Jump";
	[Property, InputAction, Feature( "Swimming" )] public string SwimDownAction { get; set; } = "";
	private bool IsSwimming => WaterLevel > 0.5f;
	float WaterLevel = 0;
	public virtual void CheckWater()
	{
		if ( !EnableSwimming ) { WaterLevel = 0; return; }

		// Use a point trace from foot to head — same as Source.
		// A box trace at corners of a water trigger gives wrong results because
		// the box clips outside the trigger, shrinking the fraction prematurely.
		var foot = WorldPosition;
		var head = WorldPosition + Vector3.Up * Controller.Height;

		var pm = Scene.Trace.Ray( head, foot )
					.WithTag( WaterTag )
					.HitTriggers()
					.IgnoreGameObjectHierarchy( GameObject )
					.Run();
		WaterLevel = 1 - pm.Fraction;

		if ( WaterLevel > 0.1f )
		{
			if ( WaterLevel > 0.4f )
			{
				CheckWaterJump();
			}
			// If we are falling again, then we must not trying to jump out of water any more.
			if ( (Controller.Velocity.z < 0.0f) && IsJumpingFromWater )
			{
				WaterJumpTime = 0.0f;
			}
		}
	}

	public virtual void SwimMove()
	{
		Controller.IsOnGround = false;

		// Build wish velocity from full 3D view angles (pitch included), like Source WaterMove.
		// This means looking down swims you down, looking up swims you up.
		// Normalize before scaling so diagonal input (forward+strafe) doesn't exceed 1x speed.
		// HL2 does VectorNormalize(wishdir) before capping - without this you get ~1.41x speed diagonally.
		var wishvel = WishMove * EyeAngles.ToRotation();
		var wishspeed = wishvel.Length;
		if ( wishspeed > 0 ) wishvel = wishvel.Normal;
		wishspeed = MathF.Min( wishspeed, 1f ) * GetWishSpeed() * SwimmingSpeedScale;
		wishvel *= wishspeed;

		// Jump key moves up; no input sinks at 60 u/s (Source behaviour)
		if ( Input.Down( SwimUpAction ) )
		{
			wishvel = wishvel.WithZ( wishvel.z + GetWishSpeed() * SwimmingSpeedScale );
		}
		else if ( WishMove.IsNearlyZero() )
		{
			wishvel = wishvel.WithZ( wishvel.z - 60f );
		}

		if ( !string.IsNullOrEmpty( SwimDownAction ) && Input.Down( SwimDownAction ) )
			wishvel = wishvel.WithZ( -GetWishSpeed() * SwimmingSpeedScale );

		Controller.WishVelocity = wishvel;

		// Water friction: proportional speed bleed before acceleration (Source WaterMove).
		// Unlike ground friction this doesn't use stop-speed — it's a straight percentage.
		var speed = Controller.Velocity.Length;
		if ( speed > 0 )
		{
			var newspeed = speed - Time.Delta * speed * SwimmingFriction * Controller.SurfaceFriction;
			if ( newspeed < 0.1f ) newspeed = 0;
			Controller.Velocity *= newspeed / speed;
		}

		// Accelerate up to wish speed (standard Accelerate, no special air cap needed in water)
		Controller.Acceleration = Controller.BaseAcceleration;
		Controller.Accelerate( wishvel );

		// Move without applying wish velocity a second time or gravity
		Controller.Move( withWishVelocity: false, withGravity: false );
	}

	protected float WaterJumpTime { get; set; }
	protected Vector3 WaterJumpVelocity { get; set; }
	protected bool IsJumpingFromWater => WaterJumpTime > 0;
	public virtual float WaterJumpHeight => 8;
	protected void CheckWaterJump()
	{
		// Already water jumping.
		if ( IsJumpingFromWater )
			return;

		// Don't hop out if we just jumped in
		// only hop out if we are moving up
		if ( Controller.Velocity.z < -180 )
			return;

		// See if we are backing up
		var flatvelocity = Controller.Velocity.WithZ( 0 );

		// Must be moving
		var curspeed = flatvelocity.Length;
		flatvelocity = flatvelocity.Normal;

		// see if near an edge
		var flatforward = Head.WorldRotation.Forward.WithZ( 0 ).Normal;

		// Are we backing into water from steps or something?  If so, don't pop forward
		if ( curspeed != 0 && Vector3.Dot( flatvelocity, flatforward ) < 0 )
			return;

		var vecStart = WorldPosition + (Controller.BoundingBox.Mins + Controller.BoundingBox.Maxs) * .5f;
		var vecEnd = vecStart + flatforward * 24;

		var tr = Controller.BuildTrace( vecStart, vecEnd ).Run();
		if ( tr.Fraction == 1 )
			return;

		vecStart.z = WorldPosition.z + HeadHeight + WaterJumpHeight;
		vecEnd = vecStart + flatforward * 24;
		WaterJumpVelocity = tr.Normal * -50;

		tr = Controller.BuildTrace( vecStart, vecEnd ).Run();
		if ( tr.Fraction < 1.0 )
			return;

		// Now trace down to see if we would actually land on a standable surface.
		vecStart = vecEnd;
		vecEnd.z -= 1024;

		tr = Controller.BuildTrace( vecStart, vecEnd ).Run();
		if ( tr.Fraction < 1 && tr.Normal.z >= 0.7f )
		{
			Controller.Velocity += new Vector3( 0, 0, 256 ) * WorldScale;
			WaterJumpTime = 2000;
		}
	}
}
