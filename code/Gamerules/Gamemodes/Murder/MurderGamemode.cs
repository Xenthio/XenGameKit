// ─── Role data ────────────────────────────────────────────────────────────────

public sealed class RoleInfo
{
	public string RoleName   { get; init; } = "Unknown";
	public Color  RoleColor  { get; init; } = Color.White;
	public int    MaxPlayers { get; init; } = -1; // -1 = unlimited
	public bool   IsSecret   { get; init; } = false;
}

// ─── Base role ────────────────────────────────────────────────────────────────

/// <summary>
/// Plain C# class — not a Component. Subclass to define per-role behaviour.
/// All methods are host-only unless noted.
/// </summary>
public abstract class BaseRole
{
	public abstract RoleInfo Info { get; }

	// Equip the player and send any private reveals at round start.
	public virtual void OnRoundStart( Player player, MurderGamemode gamemode ) { }

	// Called after any player dies — use to cascade win checks or react to specific deaths.
	public virtual void OnPlayerDied( Player player, PlayerDiedParams args, MurderGamemode gamemode ) { }

	// Return false to block this attacker from damaging this victim.
	public virtual bool CanDamage( Player attacker, Player victim, MurderGamemode gamemode ) => true;

	// Return true if this role's win condition is satisfied.
	public virtual bool CheckWinCondition( MurderGamemode gamemode ) => false;
}

// ─── Roles ────────────────────────────────────────────────────────────────────

public sealed class MurdererRole : BaseRole
{
	public override RoleInfo Info { get; } = new()
	{
		RoleName   = "Murderer",
		RoleColor  = Color.Red,
		MaxPlayers = 1,
		IsSecret   = true,
	};

	public override void OnRoundStart( Player player, MurderGamemode gamemode )
		=> GameManager.GiveToPlayer( player, "weapon_crowbar" );

	public override bool CanDamage( Player attacker, Player victim, MurderGamemode gamemode )
		=> attacker != victim;

	// Murderer wins when every surviving player is also a murderer (i.e., everyone else is dead).
	public override bool CheckWinCondition( MurderGamemode gamemode )
		=> gamemode.AlivePlayers().All( p => gamemode.GetRole( p ) is MurdererRole );
}

public sealed class DetectiveRole : BaseRole
{
	public override RoleInfo Info { get; } = new()
	{
		RoleName   = "Detective",
		RoleColor  = new Color( 0.2f, 0.5f, 1f ),
		MaxPlayers = 1,
		IsSecret   = true,
	};

	public override void OnRoundStart( Player player, MurderGamemode gamemode )
		=> GameManager.GiveToPlayer( player, "weapon_pistol" );

	public override bool CanDamage( Player attacker, Player victim, MurderGamemode gamemode )
		=> attacker != victim;

	// Detective + Innocents win when the murderer is dead.
	public override bool CheckWinCondition( MurderGamemode gamemode )
		=> !gamemode.AlivePlayers().Any( p => gamemode.GetRole( p ) is MurdererRole );
}

public sealed class InnocentRole : BaseRole
{
	public override RoleInfo Info { get; } = new()
	{
		RoleName   = "Innocent",
		RoleColor  = Color.White,
		MaxPlayers = -1,
		IsSecret   = false,
	};

	// Innocents are unarmed — they rely on found weapons or the detective.
	public override bool CanDamage( Player attacker, Player victim, MurderGamemode gamemode ) => false;

	// Same win condition as detective.
	public override bool CheckWinCondition( MurderGamemode gamemode )
		=> !gamemode.AlivePlayers().Any( p => gamemode.GetRole( p ) is MurdererRole );
}

// ─── Gamemode ─────────────────────────────────────────────────────────────────

/// <summary>
/// Murder-style hidden-role gamemode. One Murderer tries to eliminate everyone;
/// the Detective and Innocents win by taking the Murderer out.
///
/// Roles are assigned and revealed only to their owner — identity never syncs.
/// Safe to delete this whole file without breaking anything else.
/// </summary>
public sealed class MurderGamemode : BaseGamemode
{
	[Property] public bool  IncludeMurderer       { get; set; } = true;
	[Property] public bool  IncludeDetective      { get; set; } = true;
	[Property] public float RoundTimeLimitSeconds { get; set; } = 180f;
	[Property] public float IntermissionSeconds   { get; set; } = 5f;

	// Role counts synced so HUDs can show "X Innocents remain" without leaking identities.
	[Sync( SyncFlags.FromHost )] public int   MurdererCount  { get; private set; }
	[Sync( SyncFlags.FromHost )] public int   DetectiveCount { get; private set; }
	[Sync( SyncFlags.FromHost )] public int   InnocentCount  { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool  MatchOver      { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool  IsIntermission { get; private set; }
	[Sync( SyncFlags.FromHost )] public float TimeRemaining  { get; private set; }

	// Host-only. Role identities must never reach the network.
	readonly Dictionary<Guid, BaseRole> _roles = new();

	bool      _intermission;
	TimeSince _roundTimer;

	public override void OnGamemodeStart()
	{
		base.OnGamemodeStart();
		if ( !Networking.IsHost ) return;

		MatchOver    = false;
		_intermission = false;
		StartRound();
	}

	public override void OnHostBecame()
	{
		_roundTimer = _intermission
			? IntermissionSeconds   - TimeRemaining
			: RoundTimeLimitSeconds - TimeRemaining;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !IsActive || MatchOver ) return;

		if ( _intermission )
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

		RoundNumber   = RoundNumber + 1;
		_intermission = false;
		IsIntermission = false;
		TimeRemaining = RoundTimeLimitSeconds > 0 ? RoundTimeLimitSeconds : float.MaxValue;
		_roundTimer   = 0;

		SetPhase( RoundPhase.Preparing );
		_roles.Clear();

		RespawnAll();
		AssignRoles();
		RefreshRoleCounts();

		SetPhase( RoundPhase.Active );
		Global.IGamemodeEvents.Post( x => x.OnRoundStart( new RoundStartEvent { RoundNumber = RoundNumber } ) );
		AnnounceRoundStart( RoundNumber );
	}

	void EndRound( bool murdererWon, string reason )
	{
		if ( !Networking.IsHost ) return;

		_intermission  = true;
		IsIntermission = true;
		TimeRemaining  = IntermissionSeconds;
		_roundTimer    = 0;

		SetPhase( RoundPhase.PostRound );
		Global.IGamemodeEvents.Post( x => x.OnRoundEnd( new RoundEndEvent { Reason = RoundEndReason.ForceEnd, WinningTeam = -1 } ) );
		AnnounceRoundEnd( murdererWon, reason );
	}

	void AssignRoles()
	{
		if ( !Networking.IsHost ) return;

		// Build a pool of special roles; everyone else becomes Innocent.
		var pool = new List<BaseRole>();
		if ( IncludeMurderer  ) pool.Add( new MurdererRole() );
		if ( IncludeDetective ) pool.Add( new DetectiveRole() );

		var shuffled = PlayerData.All.OrderBy( _ => Random.Shared.NextSingle() ).ToList();

		for ( int i = 0; i < shuffled.Count; i++ )
		{
			var pd   = shuffled[i];
			var role = i < pool.Count ? pool[i] : new InnocentRole();
			_roles[pd.PlayerId] = role;

			var player = Scene.GetAll<Player>().FirstOrDefault( p => p.IsValid() && p.PlayerData == pd );
			if ( !player.IsValid() ) continue;

			role.OnRoundStart( player, this );

			// Role reveal is sent only to that player's connection — never broadcast.
			if ( pd.Connection is { } conn )
				using ( Rpc.FilterInclude( conn ) )
					RpcRevealRole( role.Info.RoleName );
		}
	}

	// ─── Public helpers for role classes to call back into ───────────────────

	/// <summary>Host-only. Returns the role for this player, or null.</summary>
	public BaseRole GetRole( Player player )
	{
		if ( !player.PlayerData.IsValid() ) return null;
		_roles.TryGetValue( player.PlayerData.PlayerId, out var role );
		return role;
	}

	/// <summary>Host-only. All players currently alive in the scene.</summary>
	public IEnumerable<Player> AlivePlayers()
		=> Scene.GetAll<Player>().Where( p => p.IsValid() && !p.IsDead );

	// ─── BaseGamemode overrides ───────────────────────────────────────────────

	public override bool CanDamage( Player attacker, Player victim )
	{
		if ( attacker == victim ) return true;
		var role = GetRole( attacker );
		return role?.CanDamage( attacker, victim, this ) ?? false;
	}

	public override void RequestRespawn( PlayerData playerData ) { } // wait for next round

	public override Transform? GetSpawnLocation( PlayerData playerData )
	{
		var spawns = Scene.GetAllComponents<SpawnPoint>().ToArray();
		if ( spawns.Length == 0 ) return null;

		var living = AlivePlayers().ToArray();
		if ( living.Length == 0 ) return Random.Shared.FromArray( spawns ).WorldTransform;

		return spawns
			.OrderByDescending( sp => living.Min( p => (sp.WorldPosition - p.WorldPosition).LengthSquared ) )
			.First()
			.WorldTransform;
	}

	protected override void OnPlayerDied( Player player, PlayerDiedParams args )
	{
		if ( !Networking.IsHost || !IsActive || MatchOver || _intermission ) return;

		var role = GetRole( player );
		role?.OnPlayerDied( player, args, this );

		RefreshRoleCounts();
		CheckWinConditions();
	}

	// ─── Internal ────────────────────────────────────────────────────────────

	void CheckWinConditions()
	{
		if ( !Networking.IsHost ) return;

		foreach ( var role in _roles.Values.Distinct() )
		{
			if ( !role.CheckWinCondition( this ) ) continue;

			bool murdererWon = role is MurdererRole;
			EndRound( murdererWon, murdererWon ? "The Murderer eliminated everyone!" : "The Murderer has been found!" );
			return;
		}
	}

	void RefreshRoleCounts()
	{
		if ( !Networking.IsHost ) return;

		int m = 0, d = 0, inn = 0;
		foreach ( var p in AlivePlayers() )
		{
			switch ( GetRole( p ) )
			{
				case MurdererRole:  m++;   break;
				case DetectiveRole: d++;   break;
				case InnocentRole:  inn++; break;
			}
		}

		MurdererCount  = m;
		DetectiveCount = d;
		InnocentCount  = inn;
	}

	[Rpc.Broadcast]
	void RpcRevealRole( string roleName ) =>
		Log.Info( $"[Murder] Your role this round: {roleName}" );

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceRoundStart( int round ) =>
		Log.Info( $"[Murder] Round {round} — find the Murderer!" );

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceRoundEnd( bool murdererWon, string reason ) =>
		Log.Info( $"[Murder] {(murdererWon ? "Murderer" : "Innocents")} win — {reason}" );
}
