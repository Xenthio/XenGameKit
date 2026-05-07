/// <summary>
/// Posts Local.IPlayerEvents camera events from the scene-wide ICameraSetup pipeline,
/// scoped to this player. Attach to the player GameObject.
/// Allows weapons (scopes, etc.) to modify camera per-player via OnCameraMove / OnCameraSetup.
/// </summary>
public class PlayerCameraEvents : Component, ICameraSetup
{
	[RequireComponent] public Player Player { get; set; }

	/// <summary>
	/// Disable viewmodel camera bone animations (the subtle positional shift when shooting).
	/// Useful for gamemodes that want a cleaner or more stylised look.
	/// </summary>
	[Property] public bool DisableViewmodelCameraAnimations
	{
		get => ViewModel.DisableCameraAnimations;
		set => ViewModel.DisableCameraAnimations = value;
	}

	void ICameraSetup.PreSetup( CameraComponent cc )
	{
		if ( !Player.IsLocalPlayer ) return;

		var angles = cc.WorldRotation.Angles();
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, x => x.OnCameraMove( ref angles ) );
		cc.WorldRotation = angles.ToRotation();
	}

	void ICameraSetup.PostSetup( CameraComponent cc )
	{
		if ( !Player.IsLocalPlayer ) return;

		Local.IPlayerEvents.PostToGameObject( Player.GameObject, x => x.OnCameraSetup( cc ) );
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, x => x.OnCameraPostSetup( cc ) );

		// Hide third-person-only objects (e.g. world model props parented to the player root)
		// when in first person. Tag any such renderer as "thirdperson" and it will
		// automatically be excluded from the first-person camera view.
		var isFirstPerson = Player.WalkController?.CameraMode ==
			XMovement.PlayerWalkControllerComplex.CameraModes.FirstPerson;

		if ( isFirstPerson )
			cc.RenderExcludeTags.Add( "thirdperson" );
		else
			cc.RenderExcludeTags.Remove( "thirdperson" );
	}
}
