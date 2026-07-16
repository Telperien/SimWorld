namespace Simulation;

public sealed class VegetationType
{
    public required string Name { get; init; }
    public required byte Id { get; init; }
    public required uint Color { get; init; }
    public required int MatureStage { get; init; }
    public required bool Flammable { get; init; }
    public required int FoodValue { get; init; }

    // 0 = immortel par l'âge (sort par un autre chemin, ex: consommation).
    public required int LifespanTicks { get; init; }
    public required int LifespanVarianceTicks { get; init; }
}
