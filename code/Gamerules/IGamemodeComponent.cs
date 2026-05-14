// Interface any Component on the gamemode GameObject can implement.
// BaseGamemode finds all of them automatically and calls these at the right times,
// so you don't need to subclass BaseGamemode just to hook the lifecycle.
public interface IGamemodeComponent
{
	// Called when the gamemode activates (runs on host and clients).
	void OnGamemodeStart() { }

	// Called when the gamemode is torn down.
	void OnGamemodeEnd() { }

	// Called whenever BaseGamemode.Phase changes. Both old and new names are passed
	// so you can react to specific transitions (e.g. Preparing -> Active = go!).
	void OnPhaseChanged( string oldPhase, string newPhase ) { }

	// Called right after a player spawns. Good for giving equipment, revealing roles, etc.
	void OnPlayerSpawned( Player player ) { }

	// Called when a player dies. Host-only.
	void OnPlayerDied( Player player, PlayerDiedParams args ) { }
}
