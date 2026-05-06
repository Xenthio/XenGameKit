public partial class BaseWeapon
{
	[Property, FeatureEnabled( "Ammo" )] public bool UsesAmmo { get; set; } = true;
	[Property, Feature( "Ammo" )] public bool UsesClips { get; set; } = true;
	[Property, Feature( "Ammo" ), ShowIf( nameof( UsesClips ), true )] public int ClipMaxSize { get; set; } = 30;
	[Property, Feature( "Ammo" ), ShowIf( nameof( UsesClips ), true )] public int ClipContents { get; set; } = 20;
	[Property, Feature( "Ammo" )] public AmmoResource AmmoType { get; set; }

	[Property, Feature( "Ammo" )] public int MaxReserveAmmo
	{
		get => AmmoType?.MaxReserve ?? _maxReserveAmmo;
		set => _maxReserveAmmo = value;
	}
	private int _maxReserveAmmo = 120;

	[Property, Feature( "Ammo" )] public int ReserveAmmo
	{
		get
		{
			if ( AmmoType is not null )
				return GetAmmoInventory()?.GetAmmo( AmmoType ) ?? 0;
			return _reserveAmmo;
		}
		set
		{
			if ( AmmoType is not null )
			{
				GetAmmoInventory()?.SetAmmo( AmmoType, value );
				return;
			}
			_reserveAmmo = value;
		}
	}

	[Sync] private int _reserveAmmo { get; set; } = 0;

	[Property, Feature( "Ammo" )] public int StartingAmmo { get; set; } = 0;
	[Property, Feature( "Ammo" )] public float ReloadTime { get; set; } = 2.5f;

	private AmmoInventory GetAmmoInventory() => Owner?.GetComponent<AmmoInventory>();

	public override bool CanSwitch() => true;

	public bool TakeAmmo( int count )
	{
		if ( !UsesAmmo ) return true;
		if ( WeaponConVars.UnlimitedAmmo ) return true;

		if ( UsesClips )
		{
			if ( ClipContents < count ) return false;
			ClipContents -= count;
			return true;
		}

		if ( WeaponConVars.InfiniteReserves ) return true;

		if ( AmmoType is not null )
		{
			var inv = GetAmmoInventory();
			if ( inv is null ) return false;
			return inv.TakeAmmo( AmmoType, count );
		}

		if ( _reserveAmmo < count ) return false;
		_reserveAmmo -= count;
		return true;
	}

	public bool HasAmmo()
	{
		if ( !UsesAmmo ) return true;
		if ( WeaponConVars.UnlimitedAmmo ) return true;
		if ( UsesClips ) return ClipContents > 0;
		if ( WeaponConVars.InfiniteReserves ) return true;
		return ReserveAmmo > 0;
	}

	public int AddReserveAmmo( int count )
	{
		if ( AmmoType is not null )
			return GetAmmoInventory()?.AddAmmo( AmmoType, count ) ?? 0;

		var space = _maxReserveAmmo - _reserveAmmo;
		var toAdd = Math.Min( count, space );
		_reserveAmmo += toAdd;
		return toAdd;
	}
}
