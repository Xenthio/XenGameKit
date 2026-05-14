/// <summary>
/// CS-style Team Deathmatch. Rounds end when one team is wiped or time runs out.
/// First team to <see cref="RoundsToWin"/> wins the match. Dead players sit out until next round.
/// </summary>
public sealed class TDMGamemode : BaseGamemode
{
	[Property] public int   RoundsToWin           { get; set; } = 8;
	[Property] public float RoundTimeLimitSeconds { get; set; } = 120f;
	[Property] public float FreezeTimeSeconds     { get; set; } = 3f;
	[Property] public float IntermissionSeconds   { get; set; } = 5f;

	[Sync( SyncFlags.FromHost )] public int   Team0Score     { get; private set; }
	[Sync( SyncFlags.FromHost )] public int   Team1Score     { get; private set; }
	[Sync( SyncFlags.FromHost )] public float TimeRemaining  { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool  IsFreezeTime   { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool  IsIntermission { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool  MatchOver      { get; private set; }

	[RequireComponent] public TeamManager TeamManager { get; private set; }

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
		if ( IsIntermission )
		{
			SetPhase( RoundPhase.PostRound );
			_phaseTimer = IntermissionSeconds - TimeRemaining;
		}
		else if ( IsFreezeTime )
		{
			SetPhase( RoundPhase.Preparing );
			_phaseTimer = FreezeTimeSeconds - TimeRemaining;
		}
		else
		{
			SetPhase( MatchOver ? RoundPhase.MatchOver : RoundPhase.Active );
			_phaseTimer = RoundTimeLimitSeconds - TimeRemaining;
		}
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

		if ( RoundTimeLimitSeconds > 0 )
		{
			TimeRemaining = MathF.Max( 0f, RoundTimeLimitSeconds - _phaseTimer );
			if ( TimeRemaining <= 0f ) EndRound( RoundEndReason.TimeLimit, -1 );
		}
	}

	void StartRound()
	{
		if ( !Networking.IsHost ) return;

		RoundNumber++;
		IsFreezeTime   = FreezeTimeSeconds > 0;
		IsIntermission = false;
		SetPhase( IsFreezeTime ? RoundPhase.Preparing : RoundPhase.Active );
		TimeRemaining  = IsFreezeTime ? FreezeTimeSeconds : RoundTimeLimitSeconds;
		_phaseTimer    = 0;

		TeamManager.AssignAllTeams();
		RespawnAllPlayers();

		AnnounceRoundStart( RoundNumber );
		Global.IGamemodeEvents.Post( x => x.OnRoundStart( new RoundStartEvent { RoundNumber = RoundNumber } ) );
	}

	void EndFreezeTime()
	{
		if ( !Networking.IsHost ) return;
		IsFreezeTime  = false;
		SetPhase( RoundPhase.Active );
		TimeRemaining = RoundTimeLimitSeconds;
		_phaseTimer   = 0;
	}

	void EndRound( RoundEndReason reason, int winningTeam )
	{
		if ( !Networking.IsHost ) return;

		IsIntermission = true;
		IsFreezeTime   = false;
		SetPhase( RoundPhase.PostRound );
		TimeRemaining  = IntermissionSeconds;
		_phaseTimer    = 0;

		if ( winningTeam == 0 ) Team0Score++;
		if ( winningTeam == 1 ) Team1Score++;

		AnnounceRoundEnd( reason, winningTeam );
		Global.IGamemodeEvents.Post( x => x.OnRoundEnd( new RoundEndEvent { Reason = reason, WinningTeam = winningTeam } ) );

		if ( Team0Score >= RoundsToWin ) EndMatch( MatchEndReason.RoundLimit, 0 );
		if ( Team1Score >= RoundsToWin ) EndMatch( MatchEndReason.RoundLimit, 1 );
	}

	void EndMatch( MatchEndReason reason, int winningTeam )
	{
		if ( !Networking.IsHost ) return;

		MatchOver      = true;
		IsActive       = false;
		IsIntermission = false;
		SetPhase( RoundPhase.MatchOver );

		AnnounceMatchEnd( reason, winningTeam );
		Global.IGamemodeEvents.Post( x => x.OnMatchEnd( new MatchEndEvent { Reason = reason, WinningTeam = winningTeam } ) );
	}

	protected override void OnPlayerDied( Player player, PlayerDiedParams args )
	{
		if ( !Networking.IsHost || !IsActive || MatchOver || IsIntermission ) return;
		CheckElimination();
	}

	protected override void OnPlayerSpawned( Player player )
	{
		if ( !Networking.IsHost ) return;
		if ( player.PlayerData.IsValid() && player.PlayerData.TeamIndex < 0 )
			TeamManager.AssignTeam( player.PlayerData );
	}

	protected override void OnPlayerDamaging( PlayerDamageEvent e )
	{
		if ( IsFreezeTime ) { e.Cancelled = true; return; }
		base.OnPlayerDamaging( e );
	}

	public override void RequestRespawn( PlayerData playerData ) { } // wait for round end

	public override Transform? GetSpawnLocation( PlayerData playerData )
	{
		if ( playerData.TeamIndex < 0 ) return null;

		var spawns = Scene.GetAllComponents<TeamSpawnPoint>()
			.Where( sp => sp.TeamIndex == playerData.TeamIndex )
			.ToArray();

		if ( spawns.Length == 0 ) return null;

		var living = Scene.GetAll<Player>().Where( p => !p.IsDead ).ToArray();
		if ( living.Length == 0 ) return Random.Shared.FromArray( spawns ).WorldTransform;

		TeamSpawnPoint best    = null;
		float          bestDist = float.MinValue;

		foreach ( var sp in spawns )
		{
			float closest = living.Min( p => (sp.WorldPosition - p.WorldPosition).LengthSquared );
			if ( closest > bestDist ) { bestDist = closest; best = sp; }
		}

		return best?.WorldTransform;
	}

	void CheckElimination()
	{
		bool t0 = TeamManager.IsTeamAlive( 0 );
		bool t1 = TeamManager.IsTeamAlive( 1 );

		if ( !t0 && !t1 ) EndRound( RoundEndReason.TeamEliminated, -1 );
		else if ( !t0 )   EndRound( RoundEndReason.TeamEliminated, 1 );
		else if ( !t1 )   EndRound( RoundEndReason.TeamEliminated, 0 );
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

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceRoundStart( int roundNumber )
	{
		var t0 = TeamManager?.GetTeam( 0 )?.TeamName ?? "Team 0";
		var t1 = TeamManager?.GetTeam( 1 )?.TeamName ?? "Team 1";
		Log.Info( $"[TDM] Round {roundNumber} — {t0} vs {t1}!" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceRoundEnd( RoundEndReason reason, int winningTeam )
	{
		var winner = winningTeam switch
		{
			0 => TeamManager?.GetTeam( 0 )?.TeamName ?? "Team 0",
			1 => TeamManager?.GetTeam( 1 )?.TeamName ?? "Team 1",
			_ => "Draw",
		};
		Log.Info( $"[TDM] Round over — {winner} | {Team0Score}-{Team1Score}" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceMatchEnd( MatchEndReason reason, int winningTeam )
	{
		var winner = winningTeam switch
		{
			0 => TeamManager?.GetTeam( 0 )?.TeamName ?? "Team 0",
			1 => TeamManager?.GetTeam( 1 )?.TeamName ?? "Team 1",
			_ => "Draw",
		};
		Log.Info( $"[TDM] Match over — {winner} wins! Final: {Team0Score}-{Team1Score}" );
	}
}
