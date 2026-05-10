using XMovement;

/// <summary>
/// HL2-style +USE prop carry. Trace from eye on Use press to find a physics prop,
/// hold it in front of the player each tick via a spring-damper (mirrors HL2's
/// CGrabController / IPhysicsMotionController shadow approach).
/// 
/// Key HL2 design references (weapon_physcannon.cpp / CGrabController):
///   - Shadow controller drives the object toward a target pos/ang every physics tick
///   - maxSpeed / maxAngular cap how fast it can be steered (prevents tunnelling)
///   - Rotation is tracked in player-local space so the object keeps its relative angle
///   - On throw: forward velocity = viewDir * mass-scaled force
///   - On drop: clear velocity so it doesn't launch away
///   - Gravity compensation: remove gravity while held so it doesn't sag
///   - Mass limit: props above ~35 kg resist / can't be picked up
/// </summary>
public class PlayerPropCarry : Component
{
	[RequireComponent] public Player Player { get; set; }

	// -------------------------------------------------------------------------
	// Tunable properties (exposed so they can be tweaked per-prefab in editor)
	// -------------------------------------------------------------------------

	/// <summary>Max trace distance for the initial grab ray.</summary>
	[Property, Group( "Carry" )] public float PickupRange { get; set; } = 85f;

	/// <summary>World units in front of the eye to hold the object.</summary>
	[Property, Group( "Carry" )] public float HoldDistance { get; set; } = 72f;
	/// <summary>Minimum hold distance when close to walls/obstacles.</summary>
	[Property, Group( "Carry" )] public float MinHoldDistance { get; set; } = 24f;
	/// <summary>How much to pull back from walls when the hold ray hits geometry.</summary>
	[Property, Group( "Carry" )] public float HoldWallPadding { get; set; } = 8f;

	/// <summary>Spring constant — how aggressively the object chases the hold point.</summary>
	[Property, Group( "Carry" )] public float SpringStrength { get; set; } = 80f;

	/// <summary>Damping factor — kills oscillation. Higher = stiffer.</summary>
	[Property, Group( "Carry" )] public float Damping { get; set; } = 15f;

	/// <summary>Angular damping — prevents free spinning.</summary>
	[Property, Group( "Carry" )] public float AngularDamping { get; set; } = 30f;
	/// <summary>Mass assigned while an object is held, matching HL2's reduced carry mass idea.</summary>
	[Property, Group( "Carry" )] public float HeldMass { get; set; } = 1f;

	/// <summary>Max speed the object is allowed to move while being steered (units/s).</summary>
	[Property, Group( "Carry" )] public float MaxCarrySpeed { get; set; } = 600f;

	/// <summary>Max angular speed while carried (deg/s).</summary>
	[Property, Group( "Carry" )] public float MaxCarryAngular { get; set; } = 180f;

	/// <summary>If true, rotate held props using HL2-style player-space angle tracking.</summary>
	[Property, Group( "Carry" )] public bool UseHl2CarryRotation { get; set; } = true;
	/// <summary>HL2 +USE carry ignores player pitch when resolving held prop orientation.</summary>
	[Property, Group( "Carry" )] public bool IgnoreCarryPitch { get; set; } = true;
	/// <summary>Snap carry angles toward major axes like HL2's 30-degree alignment step.</summary>
	[Property, Group( "Carry" )] public bool AlignCarryAngles { get; set; } = true;

	/// <summary>Objects heavier than this (kg) cannot be picked up.</summary>
	[Property, Group( "Carry" )] public float MaxMass { get; set; } = 35f;

	/// <summary>Throw impulse velocity (units/s). HL2 physcannon_maxforce is ~1000.</summary>
	[Property, Group( "Carry" )] public float ThrowForce { get; set; } = 800f;

	/// <summary>How far the object can wander from hold point before being auto-dropped.</summary>
	[Property, Group( "Carry" )] public float MaxErrorDistance { get; set; } = 200f;

	// -------------------------------------------------------------------------
	// Runtime state
	// -------------------------------------------------------------------------

	/// <summary>The rigidbody currently being carried. Null if not carrying.</summary>
	public Rigidbody HeldObject { get; private set; }
	public bool IsCarrying => HeldObject.IsValid();

	static float DeltaAngle( float a, float b )
	{
		var delta = (b - a) % 360f;
		if ( delta > 180f ) delta -= 360f;
		else if ( delta < -180f ) delta += 360f;
		return delta;
	}

	// Player-space angles at the moment of grab, preserved like HL2's m_attachedAnglesPlayerSpace
	private Angles _attachedAnglesPlayerSpace;
	// Object-space offset from the grabbed point to the rigidbody origin.
	private Vector3 _attachedPositionObjectSpace;
	private Vector3 _lastTargetPosition;
	private float _contactAmount = 1f;
	private float _savedMass;
	private bool _savedUseController;
	private bool _savedEnableImpactDamage;
	private float _savedImpactDamage;
	private float _savedMinImpactDamageSpeed;
	private BaseCarryable _holsteredWeapon;

	Rotation GetPlayerCarryRotation()
	{
		var eyeAngles = Player.WalkController?.EyeAngles ?? Player.EyeTransform.Rotation.Angles();
		if ( IgnoreCarryPitch )
			eyeAngles.pitch = 0f;

		return eyeAngles.ToRotation();
	}

	float GetHeldRadius()
	{
		if ( !HeldObject.IsValid() )
			return 0f;

		var bounds = HeldObject.GameObject.GetBounds();
		return MathF.Max( 1f, MathF.Max( bounds.Extents.x, MathF.Max( bounds.Extents.y, bounds.Extents.z ) ) );
	}

	void HolsterActiveWeapon()
	{
		var inventory = Player.Components.Get<PlayerInventory>();
		if ( inventory is null )
			return;

		_holsteredWeapon = inventory.ActiveWeapon;
		if ( _holsteredWeapon.IsValid() )
			inventory.SwitchWeapon( null, true );
	}

	void RestoreHolsteredWeapon()
	{
		var inventory = Player.Components.Get<PlayerInventory>();
		if ( inventory is null )
			return;

		if ( _holsteredWeapon.IsValid() && _holsteredWeapon.Owner == Player )
			inventory.SwitchWeapon( _holsteredWeapon, true );
		else if ( !inventory.ActiveWeapon.IsValid() )
			inventory.SwitchWeapon( inventory.Weapons.FirstOrDefault(), true );

		_holsteredWeapon = null;
	}

	static Rotation AlignCarryRotation( Rotation rotation, float cosineAlignAngle )
	{
		var forward = rotation.Forward;
		var right = rotation.Right;
		var up = rotation.Up;

		AlignAxis( ref up, cosineAlignAngle );
		right = Vector3.Cross( up, forward ).Normal;
		forward = Vector3.Cross( right, up ).Normal;

		AlignAxis( ref right, cosineAlignAngle );
		forward = Vector3.Cross( right, up ).Normal;
		up = Vector3.Cross( forward, right ).Normal;

		AlignAxis( ref forward, cosineAlignAngle );
		right = Vector3.Cross( up, forward ).Normal;
		up = Vector3.Cross( forward, right ).Normal;

		return Rotation.LookAt( forward, up );
	}

	static void AlignAxis( ref Vector3 axis, float cosineAlignAngle )
	{
		var basis = new[] { Vector3.Right, Vector3.Up, Vector3.Forward };
		foreach ( var basisAxis in basis )
		{
			var dot = axis.Dot( basisAxis );
			if ( MathF.Abs( dot ) <= cosineAlignAngle )
				continue;

			axis = basisAxis * MathF.Sign( dot );
			return;
		}
	}

	// Original gravity scale — restored on drop
	private float _savedGravityScale;
	// Saved linear/angular damping — we crank these while held
	private float _savedLinearDamping;
	private float _savedAngularDamping;

	const float CarryAngleAlignmentCosine = 0.8660254f;

	// -------------------------------------------------------------------------
	// Update
	// -------------------------------------------------------------------------

	protected override void OnUpdate()
	{
		// Prop carry is purely local — only the carrying player runs this
		if ( Player.IsProxy ) return;

		var usePressed    = Input.Pressed( "Use" );
		var attackPressed = Input.Pressed( "attack1" );

		if ( IsCarrying )
		{
			if ( attackPressed )
			{
				Throw();
				return;
			}

			if ( usePressed ) // Drop on press, not release
			{
				Drop( clearVelocity: true );
				return;
			}

		}
		else
		{
			if ( usePressed )
				TryPickup();
		}
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();

		if ( Player.IsProxy ) return;
		if ( !IsCarrying ) return;

		UpdateCarry();
	}

	// -------------------------------------------------------------------------
	// Pickup
	// -------------------------------------------------------------------------

	void TryPickup()
	{
		if ( !Player.WalkController.IsValid() ) return;

		var tr = Scene.Trace.Ray( Player.EyeTransform.ForwardRay, PickupRange )
			.IgnoreGameObjectHierarchy( Player.GameObject )
			.WithoutTags( "player", "playercontroller", "trigger", "weapon" )
			.Run();

		if ( !tr.Hit || !tr.GameObject.IsValid() ) return;

		// Walk up to find a rigidbody (may be a child mesh that was hit)
		var rb = tr.GameObject.GetComponentInParent<Rigidbody>( true );
		if ( !rb.IsValid() ) return;

		// Don't grab players
		if ( rb.GameObject.Root.GetComponent<Player>( true ).IsValid() ) return;

		// HL2-style rules: must be a moveable physics object under the mass limit
		// Static/constrained objects are excluded automatically — no tag needed.
		if ( !rb.MotionEnabled ) return;
		if ( rb.Mass > MaxMass ) return;

		// Don't grab weapons sitting in the world (they use IPressable for pickup)
		if ( rb.GetComponent<DroppedWeapon>( true ).IsValid() ) return;

		Attach( rb, tr.HitPosition );
	}

	void Attach( Rigidbody rb, Vector3 grabPosition )
	{
		HeldObject = rb;

		// Record player-space angles at grab time (mirrors HL2 CGrabController).
		var carryRot = GetPlayerCarryRotation();
		var heldRotation = rb.WorldRotation;
		if ( AlignCarryAngles )
			heldRotation = AlignCarryRotation( heldRotation, CarryAngleAlignmentCosine );

		_attachedAnglesPlayerSpace = (carryRot.Inverse * heldRotation).Angles();

		var anchorPoint = rb.GameObject.GetBounds().Center;
		if ( anchorPoint == Vector3.Zero )
			anchorPoint = grabPosition;

		_attachedPositionObjectSpace = heldRotation.Inverse * (anchorPoint - rb.WorldPosition);
		_lastTargetPosition = rb.WorldPosition;
		_contactAmount = 1f;

		// Save and override physics params while held
		_savedGravityScale    = rb.GravityScale;
		_savedLinearDamping   = rb.LinearDamping;
		_savedAngularDamping  = rb.AngularDamping;
		_savedMass            = rb.PhysicsBody.Mass;
		_savedUseController   = rb.PhysicsBody.UseController;
		_savedEnableImpactDamage = rb.EnableImpactDamage;
		_savedImpactDamage = rb.ImpactDamage;
		_savedMinImpactDamageSpeed = rb.MinImpactDamageSpeed;

		rb.GravityScale    = 0f; // gravity compensation like HL2 held objects
		rb.LinearDamping   = MathF.Max( rb.LinearDamping, Damping );
		rb.AngularDamping  = MathF.Max( rb.AngularDamping, AngularDamping );
		rb.AngularVelocity = Vector3.Zero;
		rb.EnableImpactDamage = false;
		rb.ImpactDamage = 0f;
		rb.MinImpactDamageSpeed = float.MaxValue;
		rb.PhysicsBody.UseController = true;
		rb.PhysicsBody.Mass = HeldMass;

		HolsterActiveWeapon();
	}

	// -------------------------------------------------------------------------
	// Per-frame carry update (spring-damper, mirrors CGrabController::Simulate)
	// -------------------------------------------------------------------------

	void UpdateCarry()
	{
		if ( !HeldObject.IsValid() ) { HeldObject = null; return; }
		if ( !HeldObject.MotionEnabled ) { Drop( clearVelocity: false ); return; }

		var eye = Player.EyeTransform;
		var heldRadius = GetHeldRadius();
		var targetDistance = MathF.Max( HoldDistance, 24f + heldRadius * 2f );
		var blocked = false;
		var blockNormal = Vector3.Zero;

		// HL2-style hold target clamping: if blocked, pull the target back toward player.
		var holdTrace = Scene.Trace
			.Ray( eye.Position, eye.Position + eye.Forward * HoldDistance )
			.IgnoreGameObjectHierarchy( Player.GameObject )
			.IgnoreGameObjectHierarchy( HeldObject.GameObject )
			.WithTag( "map" )
			.WithoutTags( "trigger" )
			.Run();

		if ( holdTrace.Hit )
		{
			blocked = true;
			blockNormal = holdTrace.Normal;
			var blockedDistance = Math.Max( MinHoldDistance, eye.Position.Distance( holdTrace.HitPosition ) - heldRadius - HoldWallPadding );
			if ( holdTrace.Distance < heldRadius * 2f )
				blockedDistance = Math.Max( MinHoldDistance, heldRadius * 0.5f );
			targetDistance = Math.Min( targetDistance, blockedDistance );
		}

		var contactTarget = blocked ? 0.1f : 1.0f;
		var contactStep = Time.Delta * 8f;
		if ( _contactAmount < contactTarget )
			_contactAmount = MathF.Min( contactTarget, _contactAmount + contactStep );
		else
			_contactAmount = MathF.Max( contactTarget, _contactAmount - contactStep );

		var targetGrabPoint = eye.Position + eye.Forward * targetDistance;
		var desiredWorldRot = UseHl2CarryRotation
			? GetPlayerCarryRotation() * _attachedAnglesPlayerSpace.ToRotation()
			: HeldObject.WorldRotation;

		if ( blocked && UseHl2CarryRotation )
		{
			var currentAngles = HeldObject.WorldRotation.Angles();
			var desiredAngles = desiredWorldRot.Angles();
			var blend = _contactAmount * _contactAmount * _contactAmount;
			desiredWorldRot = new Angles(
				currentAngles.pitch + DeltaAngle( currentAngles.pitch, desiredAngles.pitch ) * blend,
				currentAngles.yaw + DeltaAngle( currentAngles.yaw, desiredAngles.yaw ) * blend,
				currentAngles.roll + DeltaAngle( currentAngles.roll, desiredAngles.roll ) * blend
			).ToRotation();
		}

		var targetPos = targetGrabPoint - (desiredWorldRot * _attachedPositionObjectSpace);

		if ( blocked )
		{
			var slideDelta = targetPos - _lastTargetPosition;
			var intoWall = blockNormal * slideDelta.Dot( blockNormal );
			targetPos = _lastTargetPosition + (slideDelta - intoWall);
		}

		var delta = targetPos - HeldObject.WorldPosition;
		var dist  = delta.Length;

		if ( dist > MaxErrorDistance )
		{
			Drop( clearVelocity: false );
			return;
		}

		var targetTransform = new Transform( targetPos, desiredWorldRot );
		HeldObject.PhysicsBody.Move( targetTransform, Time.Delta );
		_lastTargetPosition = targetPos;
	}

	// -------------------------------------------------------------------------
	// Drop / Throw
	// -------------------------------------------------------------------------

	void Drop( bool clearVelocity )
	{
		if ( !HeldObject.IsValid() ) { HeldObject = null; return; }

		RestorePhysicsParams();

		if ( clearVelocity )
		{
			HeldObject.Velocity        = Vector3.Zero;
			HeldObject.AngularVelocity = Vector3.Zero;
		}

		HeldObject = null;
		RestoreHolsteredWeapon();
	}

	void Throw()
	{
		if ( !HeldObject.IsValid() ) { HeldObject = null; return; }

		RestorePhysicsParams();

		// HL2 +USE throw: heavier objects get more launch scaling to avoid tiny props over-flying.
		var massScale = MathX.Remap( HeldObject.Mass, 0.5f, 15f, 0.5f, 4f );
		HeldObject.Velocity = Player.EyeTransform.Forward * ThrowForce * massScale;
		HeldObject.AngularVelocity = Vector3.Zero;

		HeldObject = null;
		RestoreHolsteredWeapon();
	}

	void RestorePhysicsParams()
	{
		if ( !HeldObject.IsValid() ) return;

		HeldObject.GravityScale   = _savedGravityScale;
		HeldObject.LinearDamping  = _savedLinearDamping;
		HeldObject.AngularDamping = _savedAngularDamping;
		HeldObject.EnableImpactDamage = _savedEnableImpactDamage;
		HeldObject.ImpactDamage = _savedImpactDamage;
		HeldObject.MinImpactDamageSpeed = _savedMinImpactDamageSpeed;
		HeldObject.PhysicsBody.Mass = _savedMass;
		HeldObject.PhysicsBody.UseController = _savedUseController;
	}

	// Drop held object if this component or the player is destroyed
	protected override void OnDisabled()  => Drop( clearVelocity: false );
	protected override void OnDestroy()   => Drop( clearVelocity: false );
}
