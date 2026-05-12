/// <summary>
/// Scene singleton that manages the active gamemode. Add it alongside <see cref="GameManager"/> in your scene.
/// Set <see cref="GamemodePrefab"/> in the inspector, or leave it null for a mode-less session.
/// </summary>
public sealed class GamemodeManager : GameObjectSystem<GamemodeManager>, ISceneStartup, Component.INetworkListener
{
	[Property] public GameObject GamemodePrefab { get; set; }

	public BaseGamemode ActiveGamemode { get; private set; }

	GameObject _activeHud;

	public GamemodeManager( Scene scene ) : base( scene ) { }

	void ISceneStartup.OnHostInitialize()
	{
		if ( !Networking.IsHost || GamemodePrefab is null ) return;
		ActivateGamemodePrefab( GamemodePrefab );
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
	}

	void ActivateGamemodePrefab( GameObject prefab )
	{
		var go = prefab.Clone( new CloneConfig { Transform = Transform.Zero } );
		go.NetworkSpawn( null );
		go.Network.SetOwnerTransfer( OwnerTransfer.Fixed );

		ActiveGamemode = go.Components.Get<BaseGamemode>( true );
		ActiveGamemode?.OnGamemodeStart();

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
