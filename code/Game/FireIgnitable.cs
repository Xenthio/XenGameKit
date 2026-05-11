/// <summary>
/// HL2-style fire propagation for all props, implemented as an <see cref="IPropExtension"/>.
/// Add to the GameManager GO alongside <see cref="PropExtensionSystem"/>.
/// No per-prop setup required.
/// </summary>
[Title( "Fire Prop Extension" )]
[Category( "Fire" )]
[Icon( "local_fire_department" )]
public sealed class FirePropExtension : Component, IPropExtension
{
	/// <summary>Radius to search for gibs to ignite when a burning prop breaks.</summary>
	[Property] public float GibFireRadius { get; set; } = 128f;

	/// <summary>Min/max fuel seconds for gib fire. Mirrors HL2's 5–10 s range.</summary>
	[Property] public Vector2 GibFuelRange { get; set; } = new Vector2( 5f, 10f );

	// ── IPropExtension ────────────────────────────────────────────────────────

	/// <summary>
	/// Nothing to attach per-prop for fire — we react to damage and break events globally.
	/// </summary>
	public void OnPropCreated( Prop prop ) { }

	/// <summary>Ignite flammable props that receive burn damage.</summary>
	public void OnPropDamaged( Prop prop, Sandbox.DamageInfo info )
	{
		if ( IsProxy ) return;
		if ( !prop.IsFlammable ) return;
		if ( prop.GetComponent<FireComponent>( true ).IsValid() ) return;
		if ( !info.Tags.Has( DamageTags.Burn ) ) return;

		FireSystem.Ignite( prop.GameObject );
	}

	/// <summary>Propagate fire to gibs if the prop was burning when it broke.</summary>
	public void OnPropBroke( Prop prop, List<Gib> gibs )
	{
		if ( IsProxy ) return;
		if ( !prop.IsValid() ) return;

		var fire = prop.GetComponent<FireComponent>( true );
		if ( !fire.IsValid() || !fire.IsBurning ) return;

		foreach ( var gib in gibs )
		{
			if ( !gib.IsValid() || gib.IsProxy ) continue;
			if ( gib.WorldPosition.Distance( prop.WorldPosition ) > GibFireRadius ) continue;

			var gibFire = FireSystem.Ignite( gib.GameObject );
			if ( !gibFire.IsValid() ) continue;

			gibFire.InfiniteFuel  = false;
			gibFire.FuelSeconds   = Random.Shared.Float( GibFuelRange.x, GibFuelRange.y );
			gibFire.RemainingFuel = gibFire.FuelSeconds;
		}
	}
}
