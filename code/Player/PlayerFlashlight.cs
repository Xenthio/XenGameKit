/// <summary>
/// HL2-style player flashlight.
/// Parented to the Head GO so rotation is free. Each frame the local position
/// is pushed forward along the head's forward axis based on how close the
/// nearest surface is — the HL2 trick that stops the cone clipping into nearby walls.
/// Edit cone, radius, colour, cookie etc. directly on the SpotLight in the prefab.
/// </summary>
public sealed class PlayerFlashlight : Component
{
	[RequireComponent] public Player Player { get; set; }

	/// <summary>The "flashlight" child GameObject under Head in the player prefab.</summary>
	[Property] public GameObject LightObject { get; set; }

	/// <summary>Input action that toggles the flashlight.</summary>
	[Property, InputAction] public string FlashlightAction { get; set; } = "Flashlight";

	/// <summary>
	/// Fraction of trace distance to push the light forward.
	/// Close wall = bigger offset so the cone wraps the surface. HL2 uses ~0.1.
	/// </summary>
	[Property, Range( 0, 1 )] public float ForwardBias { get; set; } = 0.1f;

	/// <summary>Minimum forward offset — keeps the light out of the player's head.</summary>
	[Property] public float MinForwardOffset { get; set; } = 8f;

	/// <summary>Maximum forward offset.</summary>
	[Property] public float MaxForwardOffset { get; set; } = 64f;

	/// <summary>How far the trace searches for nearby geometry.</summary>
	[Property] public float TraceRange { get; set; } = 1600f;

	[Sync] public bool IsOn { get; private set; } = false;

	protected override void OnUpdate()
	{
		if ( !IsProxy && Player.IsLocalPlayer && Input.Pressed( FlashlightAction ) )
			ToggleFlashlight();

		if ( !LightObject.IsValid() ) return;

		LightObject.Enabled = IsOn;

		if ( !IsOn ) return;

		// Trace from the eye along the view direction to find the nearest surface
		var eye = LightObject.Parent.WorldPosition;
		var dir = LightObject.Parent.WorldRotation.Forward;

		var tr = Scene.Trace
			.Ray( eye, eye + dir * TraceRange )
			.IgnoreGameObjectHierarchy( Player.GameObject )
			.WithoutTags( "playercontroller", "trigger" )
			.Run();

		var hitDist = tr.Hit ? tr.Distance : TraceRange;
		var offset  = MathX.Clamp( hitDist * ForwardBias, MinForwardOffset, MaxForwardOffset );

		// Local X is forward in Head space
		LightObject.LocalPosition = new Vector3( offset, 0, 0 );
	}

	[Rpc.Broadcast]
	void ToggleFlashlight()
	{
		IsOn = !IsOn;
	}
}
