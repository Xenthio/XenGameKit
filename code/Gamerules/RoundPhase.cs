/// <summary>
/// Well-known <see cref="BaseGamemode.Phase"/> string constants.
/// Gamemodes are free to define their own additional phase names — just use string constants.
/// Prefer these over raw strings so consumers can switch/compare without typos.
/// </summary>
public static class RoundPhase
{
	/// <summary>Not enough players connected, or server is idle between sessions.</summary>
	public const string WaitingForPlayers = nameof( WaitingForPlayers );

	/// <summary>
	/// Round is about to start — freeze time, countdown, role/team assignment.
	/// Players are alive but movement and damage may be suppressed.
	/// </summary>
	public const string Preparing = nameof( Preparing );

	/// <summary>Round is live. Damage and movement are enabled.</summary>
	public const string Active = nameof( Active );

	/// <summary>Round just ended. Short intermission before the next round starts.</summary>
	public const string PostRound = nameof( PostRound );

	/// <summary>Match is over. Waiting for a vote, map change, or lobby return.</summary>
	public const string MatchOver = nameof( MatchOver );
}
