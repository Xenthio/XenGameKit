/// <summary>
/// Source-style spectator. After death, if the gamemode doesn't respawn the player, this kicks in.
/// Lets dead players free-fly and cycle through living players with +attack / +attack2.
///
/// Add this to the Player GameObject alongside Player. It activates automatically on death
/// and deactivates on respawn. No setup needed — just drop it on your player prefab.
///
/// Spectator modes (matching Source):
///   Free     — noclip-style free roam (OBS_MODE_ROAMING in Source)
///   FirstPerson — locked to a target's eyes (OBS_MODE_IN_EYE)
///   ThirdPerson — orbiting camera behind a target (OBS_MODE_CHASE)
/// </summary>
public class PlayerSpectator : Component, Local.IPlayerEvents
{
	public enum SpectatorMode { Free, FirstPerson, ThirdPerson }

	[Property] public SpectatorMode DefaultMode { get; set; } = SpectatorMode.ThirdPerson;
	[Property] public float         FlySpeed    { get; set; } = 500f;
	[Property] public float         OrbitDist   { get; set; } = 150f;

	[RequireComponent] public Player Player { get; set; }

	public bool IsSpectating { get; private set; }
	public Player            Target      { get; private set; }
	public SpectatorMode     Mode        { get; private set; }

	CameraComponent _camera;

	// ─── Lifecycle ─────────────────────────────────────────────────────────────

	void Local.IPlayerEvents.OnDied( PlayerDiedParams args )
	{
		if ( !Player.IsLocalPlayer ) return;
		IsSpectating = true;
		Mode         = DefaultMode;
		PickNextTarget( 1 );
		Player.WalkController.Enabled = false;
	}

	void Local.IPlayerEvents.OnSpawned()
	{
		IsSpectating = false;
		Target       = null;
	}

	protected override void OnUpdate()
	{
		if ( !IsSpectating || !Player.IsLocalPlayer ) return;

		HandleModeSwitch();
		HandleTargetCycle();

		switch ( Mode )
		{
			case SpectatorMode.Free:        UpdateFree();        break;
			case SpectatorMode.FirstPerson: UpdateFirstPerson(); break;
			case SpectatorMode.ThirdPerson: UpdateThirdPerson(); break;
		}
	}

	// ─── Input ────────────────────────────────────────────────────────────────

	void HandleModeSwitch()
	{
		// Cycle mode on +jump
		if ( Input.Pressed( "Jump" ) )
		{
			Mode = Mode switch
			{
				SpectatorMode.Free        => SpectatorMode.FirstPerson,
				SpectatorMode.FirstPerson => SpectatorMode.ThirdPerson,
				_                         => SpectatorMode.Free,
			};
		}
	}

	void HandleTargetCycle()
	{
		if ( Mode == SpectatorMode.Free ) return;

		if ( Input.Pressed( "attack1" ) ) PickNextTarget( 1 );
		if ( Input.Pressed( "attack2" ) ) PickNextTarget( -1 );
	}

	void PickNextTarget( int direction )
	{
		var alive = Game.ActiveScene.GetAll<Player>()
			.Where( p => p.IsValid() && !p.IsDead && p != Player )
			.ToList();

		if ( alive.Count == 0 ) { Target = null; return; }

		int current = Target.IsValid() ? alive.IndexOf( Target ) : -1;
		int next    = ( current + direction + alive.Count ) % alive.Count;
		Target      = alive[next];
	}

	// ─── Camera modes ─────────────────────────────────────────────────────────

	void UpdateFree()
	{
		if ( _camera is null ) FindCamera();
		if ( _camera is null ) return;

		var angles = _camera.WorldRotation.Angles();
		angles    += Input.AnalogLook;
		angles.roll = 0f;

		var rot = angles.ToRotation();
		var vel = rot * Input.AnalogMove * FlySpeed;

		_camera.WorldPosition += vel * Time.Delta;
		_camera.WorldRotation  = rot;
	}

	void UpdateFirstPerson()
	{
		if ( !Target.IsValid() ) { PickNextTarget( 1 ); return; }
		if ( _camera is null ) FindCamera();
		if ( _camera is null ) return;

		_camera.WorldPosition = Target.EyeTransform.Position;
		_camera.WorldRotation = Target.EyeTransform.Rotation;
	}

	void UpdateThirdPerson()
	{
		if ( !Target.IsValid() ) { PickNextTarget( 1 ); return; }
		if ( _camera is null ) FindCamera();
		if ( _camera is null ) return;

		var angles  = _camera.WorldRotation.Angles();
		angles     += Input.AnalogLook;
		angles.roll = 0f;

		var rot       = angles.ToRotation();
		var targetPos = Target.WorldPosition + Vector3.Up * 64f;
		var wantPos   = targetPos - rot.Forward * OrbitDist;

		// Push out of walls
		var tr = Game.ActiveScene.Trace
			.Ray( targetPos, wantPos )
			.IgnoreGameObjectHierarchy( Player.GameObject )
			.IgnoreGameObjectHierarchy( Target.GameObject )
			.WithoutTags( "trigger" )
			.Run();

		_camera.WorldPosition = tr.Hit ? tr.HitPosition + tr.Normal * 4f : wantPos;
		_camera.WorldRotation = Rotation.LookAt( targetPos - _camera.WorldPosition );
	}

	void FindCamera()
	{
		_camera = Game.ActiveScene.GetAll<CameraComponent>().FirstOrDefault( c => c.IsMainCamera );
	}
}
