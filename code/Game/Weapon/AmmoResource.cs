[AssetType( Name = "Ammo Type", Extension = "ammo", Category = "XenGameKit" )]
public class AmmoResource : GameResource
{
	[Property] public string Title { get; set; }
	[Property] public Texture Icon { get; set; }
	[Property] public int MaxReserve { get; set; } = 120;
	[Property] public int DefaultStartingAmmo { get; set; } = 0;
}
