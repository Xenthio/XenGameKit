using Sandbox.Rendering;
using XMovement;

/// <summary>
/// Info about a trace attack.
/// </summary>
public record struct TraceAttackInfo( GameObject Target, float Damage, TagSet Tags = null, Vector3 Position = default, Vector3 Origin = default, Hitbox Hitbox = null )
{
	public static TraceAttackInfo From( SceneTraceResult tr, float damage, TagSet tags = default, bool localise = true )
	{
		tags ??= new();

		if ( localise && tr.Hitbox?.Tags is not null )
			tags.Add( tr.Hitbox?.Tags );

		return new TraceAttackInfo( tr.GameObject, damage, tags, tr.HitPosition, tr.StartPosition, tr.Hitbox );
	}
}

/// <summary>
/// Determines which inventory slot/bucket a carryable prefers to occupy.
/// Mirrors Source Engine's weapon bucket system — weapons of the same type share a bucket,
/// and multiple weapons in the same bucket are cycled with repeated slot-key presses.
/// 
/// Convention (matches GoldSrc/Source defaults):
///   0 = Melee / Crowbar
///   1 = Pistols / Handguns
///   2 = SMGs / Automatics
///   3 = Rifles / Shotguns
///   4 = Heavy / RPGs
///   5 = Throwables / Grenades
/// 
/// Override PreferredBucket on a derived class to hard-code the bucket,
/// or set it as a [Property] per-prefab for maximum flexibility.
/// </summary>
public enum WeaponBucket
{
	Melee       = 0,
	Pistol      = 1,
	SMG         = 2,
	Rifle       = 3,
	Heavy       = 4,
	Throwable   = 5,
}

public partial class BaseCarryable : Component
{
	[Property, Feature( "Inventory" )] public string DisplayName { get; set; } = "My Weapon";
	[Property, Feature( "Inventory" ), TextArea] public Texture DisplayIcon { get; set; }
	[Property, Feature( "Inventory" )] public int Value { get; set; } = 0;
	[Property, Feature( "Inventory" )] public float HolsterTime { get; set; } = 0f;

	/// <summary>
	/// Which slot/bucket this weapon prefers in the inventory.
	/// Used by PlayerInventory to determine where to place this weapon on pickup.
	/// Multiple weapons can share a bucket — slot-key presses cycle through them.
	/// </summary>
	[Property, Feature( "Inventory" )] public WeaponBucket Bucket { get; set; } = WeaponBucket.Melee;

	public GameObject ViewModel { get; protected set; }
	public GameObject WorldModel { get; protected set; }

	[Property] public GameObject MuzzleGameObject { get; set; }

	public virtual string InventoryIconOverride => null;
	public virtual bool ShouldAvoid => false;
	public virtual bool WantsHideHud => false;

	public WeaponModel WeaponModel
	{
		get
		{
			var go = ViewModel;

			if ( Scene.Camera.RenderExcludeTags.Contains( "firstperson" ) ) go = default;

			if ( !go.IsValid() ) go = WorldModel;
			if ( !go.IsValid() ) go = GameObject;

			var wm = go.GetComponentInChildren<WeaponModel>();
			if ( wm.IsValid() ) return wm;

			return GameObject.GetComponentInChildren<WeaponModel>();
		}
	}

	public Player Owner => GetComponentInParent<Player>( true );
	public bool HasOwner => Owner.IsValid();

	public Ray AimRay
	{
		get
		{
			if ( HasOwner )
				return Owner.EyeTransform.ForwardRay;

			var muzzle = MuzzleTransform.WorldTransform;
			return new Ray( muzzle.Position, muzzle.Rotation.Forward );
		}
	}

	public GameObject AimIgnoreRoot => HasOwner ? Owner.GameObject : GameObject;

	protected GameObject EffectiveAttacker => HasOwner ? Owner.GameObject : GameObject;

	public GameObject MuzzleTransform
	{
		get
		{
			if ( WeaponModel?.MuzzleTransform.IsValid() ?? false ) return WeaponModel.MuzzleTransform;
			if ( MuzzleGameObject.IsValid() ) return MuzzleGameObject;
			return GameObject;
		}
	}

	[Sync( SyncFlags.FromHost )] public int InventorySlot { get; set; } = -1;

	[Sync( SyncFlags.FromHost ), Change( nameof( OnItemVisibility ) )]
	public bool IsItem { get; set; } = true;

	private void OnItemVisibility( bool oldVal, bool newVal )
	{
		if ( DroppedGameObject.IsValid() )
			DroppedGameObject.Enabled = newVal;
	}

	public virtual bool CanSwitch() => true;

	protected override void OnEnabled()
	{
		CreateWorldModel();
	}

	protected override void OnDisabled()
	{
		DestroyWorldModel();
		DestroyViewModel();
	}

	protected override void OnUpdate()
	{
		var player = Owner;
		var controller = player?.WalkController;
		if ( controller is null ) return;

		if ( player.IsLocalPlayer )
		{
			if ( Scene.Camera is null ) return;

			var hud = Scene.Camera.Hud;
			var aimPos = Screen.Size * 0.5f;

			if ( controller.CameraMode == PlayerWalkControllerComplex.CameraModes.ThirdPerson )
			{
				var tr = Scene.Trace.Ray( AimRay, 4096 )
								.IgnoreGameObjectHierarchy( AimIgnoreRoot )
								.Run();

				aimPos = Scene.Camera.PointToScreenPixels( tr.EndPosition );
			}

			if ( !Scene.Camera.RenderExcludeTags.Has( "ui" ) )
				DrawHud( hud, aimPos );
		}
	}

	public virtual void DrawHud( HudPainter painter, Vector2 crosshair ) { }

	public virtual void OnAdded( Player player ) { }

	public virtual void OnDeploy() { }

	public virtual void OnHolster() { }

	public virtual void OnFrameUpdate( Player player )
	{
		if ( player is null ) return;

		if ( !player.WalkController.CameraMode.Equals( PlayerWalkControllerComplex.CameraModes.ThirdPerson ) )
			CreateViewModel();
		else
			DestroyViewModel();

		GameObject.Network.Interpolation = false;
	}

	public virtual void OnPlayerUpdate( Player player )
	{
		if ( IsProxy ) return;

		try
		{
			OnControl( player );
		}
		catch ( System.Exception e )
		{
			Log.Error( e, $"{GetType().Name}.OnControl {e.Message}" );
		}
	}

	public virtual void OnControl( Player player ) { }

	public virtual void OnCameraSetup( Player player, Sandbox.CameraComponent camera ) { }

	public virtual void OnCameraMove( Player player, ref Angles angles ) { }

	[Rpc.Host]
	public void TraceAttack( TraceAttackInfo attack )
	{
		if ( !attack.Target.IsValid() ) return;

		var attacker = EffectiveAttacker;

		var damagable = attack.Target.GetComponentInParent<IDamageable>();
		if ( damagable is not null )
		{
			var info = new DamageInfo( attack.Damage, attacker, GameObject );
			info.Position = attack.Position;
			info.Origin = attack.Origin;
			info.Tags = attack.Tags;

			damagable.OnDamage( info );
		}

		if ( attack.Target.GetComponentInChildren<Rigidbody>() is var rb && rb.IsValid() )
		{
			rb.ApplyImpulseAt( attack.Position, Vector3.Direction( attack.Origin, attack.Position ) * rb.Mass * 100 );
		}
	}

	public virtual bool IsInUse() => false;

	public virtual void OnPlayerDeath( PlayerDiedParams args ) { }
}
