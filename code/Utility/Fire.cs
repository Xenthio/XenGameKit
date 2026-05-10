/// <summary>
/// Static fire system. Call FireSystem.Ignite() to add fire to any GameObject.
/// FireComponent handles all particles, sound, and damage ticking.
/// </summary>
public static class FireSystem
{
	/// <summary>
	/// Default fire particle prefab used when creating fires programmatically.
	/// Set this in GameManager.OnStart() or similar.
	/// </summary>
	public static GameObject DefaultFireParticle { get; set; }

	public static FireComponent Ignite( GameObject go )
	{
		if ( !go.IsValid() ) return null;

		var fire = go.GetOrAddComponent<FireComponent>();
		fire.Enabled = true;
		
		// Use default if not already set
		if ( !fire.FireParticleOverride.IsValid() && DefaultFireParticle.IsValid() )
			fire.FireParticleOverride = DefaultFireParticle;

		fire.AddSelfHeat( fire.MaxHeat );
		return fire;
	}

	public static void Extinguish( GameObject go )
	{
		if ( !go.IsValid() ) return;

		var fire = go.GetComponent<FireComponent>();
		if ( fire.IsValid() )
			fire.Extinguish( fire.MaxHeat );
	}

	internal static void AddHeat( FireComponent fire, float heat, bool selfHeat )
	{
		if ( !fire.IsValid() ) return;
		if ( !fire.Enabled ) return;
		if ( heat <= 0f ) return;

		if ( !selfHeat && fire.IsBurning )
			heat *= fire.IncomingHeatScale;

		var startBurning = fire.HeatLevel <= 0f;

		if ( fire.CurrentHeatAbsorb > 0f && fire.AbsorbRate > 0f )
		{
			var absorbDamage = heat * fire.AbsorbRate;
			if ( absorbDamage > fire.CurrentHeatAbsorb )
			{
				heat -= fire.CurrentHeatAbsorb / fire.AbsorbRate;
				fire.CurrentHeatAbsorb = 0f;
			}
			else
			{
				fire.CurrentHeatAbsorb -= absorbDamage;
				heat = 0f;
			}
		}

		fire.HeatLevel = MathF.Min( fire.MaxHeat, fire.HeatLevel + heat );

		if ( startBurning && fire.HeatLevel > 0f )
			fire.SetBurningState( true );
	}

	internal static void DoExtinguish( FireComponent fire, float heat )
	{
		if ( !fire.IsValid() ) return;
		if ( !fire.Enabled ) return;
		if ( heat <= 0f ) return;

		fire.HeatLevel -= heat;
		fire.CurrentHeatAbsorb = MathF.Min( fire.MaxHeatAbsorb, fire.CurrentHeatAbsorb + fire.ExtinguishAbsorbScale * heat );

		if ( fire.HeatLevel <= 0f )
		{
			fire.HeatLevel = 0f;
			fire.SetBurningState( false );
		}
	}

	internal static void Update( FireComponent fire, float dt )
	{
		if ( !fire.IsValid() ) return;
		if ( !fire.Enabled ) return;
		if ( !Networking.IsHost ) return;
		if ( dt <= 0f ) return;

		if ( !fire.InfiniteFuel )
		{
			fire.RemainingFuel -= dt;
			if ( fire.RemainingFuel <= 0f )
			{
				DoExtinguish( fire, fire.MaxHeat );
				return;
			}
		}

		var addedHeat = fire.AttackTime > 0f
			? fire.MaxHeat / fire.AttackTime
			: fire.MaxHeat;

		addedHeat *= dt * fire.GrowthRate;
		AddHeat( fire, addedHeat, true );

		if ( !fire.IsBurning )
			return;

		var strength = fire.GetHeatFraction();
		if ( strength <= 0f )
		{
			fire.SetBurningState( false );
			return;
		}

		if ( fire.TimeUntilNextDamageTick <= 0f )
		{
			DealFireDamage( fire, strength );
			SpreadHeat( fire, dt, strength );
			fire.TimeUntilNextDamageTick = fire.DamageInterval;
		}
	}

	static void DealFireDamage( FireComponent fire, float strength )
	{
		if ( !fire.DealDamage ) return;

		var radius = fire.GetDamageRadius();
		if ( radius <= 0f ) return;

		var damage = (fire.BaseDamagePerSecond + fire.BaseDamagePerSecond * strength * fire.DamageScaleByHeat) * fire.DamageInterval;
		if ( damage <= 0f ) return;

		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var hits = scene.FindInPhysics( new Sphere( fire.WorldPosition, radius ) );
		var tags = new TagSet();
		tags.Add( DamageTags.Burn );

		foreach ( var damageable in hits.SelectMany( x => x.GetComponentsInParent<Component.IDamageable>() ).Distinct() )
		{
			if ( damageable is not Component target )
				continue;

			if ( target.GameObject == fire.GameObject )
				continue;

			if ( !fire.DamagePlayers && target.GetComponentInParent<Player>( true ).IsValid() )
				continue;

			if ( fire.RequireLineOfSight )
			{
				var tr = scene.Trace.Ray( fire.WorldPosition, target.WorldPosition )
					.IgnoreGameObjectHierarchy( fire.GameObject )
					.WithTag( "map" )
					.WithoutTags( "trigger" )
					.Run();

				if ( tr.Hit && tr.GameObject.IsValid() && !target.GameObject.Root.IsDescendant( tr.GameObject ) )
					continue;
			}

			var info = new DamageInfo( damage, fire.GameObject )
			{
				Origin = fire.WorldPosition,
				Position = target.WorldPosition,
				Tags = tags
			};

			damageable.Damage( info );
		}
	}

	static void SpreadHeat( FireComponent fire, float dt, float strength )
	{
		if ( fire.SpreadHeatScale <= 0f ) return;

		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var radius = fire.GetDamageRadius();
		if ( radius <= 0f ) return;

		var nearbyFires = scene.FindInPhysics( new Sphere( fire.WorldPosition, radius ) )
			.SelectMany( x => x.GetComponentsInParent<FireComponent>() )
			.Where( x => x.IsValid() && x != fire && x.Enabled )
			.Distinct()
			.ToArray();

		if ( nearbyFires.Length == 0 ) return;

		var outputHeat = strength * fire.HeatLevel * fire.SpreadHeatScale * dt;
		if ( outputHeat <= 0f ) return;

		var perFireHeat = outputHeat / nearbyFires.Length;
		foreach ( var nearbyFire in nearbyFires )
			AddHeat( nearbyFire, perFireHeat, false );
	}
}

/// <summary>
/// Fire component. Add to a GameObject via FireSystem.Ignite() or directly as a component.
/// Owns particles, sound, damage ticking, and heat state.
/// </summary>
public sealed class FireComponent : Component
{
	[Property, Group( "Fire" )] public bool Enabled { get; set; } = true;
	[Property, Group( "Fire" )] public bool StartLit { get; set; } = false;
	[Property, Group( "Fire" )] public bool InfiniteFuel { get; set; } = true;
	[Property, Group( "Fire" )] public float FuelSeconds { get; set; } = 10f;

	[Property, Group( "Heat" )] public float MaxHeat { get; set; } = 100f;
	[Property, Group( "Heat" )] public float AttackTime { get; set; } = 4f;
	[Property, Group( "Heat" )] public float GrowthRate { get; set; } = 1f;
	[Property, Group( "Heat" )] public float InitialHeatAbsorb { get; set; } = 8f;
	[Property, Group( "Heat" )] public float AbsorbRate { get; set; } = 1f;
	[Property, Group( "Heat" )] public float IncomingHeatScale { get; set; } = 0.25f;
	[Property, Group( "Heat" )] public float ExtinguishAbsorbScale { get; set; } = 0.75f;
	[Property, Group( "Heat" )] public float MaxHeatAbsorb { get; set; } = 64f;

	[Property, Group( "Damage" )] public bool DealDamage { get; set; } = true;
	[Property, Group( "Damage" )] public bool DamagePlayers { get; set; } = true;
	[Property, Group( "Damage" )] public bool RequireLineOfSight { get; set; } = true;
	[Property, Group( "Damage" )] public float BaseDamagePerSecond { get; set; } = 8f;
	[Property, Group( "Damage" )] public float DamageScaleByHeat { get; set; } = 1f;
	[Property, Group( "Damage" )] public float DamageInterval { get; set; } = 0.2f;
	[Property, Group( "Damage" )] public float FireSize { get; set; } = 64f;
	[Property, Group( "Damage" )] public float MinimumDamageRadius { get; set; } = 16f;

	[Property, Group( "Spread" )] public float SpreadHeatScale { get; set; } = 0.2f;

	[Property, Group( "Effects" )] public GameObject FireParticleOverride { get; set; }
	[Property, Group( "Effects" )] public SoundEvent BurnSound { get; set; }

	[Sync] public float HeatLevel { get; internal set; } = 0f;
	[Sync] public bool IsBurning { get; private set; } = false;

	internal float CurrentHeatAbsorb { get; set; }
	internal float RemainingFuel { get; set; }
	internal TimeUntil TimeUntilNextDamageTick { get; set; }

	GameObject _effectInstance;
	SoundHandle _burnSound;

	protected override void OnStart()
	{
		CurrentHeatAbsorb = InitialHeatAbsorb;
		RemainingFuel = FuelSeconds;
		TimeUntilNextDamageTick = 0f;

		if ( StartLit )
		{
			HeatLevel = MaxHeat;
			SetBurningState( true );
		}
	}

	protected override void OnUpdate()
	{
		FireSystem.Update( this, Time.Delta );
	}

	protected override void OnDisabled()
	{
		SetBurningState( false );
	}

	protected override void OnDestroy()
	{
		SetBurningState( false );
	}

	public void AddHeat( float heat ) => FireSystem.AddHeat( this, heat, false );
	public void AddSelfHeat( float heat ) => FireSystem.AddHeat( this, heat, true );
	public void Extinguish( float heat ) => FireSystem.DoExtinguish( this, heat );

	public float GetHeatFraction()
	{
		if ( MaxHeat <= 0f ) return 0f;
		return Math.Clamp( HeatLevel / MaxHeat, 0f, 1f );
	}

	public float GetDamageRadius()
	{
		var strength = GetHeatFraction();
		var radius = FireSize * 0.5f * strength;
		return Math.Max( MinimumDamageRadius, radius );
	}

	internal void SetBurningState( bool burning )
	{
		if ( IsBurning == burning )
			return;

		IsBurning = burning;
		BroadcastBurnState( burning );
	}

	[Rpc.Broadcast]
	void BroadcastBurnState( bool burning )
	{
		if ( Application.IsDedicatedServer ) return;

		if ( burning )
		{
			if ( !_effectInstance.IsValid() && FireParticleOverride.IsValid() )
			{
				_effectInstance = FireParticleOverride.Clone( new CloneConfig
				{
					Parent = GameObject,
					Transform = new Transform( Vector3.Zero, Rotation.Identity ),
					StartEnabled = true
				} );

				// Set the emitter target to the GameObject this fire is on
				var emitter = _effectInstance.GetComponent<ParticleModelEmitter>();
				if ( emitter.IsValid() )
					emitter.Target = GameObject;
			}

			if ( BurnSound.IsValid() && !_burnSound.IsValid() )
				_burnSound = Sound.Play( BurnSound, WorldPosition );
		}
		else
		{
			if ( _effectInstance.IsValid() )
				_effectInstance.Destroy();

			if ( _burnSound.IsValid() )
				_burnSound.Stop();
		}
	}
}

/// <summary>
/// Thin wrapper component for map-placed fires. Calls FireSystem.Ignite() on start.
/// Use this in the editor to create pre-placed burning entities.
/// </summary>
public sealed class EnvFire : Component
{
	protected override void OnStart()
	{
		var fireComponent = GetComponent<FireComponent>();
		if ( !fireComponent.IsValid() )
		{
			fireComponent = Components.Create<FireComponent>();
		}
	}
}
