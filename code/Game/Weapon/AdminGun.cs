using Sandbox.Rendering;

/// <summary>
/// Admin/debug weapon. Left click fires rapid hitscan bullets, right click spawns an explosion at the aim point.
/// No ammo required — infinite everything.
/// </summary>
public class AdminGun : BaseBulletWeapon
{
	[Property, Group( "Admin Gun" )] public float ExplosionMagnitude { get; set; } = 100f;
	[Property, Group( "Admin Gun" )] public float ExplosionRadius    { get; set; } = 256f;

	// Rapid autofire on primary
	protected override float GetPrimaryFireRate() => 0.05f;

	public override void PrimaryAttack()
	{
		AddShootDelay( GetPrimaryFireRate() );
		TimeSinceShoot = 0;

		Bullet.Fire( new BulletInfo
		{
			Origin    = AimRay.Position,
			Direction = AimRay.Forward,
			Damage    = Damage,
			Radius    = BulletRadius,
			Range     = Range,
			Force     = ShootForce,
			Count     = 1,
			Attacker  = Owner?.GameObject,
			Weapon    = GameObject,
		} );

		if ( HasOwner )
		{
			Owner.WalkController.EyeAngles += new Angles(
				Random.Shared.Float( RecoilPitch.x, RecoilPitch.y ),
				Random.Shared.Float( RecoilYaw.x,   RecoilYaw.y ),
				0 );
		}
	}

	// Right click fires an explosion at the aim hit point
	protected override float GetSecondaryFireRate() => 0.5f;

	public override void SecondaryAttack()
	{
		if ( !Networking.IsHost ) return;

		AddShootDelay( GetSecondaryFireRate() );

		var tr = Scene.Trace
			.Ray( AimRay, Range )
			.IgnoreGameObjectHierarchy( Owner?.GameObject )
			.WithoutTags( "trigger", "playercontroller" )
			.Run();

		var pos = tr.Hit ? tr.HitPosition : AimRay.Position + AimRay.Forward * 512f;

		Explosion.Blast( new ExplosionInfo
		{
			Position    = pos,
			Magnitude   = ExplosionMagnitude,
			Radius      = ExplosionRadius,
			DoDamage    = true,
			DamageForce = 1f,
			Attacker    = Owner?.GameObject,
			Weapon      = GameObject,
		} );
	}

	// Crosshair matches primary aim cone
	public override void DrawCrosshair( HudPainter hud, Vector2 center )
	{
		var t    = GetAimConeAmount();
		var gap  = AimConeBase.x + t * AimConeSpread.x;
		var len  = 8f;
		var w    = 2f;
		var col  = CanPrimaryAttack() ? CrosshairCanShoot : CrosshairNoShoot;

		hud.SetBlendMode( BlendMode.Lighten );
		hud.DrawLine( center + Vector2.Left * (len + gap), center + Vector2.Left * gap, w, col );
		hud.DrawLine( center - Vector2.Left * (len + gap), center - Vector2.Left * gap, w, col );
		hud.DrawLine( center + Vector2.Up   * (len + gap), center + Vector2.Up   * gap, w, col );
		hud.DrawLine( center - Vector2.Up   * (len + gap), center - Vector2.Up   * gap, w, col );
	}
}
