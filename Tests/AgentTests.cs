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

    private static SpeciesCatalog LoadSpeciesCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "species.json");
        return SpeciesCatalog.Load(File.ReadAllText(path));
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
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 5, size: 128, catalog, vegetation, species, config);

        // AgentCapacity est la taille du tableau (avec marge pour les
        // naissances, cf. AgentCapacityMultiplier, s14) -- seuls les
        // AliveCount premiers slots sont des agents réellement spawnés.
        for (int i = 0; i < world.AliveCount; i++)
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
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 1, size: 512, catalog, vegetation, species, config);

        // AgentCapacity est désormais la taille du TABLEAU (avec marge
        // pour les naissances, cf. AgentCapacityMultiplier, s14), pas la
        // population initiale -- la densité initiale se lit sur AliveCount.
        Assert.InRange(world.AliveCount, 150, 250);
        Assert.True(world.AgentCapacity > world.AliveCount, "aucune marge de croissance dans le tableau Agent[]");
    }

    [Fact]
    public void Agents_Movement_IsDeterministic_ForSameSeed()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();

        var a = new World(seed: 21, size: 128, catalog, vegetation, species, config);
        var b = new World(seed: 21, size: 128, catalog, vegetation, species, config);

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
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 3, size: 64, catalog, vegetation, species, config);
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
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 21, size: 128, catalog, vegetation, species, config);

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
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 4, size: 64, catalog, vegetation, species, config);
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
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 6, size: 64, catalog, vegetation, species, config);

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
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 90, size: 64, catalog, vegetation, species, config);
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
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed, size: 256, catalog, vegetation, species, config);

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
        var species = LoadSpeciesCatalog();
        var baseConfig = LoadSimulationConfig();
        var scarcityConfig = baseConfig with { AgentDensity = 0.003, BushDensity = 0.005, TreeDensity = 0.002 };
        var world = new World(seed: 60, size: 128, catalog, vegetation, species, scarcityConfig);

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
        var species = LoadSpeciesCatalog();
        // Jamais affamé (seuil de recherche hors de portée) : seule
        // l'errance idle est en jeu, jamais le Seeking piloté par BFS.
        var config = LoadSimulationConfig() with { HungerSeekThreshold = 255, IdleMoveChance = 1.0 };
        var world = new World(seed: 91, size: 1024, catalog, vegetation, species, config);

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

    [Fact]
    public void Agent_DiesOfOldAge_AtLifespan()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 95, size: 64, catalog, vegetation, species, config);

        int initialCount = world.AliveCount;
        world.SetAgentLifespan(0, 100);
        world.SetAgentAge(0, 99);

        for (int i = 0; i < 8; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.AliveCount < initialCount);
        Assert.True(world.GetDeathCount(DeathCause.Age) > 0);
    }

    [Fact]
    public void Agents_InitialAges_AreRandomized()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 96, size: 512, catalog, vegetation, species, config);

        uint firstAge = world.GetAgent(0).Age;
        bool allSameAge = true;
        for (int i = 1; i < world.AliveCount; i++)
        {
            if (world.GetAgent(i).Age != firstAge)
            {
                allSameAge = false;
                break;
            }
        }

        Assert.False(allSameAge, "tous les agents initiaux ont le même âge -- vague de cohorte garantie");
    }

    [Fact]
    public void Agent_EscapesLargeDesert()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        // HungerIncreasePerThink=0 isole la mécanique de navigation de
        // l'économie de survie -- l'objectif est de prouver que le
        // gradient guide l'agent jusqu'à une ressource hors de portée
        // BFS, pas de calibrer un timing de survie serré sur 50 tuiles.
        var config = LoadSimulationConfig() with { HungerIncreasePerThink = 0 };
        var world = new World(seed: 300, size: 256, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }

        // Buisson mûr à 50 tuiles -- au-delà du BFS (±16, MaxFoodSearchRadius)
        // ET du désert de rayon ~40 diagnostiqué en s14b.
        vegetation.TryGetId("bush", out byte bushType);
        int matureStage = vegetation.Get(bushType).MatureStage;
        const int centerX = 128, centerY = 128;
        const int bushX = centerX + 50, bushY = centerY;
        world.ForceSpawnVegetation(bushX, bushY, bushType, (byte)matureStage);
        world.SetVegetationFoodRemaining(bushX, bushY, 100_000);

        world.SetAgentPosition(0, centerX + 0.5f, centerY + 0.5f);
        world.SetAgentHunger(0, config.HungerSeekThreshold);

        for (int i = 0; i < 200_000 && world.GetAgent(0).State != AgentState.Dead; i++)
        {
            world.Tick(World.TickIntervalSeconds);
            if (world.MealsEaten > 0)
            {
                break;
            }
        }

        Assert.True(world.MealsEaten > 0, "l'agent n'a jamais atteint le buisson malgré le gradient -- la cécité au-delà du BFS n'est pas réglée");
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void StarvationDeaths_AreNotBlindDeaths(int seed)
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed, size: 512, catalog, vegetation, species, config);

        for (int i = 0; i < 2_000_000; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        int totalHungerDeaths = world.GetDeathCount(DeathCause.Hunger);
        if (totalHungerDeaths == 0)
        {
            return;
        }

        // Mesure le COMPORTEMENT, pas la distance brute (session 14d) :
        // la distance >=33 ne discrimine plus "aveugle, immobile" de "a
        // perçu une source via le gradient, en route, budget de faim
        // insuffisant" (écologie normale). Le vrai signe d'aveuglement
        // est SeekOutcome.BlindWander -- aucun signal connu (ni BFS, ni
        // gradient) au dernier cycle de décision avant la mort.
        int[] seekOutcomeHistogram = world.GetDeathSeekOutcomeHistogram();
        int blindDeaths = seekOutcomeHistogram[SeekOutcome.BlindWander];

        double blindFraction = (double)blindDeaths / totalHungerDeaths;
        Assert.True(blindFraction < 0.20,
            $"{blindFraction:P0} des morts de faim n'avaient aucun signal (ni BFS ni gradient) à leur dernier cycle de décision -- vraie cécité");
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void Population_OscillatesWithinBounds_NeverExtinct_NeverArrayLimited(int seed)
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed, size: 512, catalog, vegetation, species, config);

        const int totalTicks = 2_000_000;
        const int thirdTicks = totalTicks / 3;
        int firstThirdMaxPop = 0;
        int lastThirdMaxPop = 0;

        for (int i = 0; i < totalTicks; i++)
        {
            world.Tick(World.TickIntervalSeconds);

            if (i < thirdTicks)
            {
                firstThirdMaxPop = Math.Max(firstThirdMaxPop, world.AliveCount);
            }
            else if (i >= totalTicks - thirdTicks)
            {
                lastThirdMaxPop = Math.Max(lastThirdMaxPop, world.AliveCount);
            }
        }

        // Plancher nettement au-dessus du risque d'extinction par effet
        // Allee (creux observé avant fix : 20 sur seed 42) -- suit le
        // minimum sur TOUS les ticks (World.MinAliveCountEverObserved),
        // pas seulement des points d'échantillonnage qui peuvent rater
        // un creux court entre deux mesures.
        Assert.True(world.MinAliveCountEverObserved > 50,
            $"creux minimum {world.MinAliveCountEverObserved} sur toute la durée -- risque d'extinction par effet Allee");

        Assert.True(world.BirthsTotal > 0, "aucune naissance sur 2M ticks -- rien à mesurer");
        Assert.True(world.BirthsRefusedArrayFull < world.BirthsTotal * 0.05,
            $"{world.BirthsRefusedArrayFull} naissances refusées sur {world.BirthsTotal} -- la population est limitée par la taille du tableau, pas par l'écosystème");

        // Amplitude du dernier tiers du run ne dépasse pas notablement
        // celle du premier tiers -- pas de spirale/divergence, une
        // oscillation soutenue est le comportement voulu (Lotka-Volterra).
        Assert.True(lastThirdMaxPop < firstThirdMaxPop * 3,
            $"pic du dernier tiers ({lastThirdMaxPop}) largement supérieur à celui du premier tiers ({firstThirdMaxPop}) -- amplitude croissante ?");
    }

    [Fact]
    public void Tick_StillAllocatesNothing()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 9, size: 128, catalog, vegetation, species, config);

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
