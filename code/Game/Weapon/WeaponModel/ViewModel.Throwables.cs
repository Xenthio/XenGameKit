public partial class ViewModel
{
	public enum Throwable
	{
		HEGrenade,
		SmokeGrenade,
		StunGrenade,
		Molotov,
		Flashbang
	}

	[Property, FeatureEnabled( "Throwables" )] public bool IsThrowable { get; set; }
	[Property, Feature( "Throwables" )] public Throwable ThrowableType { get; set; }

	protected override void OnEnabled()
	{
		if ( IsThrowable )
			Renderer?.Set( "throwable_type", (int)ThrowableType );
	}
}
