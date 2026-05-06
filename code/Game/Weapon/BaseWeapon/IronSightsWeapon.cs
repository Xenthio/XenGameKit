using Sandbox.Rendering;


/// <summary>
/// A bullet weapon with iron sights (ADS on right-click).
/// Tightens aim cone and triggers ironsights animations while aiming.
/// Subclass this for weapons with ADS, or just use it directly in a prefab.
/// </summary>
public class IronSightsWeapon : BaseBulletWeapon
{
	/// <summary>Multiplier applied to aim cone and recoil while aiming down sights.</summary>
	[Property, Group( "Iron Sights" )] public float AimScale { get; set; } = 0.2f;

	public bool IsAiming { get; private set; }

	public override bool CanSecondaryAttack() => false;

	public override void OnControl( Player player )
	{
		base.OnControl( player );

		var wantsAim = Input.Down( "attack2" );
		if ( wantsAim == IsAiming ) return;

		IsAiming = wantsAim;
		ViewModel?.RunEvent<ViewModel>( x =>
		{
			x.Renderer?.Set( "ironsights", IsAiming ? 1 : 0 );
			x.Renderer?.Set( "ironsights_fire_scale", IsAiming ? AimScale : 1f );
		} );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		IsAiming = false;
	}

	public override void PrimaryAttack() => ShootBullet( IsAiming ? AimScale : 1f );

	public override void DrawHud( HudPainter painter, Vector2 crosshair )
	{
		// Hide crosshair while aiming — the iron sights are the reticle
		if ( IsAiming ) return;
		base.DrawHud( painter, crosshair );
	}
}
