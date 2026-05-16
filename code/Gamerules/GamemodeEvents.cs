// Round/match event data and the scene event interface.

public enum RoundEndReason
{
	TimeLimit,
	TeamEliminated,
	ScoreLimit,
	ForceEnd,
}

public enum MatchEndReason
{
	RoundLimit,
	ScoreLimit,
	TimeLimit,
	ForceEnd,
}

public class RoundStartEvent { public int RoundNumber { get; init; } }
public class RoundEndEvent   { public RoundEndReason Reason { get; init; } public int WinningTeam { get; init; } }
public class MatchEndEvent   { public MatchEndReason Reason { get; init; } public int WinningTeam { get; init; } }

public static partial class Global
{
	public interface IGamemodeEvents : ISceneEvent<IGamemodeEvents>
	{
		void OnRoundStart( RoundStartEvent e ) { }
		void OnRoundEnd( RoundEndEvent e ) { }
		void OnMatchEnd( MatchEndEvent e ) { }
		void OnPhaseChanged( string oldPhase, string newPhase ) { }
		void OnTeamAssigned( PlayerData playerData, int teamIndex ) { }

		// Fires on all clients for every kill. Implement on your HUD to display a killfeed.
		void OnKillFeedEntry( string killerName, string victimName, string weaponName ) { }
	}
}
