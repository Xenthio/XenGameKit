/// <summary>
/// Scene singleton that manages the active gamemode. Add it alongside <see cref="GameManager"/> in your scene.
/// Set <see cref="GamemodePrefab"/> in the inspector, or leave it null for a mode-less session.
/// </summary>
public sealed class GamemodeManager : GameObjectSystem<GamemodeManager>, ISceneStartup, Component.INetworkListener,
	GameRulesService.IGameRulesProvider
{
	[Property] public GameObject GamemodePrefab { get; set; }

	public BaseGamemode ActiveGamemode { get; private set; }

	GameObject _activeHud;

	public GamemodeManager( Scene scene ) : base( scene ) { }

	void ISceneStartup.OnHostInitialize()
	{
		if ( !Networking.IsHost ) return;

		// Prefer the gamemode baked into the prefab property (editor/scene authoring).
		// Fall back to the runtime selection stored by PlayModal in LaunchArguments.
		var prefabToUse = GamemodePrefab;

		if ( prefabToUse is null
		     && LaunchArguments.GameSettings is not null
		     && LaunchArguments.GameSettings.TryGetValue( "xgk_gamemode", out var gmPath )
		     && !string.IsNullOrEmpty( gmPath ) )
		{
			prefabToUse = GameObject.GetPrefab( gmPath );
			if ( prefabToUse is null )
				Log.Warning( $"[GamemodeManager] Could not find gamemode prefab '{gmPath}' from LaunchArguments" );
		}

		if ( prefabToUse is not null )
			ActivateGamemodePrefab( prefabToUse );
	}

	void Component.INetworkListener.OnActive( Connection channel ) { }
	void Component.INetworkListener.OnDisconnected( Connection channel ) { }

	void Component.INetworkListener.OnBecameHost( Connection previousHost )
	{
		// Synced state is already restored — just re-wire the reference and let the gamemode know
		ActiveGamemode = Scene.GetAllComponents<BaseGamemode>().FirstOrDefault();
		ActiveGamemode?.OnHostBecame();
	}

	/// <summary>
	/// Called by PlayerDeathEffect when a player needs to respawn. Host-only.
	/// </summary>
	public void RequestRespawn( PlayerData playerData )
	{
		if ( !Networking.IsHost ) return;

		if ( ActiveGamemode is not null )
			ActiveGamemode.RequestRespawn( playerData );
		else
			GameManager.Current?.SpawnPlayerDelayed( playerData );
	}

	/// <summary>
	/// Returns a custom spawn location from the gamemode, or null for the default. Host-only.
	/// </summary>
	public Transform? GetSpawnLocation( PlayerData playerData )
	{
		if ( !Networking.IsHost ) return null;
		return ActiveGamemode?.GetSpawnLocation( playerData );
	}

	/// <summary>
	/// Delegates to the active gamemode to give the player their starting loadout.
	/// </summary>
	public void EquipPlayer( Player player )
	{
		ActiveGamemode?.EquipPlayer( player );
	}

	/// <summary>
	/// Delegates respawn delay to the active gamemode, falling back to 5 seconds.
	/// </summary>
	public float GetRespawnDelay( PlayerData playerData )
	{
		return ActiveGamemode?.GetRespawnDelay( playerData ) ?? 5f;
	}

	/// <summary>
	/// Swap to a different gamemode prefab at runtime. Host-only.
	/// </summary>
	public void SwitchGamemode( GameObject newGamemodePrefab )
	{
		if ( !Networking.IsHost ) return;

		if ( ActiveGamemode.IsValid() )
		{
			ActiveGamemode.OnGamemodeEnd();
			ActiveGamemode.GameObject.Destroy();
			ActiveGamemode = null;
		}

		DestroyHud();

		if ( newGamemodePrefab is not null )
			ActivateGamemodePrefab( newGamemodePrefab );
		else
			GameRulesService.Current = null; // nothing active
	}

	void ActivateGamemodePrefab( GameObject prefab )
	{
		var go = prefab.Clone( new CloneConfig { Transform = Transform.Zero } );
		go.NetworkSpawn( null );
		go.Network.SetOwnerTransfer( OwnerTransfer.Fixed );

		ActiveGamemode = go.Components.Get<BaseGamemode>( true );
		ActiveGamemode?.OnGamemodeStart();

		// Register with the service so the FPS base can reach us without a direct reference
		GameRulesService.Current = this;

		SpawnHud();
	}

	void SpawnHud()
	{
		DestroyHud();
		var hudPrefab = ActiveGamemode?.HudPrefab;
		if ( hudPrefab is null ) return;
		_activeHud = hudPrefab.Clone( new CloneConfig { Transform = Transform.Zero } );
	}

	void DestroyHud()
	{
		if ( !_activeHud.IsValid() ) return;
		_activeHud.Destroy();
		_activeHud = null;
	}
}
