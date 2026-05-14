/// <summary>
/// Thin decoupling layer between the FPS base (Tier 1–3) and the gamerules tier (Tier 4+).
///
/// The base layer calls through <see cref="Current"/> without knowing any concrete gamemode type.
/// The gamerules tier sets <see cref="Current"/> when it activates and clears it when it shuts down.
/// If no gamerules provider is registered, base behaviour falls through to sane defaults.
/// </summary>
public static class GameRulesService
{
	/// <summary>
	/// The active gamerules provider, or null when running without a gamemode manager.
	/// </summary>
	public static IGameRulesProvider Current { get; set; }

	/// <summary>
	/// Interface the gamerules tier must implement to plug into the FPS base.
	/// </summary>
	public interface IGameRulesProvider
	{
		/// <summary>
		/// Return a custom spawn location for this player, or null to fall back to the default.
		/// </summary>
		Transform? GetSpawnLocation( PlayerData playerData );

		/// <summary>
		/// Called after a player spawns so the active gamemode can give them their starting loadout.
		/// </summary>
		void EquipPlayer( Player player );

		/// <summary>
		/// Route a respawn request through the gamemode (which may defer it until the next round).
		/// </summary>
		void RequestRespawn( PlayerData playerData );

		/// <summary>
		/// Return the respawn delay in seconds for this player. <see cref="PlayerDeathEffect"/> uses
		/// this so gamemodes can control the death-screen duration without subclassing the effect.
		/// </summary>
		float GetRespawnDelay( PlayerData playerData );
	}
}
