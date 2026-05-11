/// <summary>
/// Attaches <see cref="PropBuoyancy"/> to every non-static prop via
/// <see cref="IPropExtension.OnPropCreated"/>.
///
/// Add alongside <see cref="PropExtensionSystem"/> on your GameManager GO.
///
/// Requires water volumes to be tagged <c>"water"</c> and have a trigger <see cref="Collider"/>
/// (e.g. <see cref="BoxCollider"/> with IsTrigger = true). The sandbox <see cref="WaterVolume"/>
/// component can coexist on the same GO — this extension is additive, not a replacement.
/// </summary>
[Title( "Buoyancy Prop Extension" )]
[Category( "Physics" )]
[Icon( "water" )]
public sealed class BuoyancyPropExtension : Component, IPropExtension
{
	/// <summary>Tag that identifies water trigger volumes in the scene.</summary>
	[Property] public string WaterTag { get; set; } = "water";

	/// <summary>Fraction submerged before fire is extinguished (0.5 = half, matching HL2).</summary>
	[Property, Range( 0f, 1f )] public float ExtinguishFraction { get; set; } = 0.5f;

	public void OnPropCreated( Prop prop )
	{
		// Skip static props — they have no Rigidbody and can't float
		if ( prop.IsStatic ) return;
		// Skip gibs — they fade out quickly anyway and buoyancy adds little value
		if ( prop is Gib ) return;

		var buoyancy = prop.GetOrAddComponent<PropBuoyancy>();
		buoyancy.WaterTag = WaterTag;
		buoyancy.ExtinguishSubmergedFraction = ExtinguishFraction;
	}
}
