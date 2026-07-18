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

        // SeedInitialVegetation (s15) plante à la construction, avant
        // que ce helper ne recouvre la carte de sable -- sans ce clear,
        // les buissons déjà posés survivent au changement de terrain.
        world.ClearAllVegetation();

        // Le pool du clan (session 18) est seedé à la construction,
        // INDÉPENDAMMENT de la végétation -- sans ce zérotage, un agent
        // continue de manger depuis la réserve bancaire du clan même
        // sur une carte sans aucun buisson, rendant "sans nourriture"
        // faux.
        for (int c = 0; c < world.ClanCount; c++)
        {
            world.SetClanFoodPool(c, 0);
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
        // AllowStarvationDeath=true explicite (session 19b/19c) : ce test
        // vérifie la mécanique de mort de faim elle-même, désormais
        // désactivée par défaut.
        var config = LoadSimulationConfig() with { AllowStarvationDeath = true };
        var world = new World(seed: 3, size: 64, catalog, vegetation, species, config);
        MakeFoodless(world, catalog);

        // Hunger de depart etale sur [0, HungerSeekThreshold) depuis la
        // session 18 (evite une rafale de repas synchronisee au demarrage,
        // cf. JOURNAL) -- remis a 0 pour tous ici afin que le calcul de
        // deathTicks (base sur un depart a 0) reste exact pour CE test
        // controle.
        for (int i = 0; i < world.AliveCount; i++)
        {
            world.SetAgentHunger(i, 0);
        }

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
        // Ce test exerce explicitement le mécanisme quand il est RALLUMÉ
        // (session 19b/19c : AllowStarvationDeath=false par défaut) --
        // sans cet override, la population survivrait indéfiniment,
        // affamée mais vivante, contredisant le nom du test.
        var config = LoadSimulationConfig() with { AllowStarvationDeath = true };
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
        // AllowStarvationDeath=true explicite (session 19b/19c) : ce test
        // vérifie la mécanique de mort de faim elle-même, désormais
        // désactivée par défaut.
        var scarcityConfig = baseConfig with { AgentDensity = 0.003, BushDensity = 0.005, TreeDensity = 0.002, AllowStarvationDeath = true };
        var world = new World(seed: 60, size: 128, catalog, vegetation, species, scarcityConfig);

        // Le pool du clan (session 18) demarre avec une reserve bancaire
        // (TargetFoodPoolPerCapita * population), independante de
        // BushDensity -- sans la retirer, cette reserve absorbe la
        // penurie sur la courte fenetre du test (4000 ticks), masquant
        // la rarete de vegetation que ce test veut justement exercer.
        for (int c = 0; c < world.ClanCount; c++)
        {
            world.SetClanFoodPool(c, 0);
        }

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

        // SeedInitialVegetation (s15) plante à la construction, avant
        // le passage en herbe ci-dessus -- sans ce clear, des buissons
        // déjà mûrs ailleurs sur la carte pourraient nourrir l'agent
        // sans jamais solliciter le gradient, ce que ce test vérifie
        // justement.
        world.ClearAllVegetation();

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

        // Depuis la session 18 (split récolte/manger), la plupart des
        // agents ne cherchent JAMAIS de nourriture -- ils mangent
        // directement depuis le pool du clan, sans déplacement. Ce test
        // ne mesure donc plus la cécité que pour les CUEILLEURS morts
        // en transit/en récolte (LastSeekOutcome n'a de sens que pour
        // eux, cf. StateAtDeath), pas pour l'ensemble des morts de faim
        // (qui incluent désormais des morts "pool à sec" en Eating,
        // sans rapport avec une recherche ratée).
        int harvesterHungerDeaths = world.HungerDeathsWhileHarvesting;
        if (harvesterHungerDeaths == 0)
        {
            return;
        }

        int[] seekOutcomeHistogram = world.GetDeathSeekOutcomeHistogram();
        int blindDeaths = seekOutcomeHistogram[SeekOutcome.BlindWander];

        double blindFraction = (double)blindDeaths / harvesterHungerDeaths;
        Assert.True(blindFraction < 0.15,
            $"{blindFraction:P0} des cueilleurs morts de faim en transit/récolte n'avaient aucun signal (ni BFS ni gradient) à leur dernier cycle de décision -- vraie cécité");
    }

    // Remplace l'ancien Population_OscillatesWithinBounds_NeverExtinct_
    // NeverArrayLimited (s19) -- scindé en deux tests à responsabilité
    // unique (session 19b/19c), sur un système dont le deadlock
    // Eating/Harvest est désormais éliminé (session 19c).
    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void Population_NotArrayLimited(int seed)
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

        // Strict (pas juste <5% comme l'ancien test, cf. plan s19b point
        // 3) : AgentCapacityMultiplier doit être assez haut pour que le
        // TABLEAU ne soit jamais le facteur limitant pendant le calibrage
        // -- une seule naissance refusée invalide la mesure.
        Assert.True(world.BirthsTotal > 0, "aucune naissance sur 2M ticks -- rien à mesurer");
        Assert.Equal(0, world.BirthsRefusedArrayFull);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void Population_OscillationDoesNotDiverge(int seed)
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

        Assert.True(world.MinAliveCountEverObserved > 50,
            $"creux minimum {world.MinAliveCountEverObserved} sur toute la durée -- risque d'extinction par effet Allee");

        // Amplitude du dernier tiers du run ne dépasse pas notablement
        // celle du premier tiers -- pas de spirale/divergence, une
        // oscillation soutenue est le comportement voulu (Lotka-Volterra).
        // Diagnostiqué comme réel (pas un artefact du deadlock Eating/
        // Harvest) si ce test échoue encore après la session 19c -- cf.
        // plan, point 5 (gain du frein clanPoolRatio).
        Assert.True(lastThirdMaxPop < firstThirdMaxPop * 3,
            $"pic du dernier tiers ({lastThirdMaxPop}) largement supérieur à celui du premier tiers ({firstThirdMaxPop}) -- amplitude divergente ?");
    }

    [Fact]
    public void No_Eating_State_Exists()
    {
        // Session 19c : manger n'est plus un état FSM exclusif (fix du
        // deadlock Eating/Harvest) -- preuve directe que l'état a bien
        // disparu de l'enum, pas juste cessé d'être utilisé quelque part.
        Assert.DoesNotContain("Eating", Enum.GetNames(typeof(AgentState)));
    }

    [Fact]
    public void Agent_EatsPassively_WhileHarvesting()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 70, size: 64, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("bush", out byte bushType);
        byte matureStage = (byte)vegetation.Get(bushType).MatureStage;

        Agent agent = world.GetAgent(0);
        int x = (int)MathF.Floor(agent.X);
        int y = (int)MathF.Floor(agent.Y);
        world.SetTerrainId(x, y, grass);
        world.ForceSpawnVegetation(x, y, bushType, matureStage);
        world.SetVegetationFoodRemaining(x, y, 100_000);

        // Pool modeste, PAS énorme : HarvestTick repasse en Idle dès que
        // le pool du clan atteint sa cible (TargetFoodPoolPerCapita ×
        // population du clan) -- un pool trop généreux ferait sortir
        // l'agent de Harvesting immédiatement, invalidant le test. 500
        // reste largement sous la cible réaliste tout en couvrant les
        // quelques bouchées consommées sur la fenêtre du test.
        for (int c = 0; c < world.ClanCount; c++)
        {
            world.SetClanFoodPool(c, 500);
        }

        world.SetAgentTarget(0, x, y);
        world.SetAgentState(0, AgentState.Harvesting);
        world.SetAgentHunger(0, 200);
        byte hungerBefore = world.GetAgent(0).Hunger;

        for (int i = 0; i < 8; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        // La preuve directe du point 2 du brief s19c : un agent affamé
        // en Harvesting mange depuis le pool SANS jamais quitter son état
        // de récolte (contrairement à l'ancien Eating, qui aurait
        // remplacé Harvesting).
        Assert.True(world.GetAgent(0).Hunger < hungerBefore);
        Assert.Equal(AgentState.Harvesting, world.GetAgent(0).State);
    }

    [Fact]
    public void No_Starvation_Deadlock()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 71, size: 64, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }
        world.ClearAllVegetation();

        vegetation.TryGetId("bush", out byte bushType);
        byte matureStage = (byte)vegetation.Get(bushType).MatureStage;
        int bushX = world.Size / 2, bushY = world.Size / 2;
        world.ForceSpawnVegetation(bushX, bushY, bushType, matureStage);
        world.SetVegetationFoodRemaining(bushX, bushY, 100_000);

        // Le scénario exact du deadlock (session 19b) : toute la
        // population affamée EN MÊME TEMPS, pool à sec -- tous les agents
        // placés à côté du buisson pour éliminer la variable "distance de
        // trajet", le test porte sur le déblocage d'état, pas la
        // navigation (déjà couverte par Agent_EscapesLargeDesert).
        for (int c = 0; c < world.ClanCount; c++)
        {
            world.SetClanFoodPool(c, 0);
        }
        for (int i = 0; i < world.AliveCount; i++)
        {
            world.SetAgentPosition(i, bushX + 0.5f, bushY + 0.5f);
            world.SetAgentHunger(i, config.HungerSeekThreshold);
        }

        bool sawHarvesting = false;
        int maxPoolObserved = 0;
        for (int i = 0; i < 20_000 && !(sawHarvesting && maxPoolObserved > 0); i++)
        {
            world.Tick(World.TickIntervalSeconds);
            for (int a = 0; a < world.AliveCount; a++)
            {
                if (world.GetAgent(a).State == AgentState.Harvesting)
                {
                    sawHarvesting = true;
                    break;
                }
            }
            for (int c = 0; c < world.ClanCount; c++)
            {
                maxPoolObserved = Math.Max(maxPoolObserved, world.GetClan(c).FoodPool);
            }
        }

        // Avant le fix s19c, ce scénario était un verrou permanent :
        // personne ne redevenait jamais éligible à la cueillette. Preuve
        // directe que c'est désormais impossible.
        Assert.True(sawHarvesting, "aucun agent n'est jamais devenu cueilleur -- deadlock Eating/Harvest ?");
        Assert.True(maxPoolObserved > 0, "le pool du clan n'est jamais remonté -- deadlock persistant");
    }

    [Fact]
    public void Tick_StillAllocatesNothing()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 9, size: 128, catalog, vegetation, species, config);

        // Chauffe allongee (5 -> 500 ticks, session 18) : le declencheur
        // de recolte teste desormais TOUS les agents Idle non affames a
        // chaque tick de pensee (avant : seulement les agents affames,
        // bien plus rare sur une fenetre courte). _agentPaths[index]
        // (List<int>) doit avoir l'occasion de croitre au moins une
        // fois AVANT la mesure -- sans quoi sa premiere croissance (une
        // vraie allocation tas, ponctuelle) tombe dans la fenetre
        // mesuree au lieu d'avant, un faux negatif de ce test, pas une
        // vraie fuite par tick.
        for (int i = 0; i < 500; i++)
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
