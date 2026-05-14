/// <summary>
/// Source-style death effect. On death:
/// - A ragdoll is spawned in place using ModelPhysics (same approach as sandbox gamemode)
/// - Camera locks at death position
/// - Screen fades to grey/dark with "YOU DIED"
/// - After the gamemode's respawn delay, RequestRespawn is called.
///   Delay is controlled by <see cref="BaseGamemode.GetRespawnDelay"/> — no need to set it here.
/// </summary>
public class PlayerDeathEffect : Component, Local.IPlayerEvents
{
	[RequireComponent] public Player Player { get; set; }

	bool _isDead = false;
	TimeSince _timeSinceDeath;
	float _respawnDelay = 5f; // cached at death time from GameRulesService

	void Local.IPlayerEvents.OnDied( PlayerDiedParams args )
	{
		if ( _isDead ) return;
		_isDead = true;
		_timeSinceDeath = 0;

		// Cache the delay from the gamemode now — GameRulesService may change between now and respawn
		_respawnDelay = GameRulesService.Current?.GetRespawnDelay( Player.PlayerData ) ?? 5f;

		Player.WalkController.Enabled = false;

		CreateRagdoll( Player.Movement.Velocity, args.Attacker?.WorldPosition ?? WorldPosition );

		// Hide the player GO (ragdoll takes over visually)
		if ( Networking.IsHost )
			Player.GameObject.Enabled = false;
	}

	void Local.IPlayerEvents.OnSpawned()
	{
		_isDead = false;
		Player.WalkController.Enabled = true;
		Player.GameObject.Enabled = true;

		if ( Player.IsLocalPlayer )
			IsDeathScreenActive = false;
	}

	protected override void OnUpdate()
	{
		if ( !_isDead ) return;
		if ( !Networking.IsHost ) return;
		if ( _timeSinceDeath < _respawnDelay ) return;

		_isDead = false;

		var playerData = Player.PlayerData;
		Player.GameObject.Destroy();

		// Route through GameRulesService so modes like TDM can hold off respawns.
		// GamemodeManager registers itself there — the base never references it directly.
		if ( GameRulesService.Current is not null )
			GameRulesService.Current.RequestRespawn( playerData );
		else
			GameManager.Current?.SpawnPlayerDelayed( playerData );
	}

	/// <summary>
	/// Spawns a physics ragdoll at the player's current position, matching bones.
	/// Broadcast so all clients see it.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void CreateRagdoll( Vector3 velocity, Vector3 origin )
	{
		var bodyGo = Player.Body;
		if ( !bodyGo.IsValid() ) return;

		var renderer = bodyGo.GetComponent<SkinnedModelRenderer>();
		if ( !renderer.IsValid() ) return;

		// Spawn ragdoll GO at player world position
		var ragdoll = new GameObject( true, $"{Player.DisplayName} Ragdoll" );
		ragdoll.Tags.Add( "ragdoll" );
		ragdoll.WorldTransform = Player.WorldTransform;

		// Copy the main body renderer
		var ragdollRenderer = ragdoll.Components.Create<SkinnedModelRenderer>();
		ragdollRenderer.CopyFrom( renderer );
		ragdollRenderer.UseAnimGraph = false;

		// Copy clothing
		foreach ( var clothing in renderer.GameObject.Children
			.Where( x => x.Tags.Has( "clothing" ) )
			.SelectMany( x => x.Components.GetAll<SkinnedModelRenderer>() ) )
		{
			if ( !clothing.IsValid() ) continue;
			var clothingGo = new GameObject( true, clothing.GameObject.Name );
			clothingGo.Parent = ragdoll;
			var clothingRenderer = clothingGo.Components.Create<SkinnedModelRenderer>();
			clothingRenderer.CopyFrom( clothing );
			clothingRenderer.BoneMergeTarget = ragdollRenderer;
		}

		// Add physics — this is what makes it ragdoll
		var physics = ragdoll.Components.Create<ModelPhysics>();
		physics.Model = ragdollRenderer.Model;
		physics.Renderer = ragdollRenderer;
		physics.CopyBonesFrom( renderer, true );

		// Apply death velocity
		ApplyRagdollForce( physics, velocity, origin );

		// Hide original body
		renderer.Enabled = false;

		// Show death screen on the local client who died
		if ( Player.IsLocalPlayer )
			IsDeathScreenActive = true;

		// Clean up ragdoll after 30s
		ragdollRenderer.Invoke( 30f, ragdoll.Destroy );
	}

	async void ApplyRagdollForce( ModelPhysics physics, Vector3 velocity, Vector3 origin )
	{
		// Brief delay so physics bodies are initialised
		await GameTask.Delay( 10 );

		if ( !physics.IsValid() ) return;

		foreach ( var body in physics.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;

			// Base velocity inherited from player
			rb.Velocity = velocity * 0.5f;

			// Extra push away from damage origin
			if ( origin != Vector3.Zero && velocity.Length > 10f )
				rb.ApplyImpulse( Vector3.Direction( origin, rb.WorldPosition ) * velocity.Length * rb.Mass * 0.3f );
		}
	}

	public static bool IsDeathScreenActive { get; private set; }
}
