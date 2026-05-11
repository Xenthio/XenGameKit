using Sandbox.Utility;

[Title( "Noise Operator" )]
[Category( "Particles" )]
[Icon( "air" )]
public sealed class ParticleNoiseOperator : ParticleController
{
	public enum NoiseMode
	{
		/// <summary>Standard Perlin/Fbm — zero-mean, good for velocity turbulence.</summary>
		Fbm = 0,
		/// <summary>Billow (ridge) noise — produces positive bumps. NOT suitable for velocity
		/// fields as it has a systematic negative bias; use for density/scale modulation only.</summary>
		Billow = 1,
	}

	[Property, Group( "Noise" )] public bool EnabledNoise { get; set; } = true;
	[Property, Group( "Noise" )] public NoiseMode Mode { get; set; } = NoiseMode.Fbm;
	[Property, Group( "Noise" )] public float Frequency { get; set; } = 2.4f;
	[Property, Group( "Noise" )] public int Octaves { get; set; } = 4;
	[Property, Group( "Noise" )] public float Gain { get; set; } = 0.5f;
	[Property, Group( "Noise" )] public float Lacunarity { get; set; } = 2f;
	[Property, Group( "Noise" )] public float Seed { get; set; } = 1337f;

	/// <summary>
	/// How much world-space position contributes to the noise lookup.
	/// Larger values = more spatial variation between nearby particles.
	/// For model-surface fire keep this at 0.08–0.15 so particles spread over
	/// the model surface sample clearly different parts of the noise field.
	/// </summary>
	[Property, Group( "Noise" )] public float SpatialScale { get; set; } = 0.1f;

	/// <summary>How fast the noise field scrolls over time.</summary>
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

	// Three axis-specific time offsets so each axis scrolls through an
	// orthogonal direction in noise space; irrational ratios prevent re-sync.
	float _timeX;
	float _timeY;
	float _timeZ;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		_timeX = _timeY = _timeZ = 0f;
	}

	protected override void OnAfterStep( float delta )
	{
		base.OnAfterStep( delta );
		var rate = Math.Max( 0f, TimeScrollRate ) * delta;
		_timeX += rate;
		_timeY += rate * 0.7193f;   // irrational ratios → never re-synchronise
		_timeZ += rate * 1.3071f;
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
		var freq = Math.Max( 0.01f, Frequency * frequencyLife );

		// Spatial position in noise space — large enough to visibly separate nearby
		// particles on a model surface (default SpatialScale 0.1 = 10 units apart →
		// 1 unit apart in noise space, clearly different at typical frequencies).
		var pos = particle.Position * Math.Max( 0.0001f, SpatialScale );

		// Each axis lives in a different orthogonal "lane" of noise space.
		// The seed bias (large prime multiples) moves each axis to a
		// completely different region so their outputs are uncorrelated.
		// The time terms scroll each axis independently.
		var nx = Sample( new Vector3( pos.x + _timeX, pos.y,           pos.z           ), Seed,           freq );
		var ny = Sample( new Vector3( pos.y + _timeY, pos.z,           pos.x + 317.3f  ), Seed + 5923.7f, freq );
		var nz = Sample( new Vector3( pos.z + _timeZ, pos.x + 211.9f,  pos.y + 137.1f  ), Seed + 9871.3f, freq );

		var n = new Vector3( nx, ny, nz );

		if ( AffectVelocity )
		{
			particle.Velocity += new Vector3(
				n.x * VelocityAmplitude.x,
				n.y * VelocityAmplitude.y,
				n.z * VelocityAmplitude.z
			) * (strength * delta);
		}

		if ( AffectAngles )
		{
			particle.Angles += new Angles(
				n.x * AngularAmplitude.pitch,
				n.y * AngularAmplitude.yaw,
				n.z * AngularAmplitude.roll
			) * (strength * delta);
		}
	}

	/// <summary>
	/// Sample the noise field at <paramref name="p"/> and return a value in [-1, 1]
	/// with mean 0, suitable for use as a velocity delta.
	/// </summary>
	float Sample( Vector3 p, float seed, float frequency )
	{
		var fractal = new Noise.FractalParameters(
			(int)(seed + 5000f),
			frequency,
			Math.Max( 1, Octaves ),
			Math.Max( 0f, Gain ),
			Math.Max( 0.01f, Lacunarity )
		);

		// PerlinField returns [0,1]; map to [-1,1] so mean is 0.
		float raw = Mode switch
		{
			NoiseMode.Fbm   => Noise.PerlinField( fractal ).Sample( p ),
			// Billow: abs(centered) gives ridge shapes; still [-1,1] zero-mean.
			NoiseMode.Billow => MathF.Abs( Noise.PerlinField( fractal ).Sample( p ) * 2f - 1f ) * 2f - 1f,
			_               => 0f,
		};

		// PerlinField → [0,1]; shift to [-1,1]
		return Mode == NoiseMode.Fbm ? raw * 2f - 1f : raw;
	}

	// ── Editor visualisation ──────────────────────────────────────────────────

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		// Draw a 5×5 grid of arrows showing the XY plane of the noise field
		// at Z = 0, using the current time state.
		const int steps = 5;
		const float worldSpan = 80f;
		const float arrowScale = 12f;

		float freq = Math.Max( 0.01f, Frequency );

		using ( Gizmo.Scope( "NoiseViz", global::Transform.Zero ) )
		{
			for ( int xi = 0; xi <= steps; xi++ )
			{
				for ( int yi = 0; yi <= steps; yi++ )
				{
					var world = new Vector3(
						MathX.Lerp( -worldSpan * 0.5f, worldSpan * 0.5f, xi / (float)steps ),
						MathX.Lerp( -worldSpan * 0.5f, worldSpan * 0.5f, yi / (float)steps ),
						0f );

					var pos = world * Math.Max( 0.0001f, SpatialScale );

					var nx = Sample( new Vector3( pos.x + _timeX, pos.y,          pos.z          ), Seed,           freq );
					var ny = Sample( new Vector3( pos.y + _timeY, pos.z,          pos.x + 317.3f ), Seed + 5923.7f, freq );
					var nz = Sample( new Vector3( pos.z + _timeZ, pos.x + 211.9f, pos.y + 137.1f ), Seed + 9871.3f, freq );

					var arrow = new Vector3( nx * VelocityAmplitude.x,
					                        ny * VelocityAmplitude.y,
					                        nz * VelocityAmplitude.z ).Normal * arrowScale;

					// Colour by Z component (blue = up, red = down)
					var t = nz * 0.5f + 0.5f;
					Gizmo.Draw.Color = Color.Lerp( Color.Red, Color.Cyan, t ).WithAlpha( 0.8f );
					Gizmo.Draw.Arrow( WorldPosition + world, WorldPosition + world + arrow, 2f, 1f );
				}
			}
		}
	}
}
