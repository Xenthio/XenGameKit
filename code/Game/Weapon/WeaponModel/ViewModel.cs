using System.Threading;
using XMovement;

public sealed partial class ViewModel : WeaponModel, ICameraSetup
{
	// ICameraSetup is defined in code/Utility/CameraSetup.cs
	public record struct ReloadSoundEntry
	{
		[KeyProperty] public float Time { get; set; }
		[Property, KeyProperty] public SoundEvent Sound { get; set; }
	}

	[Property, Group( "Reload Sounds" )] public List<ReloadSoundEntry> ReloadSoundEvents { get; set; } = new();
	[Property, Group( "Reload Sounds" )] public List<ReloadSoundEntry> IncrementalReloadSoundEvents { get; set; } = new();
	[Property, Group( "Reload Sounds" )] public List<ReloadSoundEntry> IncrementalReloadStartSounds { get; set; } = new();
	[Property, Group( "Reload Sounds" )] public List<ReloadSoundEntry> IncrementalReloadFinishSounds { get; set; } = new();

	private CancellationTokenSource _reloadSoundCts;
	private CancellationTokenSource _reloadFinishSoundCts;

	[Property, Group( "Animation" )] public bool IsIncremental { get; set; } = false;
	[Property, Group( "Animation" )] public float AnimationSpeed { get; set; } = 1.0f;
	[Property, Group( "Animation" )] public float IncrementalAnimationSpeed { get; set; } = 3.0f;
	[Property] public bool UseFastAnimations { get; set; } = false;

	[Property, Group( "Inertia" )] Vector2 InertiaScale { get; set; } = new Vector2( 2, 2 );

	public bool IsAttacking { get; set; }
	TimeSince AttackDuration;

	bool _reloadFinishing;
	TimeSince _reloadFinishTimer;

	Vector2 lastInertia;
	Vector2 currentInertia;
	bool isFirstUpdate = true;

	protected override void OnStart()
	{
		foreach ( var renderer in GetComponentsInChildren<ModelRenderer>() )
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	protected override void OnUpdate()
	{
		UpdateAnimation();
	}

	void ApplyInertia()
	{
		var rot = Scene.Camera.WorldRotation.Angles();

		if ( isFirstUpdate )
		{
			lastInertia = new Vector2( rot.pitch, rot.yaw );
			currentInertia = Vector2.Zero;
			isFirstUpdate = false;
		}

		var newPitch = rot.pitch;
		var newYaw = rot.yaw;

		currentInertia = new Vector2( Angles.NormalizeAngle( newPitch - lastInertia.x ), Angles.NormalizeAngle( lastInertia.y - newYaw ) );
		lastInertia = new( newPitch, newYaw );
	}

	/// <summary>
	/// When true, viewmodel camera bone animations are suppressed.
	/// Game creators can toggle this off for a cleaner or more stylised look.
	/// </summary>
	public static bool DisableCameraAnimations { get; set; } = false;

	void ICameraSetup.Setup( CameraComponent cc )  // place viewmodel
	{
		Renderer.Enabled = true;

		WorldPosition = cc.WorldPosition;
		WorldRotation = cc.WorldRotation;

		ApplyInertia();

		if ( !DisableCameraAnimations )
			ApplyAnimationTransform( cc );
	}

	void ApplyAnimationTransform( CameraComponent cc )
	{
		if ( !Renderer.IsValid() ) return;

		if ( Renderer.TryGetBoneTransformLocal( "camera", out var bone ) )
		{
			var scale = 0.5f;
			cc.WorldPosition += cc.WorldRotation * bone.Position * scale;
			cc.WorldRotation *= bone.Rotation * scale;
		}
	}

	void UpdateAnimation()
	{
		var playerController = GetComponentInParent<PlayerWalkControllerComplex>();
		if ( !playerController.IsValid() ) return;

		var rot = Scene.Camera.WorldRotation.Angles();

		Renderer.Set( "b_twohanded", true );
		Renderer.Set( "deploy_type", UseFastAnimations ? 1 : 0 );
		Renderer.Set( "reload_type", UseFastAnimations ? 1 : 0 );
		Renderer.Set( "b_grounded", playerController.Controller.IsOnGround );
		Renderer.Set( "move_bob", playerController.Controller.Velocity.Length.Remap( 0, playerController.RunSpeed * 2f ) );
		Renderer.Set( "aim_pitch", rot.pitch );
		Renderer.Set( "aim_pitch_inertia", currentInertia.x * InertiaScale.x );
		Renderer.Set( "aim_yaw", rot.yaw );
		Renderer.Set( "aim_yaw_inertia", currentInertia.y * InertiaScale.y );
		Renderer.Set( "attack_hold", IsAttacking ? AttackDuration.Relative.Clamp( 0f, 1f ) : 0f );

		if ( _reloadFinishing && _reloadFinishTimer >= 0.5f )
		{
			_reloadFinishing = false;
			Renderer.Set( "speed_reload", AnimationSpeed );
			Renderer.Set( "b_reloading", false );
		}

		var velocity = playerController.Controller.Velocity;
		var dir = velocity;
		var forward = Scene.Camera.WorldRotation.Forward.Dot( dir );
		var sideward = Scene.Camera.WorldRotation.Right.Dot( dir );
		var angle = MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees();

		Renderer.Set( "move_direction", angle );
		Renderer.Set( "move_speed", velocity.Length );
		Renderer.Set( "move_groundspeed", velocity.WithZ( 0 ).Length );
		Renderer.Set( "move_y", sideward );
		Renderer.Set( "move_x", forward );
		Renderer.Set( "move_z", velocity.z );
	}

	public override void OnAttack()
	{
		Renderer?.Set( "b_attack", true );

		DoMuzzleEffect();
		DoEjectBrass();

		if ( IsThrowable )
		{
			Renderer?.Set( "b_throw", true );

			Invoke( 0.5f, () =>
			{
				Renderer?.Set( "b_deploy_new", true );
				Renderer?.Set( "b_pull", false );
			} );
		}
	}

	public override void CreateRangedEffects( BaseWeapon weapon, Vector3 hitPoint, Vector3? origin )
	{
		DoTracerEffect( hitPoint, origin );
	}

	public void OnReloadStart()
	{
		_reloadFinishing = false;
		Renderer?.Set( "speed_reload", AnimationSpeed );
		Renderer?.Set( IsIncremental ? "b_reloading" : "b_reload", true );

		if ( IsIncremental )
			StartSounds( IncrementalReloadStartSounds, ref _reloadFinishSoundCts );

		StartSounds( ReloadSoundEvents, ref _reloadSoundCts );

		// Drop magazine after MagazineDropTime seconds (cancels if reload is cancelled)
		DoDropMagazine( _reloadSoundCts?.Token ?? default );
	}

	public void OnIncrementalReload()
	{
		Renderer?.Set( "speed_reload", IncrementalAnimationSpeed );
		Renderer?.Set( "b_reloading_shell", true );

		StartSounds( IncrementalReloadSoundEvents, ref _reloadSoundCts );
	}

	public void OnReloadFinish()
	{
		CancelSounds( ref _reloadSoundCts );

		if ( IsIncremental )
		{
			StartSounds( IncrementalReloadFinishSounds, ref _reloadFinishSoundCts );
			_reloadFinishing = true;
			_reloadFinishTimer = 0;
		}
		else
		{
			Renderer?.Set( "b_reload", false );
		}
	}

	public void OnReloadCancel()
	{
		CancelSounds( ref _reloadSoundCts );
		CancelSounds( ref _reloadFinishSoundCts );
	}

	private void StartSounds( List<ReloadSoundEntry> events, ref CancellationTokenSource cts )
	{
		CancelSounds( ref cts );
		if ( events.Count == 0 ) return;
		cts = new CancellationTokenSource();
		_ = PlaySoundsAsync( events, cts.Token );
	}

	private void CancelSounds( ref CancellationTokenSource cts )
	{
		if ( cts is null ) return;
		cts.Cancel();
		cts.Dispose();
		cts = null;
	}

	private async Task PlaySoundsAsync( List<ReloadSoundEntry> events, CancellationToken ct )
	{
		var sorted = events.OrderBy( e => e.Time ).ToList();
		var elapsed = 0f;

		foreach ( var entry in sorted )
		{
			var delay = entry.Time - elapsed;
			if ( delay > 0f ) await Task.DelaySeconds( delay, ct );
			if ( ct.IsCancellationRequested ) return;
			if ( entry.Sound is not null ) GameObject.PlaySound( entry.Sound );
			elapsed = entry.Time;
		}
	}
}
