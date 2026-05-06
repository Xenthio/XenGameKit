using Sandbox.Citizen;

public sealed class PlayerInventory : Component, Local.IPlayerEvents
{
	[Property] public int MaxSlots { get; set; } = 6;

	[RequireComponent] public Player Player { get; set; }

	public IEnumerable<BaseCarryable> Weapons =>
		GetComponentsInChildren<BaseCarryable>( true ).OrderBy( x => x.InventorySlot );

	[Sync( SyncFlags.FromHost ), Change] public BaseCarryable ActiveWeapon { get; private set; }

	public void OnActiveWeaponChanged( BaseCarryable oldWeapon, BaseCarryable newWeapon )
	{
		if ( oldWeapon.IsValid() )
			oldWeapon.GameObject.Enabled = false;

		if ( newWeapon.IsValid() )
		{
			newWeapon.GameObject.Enabled = true;
			newWeapon.SetDropped( false );
		}
	}

	public BaseCarryable GetSlot( int slot )
	{
		if ( slot < 0 || slot >= MaxSlots ) return null;
		foreach ( var w in Weapons )
			if ( w.InventorySlot == slot ) return w;
		return null;
	}

	public List<BaseCarryable> GetAllInSlot( int slot )
	{
		return Weapons.Where( x => x.InventorySlot == slot ).ToList();
	}

	public int FindEmptySlot()
	{
		var weapons = Weapons;
		for ( int i = 0; i < MaxSlots; i++ )
		{
			bool occupied = false;
			foreach ( var w in weapons )
				if ( w.InventorySlot == i ) { occupied = true; break; }
			if ( !occupied ) return i;
		}
		return -1;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		var renderer = Player?.WalkController?.BodyModelRenderer;

		if ( ActiveWeapon.IsValid() )
		{
			ActiveWeapon.OnFrameUpdate( Player );
			if ( !IsProxy )
				ActiveWeapon.OnPlayerUpdate( Player );

			if ( renderer.IsValid() )
				renderer.Set( "holdtype", (int)ActiveWeapon.HoldType );
		}
		else
		{
			if ( renderer.IsValid() )
				renderer.Set( "holdtype", (int)CitizenAnimationHelper.HoldTypes.None );
		}

		OnControl();
	}

	void OnControl()
	{
		if ( Input.Pressed( "slot1" ) ) SelectSlot( 0 );
		else if ( Input.Pressed( "slot2" ) ) SelectSlot( 1 );
		else if ( Input.Pressed( "slot3" ) ) SelectSlot( 2 );
		else if ( Input.Pressed( "slot4" ) ) SelectSlot( 3 );
		else if ( Input.Pressed( "slot5" ) ) SelectSlot( 4 );

		if ( Input.MouseWheel.y > 0 ) SelectNext();
		else if ( Input.MouseWheel.y < 0 ) SelectPrev();

		if ( Input.Pressed( "drop" ) && ActiveWeapon.IsValid() )
			Drop( ActiveWeapon );
	}

	void SelectSlot( int slot )
	{
		var inSlot = GetAllInSlot( slot );
		if ( inSlot.Count == 0 ) return;

		if ( ActiveWeapon.IsValid() && ActiveWeapon.InventorySlot == slot && inSlot.Count > 1 )
		{
			var idx = inSlot.IndexOf( ActiveWeapon );
			var next = inSlot[(idx + 1) % inSlot.Count];
			SwitchWeapon( next );
		}
		else
		{
			SwitchWeapon( inSlot[0] );
		}
	}

	void SelectNext()
	{
		var all = Weapons.ToList();
		if ( all.Count == 0 ) return;
		var idx = all.IndexOf( ActiveWeapon );
		SwitchWeapon( all[(idx + 1) % all.Count] );
	}

	void SelectPrev()
	{
		var all = Weapons.ToList();
		if ( all.Count == 0 ) return;
		var idx = all.IndexOf( ActiveWeapon );
		SwitchWeapon( all[(idx - 1 + all.Count) % all.Count] );
	}

	public async void SwitchWeapon( BaseCarryable weapon, bool force = false )
	{
		if ( ActiveWeapon == weapon ) return;

		var switchEvent = new PlayerSwitchWeaponEvent { Player = Player, From = ActiveWeapon, To = weapon };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnSwitchWeapon( switchEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerSwitchWeapon( switchEvent ) );
		if ( switchEvent.Cancelled && !force ) return;

		if ( ActiveWeapon.IsValid() )
		{
			ActiveWeapon.OnHolster();
			if ( !force && ActiveWeapon.HolsterTime > 0 )
				await Task.DelaySeconds( ActiveWeapon.HolsterTime );
			ActiveWeapon.GameObject.Enabled = false;
		}

		ActiveWeapon = weapon;

		if ( ActiveWeapon.IsValid() )
		{
			ActiveWeapon.GameObject.Enabled = true;
			ActiveWeapon.SetDropped( false );
			ActiveWeapon.OnDeploy();
		}
	}

	public bool HasWeapon<T>() where T : BaseCarryable => GetWeapon<T>().IsValid();

	public T GetWeapon<T>() where T : BaseCarryable => Weapons.OfType<T>().FirstOrDefault();

	public bool Pickup( string prefabName, bool notice = true )
	{
		if ( !Networking.IsHost ) return false;

		var prefab = GameObject.GetPrefab( prefabName );
		if ( prefab is null )
		{
			Log.Warning( $"Prefab not found: {prefabName}" );
			return false;
		}

		var slot = FindEmptySlot();
		if ( slot < 0 ) return false;

		return Pickup( prefab, slot, notice );
	}

	public bool Pickup( GameObject prefab, bool notice = true )
	{
		var slot = FindEmptySlot();
		if ( slot < 0 ) return false;
		return Pickup( prefab, slot, notice );
	}

	public bool Pickup( string prefabName, int targetSlot, bool notice = true )
	{
		if ( !Networking.IsHost ) return false;

		var prefab = GameObject.GetPrefab( prefabName );
		if ( prefab is null )
		{
			Log.Warning( $"Prefab not found: {prefabName}" );
			return false;
		}

		return Pickup( prefab, targetSlot, notice );
	}

	public bool Pickup( GameObject prefab, int targetSlot, bool notice = true )
	{
		if ( !Networking.IsHost ) return false;
		if ( targetSlot < 0 || targetSlot >= MaxSlots ) return false;

		var baseCarry = prefab.Components.Get<BaseCarryable>( true );
		if ( !baseCarry.IsValid() ) return false;

		var existing = Weapons.FirstOrDefault( x => x.GameObject.Name == prefab.Name );
		if ( existing.IsValid() )
		{
			if ( existing is BaseWeapon existingWeapon && baseCarry is BaseWeapon pickupWeapon && existingWeapon.UsesAmmo )
			{
				if ( existingWeapon.ReserveAmmo >= existingWeapon.MaxReserveAmmo )
					return false;

				var ammoToGive = pickupWeapon.UsesClips ? pickupWeapon.ClipContents : pickupWeapon.StartingAmmo;
				existingWeapon.AddReserveAmmo( ammoToGive );

				if ( notice ) OnClientPickup( existing, true );
				return true;
			}
		}

		var occupant = GetSlot( targetSlot );
		if ( occupant.IsValid() ) return false;

		var clone = prefab.Clone( new CloneConfig { Parent = GameObject, StartEnabled = false } );
		clone.NetworkSpawn( false, Network.Owner );

		var cloneCarry = clone.GetComponent<BaseCarryable>( true );
		cloneCarry?.SetDropped( false );

		var weapon = clone.GetComponent<BaseCarryable>( true );
		if ( weapon is null ) return false;

		weapon.InventorySlot = targetSlot;
		weapon.OnAdded( Player );

		var pickupEvent = new PlayerPickupEvent { Player = Player, Weapon = weapon, Slot = targetSlot };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnPickup( pickupEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerPickup( pickupEvent ) );

		if ( pickupEvent.Cancelled )
		{
			weapon.DestroyGameObject();
			return false;
		}

		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnWeaponAdded( weapon ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerWeaponAdded( Player, weapon ) );

		if ( notice ) OnClientPickup( weapon );
		return true;
	}

	public void Take( BaseCarryable item, bool includeNotices )
	{
		var existing = Weapons.FirstOrDefault( x => x.GetType() == item.GetType() );
		if ( existing.IsValid() )
		{
			if ( existing is BaseWeapon existingWeapon && item is BaseWeapon pickupWeapon && existingWeapon.UsesAmmo )
			{
				if ( existingWeapon.ReserveAmmo < existingWeapon.MaxReserveAmmo )
				{
					existingWeapon.AddReserveAmmo( pickupWeapon.ClipContents );
					if ( includeNotices ) OnClientPickup( existing, true );
				}
			}
			item.DestroyGameObject();
			return;
		}

		var slot = FindEmptySlot();
		if ( slot < 0 ) return;

		item.GameObject.SetParent( GameObject, false );
		item.LocalTransform = global::Transform.Zero;
		item.InventorySlot = slot;
		item.GameObject.Enabled = false;

		if ( Network.Owner is not null )
			item.Network.AssignOwnership( Network.Owner );
		else
			item.Network.DropOwnership();

		item.OnAdded( Player );

		var pickupEvent = new PlayerPickupEvent { Player = Player, Weapon = item, Slot = slot };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnPickup( pickupEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerPickup( pickupEvent ) );

		if ( pickupEvent.Cancelled )
		{
			item.DestroyGameObject();
			return;
		}

		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnWeaponAdded( item ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerWeaponAdded( Player, item ) );

		if ( includeNotices ) OnClientPickup( item );
	}

	public bool Drop( BaseCarryable weapon )
	{
		if ( !Networking.IsHost )
		{
			HostDrop( weapon );
			return true;
		}

		if ( !weapon.IsValid() ) return false;
		if ( weapon.Owner != Player ) return false;

		var dropEvent = new PlayerDropEvent { Player = Player, Weapon = weapon };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnDrop( dropEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerDrop( dropEvent ) );
		if ( dropEvent.Cancelled ) return false;

		var dropPosition = Player.EyeTransform.Position + Player.EyeTransform.Forward * 48f;
		var dropVelocity = Player.EyeTransform.Forward * 200f + Vector3.Up * 100f;

		if ( ActiveWeapon == weapon )
			SwitchWeapon( null, true );

		// Unparent and drop in-place — the DroppedWeapon component on the same prefab handles pickup.
		// Weapons that lack a DroppedWeapon component just get destroyed.
		var droppedWeapon = weapon.GetComponent<DroppedWeapon>( true );
		if ( droppedWeapon.IsValid() )
		{
			weapon.GameObject.SetParent( null, true );
			weapon.SetDropped( true );
			weapon.GameObject.Enabled = true;
			weapon.Network.DropOwnership();

			if ( weapon.GetComponent<Rigidbody>( true ) is { } rb )
			{
				rb.Velocity = (Player.Movement?.Velocity ?? Vector3.Zero) + dropVelocity;
				rb.AngularVelocity = Vector3.Random * 8.0f;
			}
		}
		else
		{
			weapon.DestroyGameObject();
		}

		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnWeaponRemoved( weapon ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerWeaponRemoved( Player, weapon ) );

		_ = FinishDropAsync();
		return true;
	}

	[Rpc.Host]
	private void HostDrop( BaseCarryable weapon ) => Drop( weapon );

	private async Task FinishDropAsync()
	{
		await Task.Yield();
		var best = GetBestWeapon();
		if ( best.IsValid() )
			SwitchWeapon( best );
	}

	private BaseCarryable GetBestWeapon()
	{
		return Weapons
			.Where( x => x != ActiveWeapon && x.CanSwitch() && !x.ShouldAvoid )
			.OrderByDescending( x => x.Value )
			.FirstOrDefault();
	}

	public void MoveSlot( int fromSlot, int toSlot )
	{
		if ( !Networking.IsHost ) { HostMoveSlot( fromSlot, toSlot ); return; }
		if ( fromSlot == toSlot ) return;
		if ( fromSlot < 0 || fromSlot >= MaxSlots ) return;
		if ( toSlot < 0 || toSlot >= MaxSlots ) return;

		var fromWeapon = GetSlot( fromSlot );
		if ( !fromWeapon.IsValid() ) return;

		var moveEvent = new PlayerMoveSlotEvent { Player = Player, FromSlot = fromSlot, ToSlot = toSlot };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnMoveSlot( moveEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerMoveSlot( moveEvent ) );
		if ( moveEvent.Cancelled ) return;

		var toWeapon = GetSlot( toSlot );
		if ( toWeapon.IsValid() )
			toWeapon.InventorySlot = fromSlot;

		fromWeapon.InventorySlot = toSlot;
	}

	[Rpc.Host]
	private void HostMoveSlot( int fromSlot, int toSlot ) => MoveSlot( fromSlot, toSlot );

	[Rpc.Owner]
	private void OnClientPickup( BaseCarryable weapon, bool justAmmo = false )
	{
		if ( !weapon.IsValid() ) return;

		if ( ShouldAutoswitchTo( weapon ) )
			SwitchWeapon( weapon );
	}

	private bool ShouldAutoswitchTo( BaseCarryable item )
	{
		if ( !ActiveWeapon.IsValid() ) return true;
		if ( ActiveWeapon.IsInUse() ) return false;

		if ( item is BaseWeapon weapon && weapon.UsesAmmo )
		{
			if ( !weapon.HasAmmo() && !weapon.CanReload() )
				return false;
		}

		return item.Value > ActiveWeapon.Value;
	}

	void Local.IPlayerEvents.OnSpawned()
	{
		// Gamemodes can hook this to give default weapons
	}

	void Local.IPlayerEvents.OnDied( PlayerDiedParams args )
	{
		if ( !Networking.IsHost ) return;

		// Drop all weapons so they can be picked up
		foreach ( var weapon in Weapons.ToList() )
		{
			weapon.OnPlayerDeath( args );
			Drop( weapon );
		}
	}
}
