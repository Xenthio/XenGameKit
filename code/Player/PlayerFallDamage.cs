/// <summary>
/// Source-engine-accurate fall damage.
/// Damage kicks in at 580 u/s fall speed (safe zone), reaches 100 damage at 1024 u/s.
/// Matches player.cpp from Source SDK.
/// </summary>
public class PlayerFallDamage : Component, Local.IPlayerEvents
{
	[RequireComponent] public Player Player { get; set; }

	/// <summary>Fall speed (u/s) below which no damage is taken. Source default: 580.</summary>
	[Property] public float SafeFallSpeed { get; set; } = 580f;

	/// <summary>Fall speed (u/s) that deals maximum damage (100). Source default: 1024.</summary>
	[Property] public float MaxFallSpeed { get; set; } = 1024f;

	/// <summary>Scales all fall damage. 1.0 = standard, 0 = no fall damage.</summary>
	[Property] public float DamageMultiplier { get; set; } = 1.0f;

	void Local.IPlayerEvents.OnLand( float distance, Vector3 impactVelocity )
	{
		if ( IsProxy ) return;

		// Source measures fall damage by downward velocity on impact, not distance
		var fallSpeed = -impactVelocity.z;
		if ( fallSpeed < SafeFallSpeed ) return;

		// Remap [SafeFallSpeed, MaxFallSpeed] → [0, 100] damage
		var damage = MathX.Remap( fallSpeed, SafeFallSpeed, MaxFallSpeed, 0f, 100f ) * DamageMultiplier;
		damage = MathF.Max( 1f, damage );

		var info = new DamageInfo( damage, (GameObject)null )
		{
			Tags = new TagSet { DamageTags.Fall }
		};
		Player.OnDamage( info );
	}
}
