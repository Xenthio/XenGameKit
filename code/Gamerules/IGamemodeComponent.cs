/// <summary>
/// Optional interface that any <see cref="Component"/> on the gamemode GameObject can implement.
/// <see cref="BaseGamemode"/> discovers and calls these automatically so components
/// can hook the mode lifecycle without subclassing BaseGamemode.
/// </summary>
public interface IGamemodeComponent
{
	/// <summary>Called when the gamemode activates (host + clients, before first update).</summary>
	void OnGamemodeStart() { }

	/// <summary>Called when the gamemode is torn down (host + clients).</summary>
	void OnGamemodeEnd() { }

	/// <summary>
	/// Called when the phase changes. Both old and new names are provided so
	/// components can react to specific transitions (e.g. Preparing → Active = round live).
	/// </summary>
	void OnPhaseChanged( string oldPhase, string newPhase ) { }

	/// <summary>
	/// Called right after a player spawns — use for equip, HUD setup, role reveal, etc.
	/// Host-only by default; clients hear it via Global.IPlayerEvents.
	/// </summary>
	void OnPlayerSpawned( Player player ) { }

	/// <summary>Called when a player dies. Host-only.</summary>
	void OnPlayerDied( Player player, PlayerDiedParams args ) { }
}
