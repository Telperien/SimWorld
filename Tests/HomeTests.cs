using Simulation;

namespace Tests;

public class HomeTests
{
    // Catalogue synthetique + config fertile + couple isole : meme
    // patron que ReproductionTests/ClanTests.MakeFertileCouple (chaque
    // fichier de test garde sa propre copie, pas de partage).
    private static Catalog<SpeciesType> MakeFertileSpeciesCatalog()
    {
        return SpeciesCatalog.Load("""
        {
          "human": { "id": 0, "lifespanTicks": 2000000, "lifespanVarianceTicks": 0, "maturityAge": 0, "gestationTicks": 4 }
        }
        """);
    }

    private static SimulationConfig LoadFertileConfig()
    {
        var baseConfig = TestCatalogs.LoadSimulation();
        return baseConfig with { MateSearchRadius = 10, BaseConceptionChance = 1.0, TargetFoodPerCapita = 0.1, BaseHarvestChance = 0.0 };
    }

    private static World MakeFertileCouple(
        Catalog<TerrainType> catalog, Catalog<VegetationType> vegetation, Catalog<SpeciesType> species, SimulationConfig config, int seed)
    {
        var world = new World(seed, size: 128, catalog, vegetation, species, config);
        world.ClearAllVegetation();

        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("bush", out byte bushType);
        int matureStage = vegetation.Get(bushType).MatureStage;
        world.SetTerrainId(10, 10, grass);
        world.ForceSpawnVegetation(10, 10, bushType, (byte)matureStage);

        world.SetAgentSex(0, 0);
        world.SetAgentSex(1, 1);
        world.SetAgentPosition(0, 10f, 10f);
        world.SetAgentPosition(1, 11f, 10f);
        world.SetAgentHunger(0, 0);
        world.SetAgentHunger(1, 0);

        uint coupleClanId = world.GetAgent(0).ClanId;
        world.SetAgentClanId(1, coupleClanId);
        uint otherClanId = world.GetClan(0).Id == coupleClanId ? world.GetClan(1).Id : world.GetClan(0).Id;
        for (int i = 2; i < world.AliveCount; i++)
        {
            world.SetAgentClanId(i, otherClanId);
        }

        for (int i = 2; i < world.AliveCount; i++)
        {
            world.SetAgentHunger(i, 200);
        }

        for (int c = 0; c < world.ClanCount; c++)
        {
            world.SetClanFoodPool(c, 0);
        }

        for (int c = 0; c < world.ClanCount; c++)
        {
            if (world.GetClan(c).Id == coupleClanId)
            {
                world.SetClanFoodPool(c, 1_000_000);
                break;
            }
        }

        return world;
    }

    [Fact]
    public void Home_IsCreatedPerClan_AtClanSpawnCenter()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed: 900, size: 128, catalog, vegetation, species, config);

        Assert.Equal(world.ClanCount, world.HomeCount);
        for (int c = 0; c < world.HomeCount; c++)
        {
            Home home = world.GetHome(c);
            Assert.Equal(world.GetClan(c).Id, home.ClanId);
            Assert.InRange(home.X, 0, world.Size - 1);
            Assert.InRange(home.Y, 0, world.Size - 1);
        }
    }

    [Fact]
    public void Agent_Never_HasInvalidHomeId()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed: 901, size: 128, catalog, vegetation, species, config);

        for (int i = 0; i < world.AliveCount; i++)
        {
            Agent agent = world.GetAgent(i);
            Assert.NotEqual(Home.NoHome, agent.HomeId);
            Home home = world.GetHomeById(agent.HomeId);
            Assert.Equal(agent.ClanId, home.ClanId);
        }
    }

    [Fact]
    public void Newborn_InheritsMotherHome()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = MakeFertileSpeciesCatalog();
        var config = LoadFertileConfig();
        var world = MakeFertileCouple(catalog, vegetation, species, config, seed: 902);

        uint motherHome = world.GetAgent(0).HomeId;
        uint motherId = world.GetAgent(0).Id;

        for (int i = 0; i < 100; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        uint? newbornHome = null;
        for (int i = 0; i < world.AliveCount; i++)
        {
            Agent candidate = world.GetAgent(i);
            if (candidate.MotherId == motherId)
            {
                newbornHome = candidate.HomeId;
                break;
            }
        }

        Assert.NotNull(newbornHome);
        Assert.Equal(motherHome, newbornHome);
    }

    // Couvre toute la carte de sable (marchable partout, cf. terrain.json)
    // pour que l'ancrage ne soit jamais bloqué par un obstacle non lié au
    // mecanisme teste -- meme patron que AgentTests.MakeFoodless.
    private static void MakeFullyWalkable(World world, Catalog<TerrainType> catalog)
    {
        catalog.TryGetId("sand", out byte sand);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, sand);
            }
        }
    }

    [Fact]
    public void HomeAnchor_BiasesWanderTowardHome()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var baseConfig = TestCatalogs.LoadSimulation();
        var config = baseConfig with
        {
            HomeAnchorChance = 1.0,
            IdleMoveChance = 1.0,
            BaseHarvestChance = 0.0,
            HungerSeekThreshold = 255,
            BaseConceptionChance = 0.0,
        };
        var world = new World(seed: 903, size: 128, catalog, vegetation, species, config);
        MakeFullyWalkable(world, catalog);

        Agent agent = world.GetAgent(0);
        Home home = world.GetHomeById(agent.HomeId);
        uint agentId = agent.Id;

        // Place l'agent loin de son foyer, sur l'axe X, en restant dans
        // les bornes de la carte.
        float startX = Math.Clamp(home.X - 40, 0f, world.Size - 1f);
        world.SetAgentPosition(0, startX, home.Y + 0.5f);
        world.SetAgentHunger(0, 0);

        agent = world.GetAgent(0);
        double dx0 = agent.X - (home.X + 0.5);
        double dy0 = agent.Y - (home.Y + 0.5);
        double initialDistance = Math.Sqrt(dx0 * dx0 + dy0 * dy0);

        for (int i = 0; i < 20000; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        int index = -1;
        for (int i = 0; i < world.AliveCount; i++)
        {
            if (world.GetAgent(i).Id == agentId)
            {
                index = i;
                break;
            }
        }
        Assert.True(index >= 0, "l'agent suivi n'a pas survecu a la fenetre du test");

        Agent after = world.GetAgent(index);
        double dx1 = after.X - (home.X + 0.5);
        double dy1 = after.Y - (home.Y + 0.5);
        double finalDistance = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

        Assert.True(finalDistance < initialDistance * 0.5,
            $"distance au foyer non reduite par l'ancrage : initiale={initialDistance:F1} finale={finalDistance:F1}");
    }

    [Fact]
    public void HomeAnchor_DoesNotInterfere_WithHarvestingOrSeeking()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var baseConfig = TestCatalogs.LoadSimulation();
        var config = baseConfig with { HomeAnchorChance = 1.0, IdleMoveChance = 1.0 };
        var world = new World(seed: 904, size: 128, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("bush", out byte bushType);
        byte matureStage = (byte)vegetation.Get(bushType).MatureStage;

        Agent agent = world.GetAgent(0);
        int x = (int)MathF.Floor(agent.X);
        int y = (int)MathF.Floor(agent.Y);
        world.SetTerrainId(x, y, grass);
        world.ForceSpawnVegetation(x, y, bushType, matureStage);
        world.SetVegetationFoodRemaining(x, y, 100_000);

        for (int c = 0; c < world.ClanCount; c++)
        {
            world.SetClanFoodPool(c, 500);
        }

        world.SetAgentTarget(0, x, y);
        world.SetAgentState(0, AgentState.Harvesting);
        world.SetAgentHunger(0, 200);

        for (int i = 0; i < 50; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        // Preuve directe de la protection structurelle : meme avec
        // l'ancrage force a 100%, un agent en Harvesting n'est jamais
        // devie vers son foyer -- TryStartMoving n'est jamais atteint
        // pour un etat qui occupe physiquement l'agent.
        Agent after = world.GetAgent(0);
        Assert.Equal(AgentState.Harvesting, after.State);
        Assert.Equal(x, after.TargetX);
        Assert.Equal(y, after.TargetY);
    }
}
