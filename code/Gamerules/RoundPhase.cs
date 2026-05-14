// Well-known Phase string constants for BaseGamemode.
// Using strings instead of an enum means gamemodes can define their own phases
// without touching this file. Just match against these where you need to.
public static class RoundPhase
{
	// Not enough players, or the server is sitting idle.
	public const string WaitingForPlayers = nameof( WaitingForPlayers );

	// Round is about to begin - freeze time, role assignment, countdowns, etc.
	// Players are alive but movement/damage may be suppressed.
	public const string Preparing = nameof( Preparing );

	// Round is live. Damage and movement are on.
	public const string Active = nameof( Active );

	// Round just ended. Short break before the next one starts.
	public const string PostRound = nameof( PostRound );

	// Match is over. Waiting for map change, vote, or lobby.
	public const string MatchOver = nameof( MatchOver );
}
