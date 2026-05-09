using Sandbox;
namespace XMovement;

public partial class PlayerWalkControllerComplex : Component
{
	[Property, Group( "Camera" )]
	public bool UseSceneCamera { get; set; } = true;

	[Property, Group( "Camera" ), Change( "SetupCamera" )]
	public CameraModes CameraMode { get; set; } = CameraModes.ThirdPerson;
	public enum CameraModes
	{
		FirstPerson,
		ThirdPerson,
		Manual,
	}

	private bool _isfirstperson => CameraMode == CameraModes.FirstPerson && !UseSceneCamera;
	private bool _isthirdperson => CameraMode == CameraModes.ThirdPerson && !UseSceneCamera;
	private bool _ismanual => CameraMode == CameraModes.Manual && !UseSceneCamera;
	private bool _canToggleCamera => CameraMode != CameraModes.Manual && !UseSceneCamera;

	[Property, Group( "Camera" ), ShowIf( "_ismanual", true )]
	public CameraComponent Camera { get; set; }


	[Property, Group( "Camera" ), ShowIf( "_isfirstperson", true ), Change( "SetupCamera" )]
	public bool PlayerShadowsOnly { get; set; } = true;


	[Property, Group( "Camera" ), ShowIf( "_isfirstperson", true ), Change( "SetupCamera" )]
	public Vector3 FirstPersonOffset { get; set; } = new Vector3( 0, 0, 0 );


	[Property, Group( "Camera" ), ShowIf( "_isthirdperson", true ), Change( "SetupCamera" )]
	public Vector3 ThirdPersonOffset { get; set; } = new Vector3( -180, 0, 0 );

	[Property, InputAction, Group( "Camera" ), ShowIf( "_canToggleCamera", true )]
	public string CameraToggleAction { get; set; } = "View";

	/// <summary>
	/// If true, the camera will apply user preferences such as FOV and other settings.
	/// </summary>
	[Property, Group( "Camera" )]
	public bool ApplyUserPreferences { get; set; } = true;

	public virtual void OnCameraModeChanged() { }
	public void SetupCamera()
	{
		OnCameraModeChanged();
		if ( UseSceneCamera )
		{
			if ( !Game.IsPlaying ) return;
			if ( Scene.Camera.IsValid() ) Camera = Scene.Camera;
		}
		if ( CameraMode != CameraModes.Manual && !Camera.IsValid() )
		{
			var cameraobj = Scene.CreateObject();
			cameraobj.SetParent( Head );
			cameraobj.Name = "Camera";
			Camera = cameraobj.AddComponent<CameraComponent>();
			Camera.Enabled = false;
			Camera.TargetEye = StereoTargetEye.Both;
		}
		if ( CameraMode == CameraModes.FirstPerson )
		{
			Camera.LocalPosition = FirstPersonOffset;
		}
		if ( CameraMode == CameraModes.ThirdPerson )
		{
			Camera.LocalPosition = ThirdPersonOffset;
		}
		if ( Game.IsPlaying )
		{
			if ( !IsProxy ) Camera.Enabled = true;
			UpdateBodyVisibility();
			if ( ApplyUserPreferences )
			{
				Camera.FieldOfView = Preferences.FieldOfView;
			}
		}
	}

	public void UpdateCamera()
	{
		if ( UseSceneCamera && !Camera.IsValid() )
		{
			if ( Scene.Camera.IsValid() ) Camera = Scene.Camera;
		}
		if ( CameraMode == CameraModes.ThirdPerson )
		{
			var start = Head.WorldPosition;
			var end = start + (ThirdPersonOffset * Head.WorldRotation);
			var tr = Scene.Trace.Ray( start, end ).IgnoreDynamic().Radius( 6f ).Run();
			var camPos = tr.EndPosition;

			if ( UseSceneCamera )
			{
				// Scene camera has no parent — drive world pos/rot directly
				Camera.WorldPosition = camPos;
				Camera.WorldRotation = Rotation.LookAt( start - camPos, Vector3.Up );
			}
			else
			{
				// Owned camera is parented to Head — use local position
				Camera.LocalPosition = ThirdPersonOffset * tr.Fraction;
			}
		}
		if ( CameraMode == CameraModes.FirstPerson )
		{
			if ( UseSceneCamera )
			{
				Camera.WorldPosition = Head.WorldPosition + (FirstPersonOffset * Head.WorldRotation);
				Camera.WorldRotation = Head.WorldRotation;
			}
		}
		if ( Input.Pressed( CameraToggleAction ) )
		{
			if ( CameraMode == CameraModes.ThirdPerson ) CameraMode = CameraModes.FirstPerson;
			else if ( CameraMode == CameraModes.FirstPerson ) CameraMode = CameraModes.ThirdPerson;
		}
	}

	public void UpdateBodyVisibility()
	{
		if ( IsProxy )
		{
			foreach ( var mdlrenderer in Body.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndChildren ) )
			{
				mdlrenderer.RenderType = Sandbox.ModelRenderer.ShadowRenderType.On;
			}
			return;
		}
		if ( CameraMode == CameraModes.FirstPerson )
		{
			foreach ( var mdlrenderer in Body.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndChildren ) )
			{
				mdlrenderer.RenderType = PlayerShadowsOnly ? Sandbox.ModelRenderer.ShadowRenderType.ShadowsOnly : Sandbox.ModelRenderer.ShadowRenderType.On;
			}
		}
		if ( CameraMode == CameraModes.ThirdPerson )
		{
			if ( BodyModelRenderer.RenderType == Sandbox.ModelRenderer.ShadowRenderType.ShadowsOnly && PlayerShadowsOnly )
			{
				foreach ( var mdlrenderer in Body.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndChildren ) )
				{
					mdlrenderer.RenderType = Sandbox.ModelRenderer.ShadowRenderType.On;
				}
			}
		}
	}
}
