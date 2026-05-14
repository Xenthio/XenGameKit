// Drop this alongside a BaseGamemode to get kill notifications.
// The host listens for deaths and broadcasts entries to all clients.
// Your HUD reads KillfeedComponent.Entries directly.
//
// Entries automatically expire after EntryLifetime seconds.
// The list clears when the gamemode starts or ends.
public class KillfeedComponent : Component, IGamemodeComponent
{
	[Property, Range( 4, 32 )] public int   MaxEntries    { get; set; } = 8;
	[Property]                 public float EntryLifetime { get; set; } = 6f;

	// A single kill notification. Age tracks how long ago it was received.
	public record Entry( string KillerName, string VictimName, string WeaponName, RealTimeSince Age );

	// Most-recent entries first. HUD reads this directly.
	public IReadOnlyList<Entry> Entries => _entries;

	readonly List<Entry> _entries = new();

	void IGamemodeComponent.OnGamemodeStart() => _entries.Clear();
	void IGamemodeComponent.OnGamemodeEnd()   => _entries.Clear();

	void IGamemodeComponent.OnPlayerDied( Player player, PlayerDiedParams args )
	{
		if ( !Networking.IsHost ) return;

		var killer     = args.KillerPlayer;
		var killerName = killer.IsValid() ? killer.PlayerData?.DisplayName ?? "?" : "World";
		var victimName = player.PlayerData?.DisplayName ?? "?";
		var weaponName = args.Attacker?.Name ?? "";

		BroadcastEntry( killerName, victimName, weaponName );
	}

	protected override void OnUpdate()
	{
		if ( EntryLifetime <= 0 ) return;
		_entries.RemoveAll( e => e.Age > EntryLifetime );
	}

	// Host fires this - it runs on all clients via Broadcast.
	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void BroadcastEntry( string killerName, string victimName, string weaponName )
	{
		_entries.Insert( 0, new Entry( killerName, victimName, weaponName, new RealTimeSince() ) );

		while ( _entries.Count > MaxEntries )
			_entries.RemoveAt( _entries.Count - 1 );
	}
}
