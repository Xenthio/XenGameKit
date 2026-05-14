// Base class for all gamemodes. Lives on a prefab that GamemodeManager clones at game start.
//
// You have two extension points:
//   1. Subclass and override the virtual methods for mode-specific logic.
//   2. Add IGamemodeComponent implementors alongside this on the same GameObject
//      for composable, reusable behaviour (killfeed, loadout, time limit, etc.).
//      BaseGamemode discovers and calls them automatically - no registration needed.
public abstract class BaseGamemode : Component, Global.IPlayerEvents
{
	[Property] public float DefaultRespawnDelay { get; set; } = 5f;
	[Property] public bool  AllowFriendlyFire   { get; set; } = false;

	// Spawned client-side when the gamemode activates, destroyed when it ends.
	// Good for mode-specific HUD layers like scoreboards.
	[Property] public GameObject HudPrefab { get; set; }

	[Sync( SyncFlags.FromHost )] public bool IsActive    { get; protected set; }
	[Sync( SyncFlags.FromHost )] public int  RoundNumber { get; protected set; }

	// Current phase name. Use RoundPhase constants or your own strings - anything goes.
	// Synced to all clients. React to changes via IGamemodeComponent.OnPhaseChanged
	// or Global.IGamemodeEvents.OnPhaseChanged.
	[Sync( SyncFlags.FromHost ), Change( nameof( OnPhaseNetworkChanged ) )]
	public string Phase { get; private set; } = RoundPhase.WaitingForPlayers;

	IEnumerable<IGamemodeComponent> GamemodeComponents =>
		Components.GetAll<IGamemodeComponent>( FindMode.EnabledInSelfAndDescendants );

	// Lifecycle

	public virtual void OnGamemodeStart()
	{
		IsActive = true;
		foreach ( var c in GamemodeComponents ) c.OnGamemodeStart();
	}

	public virtual void OnGamemodeEnd()
	{
		IsActive = false;
		foreach ( var c in GamemodeComponents ) c.OnGamemodeEnd();
	}

	// Called when this client becomes the new host after a migration.
	// Re-arm any host-only timers here.
	public virtual void OnHostBecame() { }

	// Phase management

	// Change the current phase. Host-only. Notifies components and the scene.
	protected void SetPhase( string newPhase )
	{
		if ( !Networking.IsHost ) return;
		if ( Phase == newPhase ) return;
		Phase = newPhase;
	}

	void OnPhaseNetworkChanged( string oldPhase, string newPhase )
	{
		foreach ( var c in GamemodeComponents ) c.OnPhaseChanged( oldPhase, newPhase );
		Global.IGamemodeEvents.Post( x => x.OnPhaseChanged( oldPhase, newPhase ) );
		OnPhaseChanged( oldPhase, newPhase );
	}

	// Override to react to phase transitions on any client.
	protected virtual void OnPhaseChanged( string oldPhase, string newPhase ) { }

	// Respawn

	// Handle a respawn request. Default: tells GameManager to spawn them now.
	// Override in round-based modes to hold off until next round.
	public virtual void RequestRespawn( PlayerData playerData )
	{
		GameManager.Current?.SpawnPlayerDelayed( playerData );
	}

	// How long PlayerDeathEffect waits before calling RequestRespawn.
	// Override per-player if you want VIPs or certain roles to respawn faster.
	public virtual float GetRespawnDelay( PlayerData playerData ) => DefaultRespawnDelay;

	// Spawn location

	// Return a spawn transform for this player, or null to use the default logic.
	public virtual Transform? GetSpawnLocation( PlayerData playerData ) => null;

	// Loadout

	// Called right after a player spawns. Delegates to LoadoutComponent if one is present,
	// then calls OnEquipPlayer so subclasses can layer on top.
	public void EquipPlayer( Player player )
	{
		Components.Get<LoadoutComponent>( FindMode.EnabledInSelfAndDescendants )?.GiveLoadout( player );
		OnEquipPlayer( player );
	}

	// Override to give weapons or items beyond what LoadoutComponent provides.
	protected virtual void OnEquipPlayer( Player player ) { }

	// Damage

	// Return false to block damage. Default: blocks friendly fire when AllowFriendlyFire is off.
	public virtual bool CanDamage( Player attacker, Player victim )
	{
		if ( attacker == victim ) return true;
		return AllowFriendlyFire || !IsOnSameTeam( attacker, victim );
	}

	protected static bool IsOnSameTeam( Player a, Player b )
	{
		if ( !a.PlayerData.IsValid() || !b.PlayerData.IsValid() ) return false;
		return a.PlayerData.TeamIndex >= 0 && a.PlayerData.TeamIndex == b.PlayerData.TeamIndex;
	}

	// Global.IPlayerEvents

	void Global.IPlayerEvents.OnPlayerDied( Player player, PlayerDiedParams args )
	{
		foreach ( var c in GamemodeComponents ) c.OnPlayerDied( player, args );
		OnPlayerDied( player, args );
	}

	void Global.IPlayerEvents.OnPlayerSpawned( Player player )
	{
		foreach ( var c in GamemodeComponents ) c.OnPlayerSpawned( player );
		OnPlayerSpawned( player );
	}

	void Global.IPlayerEvents.OnPlayerDamaging( PlayerDamageEvent e ) => OnPlayerDamaging( e );

	protected virtual void OnPlayerDied( Player player, PlayerDiedParams args ) { }
	protected virtual void OnPlayerSpawned( Player player ) { }

	protected virtual void OnPlayerDamaging( PlayerDamageEvent e )
	{
		if ( e.Cancelled ) return;

		var attacker = Game.ActiveScene.GetAll<Player>()
			.FirstOrDefault( p => p.PlayerId == e.DamageInfo.InstigatorId );

		if ( attacker.IsValid() && attacker != e.Player && !CanDamage( attacker, e.Player ) )
			e.Cancelled = true;
	}
}
