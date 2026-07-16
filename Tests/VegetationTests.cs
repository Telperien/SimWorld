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

    [Fact]
    public void Bush_Disappears_WhenFoodDepleted()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 50, size: 64, catalog, vegetation, config);

        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("bush", out byte bushType);
        byte matureStage = (byte)vegetation.Get(bushType).MatureStage;

        Agent agent = world.GetAgent(0);
        int x = (int)MathF.Floor(agent.X);
        int y = (int)MathF.Floor(agent.Y);
        world.SetTerrainId(x, y, grass);
        world.ForceSpawnVegetation(x, y, bushType, matureStage);
        world.SetVegetationFoodRemaining(x, y, config.HarvestAmountPerTick);
        world.SetAgentHunger(0, 200);

        for (int i = 0; i < 8; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.False(world.TryGetVegetationAt(x, y, out _));
    }

    [Fact]
    public void Vegetation_RegrowsAfterDelay_NotInstantly()
    {
        var catalog = LoadCatalog();
        // Capacité de végétation portée à 100 % des tuiles : sans ça, la
        // chance de repousse élevée sature la capacité avant même que le
        // balayage (point de départ tournant) atteigne la tuile (8,8),
        // ce qui ferait échouer le test pour une raison sans rapport
        // avec le délai qu'il vérifie.
        var config = LoadSimulationConfig() with { VegetationDensity = 1.0 };
        // Catalogue synthétique : chance de repousse très haute pour un
        // test déterministe qui ne dépend pas du tuning réel du jeu.
        var vegetation = VegetationCatalog.Load("""
        {
          "bush": { "id": 0, "color": "#000000", "matureStage": 1, "spawnChance": 0.9, "flammable": false, "foodValue": 10 },
          "tree": { "id": 1, "color": "#000000", "matureStage": 1, "spawnChance": 0.0, "flammable": true, "foodValue": 0 }
        }
        """);

        var world = new World(seed: 40, size: 16, catalog, vegetation, config);
        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }

        vegetation.TryGetId("bush", out byte bushType);
        world.ForceSpawnVegetation(8, 8, bushType, stage: 1);
        world.ClearVegetationAt(8, 8);

        int delayVegTicks = (config.VegetationRegrowthDelayTicks / config.VegetationTickInterval) - 1;
        for (int i = 0; i < delayVegTicks * config.VegetationTickInterval; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }
        Assert.False(world.TryGetVegetationAt(8, 8, out _));

        for (int i = 0; i < config.VegetationTickInterval * 5; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }
        Assert.True(world.TryGetVegetationAt(8, 8, out _));
    }

    [Fact]
    public void Vegetation_SpatialDistribution_IsBalanced()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 70, size: 64, catalog, vegetation, config);

        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }

        for (int i = 0; i < config.VegetationTickInterval * 200; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        int half = world.Size / 2;
        int[] quadrants = new int[4];
        for (int i = 0; i < world.VegetationCount; i++)
        {
            Vegetation veg = world.GetVegetation(i);
            int quadrant = (veg.X < half ? 0 : 1) + (veg.Y < half ? 0 : 2);
            quadrants[quadrant]++;
        }

        Assert.True(world.VegetationCount > 20, "pas assez de végétation pour juger de la répartition");

        double average = world.VegetationCount / 4.0;
        foreach (int count in quadrants)
        {
            Assert.InRange(count, average * 0.5, average * 1.5);
        }
    }

    [Fact]
    public void Ash_RecoversToGrass_OverTime()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig() with { AshToGrassChance = 0.9 };
        var world = new World(seed: 80, size: 16, catalog, vegetation, config);

        catalog.TryGetId("ash", out byte ash);
        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, ash);
            }
        }

        for (int i = 0; i < config.VegetationTickInterval * 3; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        // SetTerrainId ci-dessus ne touche pas GrassTileCount/AshTileCount
        // (entretenus uniquement par TickFire/TickAshRecovery) : on
        // vérifie donc directement les tuiles plutôt que les compteurs.
        int grassTiles = 0;
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                if (world.GetTerrainId(x, y) == grass)
                {
                    grassTiles++;
                }
            }
        }

        Assert.True(grassTiles > 0);
    }
}
