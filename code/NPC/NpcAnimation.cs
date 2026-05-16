// NpcAnimation — animator parameter wrapper.
// Keeps all the Set() / SetLookDirection() calls in one place.
// Synced properties replicate animation state to proxies.
public class NpcAnimation : Component
{
	[Sync] public Vector3  MoveVelocity { get; private set; }
	[Sync] public Rotation MoveRotation { get; private set; }
	[Sync] public bool     IsLooking    { get; private set; }
	[Sync] public Vector3  LookPos      { get; private set; }
	[Sync] public int      HoldType     { get; private set; }

	SkinnedModelRenderer _renderer;
	BaseNpc              _npc;
	float                _lastYaw = float.NaN;

	protected override void OnStart()
	{
		_npc      = GetComponent<BaseNpc>();
		_renderer = _npc?.Renderer ?? GetComponentInChildren<SkinnedModelRenderer>();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy )
		{
			if ( IsLooking ) ApplyLook( LookPos );
		}

		ApplyMove( MoveVelocity, MoveRotation );
		_renderer?.Set( "holdtype", HoldType );
	}

	// Called by NpcNavigation every frame on the host.
	public void SetMove( Vector3 velocity, Rotation reference )
	{
		MoveVelocity = velocity;
		MoveRotation = reference;
	}

	public void LookAt( Vector3 worldPos )
	{
		IsLooking = true;
		LookPos   = worldPos;
		ApplyLook( worldPos );
	}

	public void StopLooking()
	{
		IsLooking = false;
		_renderer?.SetLookDirection( "aim_eyes", Vector3.Zero, 0f );
		_renderer?.SetLookDirection( "aim_head", Vector3.Zero, 0f );
		_renderer?.SetLookDirection( "aim_body", Vector3.Zero, 0f );
	}

	public void SetHoldType( int type ) => HoldType = type;

	[Rpc.Broadcast]
	public void TriggerAttack() => _renderer?.Set( "b_attack", true );

	void ApplyLook( Vector3 target )
	{
		if ( _renderer is null ) return;
		var dir = (target - WorldPosition).Normal;
		_renderer.SetLookDirection( "aim_eyes", dir, 1f );
		_renderer.SetLookDirection( "aim_head", dir, 1f );
		_renderer.SetLookDirection( "aim_body", dir, 0.5f );
	}

	void ApplyMove( Vector3 velocity, Rotation reference )
	{
		if ( _renderer is null || reference.w == 0f ) return;

		float fwd  = reference.Forward.Dot( velocity );
		float side = reference.Right.Dot( velocity );
		float yaw  = reference.Angles().yaw.NormalizeDegrees();

		float rotSpeed = 0f;
		if ( !float.IsNaN( _lastYaw ) )
			rotSpeed = MathF.Abs( Angles.NormalizeAngle( yaw - _lastYaw ) ) / MathF.Max( Time.Delta, 0.001f );
		_lastYaw = yaw;

		_renderer.Set( "move_speed",       velocity.Length );
		_renderer.Set( "move_groundspeed", velocity.WithZ( 0 ).Length );
		_renderer.Set( "move_x",           fwd );
		_renderer.Set( "move_y",           side );
		_renderer.Set( "move_z",           velocity.z );
		_renderer.Set( "move_direction",   MathF.Atan2( side, fwd ).RadianToDegree().NormalizeDegrees() );
		_renderer.Set( "move_rotationspeed", rotSpeed );
	}
}
