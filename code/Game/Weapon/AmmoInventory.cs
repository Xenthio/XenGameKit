public sealed class AmmoInventory : Component
{
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, int> Pool { get; set; } = new();

	public int GetAmmo( AmmoResource resource )
	{
		if ( resource is null ) return 0;
		return Pool.TryGetValue( resource.ResourcePath, out var count ) ? count : 0;
	}

	public void SetAmmo( AmmoResource resource, int value )
	{
		if ( resource is null ) return;
		if ( !Networking.IsHost ) { SetAmmoRpc( resource, Math.Clamp( value, 0, resource.MaxReserve ) ); return; }
		Pool[resource.ResourcePath] = Math.Clamp( value, 0, resource.MaxReserve );
	}

	public int AddAmmo( AmmoResource resource, int count )
	{
		if ( resource is null ) return 0;
		if ( !Networking.IsHost ) { AddAmmoRpc( resource, count ); return count; }
		var current = GetAmmo( resource );
		var space = resource.MaxReserve - current;
		var toAdd = Math.Min( count, space );
		if ( toAdd <= 0 ) return 0;
		Pool[resource.ResourcePath] = current + toAdd;
		return toAdd;
	}

	public bool TakeAmmo( AmmoResource resource, int count )
	{
		if ( resource is null ) return false;
		if ( !Networking.IsHost ) { TakeAmmoRpc( resource, count ); return GetAmmo( resource ) >= count; }
		var current = GetAmmo( resource );
		if ( current < count ) return false;
		Pool[resource.ResourcePath] = current - count;
		return true;
	}

	public bool HasAmmo( AmmoResource resource, int count = 1 )
	{
		return GetAmmo( resource ) >= count;
	}

	[Rpc.Host]
	private void SetAmmoRpc( AmmoResource resource, int value )
	{
		Pool[resource.ResourcePath] = value;
	}

	[Rpc.Host]
	private void AddAmmoRpc( AmmoResource resource, int count )
	{
		var current = Pool.TryGetValue( resource.ResourcePath, out var c ) ? c : 0;
		var toAdd = Math.Min( count, resource.MaxReserve - current );
		if ( toAdd > 0 )
			Pool[resource.ResourcePath] = current + toAdd;
	}

	[Rpc.Host]
	private void TakeAmmoRpc( AmmoResource resource, int count )
	{
		var current = Pool.TryGetValue( resource.ResourcePath, out var c ) ? c : 0;
		if ( current >= count )
			Pool[resource.ResourcePath] = current - count;
	}
}
