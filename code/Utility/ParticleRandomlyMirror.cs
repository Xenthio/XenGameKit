
/// <summary>
/// Mirrors size but only sometimes
/// </summary>
[Title( "Randomly Mirror" )]
[Category( "Particles" )]
[Icon( "flip" )]
public sealed class ParticleRandomlyMirror : ParticleController
{
	[Property]
	public float MirrorChance { get; set; } = 0.5f;

	// lets also add the size range from the shape tab, since you have to disable that to use this.

	/// <summary>
	/// The scale of particles.
	/// </summary>
	[Property]
	public ParticleFloat Scale { get; set; } = 1.0f;

	/// <summary>
	/// The stretch factor of particles, affecting their aspect ratio.
	/// </summary>
	[Property]
	public ParticleFloat Stretch { get; set; } = 0.0f;

	[Property]
	public bool ApplyShape { get; set; } = true;

	protected override void OnParticleStep( Particle p, float delta )
	{

		if ( ApplyShape)
		{
			p.Size = Scale.Evaluate( p, 6211 );

			var aspect = Stretch.Evaluate( p, 62415 );
			if ( aspect < 0 )
			{
				p.Size.x *= aspect.Remap( 0, -1, 1, 2, false );
			}
			else if ( aspect > 0 )
			{
				p.Size.y *= aspect.Remap( 0, 1, 1, 2, false );
			}
		}

		// randomise but using seed so it's deterministic per-particle
		var rand = p.Rand();  
		if ( rand < MirrorChance )
		{
			p.Size = new Vector3( -p.Size.x, p.Size.y, p.Size.z ); // OnParticleStep called before the system sets its own size (which is dumb), where else can we do it?
			 
		}
	}
	 

}
