/// <summary>
/// Murder-style gamemode. One Murderer tries to eliminate everyone; the Detective and Innocents
/// win by taking the Murderer out. Roles are secret and revealed only to their owner.
///
/// Self-contained — safe to delete the whole RoleBased/ folder without breaking anything else.
/// </summary>
public sealed class RoleBasedGamemode : BaseGamemode
{
	[Property] public bool IncludeMurderer  { get; set; } = true;
	[Property] public bool IncludeDetective { get; set; } = true;
	[Property] public float RoundTimeLimitSeconds { get; set; } = 180f;
	[Property] public float IntermissionSeconds   { get; set; } = 5f;

	// Role counts are synced so HUDs can show "X Innocents remain" without leaking identities
	[Sync( SyncFlags.FromHost )] public int MurdererCount  { get; private set; }
	[Sync( SyncFlags.FromHost )] public int DetectiveCount { get; private set; }
	[Sync( SyncFlags.FromHost )] public int InnocentCount  { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool MatchOver        { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsIntermission    { get; private set; }
	[Sync( SyncFlags.FromHost )] public float TimeRemaining { get; private set; }

	// Host-only — never synced, roles must stay secret
	readonly Dictionary<Guid, BaseRole> PlayerRoles = new();
	readonly List<BaseRole>             _rolePool   = new();

	bool       _isIntermission;
	TimeSince  _roundTimer;

	public override void OnGamemodeStart()
	{
		base.OnGamemodeStart();
		if ( !Networking.IsHost ) return;

		MatchOver       = false;
		_isIntermission = false;
		StartRound();
	}

	public override void OnHostBecame()
	{
		// Reconstruct the TimeSince timer from the synced TimeRemaining value so the
		// round clock survives host migration without resetting.
		_roundTimer = _isIntermission
			? IntermissionSeconds - TimeRemaining
			: RoundTimeLimitSeconds - TimeRemaining;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !IsActive || MatchOver ) return;

		if ( _isIntermission )
		{
			TimeRemaining = MathF.Max( 0f, IntermissionSeconds - _roundTimer );
			if ( TimeRemaining <= 0f ) StartRound();
			return;
		}

		if ( RoundTimeLimitSeconds > 0 )
		{
			TimeRemaining = MathF.Max( 0f, RoundTimeLimitSeconds - _roundTimer );
			if ( TimeRemaining <= 0f )
				EndRound( murdererWon: false, "Time ran out — Innocents survive!" );
		}
	}

	void StartRound()
	{
		if ( !Networking.IsHost ) return;

		RoundNumber++;
		_isIntermission = false;
		IsIntermission  = false;
		SetPhase( RoundPhase.Preparing );
		TimeRemaining   = RoundTimeLimitSeconds > 0 ? RoundTimeLimitSeconds : float.MaxValue;
		_roundTimer     = 0;

		PlayerRoles.Clear();
		_rolePool.Clear();
		BuildRolePool();
		RespawnAllPlayers();
		AssignRoles();
		RefreshRoleCounts();

		SetPhase( RoundPhase.Active );
		AnnounceRoundStart( RoundNumber );
		Global.IGamemodeEvents.Post( x => x.OnRoundStart( new RoundStartEvent { RoundNumber = RoundNumber } ) );
	}

	void EndRound( bool murdererWon, string reason )
	{
		if ( !Networking.IsHost ) return;

		_isIntermission = true;
		IsIntermission  = true;
		SetPhase( RoundPhase.PostRound );
		TimeRemaining   = IntermissionSeconds;
		_roundTimer     = 0;

		AnnounceRoundEnd( murdererWon, reason );
		Global.IGamemodeEvents.Post( x => x.OnRoundEnd( new RoundEndEvent { Reason = RoundEndReason.ForceEnd, WinningTeam = -1 } ) );
	}

	void BuildRolePool()
	{
		if ( IncludeMurderer  ) _rolePool.Add( new MurdererRole() );
		if ( IncludeDetective ) _rolePool.Add( new DetectiveRole() );
	}

	void AssignRoles()
	{
		if ( !Networking.IsHost ) return;

		var shuffled = PlayerData.All.OrderBy( _ => Random.Shared.NextSingle() ).ToList();

		for ( int i = 0; i < shuffled.Count; i++ )
		{
			var pd   = shuffled[i];
			var role = i < _rolePool.Count ? _rolePool[i] : new InnocentRole();

			PlayerRoles[pd.PlayerId] = role;

			var player = Game.ActiveScene.GetAll<Player>().FirstOrDefault( p => p.IsValid() && p.PlayerData == pd );
			if ( !player.IsValid() ) continue;

			role.OnRoundStart( player, this );

			if ( pd.Connection is { } conn )
			{
				using ( Rpc.FilterInclude( conn ) )
					RpcRevealRole( role.Info.RoleName );
			}
		}
	}

	/// <summary>
	/// Returns the role assigned to this player. Host-only — never call on clients.
	/// </summary>
	public BaseRole GetRole( Guid playerId )
	{
		PlayerRoles.TryGetValue( playerId, out var role );
		return role;
	}

	public override bool CanDamage( Player attacker, Player victim )
	{
		if ( attacker == victim ) return true;
		if ( !Networking.IsHost ) return true;

		var role = attacker.PlayerData.IsValid() ? GetRole( attacker.PlayerData.PlayerId ) : null;
		return role?.CanDamage( attacker, victim, this ) ?? false;
	}

	public override Transform? GetSpawnLocation( PlayerData playerData )
	{
		var spawns = Scene.GetAllComponents<SpawnPoint>().ToArray();
		if ( spawns.Length == 0 ) return null;

		var living = Scene.GetAll<Player>().Where( p => !p.IsDead ).ToArray();
		if ( living.Length == 0 ) return Random.Shared.FromArray( spawns ).WorldTransform;

		SpawnPoint best     = null;
		float      bestDist = float.MinValue;

		foreach ( var sp in spawns )
		{
			float closest = living.Min( p => (sp.WorldPosition - p.WorldPosition).LengthSquared );
			if ( closest > bestDist ) { bestDist = closest; best = sp; }
		}

		return best?.WorldTransform;
	}

	public override void RequestRespawn( PlayerData playerData ) { } // wait for next round

	protected override void OnPlayerDied( Player player, PlayerDiedParams args )
	{
		if ( !Networking.IsHost || !IsActive || MatchOver || _isIntermission ) return;

		if ( player.PlayerData.IsValid() )
			GetRole( player.PlayerData.PlayerId )?.OnPlayerDied( player, args, this );

		RefreshRoleCounts();
		CheckWinConditions();
	}

	void CheckWinConditions()
	{
		if ( !Networking.IsHost ) return;

		if ( IncludeMurderer )
		{
			var murdererRole = PlayerRoles.Values.OfType<MurdererRole>().FirstOrDefault();
			if ( murdererRole is not null && murdererRole.CheckWinCondition( this ) )
			{
				EndRound( murdererWon: true, "The Murderer eliminated everyone!" );
				return;
			}
		}

		var innocent = PlayerRoles.Values.OfType<InnocentRole>().FirstOrDefault();
		if ( innocent is not null && innocent.CheckWinCondition( this ) )
		{
			EndRound( murdererWon: false, "The Murderer has been found!" );
			return;
		}

		var detective = PlayerRoles.Values.OfType<DetectiveRole>().FirstOrDefault();
		if ( detective is not null && detective.CheckWinCondition( this ) )
		{
			EndRound( murdererWon: false, "The Detective neutralised the Murderer!" );
		}
	}

	void RefreshRoleCounts()
	{
		if ( !Networking.IsHost ) return;

		int m = 0, d = 0, inn = 0;

		foreach ( var (id, role) in PlayerRoles )
		{
			var player = Game.ActiveScene.GetAll<Player>()
				.FirstOrDefault( p => p.IsValid() && p.PlayerData?.PlayerId == id && !p.IsDead );

			if ( player is null ) continue;

			if ( role is MurdererRole  ) m++;
			if ( role is DetectiveRole ) d++;
			if ( role is InnocentRole  ) inn++;
		}

		MurdererCount  = m;
		DetectiveCount = d;
		InnocentCount  = inn;
	}

	void RespawnAllPlayers()
	{
		if ( !Networking.IsHost ) return;

		foreach ( var pd in PlayerData.All )
		{
			var existing = Scene.GetAll<Player>().FirstOrDefault( p => p.PlayerData == pd );
			if ( existing.IsValid() ) existing.GameObject.Destroy();
			GameManager.Current?.SpawnPlayer( pd );
		}
	}

	[Rpc.Broadcast]
	void RpcRevealRole( string roleName )
	{
		// Only meaningful on the filtered client — hook up a HUD notification here
		Log.Info( $"[Murder] Your role this round: {roleName}" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceRoundStart( int roundNumber ) =>
		Log.Info( $"[Murder] Round {roundNumber} — find the Murderer!" );

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceRoundEnd( bool murdererWon, string reason ) =>
		Log.Info( $"[Murder] {( murdererWon ? "Murderer" : "Innocents" )} win — {reason}" );
}
