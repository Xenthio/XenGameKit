// Drop this alongside a BaseGamemode and populate the Weapons list in the inspector.
// BaseGamemode.EquipPlayer() will call GiveLoadout() automatically.
//
// If you need role or team-based loadouts, subclass this and override GetLoadoutFor().
public class LoadoutComponent : Component, IGamemodeComponent
{
	// Weapon prefabs to give every player when they spawn.
	[Property] public List<GameObject> Weapons { get; set; } = new();

	// Return the weapon prefabs this player should receive.
	// Override in a subclass to drive loadouts from roles, teams, or whatever else.
	public virtual IEnumerable<GameObject> GetLoadoutFor( Player player ) => Weapons;

	// Called by BaseGamemode.EquipPlayer(). Host-only.
	public void GiveLoadout( Player player )
	{
		if ( !Networking.IsHost ) return;
		if ( !player.IsValid() ) return;

		var inventory = player.Components.Get<PlayerInventory>( FindMode.EnabledInSelfAndDescendants );
		if ( inventory is null ) return;

		foreach ( var prefab in GetLoadoutFor( player ) )
		{
			if ( prefab is null ) continue;
			inventory.Pickup( prefab, notice: false );
		}
	}
}
