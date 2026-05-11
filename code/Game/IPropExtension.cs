/// <summary>
/// Implement on any Component in the scene to receive Prop lifecycle events
/// and auto-attach per-prop behaviour components.
///
/// Requires <see cref="PropExtensionSystem"/> in the scene.
///
/// <b>Auto-attaching behaviour:</b> implement <see cref="OnPropCreated"/> and call
/// <c>prop.GetOrAddComponent&lt;MyBehaviour&gt;()</c> there. The component lives on the
/// prop's GO and handles its own per-prop logic (collisions, sounds, etc.) independently.
///
/// <b>Global events:</b> <see cref="OnPropDamaged"/>, <see cref="OnPropBroke"/>, and
/// <see cref="OnPropIgnited"/> fire for every prop in the scene without any per-prop setup.
///
/// Example — contact shaking sounds:
/// <code>
/// public class PropSoundExtension : Component, IPropExtension
/// {
///     public void OnPropCreated( Prop prop )
///     {
///         // Attach our behaviour component to the prop GO — it handles itself from there
///         var sounds = prop.GetOrAddComponent&lt;PropContactSounds&gt;();
///         sounds.HitThreshold = 200f;
///     }
/// }
/// </code>
///
/// Example — fire:
/// <code>
/// public class FirePropExtension : Component, IPropExtension
/// {
///     public void OnPropDamaged( Prop prop, DamageInfo info )
///     {
///         if ( prop.IsFlammable &amp;&amp; info.Tags.Has( DamageTags.Burn ) )
///             FireSystem.Ignite( prop.GameObject );
///     }
///
///     public void OnPropBroke( Prop prop, List&lt;Gib&gt; gibs )
///     {
///         // ignite gibs if prop was burning ...
///     }
/// }
/// </code>
/// </summary>
public interface IPropExtension : ISceneEvent<IPropExtension>
{
	/// <summary>
	/// Called once when a Prop is first discovered by <see cref="PropExtensionSystem"/>.
	/// Use this to attach per-prop components to <c>prop.GameObject</c>.
	/// </summary>
	void OnPropCreated( Prop prop ) { }

	/// <summary>
	/// Called after a Prop takes damage (after the damage action, before the kill check).
	/// Not called for gibs unless you explicitly handle <see cref="Gib"/>.
	/// </summary>
	void OnPropDamaged( Prop prop, Sandbox.DamageInfo info ) { }

	/// <summary>
	/// Called when a Prop breaks. <paramref name="gibs"/> contains every spawned
	/// <see cref="Gib"/> from that break event (already resolved — no deferred frame needed).
	/// May be empty if the model has no break pieces.
	/// </summary>
	void OnPropBroke( Prop prop, List<Gib> gibs ) { }

	/// <summary>
	/// Called when a Prop is ignited (via <c>Prop.Ignite()</c>).
	/// Useful if you want to swap the engine's ignite.prefab for your own fire system.
	/// </summary>
	void OnPropIgnited( Prop prop ) { }
}
