public sealed partial class GameManager : GameObjectSystem<GameManager>, Component.INetworkListener, ISceneStartup
{
	public GameManager( Scene scene ) : base( scene )
	{
	}

	// -------------------------------------------------------------------------
	// Console Commands
	// -------------------------------------------------------------------------

	/// <summary>
	/// Spawn a prefab by name. Source/GMod style.
	/// e.g. "ent_create weapon_glock" or "ent_create entities/npc_zombie"
	/// </summary>
	[ConCmd( "ent_create", ConVarFlags.Server | ConVarFlags.Cheat )]
	public static void EntCreate( Connection source, string name )
	{
		var player = Game.ActiveScene.GetAll<Player>().FirstOrDefault( p => p.Network.Owner == source );
		var transform = player.IsValid()
			? new Transform( player.EyeTransform.Position + player.EyeTransform.Forward * 64f, player.EyeTransform.Rotation )
			: Transform.Zero;
		DoEntCreate( name, transform );
	}

	/// <summary>
	/// Give a weapon or spawn an entity directly into your inventory.
	/// </summary>
	[ConCmd( "give", ConVarFlags.Server | ConVarFlags.Cheat )]
	public static void Give( Connection source, string name )
	{
		var player = Game.ActiveScene.GetAll<Player>().FirstOrDefault( p => p.Network.Owner == source );
		if ( !player.IsValid() ) return;
		GiveToPlayer( player, name );
	}

	static void GiveToPlayer( Player player, string name )
	{
		var prefab = ResolvePrefab( name );
		if ( prefab is null ) { Log.Warning( $"give: could not find prefab '{name}'" ); return; }

		var carryable = prefab.Components.Get<BaseCarryable>( true );
		if ( carryable.IsValid() )
		{
			player.GetComponent<PlayerInventory>()?.Pickup( prefab );
			return;
		}

		// Not a weapon — spawn at player's feet
		DoEntCreate( name, player.WorldTransform );
	}

	/// <summary>
	/// Toggle noclip for the calling player.
	/// </summary>
	[ConCmd( "noclip", ConVarFlags.Server | ConVarFlags.Cheat )]
	public static void Noclip( Connection source )
	{
		var player = Game.ActiveScene.GetAll<Player>().FirstOrDefault( p => p.Network.Owner == source );
		if ( player.IsValid() )
			player.WalkController.IsNoclipping = !player.WalkController.IsNoclipping;
	}

	/// <summary>
	/// Switch to first-person camera. Source-style command.
	/// Runs on the calling client — camera mode is client-side.
	/// </summary>
	[ConCmd( "firstperson" )]
	public static void FirstPerson()
	{
		var player = Player.FindLocalPlayer();
		if ( player.IsValid() )
		{
			player.WalkController.CameraMode = XMovement.PlayerWalkControllerComplex.CameraModes.FirstPerson;
			player.WalkController.SetupCamera();
		}
	}

	/// <summary>
	/// Switch to third-person camera. Source-style command.
	/// Runs on the calling client — camera mode is client-side.
	/// </summary>
	[ConCmd( "thirdperson" )]
	public static void ThirdPerson()
	{
		var player = Player.FindLocalPlayer();
		if ( player.IsValid() )
		{
			player.WalkController.CameraMode = XMovement.PlayerWalkControllerComplex.CameraModes.ThirdPerson;
			player.WalkController.SetupCamera();
		}
	}

	/// <summary>
	/// Kill yourself. Classic.
	/// </summary>
	[ConCmd( "kill", ConVarFlags.Server )]
	public static void KillSelf( Connection source )
	{
		var player = Game.ActiveScene.GetAll<Player>().FirstOrDefault( p => p.Network.Owner == source );
		player?.OnDamage( new DamageInfo( float.MaxValue, (GameObject)null ) );
	}

	/// <summary>
	/// Ignite the entity you're aiming at.
	/// Usage: ignite [durationSeconds]
	/// </summary>
	[ConCmd( "ignite", ConVarFlags.Server | ConVarFlags.Cheat )]
	public static void Ignite( Connection source, float durationSeconds = 10f )
	{
		var player = Game.ActiveScene.GetAll<Player>().FirstOrDefault( p => p.Network.Owner == source );
		if ( !player.IsValid() )
			return;

		var tr = Game.ActiveScene.Trace.Ray( player.EyeTransform.ForwardRay, 4096f )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.WithoutTags( "player", "playercontroller", "trigger", "weapon" )
			.Run();

		if ( !tr.Hit || !tr.GameObject.IsValid() )
		{
			Log.Warning( "ignite: no valid target" );
			return;
		}

		var target = tr.GameObject.Root;
		var fireComponent = FireSystem.Ignite( target );

		if ( !fireComponent.IsValid() )
		{
			Log.Warning( "ignite: failed to create fire component" );
			return;
		}

		durationSeconds = Math.Max( 0f, durationSeconds );

		fireComponent.Enabled = true;
		fireComponent.InfiniteFuel = durationSeconds <= 0f;
		fireComponent.FuelSeconds = durationSeconds;
		fireComponent.RemainingFuel = durationSeconds;
	}

	// -------------------------------------------------------------------------
	// Prefab resolution
	// -------------------------------------------------------------------------

	static void DoEntCreate( string name, Transform spawnTransform )
	{
		var prefab = ResolvePrefab( name );
		if ( prefab is null )
		{
			Log.Warning( $"ent_create: could not find prefab '{name}'" );
			return;
		}

		var go = prefab.Clone( new CloneConfig { Transform = spawnTransform } );
		go.NetworkSpawn();
	}

	/// <summary>
	/// Resolves a prefab by short name or full path.
	/// Checks in order:
	///   prefabs/weapons/weapon_{name}.prefab
	///   prefabs/weapons/{name}.prefab
	///   prefabs/entities/{name}.prefab
	///   prefabs/{name}.prefab
	///   {name}.prefab
	///   {name}  (full path as-is)
	/// </summary>
	public static GameObject ResolvePrefab( string name )
	{
		var candidates = new[]
		{
			$"prefabs/weapons/weapon_{name}.prefab",
			$"prefabs/weapons/{name}.prefab",
			$"prefabs/entities/{name}.prefab",
			$"prefabs/{name}.prefab",
			$"{name}.prefab",
			name,
		};

		foreach ( var path in candidates )
		{
			var prefab = GameObject.GetPrefab( path );
			if ( prefab is not null ) return prefab;
		}

		return null;
	}

	// -------------------------------------------------------------------------
	// Scene / Network lifecycle
	// -------------------------------------------------------------------------

	void ISceneStartup.OnHostInitialize()
	{
		if ( !Scene.WantsSystemScene ) return;

		if ( !FireSystem.DefaultFireParticle.IsValid() )
			FireSystem.DefaultFireParticle = GameObject.GetPrefab( "prefabs/effects/fire.prefab" );

		Scene.NavMesh.AgentRadius = 20;
		Scene.NavMesh.AgentHeight = 72;
		Scene.NavMesh.IsEnabled = true;
	}

	void Component.INetworkListener.OnActive( Connection channel )
	{
		channel.CanSpawnObjects = false;

		var playerData = CreatePlayerInfo( channel );
		SpawnPlayer( playerData );
	}

	void Component.INetworkListener.OnDisconnected( Connection channel )
	{
		var pd = PlayerData.For( channel );
		if ( pd is not null )
			pd.GameObject.Destroy();
	}

	private PlayerData CreatePlayerInfo( Connection channel )
	{
		var go = new GameObject( true, $"PlayerInfo - {channel.DisplayName}" );
		var data = go.AddComponent<PlayerData>();
		data.SteamId = (long)channel.SteamId;
		data.PlayerId = channel.Id;
		data.DisplayName = channel.DisplayName;

		go.NetworkSpawn( null );
		go.Network.SetOwnerTransfer( OwnerTransfer.Fixed );

		return data;
	}

	public void SpawnPlayer( Connection connection ) => SpawnPlayer( PlayerData.For( connection ) );

	public void SpawnPlayer( PlayerData playerData )
	{
		Assert.NotNull( playerData, "PlayerData is null" );
		Assert.True( Networking.IsHost, $"Client tried to SpawnPlayer: {playerData.DisplayName}" );

		if ( Scene.GetAll<Player>().Where( x => x.Network.Owner?.Id == playerData.PlayerId ).Any() )
			return;

		var startLocation = FindSpawnLocation().WithScale( 1 );

		var playerGo = GameObject.Clone( "/prefabs/player.prefab", new CloneConfig
		{
			Name = playerData.DisplayName,
			StartEnabled = false,
			Transform = startLocation
		} );

		var player = playerGo.Components.Get<Player>( true );
		player.PlayerData = playerData;

		var owner = Connection.Find( playerData.PlayerId );
		playerGo.NetworkSpawn( owner );

		Local.IPlayerEvents.PostToGameObject( player.GameObject, x => x.OnSpawned() );
	}

	public void SpawnPlayerDelayed( PlayerData playerData )
	{
		GameTask.RunInThreadAsync( async () =>
		{
			await Task.Delay( 4000 );
			await GameTask.MainThread();
			Current?.SpawnPlayer( playerData );
		} );
	}

	public static Transform EditorSpawnLocation { get; set; }

	Transform FindSpawnLocation()
	{
		var spawnPoints = Scene.GetAllComponents<SpawnPoint>().ToArray();

		if ( spawnPoints.Length == 0 )
		{
			if ( Application.IsEditor && !EditorSpawnLocation.Position.IsNearlyZero() )
				return EditorSpawnLocation;

			return Transform.Zero;
		}

		var players = Scene.GetAll<Player>();

		if ( !players.Any() )
			return Random.Shared.FromArray( spawnPoints ).Transform.World;

		// Pick spawnpoint furthest from any player to avoid spawn-on-top
		SpawnPoint best = null;
		float bestDist = float.MinValue;

		foreach ( var sp in spawnPoints )
		{
			float closest = float.MaxValue;
			foreach ( var p in players )
			{
				float d = (sp.Transform.World.Position - p.Transform.World.Position).LengthSquared;
				if ( d < closest ) closest = d;
			}
			if ( closest > bestDist ) { bestDist = closest; best = sp; }
		}

		return best.Transform.World;
	}

	// -------------------------------------------------------------------------
	// Death handling
	// -------------------------------------------------------------------------

	[Rpc.Broadcast]
	private static void SendMessage( string msg ) => Log.Info( msg );

	public void OnDeath( Player player, DamageInfo dmg )
	{
		Assert.True( Networking.IsHost );
		Assert.True( player.IsValid(), "Player invalid" );
		Assert.True( player.PlayerData.IsValid(), $"{player.GameObject.Name}'s PlayerData invalid" );

		var weapon = dmg.Weapon;
		var attackerData = PlayerData.For( dmg.InstigatorId );
		bool isSuicide = attackerData == player.PlayerData;

		if ( attackerData.IsValid() && !isSuicide )
		{
			attackerData.Kills++;
			attackerData.AddStat( "kills" );
			if ( weapon.IsValid() )
				attackerData.AddStat( $"kills.{weapon.Name}" );
		}

		player.PlayerData.Deaths++;

		string attackerName = attackerData.IsValid() ? attackerData.DisplayName : dmg.Attacker?.Name;
		if ( string.IsNullOrEmpty( attackerName ) )
			SendMessage( $"{player.DisplayName} died (tags: {dmg.Tags})" );
		else if ( weapon.IsValid() )
			SendMessage( $"{attackerName} killed {(isSuicide ? "self" : player.DisplayName)} with {weapon.Name} (tags: {dmg.Tags})" );
		else
			SendMessage( $"{attackerName} killed {(isSuicide ? "self" : player.DisplayName)} (tags: {dmg.Tags})" );
	}
}
