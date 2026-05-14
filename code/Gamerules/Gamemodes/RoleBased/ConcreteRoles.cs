// Murderer wins by eliminating all Detectives and Innocents.
// One player per round, secret role.
public sealed class MurdererRole : BaseRole
{
	public static readonly RoleInfo RoleData = new()
	{
		RoleName   = "Murderer",
		RoleColor  = Color.Red,
		MaxPlayers = 1,
		IsSecret   = true,
	};

	public override RoleInfo Info => RoleData;

	public override void OnRoundStart( Player player, RoleBasedGamemode gamemode )
	{
		GameManager.GiveToPlayer( player, "weapon_crowbar" );
	}

	public override bool CanDamage( Player attacker, Player victim, RoleBasedGamemode gamemode ) =>
		attacker != victim;

	public override bool CheckWinCondition( RoleBasedGamemode gamemode )
	{
		foreach ( var pd in PlayerData.All )
		{
			var player = Game.ActiveScene.GetAll<Player>()
				.FirstOrDefault( p => p.IsValid() && p.PlayerData == pd && !p.IsDead );

			if ( player is null ) continue;
			if ( gamemode.GetRole( pd.PlayerId ) is not MurdererRole )
				return false; // at least one non-murderer still alive
		}

		return true;
	}
}

// Detective wins (along with Innocents) when the Murderer is dead.
// One player per round, has a pistol.
public sealed class DetectiveRole : BaseRole
{
	public static readonly RoleInfo RoleData = new()
	{
		RoleName   = "Detective",
		RoleColor  = new Color( 0.2f, 0.5f, 1f ),
		MaxPlayers = 1,
		IsSecret   = true,
	};

	public override RoleInfo Info => RoleData;

	public override void OnRoundStart( Player player, RoleBasedGamemode gamemode )
	{
		GameManager.GiveToPlayer( player, "weapon_pistol" );
	}

	public override bool CanDamage( Player attacker, Player victim, RoleBasedGamemode gamemode ) =>
		attacker != victim;

	public override bool CheckWinCondition( RoleBasedGamemode gamemode ) =>
		!IsMurdererAlive( gamemode );

	internal static bool IsMurdererAlive( RoleBasedGamemode gamemode )
	{
		foreach ( var pd in PlayerData.All )
		{
			var player = Game.ActiveScene.GetAll<Player>()
				.FirstOrDefault( p => p.IsValid() && p.PlayerData == pd && !p.IsDead );

			if ( player is not null && gamemode.GetRole( pd.PlayerId ) is MurdererRole )
				return true;
		}

		return false;
	}
}

// Innocents share the Detective's win condition — survive until the Murderer is caught.
// Unarmed by default.
public sealed class InnocentRole : BaseRole
{
	public static readonly RoleInfo RoleData = new()
	{
		RoleName   = "Innocent",
		RoleColor  = Color.White,
		MaxPlayers = -1,
		IsSecret   = false,
	};

	public override RoleInfo Info => RoleData;

	// Innocents start unarmed — they rely on the Detective or found weapons
	public override bool CanDamage( Player attacker, Player victim, RoleBasedGamemode gamemode ) => false;

	public override bool CheckWinCondition( RoleBasedGamemode gamemode ) =>
		DetectiveRole.IsMurdererAlive( gamemode ) == false;
}
