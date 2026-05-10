using Sandbox.Rendering;

public enum FragThrowType
{
	Far = 0,
	Near = 1
}

/// <summary>
/// Sandbox-style frag grenade weapon.
/// Cooks while held, supports near/far throws, and detonates through XenGameKit's Explosion utility.
/// </summary>
public sealed class FragGrenadeWeapon : BaseWeapon
{
	[Property] public GameObject GrenadePrefab { get; set; }
	[Property] public float ThrowPower { get; set; } = 1200f;
	[Property] public float Lifetime { get; set; } = 3f;
	[Property] public float Radius { get; set; } = 256f;
	[Property] public float Magnitude { get; set; } = 125f;
	[Property] public float DamageForce { get; set; } = 1f;

	TimeSince _timeSinceCooked;
	bool _isCooking;
	bool _isThrowing;
	TimeUntil _timeUntilThrown;
	FragThrowType _currentThrowType = FragThrowType.Far;
	float _throwBlend;

	public override bool IsInUse() => _isCooking || _isThrowing;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		AddShootDelay( 0.5f );
	}

	public override void OnPlayerDeath( PlayerDiedParams args )
	{
		if ( !_isCooking )
			return;

		var player = Owner;
		if ( !player.IsValid() )
			return;

		var fuseTime = MathF.Max( 0.1f, Lifetime - _timeSinceCooked );
		_isCooking = false;
		SpawnThrownGrenade( player.WorldPosition + Vector3.Up * 32f, Vector3.Down * (ThrowPower * 0.2f), fuseTime );
	}

	public override void OnControl( Player player )
	{
		if ( !player.IsValid() )
			return;

		if ( _isThrowing )
		{
			if ( _timeUntilThrown )
				FinishThrow();
			return;
		}

		if ( !_isCooking && (Input.Pressed( "attack1" ) || Input.Pressed( "attack2" )) )
		{
			if ( !CanPrimaryAttack() )
			{
				DryFire();
				return;
			}

			StartCooking();
		}

		if ( !_isCooking )
			return;

		UpdateThrowType();

		if ( _timeSinceCooked > Lifetime )
		{
			ExplodeInHand();
			return;
		}

		if ( !Input.Down( "attack1" ) && !Input.Down( "attack2" ) )
			Throw( player );
	}

	void StartCooking()
	{
		_isCooking = true;
		_timeSinceCooked = 0f;

		WeaponModel?.Renderer?.Set( "b_charge", true );
		WeaponModel?.Renderer?.Set( "charge_type", 0 );
	}

	void UpdateThrowType()
	{
		bool attack1 = Input.Down( "attack1" );
		bool attack2 = Input.Down( "attack2" );

		float target = attack1 && attack2 ? 0.5f : attack2 ? 1.0f : 0.0f;
		_throwBlend = _throwBlend.LerpTo( target, Time.Delta * 3.0f );
		_currentThrowType = _throwBlend < 0.4f ? FragThrowType.Far : FragThrowType.Near;

		WeaponModel?.Renderer?.Set( "throw_blend", _throwBlend );
		WeaponModel?.Renderer?.Set( "throw_type", (int)_currentThrowType );
	}

	void Throw( Player player, Vector3? overrideDirection = null, float powerScale = 1f )
	{
		_isCooking = false;

		if ( !TakeAmmo( 1 ) )
		{
			CleanupAfterUse();
			return;
		}

		var direction = overrideDirection ?? player.EyeTransform.Rotation.Forward;
		if ( !overrideDirection.HasValue && _currentThrowType == FragThrowType.Near )
		{
			direction = (direction + Vector3.Up * 0.3f).Normal;
			powerScale *= 0.5f;
		}

		var startPos = GetThrowPosition( player, direction );
		var velocity = GetThrowVelocity( player, direction, powerScale, true );
		var fuseTime = MathF.Max( 0.1f, Lifetime - _timeSinceCooked );

		SpawnThrownGrenade( startPos, velocity, fuseTime );

		WeaponModel?.GameObject.RunEvent<WeaponModel>( x => x.OnAttack() );
		Owner?.WalkController?.BodyModelRenderer?.Set( "b_attack", true );

		AddShootDelay( 1f );
		_isThrowing = true;
		_timeUntilThrown = 0.5f;
	}

	Vector3 GetThrowPosition( Player player, Vector3 direction )
	{
		var eye = player.EyeTransform;
		var target = eye.Position + direction * 18f + eye.Rotation.Right * 8f;

		var tr = Scene.Trace.Ray( eye.Position, target )
			.WithoutTags( "trigger", "ragdoll" )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.Run();

		return tr.Hit ? tr.EndPosition : target;
	}

	Vector3 GetThrowVelocity( Player player, Vector3 direction, float powerScale, bool addLift )
	{
		var baseVelocity = player.Movement?.Velocity ?? Vector3.Zero;
		var throwVelocity = baseVelocity + direction * (ThrowPower * powerScale);
		if ( addLift )
			throwVelocity += Vector3.Up * 100f;

		return throwVelocity;
	}

	[Rpc.Host]
	void SpawnThrownGrenade( Vector3 startPos, Vector3 velocity, float fuseTime )
	{
		if ( !GrenadePrefab.IsValid() )
			return;

		var direction = velocity.Length > 1f ? velocity.Normal : Rotation.Identity.Forward;

		var grenade = Grenade.Throw( new GrenadeThrowInfo
		{
			GrenadePrefab = GrenadePrefab,
			Position = startPos,
			Rotation = Rotation.LookAt( direction, Vector3.Up ),
			Velocity = velocity,
			AngularVelocity = Vector3.Right * 10f,
			FuseTimeOverride = fuseTime,
			Attacker = Owner?.GameObject,
			Weapon = GameObject,
		} );

		if ( !grenade.IsValid() )
			return;

		grenade.Magnitude = Magnitude;
		grenade.Radius = Radius;
		grenade.DamageForce = DamageForce;
	}

	void FinishThrow()
	{
		_isThrowing = false;

		if ( !HasAmmo() )
		{
			CleanupAfterUse();
			return;
		}

		WeaponModel?.Renderer?.Set( "b_deploy_new", true );
		WeaponModel?.Renderer?.Set( "b_pull", false );
	}

	void CleanupAfterUse()
	{
		var inventory = Owner?.GetComponent<PlayerInventory>();
		var next = inventory?.Weapons
			.Where( x => x != this && x.CanSwitch() && !x.ShouldAvoid )
			.OrderByDescending( x => x.Value )
			.FirstOrDefault();

		if ( next.IsValid() )
			inventory?.SwitchWeapon( next );
		else
			inventory?.SwitchWeapon( null, true );

		DestroyGameObject();
	}

	void ExplodeInHand()
	{
		_isCooking = false;

		if ( !TakeAmmo( 1 ) )
		{
			CleanupAfterUse();
			return;
		}

		var explosionPos = Owner.IsValid() ? Owner.EyeTransform.Position : WorldPosition;
		Explosion.Blast( new ExplosionInfo
		{
			Position = explosionPos,
			Magnitude = Magnitude,
			Radius = Radius,
			DamageForce = DamageForce,
			DoDamage = true,
			Attacker = Owner?.GameObject ?? GameObject,
			Weapon = GameObject,
			Ignore = GameObject,
		} );

		CleanupAfterUse();
	}

	public override void DrawCrosshair( HudPainter hud, Vector2 center )
	{
		var color = HasAmmo() ? CrosshairCanShoot : CrosshairNoShoot;
		hud.SetBlendMode( BlendMode.Lighten );
		hud.DrawCircle( center, 6f, color );
	}
}