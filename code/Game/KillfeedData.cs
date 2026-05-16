// Static data bus between game code and the killfeed HUD.
// BaseGamemode writes here; KillfeedDisplay reads from here.
// Keeps game code isolated from UI types.
public static class KillfeedData
{
	public record Entry( string Killer, string Victim, string Weapon, RealTimeSince Age );

	const int MaxEntries = 8;

	public static IReadOnlyList<Entry> Entries => _entries;
	static readonly List<Entry> _entries = new();

	// Call this when the scene is torn down (GamemodeManager.OnGamemodeEnd, map change, etc.)
	public static void Clear() => _entries.Clear();

	public static void Add( string killer, string victim, string weapon )
	{
		_entries.Insert( 0, new Entry( killer, victim, weapon, new RealTimeSince() ) );
		while ( _entries.Count > MaxEntries )
			_entries.RemoveAt( _entries.Count - 1 );
	}

	public static void Prune( float lifetime )
	{
		_entries.RemoveAll( e => e.Age > lifetime );
	}
}
