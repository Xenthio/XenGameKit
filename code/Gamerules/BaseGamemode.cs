// Base class for all gamemodes in XenGameKit.
//
// To make a new gamemode:
//   1. Subclass this, add [Property] fields for your tunable values.
//   2. Override the virtual hooks you need.
//   3. GamemodeManager discovers it automatically via TypeLibrary — no registration needed.
//
// No prefab asset required. The manager creates a fresh GameObject at runtime,
// adds your component to it, and calls OnGamemodeStart().

public abstract class BaseGamemode : Component, Global.IPlayerEvents
{
	[Property] public float            DefaultRespawnDelay { get; set; } = 5f;
	[Property] public bool             AllowFriendlyFire   { get; set; } = false;
	[Property] public List<GameObject> DefaultWeapons      { get; set; } = new();

	// Spawned client-side when the gamemode activates.
	// Good for mode-specific HUD layers like scoreboards.
	[Property] public GameObject HudPrefab { get; set; }

	[Sync( SyncFlags.FromHost )] public bool   IsActive    { get; protected set; }
	[Sync( SyncFlags.FromHost )] public int    RoundNumber { get; protected set; }

	[Sync( SyncFlags.FromHost ), Change( nameof(OnPhaseSync) )]
	public string Phase { get; private set; } = RoundPhase.WaitingForPlayers;

	// ─── Lifecycle ───────────────────────────────────────────────────────────

	public virtual void OnGamemodeStart() { IsActive = true; }
	public virtual void OnGamemodeEnd()   { IsActive = false; }

	// Re-arm any host-only timers here after a host migration.
	public virtual void OnHostBecame() { }

	// ─── Phase ───────────────────────────────────────────────────────────────

	protected void SetPhase( string phase )
	{
		if ( !Networking.IsHost || Phase == phase ) return;
		Phase = phase;
	}

	void OnPhaseSync( string oldPhase, string newPhase )
	{
		Global.IGamemodeEvents.Post( x => x.OnPhaseChanged( oldPhase, newPhase ) );
		OnPhaseChanged( oldPhase, newPhase );
	}

	// React to phase transitions on any client.
	protected virtual void OnPhaseChanged( string oldPhase, string newPhase ) { }

	// ─── Respawn ─────────────────────────────────────────────────────────────

	// Default: immediate delayed respawn. Override in round-based modes to hold until next round.
	public virtual void RequestRespawn( PlayerData playerData )
		=> GameManager.Current?.SpawnPlayerDelayed( playerData );

	public virtual float GetRespawnDelay( PlayerData playerData ) => DefaultRespawnDelay;

	// ─── Spawn location ──────────────────────────────────────────────────────

	// Return a spawn transform, or null to fall back to the default SpawnPoint logic.
	public virtual Transform? GetSpawnLocation( PlayerData playerData ) => null;

	// ─── Loadout ─────────────────────────────────────────────────────────────

	// Called by GamemodeManager right after a player spawns.
	public void EquipPlayer( Player player )
	{
		if ( !Networking.IsHost || !player.IsValid() ) return;

		var inv = player.Components.Get<PlayerInventory>( FindMode.EnabledInSelfAndDescendants );
		if ( inv is null ) return;

		foreach ( var prefab in GetLoadoutFor( player ) )
			if ( prefab is not null ) inv.Pickup( prefab, notice: false );

		OnEquipPlayer( player );
	}

	// Override to customise weapons per player — by team, role, class, whatever you like.
	protected virtual IEnumerable<GameObject> GetLoadoutFor( Player player ) => DefaultWeapons;

	// Override to give items on top of the base loadout (e.g. a role-specific tool).
	protected virtual void OnEquipPlayer( Player player ) { }

	// ─── Damage ──────────────────────────────────────────────────────────────

	// Return false to block the damage. Default: allows everything except friendly fire.
	public virtual bool CanDamage( Player attacker, Player victim )
	{
		if ( attacker == victim ) return true;
		return AllowFriendlyFire || !SameTeam( attacker, victim );
	}

	protected static bool SameTeam( Player a, Player b )
	{
		if ( !a.PlayerData.IsValid() || !b.PlayerData.IsValid() ) return false;
		return a.PlayerData.TeamIndex >= 0 && a.PlayerData.TeamIndex == b.PlayerData.TeamIndex;
	}

	// ─── Killfeed ────────────────────────────────────────────────────────────

	// Host fires this on death; HUDs listen via IGamemodeEvents.OnKillFeedEntry.
	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	protected void BroadcastKill( string killer, string victim, string weapon )
		=> Global.IGamemodeEvents.Post( x => x.OnKillFeedEntry( killer, victim, weapon ) );

	// ─── Respawn helper ──────────────────────────────────────────────────────

	// Destroys existing player pawns and spawns everyone fresh.
	// Used by round-based modes at the start of each round. Host only.
	protected void RespawnAll()
	{
		if ( !Networking.IsHost ) return;

		foreach ( var pd in PlayerData.All )
		{
			var existing = Scene.GetAll<Player>().FirstOrDefault( p => p.PlayerData == pd );
			if ( existing.IsValid() ) existing.GameObject.Destroy();
			GameManager.Current?.SpawnPlayer( pd );
		}
	}

	// ─── IPlayerEvents ───────────────────────────────────────────────────────

	void Global.IPlayerEvents.OnPlayerDied( Player player, PlayerDiedParams args )
	{
		if ( Networking.IsHost )
		{
			var killer = args.KillerPlayer.IsValid()
				? args.KillerPlayer.PlayerData?.DisplayName ?? "?"
				: "World";
			BroadcastKill( killer, player.PlayerData?.DisplayName ?? "?", args.Attacker?.Name ?? "" );
		}

		OnPlayerDied( player, args );
	}

	void Global.IPlayerEvents.OnPlayerSpawned( Player player ) => OnPlayerSpawned( player );

	void Global.IPlayerEvents.OnPlayerDamaging( PlayerDamageEvent e )
	{
		if ( e.Cancelled ) return;

		var attacker = Game.ActiveScene.GetAll<Player>()
			.FirstOrDefault( p => p.PlayerId == e.DamageInfo.InstigatorId );

		if ( attacker.IsValid() && attacker != e.Player && !CanDamage( attacker, e.Player ) )
			e.Cancelled = true;

		OnPlayerDamaging( e );
	}

	protected virtual void OnPlayerDied( Player player, PlayerDiedParams args ) { }
	protected virtual void OnPlayerSpawned( Player player ) { }
	protected virtual void OnPlayerDamaging( PlayerDamageEvent e ) { }
}
