using Sandbox;
namespace XMovement;

public partial class PlayerWalkControllerComplex : Component
{
	// this is a kinda crappy way to do presets.

	/// <summary>
	/// Values from popular games.
	/// </summary>
	[Property, Group( "Quick Presets" ), Change( "SetupFromPreset" )] public MovementPresets MovementPreset { get; set; }

	private void SetupFromPreset()
	{
		if ( MovementPreset == MovementPresets.None ) return;
		switch ( MovementPreset )
		{
			case MovementPresets.None:
				break;
			case MovementPresets.HalfLife:
				Controller.Gravity = new Vector3( 0, 0, 800 );
				Controller.BaseFriction = 4f;
				Controller.StopSpeed = 100f;
				Controller.BaseAcceleration = 10f;
				Controller.AirAcceleration = 10f;

				EnableWalking = false;

				DefaultSpeed = 120f;

				RunByDefault = true;
				RunSpeed = 320f;

				CrouchSpeed = RunSpeed * 0.333f;

				JumpPower = 268.3281572999747f;
				break;
			case MovementPresets.HalfLife2:
				Controller.Gravity = new Vector3( 0, 0, 600 );
				Controller.BaseFriction = 4f;
				Controller.StopSpeed = 100f;
				Controller.BaseAcceleration = 10f;
				Controller.AirAcceleration = 10f;

				EnableWalking = true;
				WalkSpeed = 150f;

				DefaultSpeed = 190f;

				RunByDefault = false;
				RunSpeed = 320f;

				CrouchSpeed = DefaultSpeed * 0.333f;

				JumpPower = 160f;
				break;
			case MovementPresets.CounterStrikeSource:
				Controller.Gravity = new Vector3( 0, 0, 800 );
				Controller.BaseFriction = 4f;
				Controller.StopSpeed = 100f;
				Controller.BaseAcceleration = 10f;
				Controller.AirAcceleration = 10f;

				EnableWalking = false;

				DefaultSpeed = 100f;

				RunByDefault = true;
				RunSpeed = 320f;

				CrouchSpeed = RunSpeed * 0.34f;

				JumpPower = 268.3281572999747f;
				break;
			case MovementPresets.TroubleInTerroristTown:
				Controller.Gravity = new Vector3( 0, 0, 600 );
				Controller.BaseFriction = 8f;
				Controller.StopSpeed = 10f;
				Controller.BaseAcceleration = 10f;
				Controller.AirAcceleration = 15f; // TTT often uses higher values for more dynamic movement

				EnableWalking = false;

				DefaultSpeed = 120f;

				RunByDefault = true;
				RunSpeed = 220f;

				CrouchSpeed = 66f;

				JumpPower = 160;
				break;
			case MovementPresets.ThreeThievesTroubleInTerroristTown:
				Controller.Gravity = new Vector3( 0, 0, 800 );
				Controller.BaseFriction = 8f;
				Controller.StopSpeed = 10f;
				Controller.BaseAcceleration = 10f;
				Controller.AirAcceleration = 15f; // TTT often uses higher values for more dynamic movement

				EnableWalking = false;

				DefaultSpeed = 120f;

				RunByDefault = true;
				RunSpeed = 220f;

				CrouchSpeed = 66f;

				JumpPower = 268.3281572999747f;
				break;
			default:
				break;
		}
	}
	public enum MovementPresets
	{
		None,
		[Title( "Half-Life" )] HalfLife,
		[Title( "Half-Life 2" )] HalfLife2,
		[Title( "Counter-Strike: Source" )] CounterStrikeSource,
		[Title( "Trouble in Terrorist Town!" )] TroubleInTerroristTown,
		[Title( "Three Thieves: Trouble in Terrorist Town!" )] ThreeThievesTroubleInTerroristTown,
	}
}

