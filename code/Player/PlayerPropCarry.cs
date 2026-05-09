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

	/// <summary>Spring constant — how aggressively the object chases the hold point.</summary>
	[Property, Group( "Carry" )] public float SpringStrength { get; set; } = 80f;

	/// <summary>Damping factor — kills oscillation. Higher = stiffer.</summary>
	[Property, Group( "Carry" )] public float Damping { get; set; } = 15f;

	/// <summary>Angular damping — prevents free spinning.</summary>
	[Property, Group( "Carry" )] public float AngularDamping { get; set; } = 30f;

	/// <summary>Max speed the object is allowed to move while being steered (units/s).</summary>
	[Property, Group( "Carry" )] public float MaxCarrySpeed { get; set; } = 600f;

	/// <summary>Max angular speed while carried (deg/s).</summary>
	[Property, Group( "Carry" )] public float MaxCarryAngular { get; set; } = 180f;

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

	// Object-space angles at the moment of grab, preserved throughout the carry
	private Angles _attachedAnglesLocalSpace;
	// Original gravity scale — restored on drop
	private float _savedGravityScale;
	// Saved linear/angular damping — we crank these while held
	private float _savedLinearDamping;
	private float _savedAngularDamping;

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

			UpdateCarry();
		}
		else
		{
			if ( usePressed )
				TryPickup();
		}
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

		Attach( rb );
	}

	void Attach( Rigidbody rb )
	{
		HeldObject = rb;

		// Record local-space angles relative to eye at grab time
		var eyeRot = Player.EyeTransform.Rotation;
		var localRot = eyeRot.Inverse * rb.WorldRotation;
		_attachedAnglesLocalSpace = localRot.Angles();

		// Save and override physics params while held
		_savedGravityScale    = rb.GravityScale;
		_savedLinearDamping   = rb.LinearDamping;
		_savedAngularDamping  = rb.AngularDamping;

		rb.GravityScale    = 0f;   // gravity compensation — object floats at hold point
		rb.LinearDamping   = 2f;   // light damping helps stability
		rb.AngularDamping  = 8f;   // stop spin
	}

	// -------------------------------------------------------------------------
	// Per-frame carry update (spring-damper, mirrors CGrabController::Simulate)
	// -------------------------------------------------------------------------

	void UpdateCarry()
	{
		if ( !HeldObject.IsValid() ) { HeldObject = null; return; }

		var eye       = Player.EyeTransform;
		var targetPos = eye.Position + eye.Forward * HoldDistance;

		// --- Position spring ---
		var delta    = targetPos - HeldObject.WorldPosition;
		var dist     = delta.Length;

		// Safety: too far away → drop (e.g. clipped through wall)
		if ( dist > MaxErrorDistance )
		{
			Drop( clearVelocity: false );
			return;
		}

		var vel       = HeldObject.Velocity;
		var spring    = delta * SpringStrength;
		var damp      = -vel * Damping;
		var newVel    = vel + (spring + damp) * Time.Delta;

		// Clamp to max carry speed
		if ( newVel.Length > MaxCarrySpeed )
			newVel = newVel.Normal * MaxCarrySpeed;

		HeldObject.Velocity = newVel;

		// --- Rotation: restore local-space angle relative to eye ---
		// This mirrors CGrabController tracking m_attachedAnglesPlayerSpace.
		// Drive angular velocity toward the desired orientation using euler delta.
		var desiredWorldRot = eye.Rotation * _attachedAnglesLocalSpace.ToRotation();
		var currentAngles   = HeldObject.WorldRotation.Angles();
		var desiredAngles   = desiredWorldRot.Angles();

		// Wrap each axis delta to [-180, 180]
		var deltaAngles = new Angles(
			DeltaAngle( currentAngles.pitch, desiredAngles.pitch ),
			DeltaAngle( currentAngles.yaw,   desiredAngles.yaw   ),
			DeltaAngle( currentAngles.roll,  desiredAngles.roll  )
		);

		var angularVel = new Vector3( deltaAngles.pitch, deltaAngles.yaw, deltaAngles.roll ) * Damping;

		// Clamp angular speed (convert to radians/s)
		var maxAngRad = MaxCarryAngular * MathF.PI / 180f;
		if ( angularVel.Length > maxAngRad )
			angularVel = angularVel.Normal * maxAngRad;

		HeldObject.AngularVelocity = angularVel;
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
	}

	void Throw()
	{
		if ( !HeldObject.IsValid() ) { HeldObject = null; return; }

		RestorePhysicsParams();

		// HL2: throw force is scaled a bit by mass so lighter objects go faster
		var massScale = MathX.Remap( HeldObject.Mass, 0f, MaxMass, 1.2f, 0.8f );
		HeldObject.Velocity = Player.EyeTransform.Forward * ThrowForce * massScale;
		HeldObject.AngularVelocity = Vector3.Zero;

		HeldObject = null;
	}

	void RestorePhysicsParams()
	{
		if ( !HeldObject.IsValid() ) return;

		HeldObject.GravityScale   = _savedGravityScale;
		HeldObject.LinearDamping  = _savedLinearDamping;
		HeldObject.AngularDamping = _savedAngularDamping;
	}

	// Drop held object if this component or the player is destroyed
	protected override void OnDisabled()  => Drop( clearVelocity: false );
	protected override void OnDestroy()   => Drop( clearVelocity: false );
}
