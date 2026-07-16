using Simulation;

namespace Tests;

public class DeterminismTests
{
    private static TerrainCatalog LoadCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "terrain.json");
        return TerrainCatalog.Load(File.ReadAllText(path));
    }

    private static VegetationCatalog LoadVegetationCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "vegetation.json");
        return VegetationCatalog.Load(File.ReadAllText(path));
    }

    private static SimulationConfig LoadSimulationConfig()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "simulation.json");
        return SimulationConfig.Load(File.ReadAllText(path));
    }

    [Fact]
    public void Agent_Id_RemainsValid_AfterMultipleDeathsAndCompactions()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 30, size: 64, catalog, vegetation, config);

        int trackedIndex = world.AliveCount - 1;
        uint trackedId = world.GetAgent(trackedIndex).Id;

        int killed = 0;
        for (int i = 0; i < world.AliveCount && killed < 3; i++)
        {
            if (i == trackedIndex)
            {
                continue;
            }
            world.SetAgentHunger(i, 254);
            killed++;
        }

        int aliveBefore = world.AliveCount;

        for (int i = 0; i < 8; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.AliveCount < aliveBefore);

        int matches = 0;
        for (int i = 0; i < world.AliveCount; i++)
        {
            if (world.GetAgent(i).Id == trackedId)
            {
                matches++;
            }
        }

        Assert.Equal(1, matches);
    }

    [Fact]
    public void Golden_Hash_MatchesCommittedValue()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 12345, size: 128, catalog, vegetation, config);

        world.Execute(new SpawnFire(64, 64, radius: 3));

        for (int i = 0; i < 5000; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(1977737263434058813UL, world.Hash());
    }
}
