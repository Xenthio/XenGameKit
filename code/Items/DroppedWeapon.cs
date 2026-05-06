public sealed class DroppedWeapon : Component, Component.IPressable
{
	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		var weapon = GetComponent<BaseCarryable>();
		if ( !weapon.IsValid() ) return null;

		var name = weapon.DisplayName.ToUpper();
		return new IPressable.Tooltip( "Pick up", "inventory_2", name );
	}

	bool IPressable.CanPress( IPressable.Event e ) => true;

	bool IPressable.Press( IPressable.Event e )
	{
		DoPickup( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	private void DoPickup( GameObject presserObject )
	{
		if ( !presserObject.IsValid() ) return;

		var player = presserObject.Root.GetComponent<Player>();
		if ( !player.IsValid() ) return;

		var inventory = player.GetComponent<PlayerInventory>();
		if ( !inventory.IsValid() ) return;

		TakeIntoInventory( inventory );
	}

	private void TakeIntoInventory( PlayerInventory inventory )
	{
		var weapon = GetComponent<BaseCarryable>();
		if ( !weapon.IsValid() ) return;

		Enabled = false;
		inventory.Take( weapon, true );
	}
}
