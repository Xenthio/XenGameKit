/// <summary>
/// Composable killfeed: add this alongside a <see cref="BaseGamemode"/> to get
/// player-kill notifications. Entries are broadcast from the host and rendered
/// by whatever HUD component reads <see cref="Entries"/>.
///
/// <b>Networking:</b> all entry data flows host → all clients via
/// <see cref="Rpc.Broadcast"/>. No client-side prediction — the host is authoritative.
/// </summary>
public class KillfeedComponent : Component, IGamemodeComponent
{
	/// <summary>Maximum entries kept in the local display list.</summary>
	[Property, Range( 4, 32 )] public int MaxEntries { get; set; } = 8;

	/// <summary>Seconds before an entry fades out. 0 = stay forever.</summary>
	[Property] public float EntryLifetime { get; set; } = 6f;

	// ── Public read-only view for HUD ─────────────────────────────────────────────

	public record Entry( string KillerName, string VictimName, string WeaponName, RealTimeSince Age );

	/// <summary>Most-recent entries first. Read this from your HUD component.</summary>
	public IReadOnlyList<Entry> Entries => _entries;

	readonly List<Entry> _entries = new();

	// ── IGamemodeComponent ────────────────────────────────────────────────────────

	void IGamemodeComponent.OnGamemodeStart() => _entries.Clear();
	void IGamemodeComponent.OnGamemodeEnd()   => _entries.Clear();

	void IGamemodeComponent.OnPlayerDied( Player player, PlayerDiedParams args )
	{
		if ( !Networking.IsHost ) return;

		var killer = args.KillerPlayer;
		var killerName = killer.IsValid() ? killer.PlayerData?.DisplayName ?? "?" : "World";
		var victimName = player.PlayerData?.DisplayName ?? "?";
		var weaponName = args.Attacker?.Name ?? "";

		BroadcastEntry( killerName, victimName, weaponName );
	}

	// ── Update: prune stale entries client-side ───────────────────────────────────

	protected override void OnUpdate()
	{
		if ( EntryLifetime <= 0 ) return;
		_entries.RemoveAll( e => e.Age > EntryLifetime );
	}

	// ── RPC: host → all clients ───────────────────────────────────────────────────

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void BroadcastEntry( string killerName, string victimName, string weaponName )
	{
		_entries.Insert( 0, new Entry( killerName, victimName, weaponName, new RealTimeSince() ) );

		// Trim to cap
		while ( _entries.Count > MaxEntries )
			_entries.RemoveAt( _entries.Count - 1 );
	}
}
