using XMovement;

public sealed partial class Player : Component, Component.IDamageable
{
	public static Player FindLocalPlayer() => Game.ActiveScene.GetAllComponents<Player>().Where( x => !x.IsProxy ).FirstOrDefault();

	[RequireComponent] public PlayerWalkControllerComplex WalkController { get; set; }
	[RequireComponent] public PlayerMovement Movement { get; set; }
	[Property] public GameObject Body { get; set; }

	[Property, Range( 0, 100 ), Sync( SyncFlags.FromHost )] public float Health { get; set; } = 100;
	[Property, Range( 0, 100 )] public float MaxHealth { get; set; } = 100;
	[Sync( SyncFlags.FromHost )] public float Armour { get; set; } = 0;
	[Property] public float MaxArmour { get; set; } = 100;

	[Sync( SyncFlags.FromHost )] public PlayerData PlayerData { get; set; }

	public bool IsLocalPlayer => !IsProxy;
	public bool IsDead => Health <= 0;
	public Guid PlayerId => PlayerData.PlayerId;
	public long SteamId => PlayerData.SteamId;
	public string DisplayName => PlayerData.DisplayName;

	public Transform EyeTransform => new( WalkController.Head.WorldPosition, WalkController.EyeAngles.ToRotation() );

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( !IsProxy )
			OnControl();
	}

	void OnControl()
	{
		if ( Input.Pressed( "die" ) )
		{
			Local.IPlayerEvents.PostToGameObject( GameObject, x => x.OnSuicide() );
			Global.IPlayerEvents.Post( x => x.OnPlayerSuicide( this ) );
			Health = 0;
			Kill( default );
		}
	}

	void Kill( DamageInfo dmg )
	{
		GameManager.Current?.OnDeath( this, dmg );
		Health = 0;

		var diedParams = new PlayerDiedParams
		{
			InstigatorId = dmg.InstigatorId,
			Attacker = dmg.Attacker,
		};

		// Fire OnDied — PlayerDeathEffect listens to this and handles ragdoll + respawn timing
		// Don't destroy the GO here; PlayerDeathEffect will handle cleanup after the death sequence
		Local.IPlayerEvents.PostToGameObject( GameObject, x => x.OnDied( diedParams ) );
		Global.IPlayerEvents.Post( x => x.OnPlayerDied( this, diedParams ) );
	}

	public void OnDamage( in Sandbox.DamageInfo damage )
	{
		if ( !Networking.IsHost ) return;
		if ( Health < 1 ) return;

		var dmg = damage as DamageInfo ?? new DamageInfo( damage.Damage, damage.Attacker, damage.Weapon );

		var damageEvent = new PlayerDamageEvent { Player = this, DamageInfo = dmg, Damage = dmg.Damage };
		Local.IPlayerEvents.PostToGameObject( GameObject, x => x.OnDamaging( damageEvent ) );
		Global.IPlayerEvents.Post( x => x.OnPlayerDamaging( damageEvent ) );

		if ( damageEvent.Cancelled ) return;

		var amount = damageEvent.Damage;

		if ( dmg.Tags.Contains( DamageTags.Headshot ) )
			amount *= 2f;

		if ( Armour > 0 )
		{
			float remaining = amount - Armour;
			Armour = Math.Max( 0, Armour - amount );
			amount = Math.Max( 0, remaining );
		}

		Health -= amount;

		NotifyOnDamage( new PlayerDamageParams
		{
			Damage = amount,
			InstigatorId = dmg.InstigatorId,
			Attacker = dmg.Attacker,
			Weapon = dmg.Weapon,
			Tags = dmg.Tags,
			Position = dmg.Position,
			Origin = dmg.Origin,
		} );

		if ( Health >= 1 ) return;

		Kill( dmg );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	private void NotifyOnDamage( PlayerDamageParams args )
	{
		Local.IPlayerEvents.PostToGameObject( GameObject, x => x.OnDamage( args ) );
		Global.IPlayerEvents.Post( x => x.OnPlayerDamage( this, args ) );
	}

	public T GetWeapon<T>() where T : BaseCarryable
	{
		return Components.Get<PlayerInventory>()?.GetWeapon<T>();
	}

	public void SwitchWeapon<T>() where T : BaseCarryable
	{
		var weapon = GetWeapon<T>();
		if ( weapon is null ) return;
		Components.Get<PlayerInventory>()?.SwitchWeapon( weapon );
	}

}
