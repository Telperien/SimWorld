using System.Text.Json;

namespace Simulation;

public sealed class SimulationConfig
{
    public required double FireSpreadChance { get; init; }
    public required double AgentDensity { get; init; }
    public required double VegetationDensity { get; init; }
    public required int VegetationTickInterval { get; init; }
    public required double IdleMoveChance { get; init; }
    public required double AgentMoveSpeed { get; init; }
    public required byte HungerIncreasePerThink { get; init; }
    public required byte HungerSeekThreshold { get; init; }
    public required byte HungerDecreasePerEatTick { get; init; }
    public required int MaxFoodSearchRadius { get; init; }
    public required double TerrainFeaturesAcrossMap { get; init; }
    public required double TerrainWaterThreshold { get; init; }
    public required double TerrainSandThreshold { get; init; }
    public required double TerrainGrassThreshold { get; init; }

    public static SimulationConfig Load(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<SimulationConfig>(json, options)
            ?? throw new ArgumentException("simulation JSON is empty or invalid", nameof(json));
    }
}
