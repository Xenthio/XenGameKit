// Blood and gore decal system.
//
// Drop a BloodDecal prefab reference on this component in the scene, or set a default
// in code. Call BloodSystem.Splat() from NPC damage handlers to get blood on surfaces.
//
// Source approach: TraceAttack fired per-bullet, placed decals at hit normals.
// We do the same — broadcast to all clients, parent to hit surface.
//
// Usage:
//   BloodSystem.Splat( hitPosition, hitNormal, hitObject );  // from damage/melee code
//
// To set a custom blood prefab per-NPC, set NpcBloodPrefab on the NPC prefab.
// Falls back to the scene-wide default if not set.
public static class BloodSystem
{
	// Scene-wide fallback. Set this from a GameManager startup or scene component.
	public static string DefaultBloodPrefabPath { get; set; } = "prefabs/effects/blood_impact.prefab";

	/// <summary>
	/// Spawn a blood splat at a hit point. Broadcasts to all clients.
	/// Call this from NPC damage code or melee impacts on living things.
	/// </summary>
	public static void Splat( Vector3 position, Vector3 normal, GameObject hitObject, string prefabPath = null )
	{
		SpawnBlood( position, normal, hitObject, prefabPath ?? DefaultBloodPrefabPath );

		// Also emit a blood smell stimulus so nearby NPCs can react
		NpcStimulusSystem.EmitSmell( position, "blood", intensity: 0.7f, source: hitObject );
	}

	[Rpc.Broadcast]
	static void SpawnBlood( Vector3 position, Vector3 normal, GameObject hitObject, string prefabPath )
	{
		if ( Application.IsDedicatedServer ) return;

		var prefab = ResourceLibrary.Get<PrefabFile>( prefabPath );
		if ( prefab is null ) return;

		var rot = Rotation.LookAt( normal * -1f, Vector3.Up );
		var go  = GameObject.Clone( prefab, new CloneConfig
		{
			Transform    = new Transform( position, rot ),
			StartEnabled = true,
		} );

		if ( hitObject.IsValid() )
			go.SetParent( hitObject, true );
	}
}
