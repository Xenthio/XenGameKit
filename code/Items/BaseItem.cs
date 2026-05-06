/// <summary>
/// Base class for all world items that can be walked over to pick up.
/// Equivalent to CItem in Source Engine — touch-triggered, respawnable, with pickup sound.
/// Add this component alongside a trigger collider on any pickup prefab.
/// </summary>
public abstract class BaseItem : Component, Component.ITriggerListener
{
	[RequireComponent] public Collider Collider { get; set; }

	[Property] public SoundEvent PickupSound { get; set; }
	[Property] public bool Respawns { get; set; } = false;
	[Property] public float RespawnTime { get; set; } = 30f;

	[Sync] public bool IsActive { get; private set; } = true;

	/// <summary>
	/// Try to give this item to the player. Return true if the player needed it and it was consumed.
	/// </summary>
	protected abstract bool OnPickup( Player player );

	void ITriggerListener.OnTriggerEnter( GameObject other )
	{
		if ( !Networking.IsHost ) return;
		if ( !IsActive ) return;

		var player = other.GetComponentInParent<Player>( true );
		if ( !player.IsValid() ) return;

		if ( !OnPickup( player ) ) return;

		PlayPickupSound();

		if ( Respawns )
		{
			Deactivate();
			Invoke( RespawnTime, Activate );
		}
		else
		{
			GameObject.Destroy();
		}
	}

	void Deactivate()
	{
		IsActive = false;
		foreach ( var r in GetComponentsInChildren<ModelRenderer>() )
			r.Enabled = false;
		Collider.Enabled = false;
	}

	void Activate()
	{
		IsActive = true;
		foreach ( var r in GetComponentsInChildren<ModelRenderer>() )
			r.Enabled = true;
		Collider.Enabled = true;
	}

	[Rpc.Broadcast]
	void PlayPickupSound()
	{
		if ( Application.IsDedicatedServer ) return;
		if ( PickupSound.IsValid() )
			Sound.Play( PickupSound, WorldPosition );
	}
}
