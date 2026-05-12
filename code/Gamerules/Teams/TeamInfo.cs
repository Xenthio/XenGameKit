public class TeamInfo
{
	[Property] public string TeamName { get; set; } = "Team";
	[Property] public Color TeamColor { get; set; } = Color.White;

	/// <summary>
	/// Max players on this team. 0 = unlimited.
	/// </summary>
	[Property] public int MaxPlayers { get; set; } = 0;
}
