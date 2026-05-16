/// <summary>
/// CS-style Team Deathmatch. Rounds end when one team is wiped or time runs out.
/// First team to RoundsToWin takes the match. Dead players sit out until next round.
/// </summary>
public sealed class TDMGamemode : BaseGamemode
{
	[Property] public int   RoundsToWin           { get; set; } = 8;
	[Property] public float RoundTimeLimitSeconds { get; set; } = 120f;
	[Property] public float FreezeTimeSeconds     { get; set; } = 3f;
	[Property] public float IntermissionSeconds   { get; set; } = 5f;

	// Team definitions — add as many as you like. Index 0 = first team, etc.
	[Property] public TeamInfo[] Teams { get; set; } = new[]
	{
		new TeamInfo { TeamName = "Red",  TeamColor = Color.Red   },
		new TeamInfo { TeamName = "Blue", TeamColor = Color.Cyan  },
	};

	[Sync( SyncFlags.FromHost )] public int   Team0Score     { get; private set; }
	[Sync( SyncFlags.FromHost )] public int   Team1Score     { get; private set; }
	[Sync( SyncFlags.FromHost )] public float TimeRemaining  { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool  IsFreezeTime   { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool  IsIntermission { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool  MatchOver      { get; private set; }

	TimeSince _phaseTimer;

	public override void OnGamemodeStart()
	{
		base.OnGamemodeStart();
		if ( !Networking.IsHost ) return;

		Team0Score = 0;
		Team1Score = 0;
		MatchOver  = false;
		StartRound();
	}

	public override void OnHostBecame()
	{
		if      ( IsIntermission ) _phaseTimer = IntermissionSeconds   - TimeRemaining;
		else if ( IsFreezeTime   ) _phaseTimer = FreezeTimeSeconds     - TimeRemaining;
		else                       _phaseTimer = RoundTimeLimitSeconds - TimeRemaining;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !IsActive || MatchOver ) return;

		if ( IsIntermission )
		{
			TimeRemaining = MathF.Max( 0f, IntermissionSeconds - _phaseTimer );
			if ( TimeRemaining <= 0f ) StartRound();
			return;
		}

		if ( IsFreezeTime )
		{
			TimeRemaining = MathF.Max( 0f, FreezeTimeSeconds - _phaseTimer );
			if ( TimeRemaining <= 0f ) EndFreezeTime();
			return;
		}

		TimeRemaining = MathF.Max( 0f, RoundTimeLimitSeconds - _phaseTimer );
		if ( RoundTimeLimitSeconds > 0 && TimeRemaining <= 0f )
			EndRound( RoundEndReason.TimeLimit, -1 );
	}

	void StartRound()
	{
		if ( !Networking.IsHost ) return;

		RoundNumber++;
		IsIntermission = false;
		IsFreezeTime   = FreezeTimeSeconds > 0;
		TimeRemaining  = IsFreezeTime ? FreezeTimeSeconds : RoundTimeLimitSeconds;
		_phaseTimer    = 0;

		SetPhase( IsFreezeTime ? RoundPhase.Preparing : RoundPhase.Active );
		AssignAllTeams();
		RespawnAll();

		Global.IGamemodeEvents.Post( x => x.OnRoundStart( new RoundStartEvent { RoundNumber = RoundNumber } ) );
		AnnounceRoundStart( RoundNumber );
	}

	void EndFreezeTime()
	{
		if ( !Networking.IsHost ) return;
		IsFreezeTime  = false;
		TimeRemaining = RoundTimeLimitSeconds;
		_phaseTimer   = 0;
		SetPhase( RoundPhase.Active );
	}

	void EndRound( RoundEndReason reason, int winningTeam )
	{
		if ( !Networking.IsHost ) return;

		IsIntermission = true;
		IsFreezeTime   = false;
		TimeRemaining  = IntermissionSeconds;
		_phaseTimer    = 0;
		SetPhase( RoundPhase.PostRound );

		if ( winningTeam == 0 ) Team0Score++;
		if ( winningTeam == 1 ) Team1Score++;

		Global.IGamemodeEvents.Post( x => x.OnRoundEnd( new RoundEndEvent { Reason = reason, WinningTeam = winningTeam } ) );
		AnnounceRoundEnd( reason, winningTeam );

		if ( Team0Score >= RoundsToWin ) EndMatch( MatchEndReason.RoundLimit, 0 );
		else if ( Team1Score >= RoundsToWin ) EndMatch( MatchEndReason.RoundLimit, 1 );
	}

	void EndMatch( MatchEndReason reason, int winningTeam )
	{
		MatchOver      = true;
		IsActive       = false;
		IsIntermission = false;
		SetPhase( RoundPhase.MatchOver );
		Global.IGamemodeEvents.Post( x => x.OnMatchEnd( new MatchEndEvent { Reason = reason, WinningTeam = winningTeam } ) );
		AnnounceMatchEnd( reason, winningTeam );
	}

	// Dead players wait for the next round.
	public override void RequestRespawn( PlayerData playerData ) { }

	protected override void OnPlayerDied( Player player, PlayerDiedParams args )
	{
		if ( !Networking.IsHost || !IsActive || MatchOver || IsIntermission ) return;
		CheckElimination();
	}

	protected override void OnPlayerSpawned( Player player )
	{
		if ( !Networking.IsHost ) return;
		if ( player.PlayerData.IsValid() && player.PlayerData.TeamIndex < 0 )
			AssignTeam( player.PlayerData );
	}

	protected override void OnPlayerDamaging( PlayerDamageEvent e )
	{
		// No damage during freeze time.
		if ( IsFreezeTime ) { e.Cancelled = true; return; }
		base.OnPlayerDamaging( e );
	}

	public override Transform? GetSpawnLocation( PlayerData playerData )
	{
		if ( playerData.TeamIndex < 0 ) return null;

		var spawns = Scene.GetAllComponents<TeamSpawnPoint>()
			.Where( sp => sp.TeamIndex == playerData.TeamIndex )
			.ToArray();

		if ( spawns.Length == 0 ) return null;

		var living = Scene.GetAll<Player>().Where( p => !p.IsDead ).ToArray();
		if ( living.Length == 0 ) return Random.Shared.FromArray( spawns ).WorldTransform;

		return spawns
			.OrderByDescending( sp => living.Min( p => (sp.WorldPosition - p.WorldPosition).LengthSquared ) )
			.First()
			.WorldTransform;
	}

	// ─── Team helpers ─────────────────────────────────────────────────────────

	void AssignAllTeams()
	{
		foreach ( var pd in PlayerData.All ) pd.TeamIndex = -1;
		foreach ( var pd in PlayerData.All ) AssignTeam( pd );
	}

	void AssignTeam( PlayerData pd )
	{
		if ( Teams.Length == 0 ) return;

		var counts = new int[Teams.Length];
		foreach ( var p in PlayerData.All )
			if ( p.TeamIndex >= 0 && p.TeamIndex < Teams.Length )
				counts[p.TeamIndex]++;

		int best = -1, bestCount = int.MaxValue;
		for ( int i = 0; i < Teams.Length; i++ )
		{
			int max = Teams[i].MaxPlayers;
			if ( max > 0 && counts[i] >= max ) continue;
			if ( counts[i] >= bestCount ) continue;
			bestCount = counts[i]; best = i;
		}

		pd.TeamIndex = best >= 0 ? best : 0;
		Global.IGamemodeEvents.Post( x => x.OnTeamAssigned( pd, pd.TeamIndex ) );
	}

	bool IsTeamAlive( int teamIndex ) => Scene.GetAll<Player>().Any( p =>
		!p.IsDead && p.PlayerData.IsValid() && p.PlayerData.TeamIndex == teamIndex );

	void CheckElimination()
	{
		bool t0 = IsTeamAlive( 0 );
		bool t1 = IsTeamAlive( 1 );

		if ( !t0 && !t1 ) EndRound( RoundEndReason.TeamEliminated, -1 );
		else if ( !t0 )   EndRound( RoundEndReason.TeamEliminated, 1 );
		else if ( !t1 )   EndRound( RoundEndReason.TeamEliminated, 0 );
	}

	TeamInfo GetTeam( int index ) => index >= 0 && index < Teams.Length ? Teams[index] : null;

	string TeamName( int index ) => GetTeam( index )?.TeamName ?? $"Team {index}";

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceRoundStart( int roundNumber ) =>
		Log.Info( $"[TDM] Round {roundNumber} — {TeamName(0)} vs {TeamName(1)}!" );

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceRoundEnd( RoundEndReason reason, int winner ) =>
		Log.Info( $"[TDM] {(winner < 0 ? "Draw" : TeamName(winner))} | {Team0Score}-{Team1Score}" );

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceMatchEnd( MatchEndReason reason, int winner ) =>
		Log.Info( $"[TDM] Match over — {(winner < 0 ? "Draw" : TeamName(winner) + " wins")}! Final: {Team0Score}-{Team1Score}" );
}
