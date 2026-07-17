namespace Simulation;

public sealed class SpeciesType
{
    public required string Name { get; init; }
    public required byte Id { get; init; }
    public required uint LifespanTicks { get; init; }
    public required uint LifespanVarianceTicks { get; init; }
    public required uint MaturityAge { get; init; }
    public required uint GestationTicks { get; init; }
}
