namespace Simulation;

public sealed class TerrainType
{
    public required string Name { get; init; }
    public required byte Id { get; init; }
    public required uint Color { get; init; }
    public required bool Walkable { get; init; }
    public required bool Flammable { get; init; }
    public required int FoodCapacity { get; init; }
}
