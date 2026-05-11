/// <summary>
/// Static fire system. Call FireSystem.Ignite() to add fire to any GameObject.
/// Spawns the project-local override of /prefabs/engine/ignite.prefab which
/// contains FireComponent (our full heat/spread system) instead of the engine's
/// flat FireDamage. Cross-game compatible: Prop.Ignite() resolves the same path.
/// </summary>
public static class FireSystem
{
	public static FireComponent Ignite( GameObject go )
	{
		if ( !go.IsValid() ) return null;

		// Re-use an existing FireComponent if already burning
		var fire = go.GetComponent<FireComponent>( true );
		if ( fire.IsValid() )
		{
			fire.AddSelfHeat( fire.MaxHeat );
			return fire;
		}

		// Spawn ignite.prefab (our project override of the engine default).
		// The prefab contains FireComponent with StartLit=true, so it self-starts.
		var prefab = ResourceLibrary.Get<PrefabFile>( "/prefabs/engine/ignite.prefab" );
		if ( prefab == null )
		{
			Log.Warning( "FireSystem.Ignite: can't find /prefabs/engine/ignite.prefab" );
			return null;
		}

		var cloned = GameObject.Clone( prefab, new CloneConfig { Parent = null, Transform = new global::Transform( go.WorldPosition ), StartEnabled = true } );

		// Wire all ParticleModelEmitters to target the burning GO
		cloned.RunEvent<ParticleModelEmitter>( x => x.Target = go );

		// Grab the FireComponent from the spawned prefab instance
		fire = cloned.GetComponentInChildren<FireComponent>( true );
		if ( !fire.IsValid() )
		{
			Log.Warning( "FireSystem.Ignite: ignite.prefab has no FireComponent" );
			cloned.Destroy();
			return null;
		}

		// Give FireComponent a reference so it can shut down emitters/sound on extinguish
		fire._igniteInstance = cloned;

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


	[Sync] public float HeatLevel { get; internal set; } = 0f;
	[Sync] public bool IsBurning { get; private set; } = false;

	internal float CurrentHeatAbsorb { get; set; }
	internal float RemainingFuel { get; set; }
	internal TimeUntil TimeUntilNextDamageTick { get; set; }

	internal GameObject _igniteInstance; // reference to spawned ignite.prefab GO for shutdown

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
		// BecomeOrphan=true on TemporaryEffect means the ignite GO survives
		// independently — stop emitters so particles and sound finish cleanly.
		ShutdownIgniteInstance();
		IsBurning = false;
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
		if ( IsBurning == burning ) return;
		IsBurning = burning;

		if ( !burning )
			ShutdownIgniteInstance();
	}

	void ShutdownIgniteInstance()
	{
		if ( !_igniteInstance.IsValid() ) return;

		// Stop all emitters so particles and sound fade out naturally.
		// TemporaryEffect (BecomeOrphan=true, WaitForChildEffects=true) destroys the GO
		// once every ParticleEffect empties.
		foreach ( var emitter in _igniteInstance.GetComponentsInChildren<ParticleModelEmitter>( true ) )
			emitter.Enabled = false;

		// Stop the looping sound immediately
		foreach ( var sound in _igniteInstance.GetComponentsInChildren<SoundBoxComponent>( true ) )
			sound.Enabled = false;

		_igniteInstance = null;
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
