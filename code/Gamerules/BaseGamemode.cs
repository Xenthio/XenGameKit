/// <summary>
/// Base class for all gamemodes. Lives on a prefab that <see cref="GamemodeManager"/> clones at game start.
/// Override only what you need — everything has a sensible default.
/// </summary>
public abstract class BaseGamemode : Component, Global.IPlayerEvents
{
	[Property] public float DefaultRespawnDelay { get; set; } = 5f;
	[Property] public bool AllowFriendlyFire { get; set; } = false;

	/// <summary>
	/// Spawned client-side when the gamemode activates, destroyed when it ends. Good for scoreboards etc.
	/// </summary>
	[Property] public GameObject HudPrefab { get; set; }

	[Sync( SyncFlags.FromHost )] public bool IsActive { get; protected set; }
	[Sync( SyncFlags.FromHost )] public int RoundNumber { get; protected set; }
	[Sync( SyncFlags.FromHost )] public RoundState RoundState { get; protected set; } = RoundState.PreRound;

	public virtual void OnGamemodeStart()
	{
		if ( !Networking.IsHost ) return;
		IsActive = true;
	}

	public virtual void OnGamemodeEnd()
	{
		if ( !Networking.IsHost ) return;
		IsActive = false;
	}

	/// <summary>
	/// Called when this client becomes the new host after a migration. Re-arm any host-only timers here.
	/// </summary>
	public virtual void OnHostBecame() { }

	/// <summary>
	/// Return false to block the damage. Default: blocks friendly fire when <see cref="AllowFriendlyFire"/> is off.
	/// </summary>
	public virtual bool CanDamage( Player attacker, Player victim )
	{
		if ( attacker == victim ) return true;
		return AllowFriendlyFire || !IsOnSameTeam( attacker, victim );
	}

	/// <summary>
	/// Return a spawn transform for this player, or null to use the default spawn logic.
	/// </summary>
	public virtual Transform? GetSpawnLocation( PlayerData playerData ) => null;

	/// <summary>
	/// Handle a respawn request. Default: delegates to GameManager. Override in round-based modes to hold off until next round.
	/// </summary>
	public virtual void RequestRespawn( PlayerData playerData )
	{
		GameManager.Current?.SpawnPlayerDelayed( playerData );
	}

	/// <summary>
	/// How long <see cref="PlayerDeathEffect"/> waits before calling <see cref="RequestRespawn"/>.
	/// Override per-player (e.g. VIPs respawn faster) or return 0 for instant respawn.
	/// Defaults to <see cref="DefaultRespawnDelay"/>.
	/// </summary>
	public virtual float GetRespawnDelay( PlayerData playerData ) => DefaultRespawnDelay;

	/// <summary>
	/// Called right after a player spawns. Give them their starting loadout here.
	/// </summary>
	public virtual void EquipPlayer( Player player ) { }

	protected static bool IsOnSameTeam( Player a, Player b )
	{
		if ( !a.PlayerData.IsValid() || !b.PlayerData.IsValid() ) return false;
		return a.PlayerData.TeamIndex >= 0 && a.PlayerData.TeamIndex == b.PlayerData.TeamIndex;
	}

	void Global.IPlayerEvents.OnPlayerDied( Player player, PlayerDiedParams args ) => OnPlayerDied( player, args );
	void Global.IPlayerEvents.OnPlayerSpawned( Player player ) => OnPlayerSpawned( player );
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
