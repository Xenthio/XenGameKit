using XMovement;

/// <summary>
/// Exposes the currently hovered IPressable from XMovement's built-in use system.
/// Use is handled natively by PlayerWalkControllerComplex — this component just
/// provides a convenient accessor for UI/HUD code.
/// </summary>
public class PlayerUse : Component
{
	[RequireComponent] public Player Player { get; set; }

	public IPressable Hovered => Player?.WalkController?.Hovering as IPressable;
}
