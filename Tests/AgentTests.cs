using Simulation;

namespace Tests;

public class AgentTests
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

    // Nombre de ticks réels nécessaires pour qu'un agent, sans jamais
    // manger, atteigne `threshold` de faim : `threshold` s'accumule par
    // pas de `HungerIncreasePerThink`, une seule fois tous les 4 ticks
    // réels (mise à jour étalée, cf. CLAUDE.md).
    private static int TicksUntilHungerThreshold(SimulationConfig config, byte threshold)
    {
        int thinkTicksNeeded = (int)Math.Ceiling(threshold / (double)config.HungerIncreasePerThink);
        return thinkTicksNeeded * 4;
    }

    private static void MakeFoodless(World world, TerrainCatalog catalog)
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
    public void Agents_SpawnOnlyOnWalkableTiles()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 5, size: 128, catalog, vegetation, config);

        for (int i = 0; i < world.AgentCapacity; i++)
        {
            Agent agent = world.GetAgent(i);
            int x = (int)MathF.Floor(agent.X);
            int y = (int)MathF.Floor(agent.Y);
            byte terrainId = world.GetTerrainId(x, y);
            Assert.True(catalog.Get(terrainId).Walkable);
        }
    }

    [Fact]
    public void Agents_Count_MatchesRequestedDensity()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 1, size: 512, catalog, vegetation, config);

        Assert.InRange(world.AgentCapacity, 150, 250);
        Assert.Equal(world.AgentCapacity, world.AliveCount);
    }

    [Fact]
    public void Agents_Movement_IsDeterministic_ForSameSeed()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();

        var a = new World(seed: 21, size: 128, catalog, vegetation, config);
        var b = new World(seed: 21, size: 128, catalog, vegetation, config);

        for (int i = 0; i < 550; i++)
        {
            a.Tick(World.TickIntervalSeconds);
            b.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(a.Hash(), b.Hash());
    }

    [Fact]
    public void Agent_Dies_WithoutFood_AfterThreshold()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 3, size: 64, catalog, vegetation, config);
        MakeFoodless(world, catalog);
        int initialCount = world.AliveCount;

        int deathTicks = TicksUntilHungerThreshold(config, 255);

        for (int i = 0; i < deathTicks - 20; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }
        Assert.Equal(initialCount, world.AliveCount);

        for (int i = 0; i < 40; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }
        Assert.True(world.AliveCount < initialCount);
    }

    [Fact]
    public void Agent_SeeksFood_WhenHungry()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 21, size: 128, catalog, vegetation, config);

        int seekTicks = TicksUntilHungerThreshold(config, config.HungerSeekThreshold);
        int maxTicks = seekTicks + 100;

        bool sawSeeking = false;
        for (int i = 0; i < maxTicks && !sawSeeking; i++)
        {
            world.Tick(World.TickIntervalSeconds);
            for (int a = 0; a < world.AliveCount; a++)
            {
                if (world.GetAgent(a).State == AgentState.Seeking)
                {
                    sawSeeking = true;
                    break;
                }
            }
        }

        Assert.True(sawSeeking);
    }

    [Fact]
    public void Population_Extinguishes_OnFoodlessMap()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 4, size: 64, catalog, vegetation, config);
        MakeFoodless(world, catalog);

        int deathTicks = TicksUntilHungerThreshold(config, 255) + 60;

        for (int i = 0; i < deathTicks; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(0, world.AliveCount);
    }

    [Fact]
    public void Agent_EatsFromMatureBush_HungerDecreases()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 6, size: 64, catalog, vegetation, config);

        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("bush", out byte bushType);
        byte matureStage = (byte)vegetation.Get(bushType).MatureStage;

        Agent agent = world.GetAgent(0);
        int x = (int)MathF.Floor(agent.X);
        int y = (int)MathF.Floor(agent.Y);
        world.SetTerrainId(x, y, grass);
        world.ForceSpawnVegetation(x, y, bushType, matureStage);
        world.SetAgentHunger(0, 200);

        for (int i = 0; i < 20; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.GetAgent(0).Hunger < 200);
    }

    [Fact]
    public void Agent_DoesNotFreeze_WhenNoFoodReachable()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 90, size: 64, catalog, vegetation, config);
        MakeFoodless(world, catalog);

        int seekTicks = TicksUntilHungerThreshold(config, config.HungerSeekThreshold);
        int cooldownRealTicks = config.SeekFailureCooldownThinkTicks * 4;
        int maxTicks = seekTicks + cooldownRealTicks + 100;

        bool sawMoving = false;
        for (int i = 0; i < maxTicks && !sawMoving; i++)
        {
            world.Tick(World.TickIntervalSeconds);
            for (int a = 0; a < world.AliveCount; a++)
            {
                if (world.GetAgent(a).State == AgentState.Moving)
                {
                    sawMoving = true;
                    break;
                }
            }
        }

        Assert.True(sawMoving);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void Population_Survives_LongRun(int seed)
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed, size: 256, catalog, vegetation, config);

        for (int i = 0; i < 500_000; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.AliveCount > 0);
    }

    [Fact]
    public void Agents_DieOfHunger_InScarcityScenario()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var baseConfig = LoadSimulationConfig();
        var scarcityConfig = baseConfig with { AgentDensity = 0.003, BushDensity = 0.005, TreeDensity = 0.002 };
        var world = new World(seed: 60, size: 128, catalog, vegetation, scarcityConfig);

        for (int i = 0; i < 4000; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.GetDeathCount(DeathCause.Hunger) > 0);
    }

    [Fact]
    public void Agent_Wandering_CoversDistance_LinearlyNotSqrt()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        // Jamais affamé (seuil de recherche hors de portée) : seule
        // l'errance idle est en jeu, jamais le Seeking piloté par BFS.
        var config = LoadSimulationConfig() with { HungerSeekThreshold = 255, IdleMoveChance = 1.0 };
        var world = new World(seed: 91, size: 1024, catalog, vegetation, config);

        catalog.TryGetId("sand", out byte sand);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, sand);
            }
        }

        // Moyenne sur TOUS les agents, pas un seul : un agent isolé peut
        // par malchance démarrer près d'un bord de carte et se faire
        // bloquer plusieurs fois de suite (vérifié empiriquement -- un
        // agent proche du bord gauche restait coincé ~70 ticks de
        // pensée), ce qui n'a rien à voir avec la qualité du mécanisme
        // d'errance lui-même. La moyenne sur toute la population lisse
        // cet effet de bord et la chance individuelle.
        int n = world.AliveCount;
        var originX = new float[n];
        var originY = new float[n];
        for (int a = 0; a < n; a++)
        {
            Agent agent = world.GetAgent(a);
            originX[a] = agent.X;
            originY[a] = agent.Y;
        }

        int thinkTicks = 150;
        for (int i = 0; i < thinkTicks * 4; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        double displacementSum = 0;
        for (int a = 0; a < n; a++)
        {
            Agent agent = world.GetAgent(a);
            double dx = agent.X - originX[a];
            double dy = agent.Y - originY[a];
            displacementSum += Math.Sqrt(dx * dx + dy * dy);
        }
        double averageDisplacement = displacementSum / n;
        double randomWalkExpectation = Math.Sqrt(thinkTicks);

        // Mesuré empiriquement (5 seeds, ~800 agents chacun) : ~18 pour
        // ~12.25 attendu par une marche aléatoire pure, un facteur ~1.5
        // constant. Marge de x1.3 pour rester robuste sans être trivial.
        Assert.True(averageDisplacement > randomWalkExpectation * 1.3,
            $"déplacement net moyen {averageDisplacement:F1} pas nettement supérieur à une marche aléatoire pure (~{randomWalkExpectation:F1})");
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void Population_StarvationIsRare_InNormalConditions(int seed)
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed, size: 512, catalog, vegetation, config);

        for (int i = 0; i < 2_000_000; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        // La population décline toujours vers 0 sans reproduction --
        // normal, ce n'est PAS le critère. Seule la CAUSE de mort compte :
        // le budget de faim ne doit plus être le tueur quasi-systématique
        // qu'il était avant le fix (198/199 avant, cf. diagnostic s12).
        int hungerDeaths = world.GetDeathCount(DeathCause.Hunger);
        Assert.True(hungerDeaths < 20, $"{hungerDeaths} morts de faim sur 199 agents -- le fix de budget énergétique n'a pas tenu");
    }

    [Fact]
    public void Tick_StillAllocatesNothing()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 9, size: 128, catalog, vegetation, config);

        for (int i = 0; i < 5; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
