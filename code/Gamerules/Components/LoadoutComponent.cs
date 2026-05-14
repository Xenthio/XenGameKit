/// <summary>
/// Composable loadout: add this alongside a <see cref="BaseGamemode"/> and populate
/// <see cref="Weapons"/> in the inspector. BaseGamemode.EquipPlayer() calls GiveLoadout()
/// automatically when this component is present.
///
/// For role-based or team-based loadouts, override <see cref="GetLoadoutFor"/> in a subclass
/// and return a custom list per-player.
/// </summary>
public class LoadoutComponent : Component, IGamemodeComponent
{
	/// <summary>Weapon prefabs to give every player when they spawn.</summary>
	[Property] public List<GameObject> Weapons { get; set; } = new();

	/// <summary>
	/// Return the list of weapon prefabs this player should receive.
	/// Override in a subclass to drive loadouts from roles, teams, or inventory configs.
	/// </summary>
	public virtual IEnumerable<GameObject> GetLoadoutFor( Player player ) => Weapons;

	/// <summary>Give this player their loadout. Called by BaseGamemode.EquipPlayer().</summary>
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
