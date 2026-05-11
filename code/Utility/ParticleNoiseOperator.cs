using Sandbox.Utility;

[Title( "Noise Operator" )]
[Category( "Particles" )]
[Icon( "air" )]
public sealed class ParticleNoiseOperator : ParticleController
{
	public enum NoiseMode
	{
		Perlin = 0,
		Fbm = 1,
		Billow = 2,
	}

	[Property, Group( "Noise" )] public bool EnabledNoise { get; set; } = true;
	[Property, Group( "Noise" )] public NoiseMode Mode { get; set; } = NoiseMode.Billow;
	[Property, Group( "Noise" )] public float Frequency { get; set; } = 2.4f;
	[Property, Group( "Noise" )] public int Octaves { get; set; } = 4;
	[Property, Group( "Noise" )] public float Gain { get; set; } = 0.5f;
	[Property, Group( "Noise" )] public float Lacunarity { get; set; } = 2f;
	[Property, Group( "Noise" )] public float Seed { get; set; } = 1337f;
	[Property, Group( "Noise" )] public float SpatialScale { get; set; } = 0.025f;
	[Property, Group( "Noise" )] public float TimeScrollRate { get; set; } = 1.1f;

	[Property, Group( "Forces" )] public Vector3 VelocityAmplitude { get; set; } = new Vector3( 24f, 24f, 38f );
	[Property, Group( "Forces" )] public Angles AngularAmplitude { get; set; } = new Angles( 0f, 0f, 28f );
	[Property, Group( "Forces" )] public bool AffectVelocity { get; set; } = true;
	[Property, Group( "Forces" )] public bool AffectAngles { get; set; } = false;

	[Property, Group( "Animation" )]
	public Curve StrengthOverLife { get; set; } = new Curve(
		new Curve.Frame( 0f, 0.35f ),
		new Curve.Frame( 0.15f, 1.0f ),
		new Curve.Frame( 0.8f, 0.65f ),
		new Curve.Frame( 1f, 0.25f )
	);

	[Property, Group( "Animation" )]
	public Curve FrequencyOverLife { get; set; } = new Curve(
		new Curve.Frame( 0f, 0.85f ),
		new Curve.Frame( 0.45f, 1.0f ),
		new Curve.Frame( 1f, 1.45f )
	);

	float _timeOffset;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		_timeOffset = 0f;
	}

	protected override void OnAfterStep( float delta )
	{
		base.OnAfterStep( delta );
		_timeOffset += delta * Math.Max( 0f, TimeScrollRate );
	}

	protected override void OnParticleStep( Particle particle, float delta )
	{
		base.OnParticleStep( particle, delta );

		if ( !EnabledNoise )
			return;

		var life = Math.Clamp( particle.LifeDelta, 0f, 1f );
		var strength = Math.Max( 0f, StrengthOverLife.Evaluate( life ) );
		if ( strength <= 0f )
			return;

		var frequencyLife = Math.Max( 0f, FrequencyOverLife.Evaluate( life ) );
		var currentFrequency = Math.Max( 0.01f, Frequency * frequencyLife );
		var t = particle.Age * currentFrequency + _timeOffset;
		var p = particle.Position * Math.Max( 0.0001f, SpatialScale );

		// Each axis scrolls at a different rate so they don't march in lockstep
		// (same time for all three axes = coherent drift in one direction).
		var nx = SampleAxis( p, t,           Seed + 101f, currentFrequency );
		var ny = SampleAxis( p, t * 0.7193f, Seed + 202f, currentFrequency );
		var nz = SampleAxis( p, t * 1.3071f, Seed + 303f, currentFrequency );
		var n = new Vector3( nx, ny, nz );

		if ( AffectVelocity )
		{
			particle.Velocity += new Vector3(
				n.x * VelocityAmplitude.x * strength,
				n.y * VelocityAmplitude.y * strength,
				n.z * VelocityAmplitude.z * strength
			) * delta;
		}

		if ( AffectAngles )
		{
			particle.Angles += new Angles(
				n.x * AngularAmplitude.pitch * strength,
				n.y * AngularAmplitude.yaw * strength,
				n.z * AngularAmplitude.roll * strength
			) * delta;
		}
	}

	float SampleAxis( Vector3 position, float time, float axisSeed, float frequency )
	{
		// Scroll the noise field along a unique diagonal per axis seed to avoid
		// all three outputs correlating with the same spatial direction.
		var scrollDir = new Vector3( 1f, axisSeed * 0.00013f % 1f, axisSeed * 0.00021f % 1f ).Normal;
		var p = position + new Vector3( axisSeed, axisSeed * 0.17f, axisSeed * 0.31f ) + scrollDir * time;
		var fractal = new Noise.FractalParameters(
			(int)(5000 + axisSeed),
			frequency,
			Math.Max( 1, Octaves ),
			Math.Max( 0f, Gain ),
			Math.Max( 0.01f, Lacunarity )
		);

		var value = Mode switch
		{
			NoiseMode.Perlin => ToCentered( Noise.Perlin( p.x + p.y, p.z ) ),
			NoiseMode.Fbm => ToCentered( Noise.PerlinField( fractal ).Sample( p ) ),
			NoiseMode.Billow => ToBillowSigned( Noise.PerlinField( fractal ).Sample( p ) ),
			_ => 0f,
		};

		return value;
	}

	static float ToCentered( float value )
	{
		// s&box noise APIs can be authored as either [0,1] or [-1,1] depending on source.
		// Normalize to a safe centered range without amplifying values.
		if ( value < 0f )
			return Math.Clamp( value, -1f, 1f );

		return value * 2f - 1f;
	}

	static float ToBillowSigned( float value )
	{
		var centered = ToCentered( value );
		return MathF.Abs( centered ) * 2f - 1f;
	}
}
