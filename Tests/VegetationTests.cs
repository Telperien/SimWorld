using Simulation;

namespace Tests;

public class VegetationTests
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
    public void Vegetation_YoungBushes_GrowIntoMature()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 2, size: 32, catalog, vegetation, config);

        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("bush", out byte bushType);
        int matureStage = vegetation.Get(bushType).MatureStage;

        world.SetTerrainId(4, 4, grass);
        world.ForceSpawnVegetation(4, 4, bushType, stage: 0);

        for (int i = 0; i < (matureStage + 2) * config.VegetationTickInterval; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.TryGetVegetationAt(4, 4, out Vegetation veg));
        Assert.True(veg.Stage >= matureStage);
    }

    [Fact]
    public void Vegetation_SpreadsOnEmptyGrass_OverTime()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 15, size: 64, catalog, vegetation, config);

        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }

        for (int i = 0; i < config.VegetationTickInterval * 3; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.VegetationCount > 0);
    }
}
