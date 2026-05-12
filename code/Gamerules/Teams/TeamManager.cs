/// <summary>
/// Handles team assignment for the gamemode. Put this on your gamemode prefab alongside the gamemode component.
/// </summary>
public class TeamManager : Component
{
	[Property] public TeamInfo[] Teams { get; set; } = Array.Empty<TeamInfo>();

	public void AssignTeam( PlayerData playerData )
	{
		if ( !Networking.IsHost || Teams.Length == 0 ) return;

		playerData.TeamIndex = GetBalancedTeamIndex();
		Global.IGamemodeEvents.Post( x => x.OnTeamAssigned( playerData, playerData.TeamIndex ) );
	}

	public void AssignAllTeams()
	{
		if ( !Networking.IsHost ) return;

		foreach ( var pd in PlayerData.All )
			pd.TeamIndex = -1;

		foreach ( var pd in PlayerData.All )
			AssignTeam( pd );
	}

	public int GetBalancedTeamIndex()
	{
		var counts = new int[Teams.Length];

		foreach ( var pd in PlayerData.All )
			if ( pd.TeamIndex >= 0 && pd.TeamIndex < Teams.Length )
				counts[pd.TeamIndex]++;

		int best = -1;
		int bestCount = int.MaxValue;

		for ( int i = 0; i < Teams.Length; i++ )
		{
			int max = Teams[i].MaxPlayers;
			if ( max > 0 && counts[i] >= max ) continue;
			if ( counts[i] >= bestCount ) continue;

			bestCount = counts[i];
			best = i;
		}

		return best >= 0 ? best : 0;
	}

	public TeamInfo GetTeam( int index )
	{
		if ( index < 0 || index >= Teams.Length ) return null;
		return Teams[index];
	}

	public IEnumerable<PlayerData> GetTeamPlayers( int teamIndex ) =>
		PlayerData.All.Where( pd => pd.TeamIndex == teamIndex );

	public bool IsTeamAlive( int teamIndex )
	{
		return GetTeamPlayers( teamIndex ).Any( pd =>
		{
			var player = Game.ActiveScene.GetAll<Player>().FirstOrDefault( p => p.PlayerData == pd );
			return player.IsValid() && !player.IsDead;
		} );
	}
}
