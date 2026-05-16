/// <summary>
/// Base for sliding and rotating doors. Works like func_door / func_door_rotating in Source.
///
/// Add this to a GameObject with a model on it. Set MoveDir or RotateAxis, Speed, and Distance/Angle.
/// Doors can be opened by players pressing E (implements IPressable), by trigger volumes,
/// or by other code calling Open() / Close() / Toggle() directly.
///
/// To create a door:
///   1. Place a model in your scene.
///   2. Add BaseDoor (or a subclass) to the same GameObject.
///   3. Set Movement = Slide or Rotate, fill in the relevant properties.
///   4. That's it. Hook OnOpened / OnClosed for custom logic.
/// </summary>
public class BaseDoor : Component, IPressable
{
	public enum DoorMovement { Slide, Rotate }
	public enum DoorState    { Closed, Opening, Open, Closing }

	// ─── Inspector properties ─────────────────────────────────────────────────

	[Property, Group( "Movement" )] public DoorMovement Movement    { get; set; } = DoorMovement.Rotate;
	[Property, Group( "Movement" )] public float        Speed       { get; set; } = 200f;

	// Slide settings
	[Property, Group( "Slide" )] public Vector3 MoveDir      { get; set; } = Vector3.Up;
	[Property, Group( "Slide" )] public float   MoveDistance { get; set; } = 80f;

	// Rotate settings
	[Property, Group( "Rotate" )] public Vector3 RotateAxis  { get; set; } = Vector3.Up;
	[Property, Group( "Rotate" )] public float   RotateAngle { get; set; } = 90f;

	// Behaviour
	[Property, Group( "Behaviour" )] public bool  StartsOpen      { get; set; } = false;
	[Property, Group( "Behaviour" )] public bool  UseToOpen       { get; set; } = true;
	[Property, Group( "Behaviour" )] public bool  AutoClose       { get; set; } = false;
	[Property, Group( "Behaviour" )] public float AutoCloseDelay  { get; set; } = 3f;
	[Property, Group( "Behaviour" )] public bool  Locked          { get; set; } = false;

	// ─── State ────────────────────────────────────────────────────────────────

	public DoorState State { get; private set; }

	/// <summary>Fires on all clients when the door finishes opening.</summary>
	public Action OnOpened { get; set; }
	/// <summary>Fires on all clients when the door finishes closing.</summary>
	public Action OnClosed { get; set; }

	Vector3    _closedPos;
	Rotation   _closedRot;
	Vector3    _openPos;
	Rotation   _openRot;
	float      _fraction; // 0 = closed, 1 = open
	TimeSince  _openedAt;

	protected override void OnStart()
	{
		_closedPos = LocalPosition;
		_closedRot = LocalRotation;

		if ( Movement == DoorMovement.Slide )
		{
			_openPos = _closedPos + MoveDir.Normal * MoveDistance;
			_openRot = _closedRot;
		}
		else
		{
			_openPos = _closedPos;
			_openRot = _closedRot * Rotation.FromAxis( RotateAxis, RotateAngle );
		}

		if ( StartsOpen )
		{
			_fraction = 1f;
			State     = DoorState.Open;
			ApplyFraction( 1f );
		}
	}

	protected override void OnUpdate()
	{
		if ( State == DoorState.Closed || State == DoorState.Open ) return;

		float target = State == DoorState.Opening ? 1f : 0f;
		float delta  = Movement == DoorMovement.Slide
			? Speed * Time.Delta / MoveDistance
			: Speed * Time.Delta / RotateAngle;

		_fraction = MathX.Approach( _fraction, target, delta );
		ApplyFraction( _fraction );

		if ( MathF.Abs( _fraction - target ) < 0.001f )
		{
			_fraction = target;
			ApplyFraction( _fraction );

			if ( State == DoorState.Opening )
			{
				State     = DoorState.Open;
				_openedAt = 0;
				OnOpened?.Invoke();
			}
			else
			{
				State = DoorState.Closed;
				OnClosed?.Invoke();
			}
		}

		if ( AutoClose && State == DoorState.Open && _openedAt > AutoCloseDelay )
			Close();
	}

	void ApplyFraction( float t )
	{
		LocalPosition = Vector3.Lerp( _closedPos, _openPos, Easing.EaseInOut( t ) );
		LocalRotation = Rotation.Lerp( _closedRot, _openRot, Easing.EaseInOut( t ) );
	}

	// ─── Public API ───────────────────────────────────────────────────────────

	public void Open()
	{
		if ( Locked || State == DoorState.Open || State == DoorState.Opening ) return;
		State = DoorState.Opening;
	}

	public void Close()
	{
		if ( State == DoorState.Closed || State == DoorState.Closing ) return;
		State = DoorState.Closing;
	}

	public void Toggle()
	{
		if ( State == DoorState.Closed || State == DoorState.Closing ) Open();
		else Close();
	}

	// ─── IPressable ───────────────────────────────────────────────────────────

	bool IPressable.Press( IPressable.Event e )
	{
		if ( !UseToOpen ) return false;
		Toggle();
		return true;
	}
}
