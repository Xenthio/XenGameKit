// NPC relationship system — Source-style disposition matrix.
//
// Every pair of NPC class types (and "player") can have a default disposition.
// Individual NPC instances can override per-target with a personal grudge/trust.
//
// Source had CAI_BaseNPC::IRelationType() with D_HT, D_FR, D_LI, D_NU.
// We mirror that with four values: Hate, Fear, Like, Ignore.
//
// Usage:
//   // Set class-level defaults at startup or in your NPC's OnStart:
//   NpcRelationships.Set<CombatNpc>( "player", NpcDisposition.Hate );
//   NpcRelationships.Set<CombatNpc>( "friendly_npc", NpcDisposition.Like );
//
//   // Override for a specific instance (this CombatNpc now hates that player):
//   npc.SetPersonalDisposition( targetGameObject, NpcDisposition.Hate );
//
//   // Query (used by NpcSenses and BaseNpc internally):
//   NpcDisposition d = npc.GetDisposition( someGameObject );
//
// Anything without an entry defaults to Ignore — NPCs won't react to unknown things.

public enum NpcDisposition
{
	Ignore, // Don't react — not in my world model
	Like,   // Friendly — follow, assist, protect
	Fear,   // Run away, cower
	Hate,   // Engage, attack
}

public static class NpcRelationships
{
	// Class-level defaults: (npcType, targetTag) → disposition
	static readonly Dictionary<(Type, string), NpcDisposition> _classTable = new();

	// Instance overrides: (npcInstanceId, targetInstanceId) → disposition
	// Keyed by scene instance id pairs so they're garbage-collected with the scene.
	static readonly Dictionary<(Guid, Guid), NpcDisposition> _instanceTable = new();

	/// <summary>
	/// Set the default disposition for all NPCs of type T toward anything tagged with targetTag.
	/// Call this in your NPC subclass's OnStart or in a static initializer.
	/// </summary>
	public static void Set<T>( string targetTag, NpcDisposition disposition ) where T : BaseNpc
		=> _classTable[(typeof(T), targetTag)] = disposition;

	public static void Set( Type npcType, string targetTag, NpcDisposition disposition )
		=> _classTable[(npcType, targetTag)] = disposition;

	/// <summary>
	/// Override the disposition between two specific instances.
	/// Good for grudges, faction events, or a player who just shot a "neutral" NPC.
	/// </summary>
	public static void SetPersonal( Guid npcId, Guid targetId, NpcDisposition disposition )
		=> _instanceTable[(npcId, targetId)] = disposition;

	/// <summary>
	/// Clear a personal override, falling back to the class default.
	/// </summary>
	public static void ClearPersonal( Guid npcId, Guid targetId )
		=> _instanceTable.Remove( (npcId, targetId) );

	/// <summary>
	/// Get the disposition from <paramref name="npcId"/> of type <paramref name="npcType"/>
	/// toward <paramref name="target"/>.
	///
	/// Resolution order:
	///   1. Personal instance override (npcId → targetId)
	///   2. Class-level default for the first matching tag on target
	///   3. Ignore
	/// </summary>
	public static NpcDisposition Get( Guid npcId, Type npcType, GameObject target )
	{
		// 1. Personal override
		if ( target.Id != Guid.Empty && _instanceTable.TryGetValue( (npcId, target.Id), out var personal ) )
			return personal;

		// 2. Class-level default — check each tag on the target
		foreach ( var tag in target.Tags )
		{
			if ( _classTable.TryGetValue( (npcType, tag), out var classDisp ) )
				return classDisp;
		}

		// 3. Walk up the type hierarchy so subclasses inherit parent defaults
		var baseType = npcType.BaseType;
		while ( baseType is not null && baseType != typeof(BaseNpc) )
		{
			foreach ( var tag in target.Tags )
			{
				if ( _classTable.TryGetValue( (baseType, tag), out var inherited ) )
					return inherited;
			}
			baseType = baseType.BaseType;
		}

		return NpcDisposition.Ignore;
	}

	/// <summary>Wipe all class and instance data. Call on scene unload.</summary>
	public static void Clear()
	{
		_classTable.Clear();
		_instanceTable.Clear();
	}
}
