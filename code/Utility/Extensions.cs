/// <summary>
/// General-purpose extension methods used across XenGameKit.
/// </summary>
public static class XenGameKitExtensions
{
	/// <summary>
	/// Randomize a direction vector within a cone of the given half-angle (degrees).
	/// Matches Source SDK's UTIL_WeaponSpread behaviour.
	/// </summary>
	public static Vector3 WithAimCone( this Vector3 direction, float degrees )
	{
		var rotation = Rotation.LookAt( direction );
		rotation *= new Angles(
			Game.Random.Float( -degrees * 0.5f, degrees * 0.5f ),
			Game.Random.Float( -degrees * 0.5f, degrees * 0.5f ),
			0 );
		return rotation.Forward;
	}

	/// <summary>
	/// Randomize a direction vector within an asymmetric cone (separate horizontal and vertical spread).
	/// </summary>
	public static Vector3 WithAimCone( this Vector3 direction, float horizontalDegrees, float verticalDegrees )
	{
		var rotation = Rotation.LookAt( direction );
		rotation *= new Angles(
			Game.Random.Float( -verticalDegrees   * 0.5f, verticalDegrees   * 0.5f ),
			Game.Random.Float( -horizontalDegrees * 0.5f, horizontalDegrees * 0.5f ),
			0 );
		return rotation.Forward;
	}
}
