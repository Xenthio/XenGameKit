/// <summary>
/// Health pickup. Equivalent to item_healthkit / item_healthvial from Source.
/// Heals the player on touch if they aren't already at max health.
/// </summary>
public class ItemHealth : BaseItem
{
	[Property] public float HealAmount { get; set; } = 25f;
	[Property] public float MaxHealthOverheal { get; set; } = 0f; // >0 allows overheal like TF2

	protected override bool OnPickup( Player player )
	{
		var max = player.MaxHealth + MaxHealthOverheal;
		if ( player.Health >= max ) return false;

		player.Health = MathF.Min( player.Health + HealAmount, max );
		return true;
	}
}

/// <summary>
/// Armour/battery pickup. Equivalent to item_battery / item_suit from Source.
/// </summary>
public class ItemArmour : BaseItem
{
	[Property] public float ArmourAmount { get; set; } = 15f;

	protected override bool OnPickup( Player player )
	{
		if ( player.Armour >= player.MaxArmour ) return false;

		player.Armour = MathF.Min( player.Armour + ArmourAmount, player.MaxArmour );
		return true;
	}
}

/// <summary>
/// Ammo pickup. Give ammo for a specific AmmoResource type.
/// </summary>
public class ItemAmmo : BaseItem
{
	[Property] public AmmoResource AmmoType { get; set; }
	[Property] public int Amount { get; set; } = 20;

	protected override bool OnPickup( Player player )
	{
		if ( AmmoType is null ) return false;

		var ammoInv = player.GetComponent<AmmoInventory>();
		if ( !ammoInv.IsValid() ) return false;

		if ( ammoInv.GetAmmo( AmmoType ) >= AmmoType.MaxReserve ) return false;

		ammoInv.AddAmmo( AmmoType, Amount );
		return true;
	}
}
