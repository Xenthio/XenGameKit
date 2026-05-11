/// <summary>
/// Global system that hooks all <see cref="Prop"/> components in the scene and dispatches
/// <see cref="IPropExtension"/> events to any implementing components.
///
/// Registered as a <see cref="GameObjectSystem"/> — auto-instantiated per-scene,
/// no GameObject required in the scene hierarchy.
///
/// Individual extensions (<see cref="FirePropExtension"/>, <see cref="BuoyancyPropExtension"/>,
/// etc.) are placed as Components on any GO in the scene (e.g. engine.scene's Prop Systems GO)
/// and are discovered automatically via <c>Scene.RunEvent</c>.
/// </summary>
public sealed class PropExtensionSystem : GameObjectSystem<PropExtensionSystem>
{
	readonly HashSet<Prop> _hooked = new();

	public PropExtensionSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, Tick, "PropExtensionSystem.Tick" );
	}

	void Tick()
	{
		foreach ( var prop in Scene.GetAllComponents<Prop>() )
		{
			if ( !_hooked.Add( prop ) ) continue;
			Hook( prop );
		}

		_hooked.RemoveWhere( p => !p.IsValid() );
	}

	void Hook( Prop prop )
	{
		Scene.RunEvent<IPropExtension>( x => x.OnPropCreated( prop ) );

		var prevDamage = prop.OnPropTakeDamage;
		prop.OnPropTakeDamage = info =>
		{
			prevDamage?.Invoke( info );
			Scene.RunEvent<IPropExtension>( x => x.OnPropDamaged( prop, info ) );
		};

		var prevBreak = prop.OnPropBreak;
		prop.OnPropBreak = () =>
		{
			prevBreak?.Invoke();
			_ = DispatchBreakNextFrame( prop );
		};
	}

	async Task DispatchBreakNextFrame( Prop prop )
	{
		var origin = prop.IsValid() ? prop.WorldPosition : Vector3.Zero;

		await Task.Yield();

		if ( !Scene.IsValid() ) return;

		var gibs = Scene.GetAllComponents<Gib>()
			.Where( g => g.IsValid() && g.WorldPosition.Distance( origin ) <= 256f )
			.ToList();

		Scene.RunEvent<IPropExtension>( x => x.OnPropBroke( prop, gibs ) );
	}
}
