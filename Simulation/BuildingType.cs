namespace Simulation;

// Bâtiment data-driven : tiers, seuil de population, coût, matériau,
// capacités dérivées. Le catalogue charge buildings.json dans ce type.
// cost/material/provides sont des slots présents dès maintenant mais
// ignorés par la simulation (les matériaux n'existent pas encore).
public sealed record BuildingType
{
    public required string Name { get; init; }
    public required byte Id { get; init; }
    public required byte Tier { get; init; }
    public required int PopThreshold { get; init; }
    public required string Sprite { get; init; }
    public required ResourceCost Cost { get; init; }
    public required string Material { get; init; }
    public required string[] Provides { get; init; }
}

public sealed record ResourceCost
{
    public required int Wood { get; init; }
    public required int Stone { get; init; }
}