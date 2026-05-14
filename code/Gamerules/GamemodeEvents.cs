// Round/match event data and scene event interfaces.

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

public class RoundStartEvent
{
	public int RoundNumber { get; init; }
}

public class RoundEndEvent
{
	public RoundEndReason Reason     { get; init; }
	public int            WinningTeam { get; init; } // -1 = draw / no teams
}

public class MatchEndEvent
{
	public MatchEndReason Reason      { get; init; }
	public int            WinningTeam { get; init; } // -1 = draw / no teams
}

public static partial class Global
{
	public interface IGamemodeEvents : ISceneEvent<IGamemodeEvents>
	{
		void OnRoundStart( RoundStartEvent e ) { }
		void OnRoundEnd( RoundEndEvent e ) { }
		void OnMatchEnd( MatchEndEvent e ) { }
		void OnTeamAssigned( PlayerData playerData, int teamIndex ) { }
		// Fires on all clients whenever BaseGamemode.Phase changes.
		void OnPhaseChanged( string oldPhase, string newPhase ) { }
	}
}
