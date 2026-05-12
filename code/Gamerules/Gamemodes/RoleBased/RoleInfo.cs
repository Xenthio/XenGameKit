public sealed class RoleInfo
{
	public string RoleName  { get; init; } = "Unknown";
	public Color  RoleColor { get; init; } = Color.White;
	public int    MaxPlayers { get; init; } = -1; // -1 = unlimited
	public bool   IsSecret  { get; init; } = false;
}
