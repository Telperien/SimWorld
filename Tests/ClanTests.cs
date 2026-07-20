using Simulation;

namespace Tests;

public class ClanTests
{

    // Catalogue synthetique : mature immediatement, gestation courte,
    // conception quasi garantie -- meme patron que ReproductionTests
    // (deterministe, independant du tuning reel du jeu).
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
        // BaseHarvestChance=0 (session 19c) : cf. ReproductionTests --
        // depuis que manger n'est plus gaté par un état exclusif, un agent
        // ambiant affamé peut redevenir cueilleur et regarnir le pool de
        // son clan, se "dé-neutralisant" pendant la fenêtre du test.
        return baseConfig with { MateSearchRadius = 10, BaseConceptionChance = 1.0, TargetFoodPerCapita = 0.1, BaseHarvestChance = 0.0 };
    }

    // Place un couple adjacent, sexes opposes, avec un buisson mur dans
    // leur cellule de grille pour que le frein progressif ne bloque pas
    // la conception -- meme patron que ReproductionTests.MakeFertileCouple.
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

        // Isole le couple dans un clan qui n'appartient qu'a eux (session
        // 19c, meme raisonnement que ReproductionTests.MakeFertileCouple)
        // avant de neutraliser/zerer -- sinon les agents ambiants du meme
        // clan mangeraient depuis le pool regarni ci-dessous et se
        // "de-neutraliseraient" pendant la fenetre du test.
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

        // Meme raisonnement que ReproductionTests.MakeFertileCouple : le
        // pool du clan demarre deja garni independamment de la
        // vegetation, sans quoi les agents ambiants hunger=200
        // ci-dessus redeviennent eligibles en quelques ticks.
        for (int c = 0; c < world.ClanCount; c++)
        {
            world.SetClanFoodPool(c, 0);
        }

        // Frein clanPoolRatio (session 18) : sans redonner une reserve au
        // clan DU COUPLE, leur propre conception resterait bloquee elle
        // aussi. Sans risque de contamination : leur clan est desormais
        // isole des agents ambiants.
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
    public void Agent_EatsFromClanPool_WithoutMoving()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed: 10, size: 128, catalog, vegetation, species, config);

        Agent before = world.GetAgent(0);
        float x = before.X;
        float y = before.Y;
        int clanIndex = FindClanIndex(world, before.ClanId);
        int poolBefore = 1000;
        world.SetClanFoodPool(clanIndex, poolBefore);
        world.SetAgentHunger(0, 200);

        world.Tick(World.TickIntervalSeconds);

        Agent after = world.GetAgent(0);
        Assert.Equal(x, after.X);
        Assert.Equal(y, after.Y);
        Assert.True(after.Hunger < 200, "l'agent n'a pas mange -- Hunger n'a pas baisse");
        Assert.True(world.GetClan(clanIndex).FoodPool < poolBefore, "le pool du clan n'a pas baisse -- le repas n'a pas ete puise dedans");
    }

    [Fact]
    public void Harvester_FillsPool_DoesNotFillOwnHunger()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed: 11, size: 64, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("bush", out byte bushType);
        int matureStage = vegetation.Get(bushType).MatureStage;

        Agent agent = world.GetAgent(0);
        int x = (int)MathF.Floor(agent.X);
        int y = (int)MathF.Floor(agent.Y);
        world.SetTerrainId(x, y, grass);
        world.ForceSpawnVegetation(x, y, bushType, (byte)matureStage);
        world.SetVegetationFoodRemaining(x, y, 1000);

        int clanIndex = FindClanIndex(world, agent.ClanId);
        world.SetClanFoodPool(clanIndex, 0);
        byte hungerBefore = 0;
        world.SetAgentHunger(0, hungerBefore);
        world.SetAgentTarget(0, x, y);
        world.SetAgentState(0, AgentState.Harvesting);

        world.Tick(World.TickIntervalSeconds);

        Agent after = world.GetAgent(0);
        // Seul l'increment normal de ThinkAgent doit avoir touche
        // Hunger -- la recolte ne doit JAMAIS nourrir directement le
        // cueilleur (c'est tout le point du split recolte/manger).
        Assert.Equal((byte)(hungerBefore + config.HungerIncreasePerThink), after.Hunger);
        Assert.True(world.GetClan(clanIndex).FoodPool > 0, "le pool du clan n'a pas recu la recolte");
    }

    [Fact]
    public void Newborn_InheritsMotherClan()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = MakeFertileSpeciesCatalog();
        var config = LoadFertileConfig();
        var world = MakeFertileCouple(catalog, vegetation, species, config, seed: 300);

        // Force le meme clan sur les deux, independamment du spawn
        // groupe naturel (session 18) -- ce test isole l'heritage, pas
        // la restriction inter-clan (testee separement).
        uint motherClan = world.GetAgent(0).ClanId;
        world.SetAgentClanId(1, motherClan);
        world.SetAgentClanId(0, motherClan);
        uint motherId = world.GetAgent(0).Id;

        for (int i = 0; i < 100; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        uint? newbornClan = null;
        for (int i = 0; i < world.AliveCount; i++)
        {
            Agent candidate = world.GetAgent(i);
            if (candidate.MotherId == motherId)
            {
                newbornClan = candidate.ClanId;
                break;
            }
        }

        Assert.NotNull(newbornClan);
        Assert.Equal(motherClan, newbornClan);
    }

    [Fact]
    public void Agents_CannotReproduce_AcrossClans()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = MakeFertileSpeciesCatalog();
        var config = LoadFertileConfig();
        var world = MakeFertileCouple(catalog, vegetation, species, config, seed: 301);

        // Clans differents, sans quoi le couple se reproduirait
        // trivialement (deja verifie par Newborn_InheritsMotherClan) --
        // ce test isole spécifiquement le filtre inter-clan.
        world.SetAgentClanId(0, 0);
        world.SetAgentClanId(1, 1);
        int initialCount = world.AliveCount;

        for (int i = 0; i < 200; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(initialCount, world.AliveCount);
    }

    [Fact]
    public void Clans_SpawnInSpatialClusters()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed: 55, size: 512, catalog, vegetation, species, config);

        // Marge genereuse (1.5x) au-dessus du rayon de grappe nominal :
        // le rejection sampling est uniforme DANS le disque, donc le
        // point le plus eloigne du centroide ne devrait quasiment
        // jamais depasser le rayon configure.
        double maxAllowedRadius = 512 * config.ClanSpawnRadiusFraction * 1.5;

        for (int c = 0; c < world.ClanCount; c++)
        {
            uint clanId = world.GetClan(c).Id;
            double sumX = 0, sumY = 0;
            int count = 0;
            for (int i = 0; i < world.AliveCount; i++)
            {
                Agent agent = world.GetAgent(i);
                if (agent.ClanId == clanId)
                {
                    sumX += agent.X;
                    sumY += agent.Y;
                    count++;
                }
            }

            if (count == 0)
            {
                continue;
            }

            double centroidX = sumX / count;
            double centroidY = sumY / count;
            double maxDist = 0;
            for (int i = 0; i < world.AliveCount; i++)
            {
                Agent agent = world.GetAgent(i);
                if (agent.ClanId != clanId)
                {
                    continue;
                }

                double dx = agent.X - centroidX;
                double dy = agent.Y - centroidY;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > maxDist)
                {
                    maxDist = dist;
                }
            }

            Assert.True(maxDist <= maxAllowedRadius,
                $"clan {c} : distance max au centroide {maxDist:F1} depasse le rayon de grappe attendu {maxAllowedRadius:F1} -- pas vraiment groupe");
        }
    }

    // Clan_PoolNeverCollapsesToZero_InNormalConditions (2M ticks) déplacé
    // dans Tests/SlowTests.cs (session refactor), cf. AgentTests.cs pour
    // le raisonnement (parallélisme xUnit entre classes).

    private static int FindClanIndex(World world, uint clanId)
    {
        for (int i = 0; i < world.ClanCount; i++)
        {
            if (world.GetClan(i).Id == clanId)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"clan id {clanId} introuvable");
    }
}
