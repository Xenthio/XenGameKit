using Sandbox;
using Sandbox.VR;
namespace XMovement;

public partial class PlayerWalkControllerComplex : Component
{
	[Property, FeatureEnabled( "VR" )] public bool EnableVR { get; set; } = false;
	[Property, Feature( "VR" )] public VRAnchor Anchor { get; set; }
	[Property] public VRTrackedObject HeadTracker { get; set; }
	[Sync] public bool IsInVR { get; set; } = false;

	private void SetupVR()
	{
		if ( !EnableVR ) return;
		IsInVR = Game.IsRunningInVR && Game.IsPlaying;
		if ( Head.IsValid() && !HeadTracker.IsValid() )
		{
			HeadTracker = Head.Components.Create<VRTrackedObject>();
			HeadTracker.PoseSource = VRTrackedObject.PoseSources.Head;
			HeadTracker.TrackingType = VRTrackedObject.TrackingTypes.All;
			HeadTracker.UseRelativeTransform = false;
		}
	}

	private void UpdateVR()
	{
		if ( !IsInVR ) return;
	}
	public virtual void VRPositionHead()
	{
		if ( !IsInVR ) return;
	}
}
