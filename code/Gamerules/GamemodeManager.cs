/// <summary>
/// Scene singleton. Manages the active gamemode — creates it, destroys it, delegates events.
///
/// Set GamemodeType in the inspector (or leave blank for a typeless session).
/// Gamemodes are discovered automatically via TypeLibrary; no registration needed.
/// </summary>
public sealed class GamemodeManager : GameObjectSystem<GamemodeManager>, ISceneStartup, Component.INetworkListener,
	GameRulesService.IGameRulesProvider
{
	// Convenient scene-singleton accessor.
	public static GamemodeManager Instance => GamemodeManager.Current;
	// Set this in the scene to the fully-qualified type name of your gamemode,
	// e.g. "FFAGamemode". Overridden at runtime by LaunchArguments (xgk_gamemode).
	[Property] public string GamemodeType { get; set; }

	public BaseGamemode ActiveGamemode { get; private set; }

	GameObject _activeHud;

	public GamemodeManager( Scene scene ) : base( scene ) { }

	void ISceneStartup.OnHostInitialize()
	{
		if ( !Networking.IsHost ) return;

		var typeName = GamemodeType;

		if ( LaunchArguments.GameSettings is not null
		     && LaunchArguments.GameSettings.TryGetValue( "xgk_gamemode", out var arg )
		     && !string.IsNullOrWhiteSpace( arg ) )
		{
			typeName = arg;
		}

		if ( !string.IsNullOrWhiteSpace( typeName ) )
			ActivateGamemode( typeName );
	}

	void Component.INetworkListener.OnActive( Connection channel ) { }
	void Component.INetworkListener.OnDisconnected( Connection channel ) { }

	void Component.INetworkListener.OnBecameHost( Connection previousHost )
	{
		ActiveGamemode = Scene.GetAllComponents<BaseGamemode>().FirstOrDefault();
		ActiveGamemode?.OnHostBecame();
	}

	// ─── GameRulesService ────────────────────────────────────────────────────

	public void RequestRespawn( PlayerData playerData )
	{
		if ( !Networking.IsHost ) return;
		if ( ActiveGamemode is not null ) ActiveGamemode.RequestRespawn( playerData );
		else GameManager.Current?.SpawnPlayerDelayed( playerData );
	}

	public Transform? GetSpawnLocation( PlayerData playerData )
	{
		if ( !Networking.IsHost ) return null;
		return ActiveGamemode?.GetSpawnLocation( playerData );
	}

	public void EquipPlayer( Player player ) => ActiveGamemode?.EquipPlayer( player );

	public float GetRespawnDelay( PlayerData playerData ) => ActiveGamemode?.GetRespawnDelay( playerData ) ?? 5f;

	// ─── Gamemode switching ──────────────────────────────────────────────────

	/// <summary>
	/// Switch to a gamemode by type name at runtime. Host-only.
	/// Pass null to go back to a mode-less session.
	/// </summary>
	public void SwitchTo( string typeName )
	{
		if ( !Networking.IsHost ) return;

		if ( ActiveGamemode.IsValid() )
		{
			ActiveGamemode.OnGamemodeEnd();
			ActiveGamemode.GameObject.Destroy();
			ActiveGamemode = null;
			DestroyHud();
		}

		GameRulesService.Current = null;

		if ( !string.IsNullOrWhiteSpace( typeName ) )
			ActivateGamemode( typeName );
	}

	/// <summary>
	/// All gamemode types registered in the TypeLibrary. Use this to populate a gamemode picker UI.
	/// </summary>
	public static IEnumerable<TypeDescription> AllGamemodeTypes()
		=> TypeLibrary.GetTypes<BaseGamemode>().Where( t => !t.IsAbstract );

	// ─── Internal ────────────────────────────────────────────────────────────

	void ActivateGamemode( string typeName )
	{
		var type = TypeLibrary.GetType( typeName );
		if ( type is null )
		{
			Log.Warning( $"[GamemodeManager] Unknown gamemode type '{typeName}'" );
			return;
		}

		var go = new GameObject();
		go.Name = typeName;
		go.NetworkSpawn( null );
		go.Network.SetOwnerTransfer( OwnerTransfer.Fixed );

		ActiveGamemode = (BaseGamemode)go.Components.Create( type );
		ActiveGamemode.OnGamemodeStart();

		GameRulesService.Current = this;
		SpawnHud();
	}

	void SpawnHud()
	{
		DestroyHud();
		var prefab = ActiveGamemode?.HudPrefab;
		if ( prefab is null ) return;
		_activeHud = prefab.Clone( new CloneConfig { Transform = Transform.Zero } );
	}

	void DestroyHud()
	{
		if ( !_activeHud.IsValid() ) return;
		_activeHud.Destroy();
		_activeHud = null;
	}
}
