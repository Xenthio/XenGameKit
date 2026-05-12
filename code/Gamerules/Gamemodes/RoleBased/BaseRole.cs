/// <summary>
/// Base class for roles in a <see cref="RoleBasedGamemode"/>. Not a Component — just a plain C# class.
/// Inherit this to define per-role win conditions, equipment, and damage rules.
/// </summary>
public abstract class BaseRole
{
	public abstract RoleInfo Info { get; }

	/// <summary>
	/// Called at the start of each round. Equip the player and send them a private role reveal here.
	/// </summary>
	public virtual void OnRoundStart( Player player, RoleBasedGamemode gamemode ) { }

	/// <summary>
	/// Called on the host whenever a player dies. Use to check cascading conditions.
	/// </summary>
	public virtual void OnPlayerDied( Player player, PlayerDiedParams args, RoleBasedGamemode gamemode ) { }

	/// <summary>
	/// Return false to block <paramref name="attacker"/> from damaging <paramref name="victim"/>.
	/// </summary>
	public virtual bool CanDamage( Player attacker, Player victim, RoleBasedGamemode gamemode ) => true;

	/// <summary>
	/// Return true if this role's win condition has been met. Checked after every death.
	/// </summary>
	public virtual bool CheckWinCondition( RoleBasedGamemode gamemode ) => false;
}
