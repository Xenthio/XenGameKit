public sealed class DroppedWeapon : Component, Component.IPressable, Component.ITriggerListener
{
	/// <summary>
	/// How long after being dropped before it can be auto-picked up by walking over it.
	/// Prevents immediately re-picking up a weapon you just dropped.
	/// </summary>
	[Property] public float WalkoverDelay { get; set; } = 1.0f;

	private TimeSince _timeSinceDropped = 0;

	protected override void OnEnabled()
	{
		_timeSinceDropped = 0;
	}

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

	void ITriggerListener.OnTriggerEnter( GameObject other )
	{
		if ( !Networking.IsHost ) return;
		if ( _timeSinceDropped < WalkoverDelay ) return;

		var player = other.GetComponentInParent<Player>( true );
		if ( !player.IsValid() ) return;

		var inventory = player.GetComponent<PlayerInventory>();
		if ( !inventory.IsValid() ) return;

		TakeIntoInventory( inventory );
	}

	void ITriggerListener.OnTriggerExit( GameObject other ) { }

	private void TakeIntoInventory( PlayerInventory inventory )
	{
		var weapon = GetComponent<BaseCarryable>();
		if ( !weapon.IsValid() ) return;

		Enabled = false;
		inventory.Take( weapon, true );
	}
}
