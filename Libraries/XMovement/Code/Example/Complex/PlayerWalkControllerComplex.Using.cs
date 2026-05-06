using Sandbox;
namespace XMovement;

public partial class PlayerWalkControllerComplex : Component
{
	[Property, FeatureEnabled( "Using" )] public bool EnableUse { get; set; } = false;
	/// <summary>
	/// The Input Action that noclipping is triggered by.
	/// </summary>
	[Property, InputAction, Feature( "Using" )] public string UseAction { get; set; } = "Use";
	[Property, Feature( "Using" )] public float UseDistance { get; set; } = 130;

	public Component Hovering { get; set; }
	public Component Using { get; set; }

	public void DoUsing()
	{
		if ( !EnableUse ) return;
		if ( IsProxy ) return;
		var evnt = new IPressable.Event() { Ray = AimRay, Source = this };
		UpdateHover( FindUsable() );

		if ( Input.Pressed( UseAction ) && CanUse( Hovering ) )
		{
			Using = Hovering;
			if ( Using is IPressable pressable ) pressable.Press( evnt );
		}
		if ( Input.Down( UseAction ) && CanUse( Using ) )
		{
			if ( Using is IPressable pressable ) pressable.Pressing( evnt );
		}
		if ( Using != null && (Input.Released( UseAction ) || !CanUse( Using )) )
		{
			if ( Using is IPressable pressable ) pressable.Release( evnt );
			Using = null;
		}
	}

	void UpdateHover( Component newhovering )
	{
		var evnt = new IPressable.Event() { Ray = AimRay, Source = this };

		if ( Hovering == newhovering )
		{
			if ( Hovering is IPressable hoveringPressable )
			{
				hoveringPressable.Look( evnt );
			}

			return;
		}

		if ( Hovering is IPressable stoppedHoveringPressable )
		{
			stoppedHoveringPressable.Blur( evnt );
		}

		Hovering = newhovering;

		if ( Hovering is IPressable startedHoveringPressable )
		{
			startedHoveringPressable.Hover( evnt );
			startedHoveringPressable.Look( evnt );
		}
	}

	public bool CanUse( Component component )
	{
		if ( !component.IsValid() ) return false;
		if ( component.WorldPosition.Distance( AimRay.Position ) > UseDistance ) return false;
		return true;
	}

	public Component FindUsable()
	{
		var tr = Scene.Trace.Ray( AimRay, UseDistance )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( !tr.GameObject.IsValid() ) return null;

		// Walk up the hierarchy - the pressable component may be on a parent
		// (e.g. DroppedWeapon is on the weapon root, but the trace hits the mesh child)
		var pressable = tr.GameObject.Components.Get<Component.IPressable>( FindMode.EverythingInSelfAndParent );

		if ( pressable != null && pressable.CanPress( new IPressable.Event() { Ray = AimRay, Source = this } ) )
			return pressable as Component;

		return null;
	}
}
