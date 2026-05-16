// Stimulus system — lets anything in the world emit sounds or smells that NPCs can react to.
//
// Usage:
//   NpcStimulusSystem.EmitSound( position, "gunshot", volume: 1f, source: gameObject );
//   NpcStimulusSystem.EmitSmell( position, "blood", radius: 300f, source: gameObject );
//
// NPCs subscribe via NpcSenses and receive the event in their OnHeardSound / OnSmelled virtuals.
// You can emit stimuli from anywhere — weapons, explosions, items, a player sneezing, whatever.
// No setup needed.

public struct NpcSoundStimulus
{
	/// <summary>Where the sound came from.</summary>
	public Vector3 Origin { get; init; }

	/// <summary>What kind of sound this is. e.g. "gunshot", "footstep", "explosion"</summary>
	public string SoundType { get; init; }

	/// <summary>0–1 volume. Louder sounds travel further (scales HearingRange on the NPC).</summary>
	public float Volume { get; init; }

	/// <summary>The GameObject that made the sound, or null for environmental.</summary>
	public GameObject Source { get; init; }
}

public struct NpcSmellStimulus
{
	/// <summary>Where the smell is coming from.</summary>
	public Vector3 Origin { get; init; }

	/// <summary>What this smell is. e.g. "blood", "food", "chemical", "corpse"</summary>
	public string SmellType { get; init; }

	/// <summary>How strong the smell is. NPCs compare this against their SmellRange.</summary>
	public float Intensity { get; init; }

	/// <summary>The GameObject emitting the smell, or null for environmental.</summary>
	public GameObject Source { get; init; }
}

public static class NpcStimulusSystem
{
	// NpcSenses subscribes to these. You fire them by calling Emit*.
	public static event Action<NpcSoundStimulus> OnSoundEmitted;
	public static event Action<NpcSmellStimulus> OnSmellEmitted;

	/// <summary>
	/// Emit a sound stimulus at a world position. Any NPC within hearing range will react.
	/// Call this from weapons, explosions, footsteps — anything that makes noise.
	/// </summary>
	public static void EmitSound( Vector3 origin, string soundType, float volume = 1f, GameObject source = null )
	{
		OnSoundEmitted?.Invoke( new NpcSoundStimulus
		{
			Origin    = origin,
			SoundType = soundType,
			Volume    = volume,
			Source    = source,
		} );
	}

	/// <summary>
	/// Emit a smell stimulus. NPCs with a smell sense and a matching interest will react.
	/// Good for: corpses, food, chemical spills, player scent trails.
	/// </summary>
	public static void EmitSmell( Vector3 origin, string smellType, float intensity = 1f, GameObject source = null )
	{
		OnSmellEmitted?.Invoke( new NpcSmellStimulus
		{
			Origin    = origin,
			SmellType = smellType,
			Intensity = intensity,
			Source    = source,
		} );
	}
}
