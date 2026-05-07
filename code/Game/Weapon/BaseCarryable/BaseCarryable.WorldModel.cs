using Sandbox.Citizen;
using XMovement;

public partial class BaseCarryable : Component
{
	public interface IEvent : ISceneEvent<IEvent>
	{
		public void OnCreateWorldModel() { }
		public void OnDestroyWorldModel() { }
	}

	[Property, Feature( "WorldModel" )] public GameObject WorldModelPrefab { get; set; }
	[Property, Feature( "WorldModel" )] public GameObject DroppedGameObject { get; set; }
	[Property, Feature( "WorldModel" )] public CitizenAnimationHelper.HoldTypes HoldType { get; set; } = CitizenAnimationHelper.HoldTypes.HoldItem;
	[Property, Feature( "WorldModel" )] public string ParentBone { get; set; } = "hold_r";

	protected void CreateWorldModel()
	{
		var walkController = GetComponentInParent<PlayerWalkControllerComplex>();
		if ( walkController?.BodyModelRenderer is null ) return;

		CreateWorldModel( walkController.BodyModelRenderer );
	}

	public void SetDropped( bool dropped )
	{
		var rb = GetComponent<Rigidbody>( true );
		if ( rb.IsValid() ) rb.Enabled = dropped;

		var col = GetComponent<ModelCollider>( true );
		if ( col.IsValid() ) col.Enabled = dropped;

		var droppedWeapon = GetComponent<DroppedWeapon>( true );
		if ( droppedWeapon.IsValid() ) droppedWeapon.Enabled = dropped;

		// Hide any Prop or ModelRenderer on the root when held — these are the
		// "dropped" world model representation and should only be visible when
		// the weapon is on the ground, not when it's in a player's inventory.
		var prop = GetComponent<Sandbox.Prop>( false );
		if ( prop.IsValid() ) prop.Enabled = dropped;

		var mr = GetComponent<ModelRenderer>( false );
		if ( mr.IsValid() ) mr.Enabled = dropped;

		if ( DroppedGameObject.IsValid() ) DroppedGameObject.Enabled = dropped;
	}

	public void CreateWorldModel( SkinnedModelRenderer renderer )
	{
		if ( renderer is null ) return;

		if ( Networking.IsHost )
			IsItem = false;

		SetDropped( false );

		var worldModel = WorldModelPrefab?.Clone( new CloneConfig
		{
			Parent = renderer.GetBoneObject( ParentBone ) ?? GameObject,
			StartEnabled = true,
			Transform = global::Transform.Zero
		} );

		if ( worldModel.IsValid() )
		{
			worldModel.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;

			// Match shadow render mode to the player body (shadows-only in first person)
			foreach ( var mr in worldModel.GetComponentsInChildren<ModelRenderer>() )
				mr.RenderType = renderer.RenderType;

			WorldModel = worldModel;
			IEvent.PostToGameObject( WorldModel, x => x.OnCreateWorldModel() );
		}
	}

	protected void DestroyWorldModel()
	{
		if ( WorldModel.IsValid() )
			IEvent.PostToGameObject( WorldModel, x => x.OnDestroyWorldModel() );

		WorldModel?.Destroy();
		WorldModel = default;

		if ( Networking.IsHost )
			IsItem = true;
	}
}
