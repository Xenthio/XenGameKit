using Sandbox.Rendering;
using XMovement;

public partial class BaseWeapon : BaseCarryable
{
	[Property] public float DeployTime { get; set; } = 0.5f;

	public override bool ShouldAvoid => !HasAmmo();

	protected TimeUntil TimeUntilNextShotAllowed;

	public void AddShootDelay( float seconds )
	{
		TimeUntilNextShotAllowed = seconds;
	}

	private static SoundEvent DryFireSound = new SoundEvent( "sounds/dry_fire.sound" );

	public void DryFire()
	{
		if ( HasAmmo() ) return;
		if ( IsReloading() ) return;
		if ( TimeUntilNextShotAllowed > 0 ) return;

		GameObject.PlaySound( DryFireSound );
	}

	public virtual void TryAutoReload()
	{
		if ( HasAmmo() ) return;
		if ( IsReloading() ) return;
		if ( TimeUntilNextShotAllowed > 0 ) return;

		DryFire();
		AddShootDelay( 0.1f );

		if ( CanReload() )
			OnReloadStart();
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();

		AddShootDelay( DeployTime );
	}

	public override void OnAdded( Player player )
	{
		base.OnAdded( player );

		if ( !UsesAmmo ) return;

		if ( AmmoType is not null )
		{
			var inv = GetAmmoInventory();
			if ( inv is not null && !inv.HasAmmo( AmmoType ) && AmmoType.DefaultStartingAmmo > 0 )
				inv.AddAmmo( AmmoType, AmmoType.DefaultStartingAmmo );
		}
		else if ( StartingAmmo > 0 )
		{
			_reserveAmmo = Math.Min( StartingAmmo, _maxReserveAmmo );
		}
	}

	public override void DrawHud( HudPainter painter, Vector2 crosshair )
	{
		DrawCrosshair( painter, crosshair );
	}

	public override void OnFrameUpdate( Player player )
	{
		if ( player is null ) return;

		if ( player.WalkController?.CameraMode != PlayerWalkControllerComplex.CameraModes.ThirdPerson )
			CreateViewModel();
		else
			DestroyViewModel();

		GameObject.Network.Interpolation = false;
	}

	public override void OnPlayerUpdate( Player player )
	{
		if ( player is null ) return;

		if ( !player.IsLocalPlayer ) return;

		OnControl( player );
	}

	public override void OnControl( Player player )
	{
		bool wantsToCancelReload = Input.Pressed( "Attack1" ) || Input.Pressed( "Attack2" );
		if ( CanCancelReload && IsReloading() && wantsToCancelReload && HasAmmo() )
			CancelReload();

		if ( CanReload() && Input.Pressed( "reload" ) )
			OnReloadStart();

		if ( CanPrimaryAttack() && WantsPrimaryAttack() )
			PrimaryAttack();

		if ( CanSecondaryAttack() && WantsSecondaryAttack() )
			SecondaryAttack();
	}

	protected virtual bool WantsPrimaryAttack() => Input.Down( "attack1" );
	protected virtual bool WantsSecondaryAttack() => Input.Down( "attack2" );

	public virtual void PrimaryAttack() { }
	public virtual void SecondaryAttack() { }

	public virtual bool CanPrimaryAttack()
	{
		if ( HasOwner && !HasAmmo() ) return false;
		if ( IsReloading() ) return false;
		if ( TimeUntilNextShotAllowed > 0 ) return false;
		return true;
	}

	public virtual bool CanSecondaryAttack()
	{
		if ( HasOwner && !HasAmmo() ) return false;
		if ( IsReloading() ) return false;
		if ( TimeUntilNextShotAllowed > 0 ) return false;
		return true;
	}

	protected virtual float GetPrimaryFireRate() => 0.1f;
	protected virtual float GetSecondaryFireRate() => 0.2f;

	public virtual void DrawCrosshair( HudPainter hud, Vector2 center )
	{
		var color = Color.Red;
		hud.DrawLine( center + Vector2.Left * 32, center + Vector2.Left * 15, 3, color );
		hud.DrawLine( center - Vector2.Left * 32, center - Vector2.Left * 15, 3, color );
		hud.DrawLine( center + Vector2.Up * 32, center + Vector2.Up * 15, 3, color );
		hud.DrawLine( center - Vector2.Up * 32, center - Vector2.Up * 15, 3, color );
	}

	protected Color CrosshairCanShoot => Color.White;
	protected Color CrosshairNoShoot => Color.Red;
}
