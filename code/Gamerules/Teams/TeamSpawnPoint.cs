/// <summary>
/// Add to a SpawnPoint to restrict it to a specific team.
/// The team index here should match the index in <see cref="TeamManager.Teams"/>.
/// </summary>
public class TeamSpawnPoint : Component
{
	[Property] public int TeamIndex { get; set; } = 0;
}
