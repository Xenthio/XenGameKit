/// <summary>
/// Base class for all gamemodes. Lives on a prefab that <see cref="GamemodeManager"/> clones at game start.
///
/// <b>Extending:</b>
/// - Override virtual methods for mode-specific logic.
/// - Add <see cref="IGamemodeComponent"/> implementors alongside this component on the same GameObject
///   for composable, reusable behaviour (killfeed, loadout, time limit, etc.).
///   BaseGamemode discovers and calls them automatically.
/// </summary>
public abstract class BaseGamemode : Component, Global.IPlayerEvents
{
	[Property] public float DefaultRespawnDelay { get; set; } = 5f;
	[Property] public bool  AllowFriendlyFire   { get; set; } = false;

	/// <summary>Spawned client-side when the gamemode activates, destroyed when it ends.</summary>
	[Property] public GameObject HudPrefab { get; set; }

	[Sync( SyncFlags.FromHost )] public bool IsActive    { get; protected set; }
	[Sync( SyncFlags.FromHost )] public int  RoundNumber { get; protected set; }

	/// <summary>
	/// Current phase name. Use <see cref="RoundPhase"/> constants or your own strings.
	/// Synced to all clients. React to changes via <see cref="IGamemodeComponent.OnPhaseChanged"/>
	/// or watch this property directly.
	/// </summary>
	[Sync( SyncFlags.FromHost ), Change( nameof( OnPhaseNetworkChanged ) )]
	public string Phase { get; private set; } = RoundPhase.WaitingForPlayers;

	// ── Backward-compat shim so existing TDM/FFA code still compiles ─────────────
	[Obsolete( "Use Phase string. See RoundPhase constants." )]
	public RoundState RoundState
	{
		get => Phase switch
		{
			RoundPhase.Preparing         => RoundState.PreRound,
			RoundPhase.Active            => RoundState.Active,
			RoundPhase.PostRound         => RoundState.PostRound,
			RoundPhase.MatchOver         => RoundState.MatchOver,
			RoundPhase.WaitingForPlayers => RoundState.PreRound,
			_                            => RoundState.PreRound,
		};
		protected set => SetPhase( value switch
		{
			RoundState.PreRound  => RoundPhase.Preparing,
			RoundState.Active    => RoundPhase.Active,
			RoundState.PostRound => RoundPhase.PostRound,
			RoundState.MatchOver => RoundPhase.MatchOver,
			_                    => RoundPhase.WaitingForPlayers,
		} );
	}
	// ─────────────────────────────────────────────────────────────────────────────

	IEnumerable<IGamemodeComponent> GamemodeComponents =>
		Components.GetAll<IGamemodeComponent>( FindMode.EnabledInSelfAndDescendants );

	// ── Lifecycle ─────────────────────────────────────────────────────────────────

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

	/// <summary>Called when this client becomes the new host after migration. Re-arm host-only timers here.</summary>
	public virtual void OnHostBecame() { }

	// ── Phase management ──────────────────────────────────────────────────────────

	/// <summary>
	/// Change the current phase. Host-only. Notifies all <see cref="IGamemodeComponent"/>s.
	/// </summary>
	protected void SetPhase( string newPhase )
	{
		if ( !Networking.IsHost ) return;
		if ( Phase == newPhase ) return;
		Phase = newPhase;
		// OnPhaseNetworkChanged fires on all clients via [Change], including host
	}

	void OnPhaseNetworkChanged( string oldPhase, string newPhase )
	{
		foreach ( var c in GamemodeComponents ) c.OnPhaseChanged( oldPhase, newPhase );
		Global.IGamemodeEvents.Post( x => x.OnPhaseChanged( oldPhase, newPhase ) );
		OnPhaseChanged( oldPhase, newPhase );
	}

	/// <summary>Override to react to phase transitions on any client.</summary>
	protected virtual void OnPhaseChanged( string oldPhase, string newPhase ) { }

	// ── Respawn ───────────────────────────────────────────────────────────────────

	/// <summary>
	/// Handle a respawn request. Default: delegates to GameManager immediately.
	/// Override in round-based modes to hold off until next round.
	/// </summary>
	public virtual void RequestRespawn( PlayerData playerData )
	{
		GameManager.Current?.SpawnPlayerDelayed( playerData );
	}

	/// <summary>
	/// How long <see cref="PlayerDeathEffect"/> waits before calling <see cref="RequestRespawn"/>.
	/// Override per-player (e.g. VIPs respawn faster) or return 0 for instant respawn.
	/// </summary>
	public virtual float GetRespawnDelay( PlayerData playerData ) => DefaultRespawnDelay;

	// ── Spawn location ────────────────────────────────────────────────────────────

	/// <summary>Return a spawn transform, or null to use default logic.</summary>
	public virtual Transform? GetSpawnLocation( PlayerData playerData ) => null;

	// ── Loadout ───────────────────────────────────────────────────────────────────

	/// <summary>
	/// Called right after a player spawns. Delegates to <see cref="LoadoutComponent"/> if present,
	/// then calls <see cref="OnEquipPlayer"/> for subclass customisation.
	/// </summary>
	public void EquipPlayer( Player player )
	{
		Components.Get<LoadoutComponent>( FindMode.EnabledInSelfAndDescendants )?.GiveLoadout( player );
		OnEquipPlayer( player );
	}

	/// <summary>Override to give additional weapons/items beyond what <see cref="LoadoutComponent"/> provides.</summary>
	protected virtual void OnEquipPlayer( Player player ) { }

	// ── Damage ────────────────────────────────────────────────────────────────────

	/// <summary>Return false to block the damage. Default: blocks FF when disabled.</summary>
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

	// ── Global.IPlayerEvents ──────────────────────────────────────────────────────

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
