/// <summary>
/// A pressable button. Like func_button in Source — press E, it fires, then resets.
///
/// Wire OnPressed to anything: doors, sounds, lights, custom logic.
/// The button optionally moves inward on press and returns after ResetDelay.
///
/// To create a button:
///   1. Place a model in your scene.
///   2. Add this component.
///   3. Subscribe to OnPressed in code, or pair with a BaseDoor via a helper component.
/// </summary>
public class func_button : Component, IPressable
{
	[Property, Group( "Behaviour" )] public float ResetDelay   { get; set; } = 1f;
	[Property, Group( "Behaviour" )] public bool  Locked       { get; set; } = false;

	// How far the button moves inward when pressed (visual feedback).
	[Property, Group( "Visual" )] public float PressDistance { get; set; } = 4f;
	[Property, Group( "Visual" )] public Vector3 PressAxis   { get; set; } = Vector3.Forward;

	/// <summary>Fires when a player presses this button. Attach your logic here.</summary>
	public Action<Player> OnPressed { get; set; }

	public bool IsPressed { get; private set; }

	Vector3   _restPos;
	TimeSince _pressedAt;

	protected override void OnStart() => _restPos = LocalPosition;

	protected override void OnUpdate()
	{
		if ( !IsPressed ) return;

		if ( _pressedAt > ResetDelay )
		{
			IsPressed     = false;
			LocalPosition = _restPos;
		}
	}

	bool IPressable.Press( IPressable.Event e )
	{
		if ( Locked || IsPressed ) return false;

		IsPressed     = true;
		_pressedAt    = 0;
		LocalPosition = _restPos + PressAxis.Normal * -PressDistance;

		var presser = (e.Source as Component)?.GameObject.GetComponent<Player>();
		OnPressed?.Invoke( presser );

		return true;
	}
}
