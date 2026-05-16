/// <summary>
/// Free For All. Everyone fights everyone — first to KillLimit wins,
/// or whoever has the most frags when time runs out.
/// </summary>
public sealed class FFAGamemode : BaseGamemode
{
	[Property] public int   KillLimit             { get; set; } = 30;
	[Property] public float RoundTimeLimitSeconds { get; set; } = 600f;

	[Sync( SyncFlags.FromHost )] public bool  MatchOver     { get; private set; }
	[Sync( SyncFlags.FromHost )] public float TimeRemaining { get; private set; }

	TimeSince _elapsed;

	public override void OnGamemodeStart()
	{
		base.OnGamemodeStart();
		if ( !Networking.IsHost ) return;

		MatchOver     = false;
		TimeRemaining = RoundTimeLimitSeconds > 0 ? RoundTimeLimitSeconds : float.MaxValue;
		_elapsed      = 0;

		SetPhase( RoundPhase.Active );
		AnnounceMatchStart();
	}

	public override void OnHostBecame() => _elapsed = RoundTimeLimitSeconds - TimeRemaining;

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !IsActive || MatchOver ) return;

		if ( RoundTimeLimitSeconds > 0 )
		{
			TimeRemaining = MathF.Max( 0f, RoundTimeLimitSeconds - _elapsed );
			if ( TimeRemaining <= 0f ) EndMatch( MatchEndReason.TimeLimit );
		}
	}

	protected override void OnPlayerDied( Player player, PlayerDiedParams args )
	{
		if ( !Networking.IsHost || !IsActive || MatchOver || KillLimit <= 0 ) return;

		var attacker = PlayerData.For( args.InstigatorId );
		if ( attacker.IsValid() && attacker.Kills >= KillLimit )
			EndMatch( MatchEndReason.ScoreLimit );
	}

	// Everyone respawns immediately — no teams, no waiting.
	public override void RequestRespawn( PlayerData playerData )
		=> GameManager.Current?.SpawnPlayerDelayed( playerData );

	public override bool CanDamage( Player attacker, Player victim ) => attacker != victim;

	void EndMatch( MatchEndReason reason )
	{
		if ( !Networking.IsHost ) return;
		MatchOver = true;
		IsActive  = false;
		SetPhase( RoundPhase.MatchOver );
		Global.IGamemodeEvents.Post( x => x.OnMatchEnd( new MatchEndEvent { Reason = reason, WinningTeam = -1 } ) );
		AnnounceMatchEnd( reason );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceMatchStart() => Log.Info( "[FFA] Match started!" );

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void AnnounceMatchEnd( MatchEndReason reason )
	{
		var winner = PlayerData.All.OrderByDescending( pd => pd.Kills ).FirstOrDefault();
		Log.Info( $"[FFA] Match over ({reason}) — winner: {(winner.IsValid() ? winner.DisplayName : "Nobody")}" );
	}
}
