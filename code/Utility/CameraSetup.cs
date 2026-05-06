public sealed class CameraSetup : Component
{
	protected override void OnPreRender()
	{
		var cc = GetComponent<CameraComponent>();
		if ( cc is null ) return;

		ICameraSetup.Post( x => x.PreSetup( cc ) );
		ICameraSetup.Post( x => x.Setup( cc ) );
		ICameraSetup.Post( x => x.PostSetup( cc ) );
	}
}

public interface ICameraSetup : ISceneEvent<ICameraSetup>
{
	void PreSetup( CameraComponent cc ) { }
	void Setup( CameraComponent cc ) { }
	void PostSetup( CameraComponent cc ) { }
}
