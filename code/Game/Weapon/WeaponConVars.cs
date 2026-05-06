public static class WeaponConVars
{
	/// <summary>
	/// 0 = normal, 1 = infinite ammo (clips still deplete), 2 = infinite ammo (no clip depletion)
	/// Mirrors Source's sv_infinite_ammo behaviour.
	/// </summary>
	[ConVar( "sv_infinite_ammo", ConVarFlags.Replicated, Help = "0: normal, 1: infinite reserves, 2: unlimited ammo (no depletion)" )]
	public static int InfiniteAmmo { get; set; } = 0;

	public static bool UnlimitedAmmo => InfiniteAmmo >= 2;
	public static bool InfiniteReserves => InfiniteAmmo >= 1;
}
