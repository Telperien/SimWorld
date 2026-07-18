using Simulation;

namespace Tests;

public class ReproductionTests
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

    // Catalogue synthétique : mature immédiatement, gestation courte,
    // conception quasi garantie -- un test déterministe qui ne dépend
    // pas du tuning réel du jeu (maturityAge/gestationTicks réels sont
    // bien plus grands).
    private static SpeciesCatalog MakeFertileSpeciesCatalog()
    {
        return SpeciesCatalog.Load("""
        {
          "human": { "id": 0, "lifespanTicks": 2000000, "lifespanVarianceTicks": 0, "maturityAge": 0, "gestationTicks": 4 }
        }
        """);
    }

    // Place un couple adjacent, sexes opposés, avec un buisson mûr dans
    // leur cellule de grille pour que le frein progressif (nourriture
    // locale / population locale) ne bloque pas la conception.
    private static (World world, uint motherId, uint fatherId) MakeFertileCouple(
        TerrainCatalog catalog, VegetationCatalog vegetation, SpeciesCatalog species, SimulationConfig config, int seed)
    {
        var world = new World(seed, size: 128, catalog, vegetation, species, config);

        // SeedInitialVegetation (s15) plante un monde déjà établi à la
        // construction -- sans ce clear, la population ambiante trouve
        // à manger partout et redevient éligible à la reproduction bien
        // avant la fin de la fenêtre de test, ce que ces tests veulent
        // justement exclure (cf. neutralisation ci-dessous).
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

        // Isole le couple dans un clan qui n'appartient QU'À EUX (session
        // 19c) : TryFindMate exige le même clan (candidate.ClanId ==
        // female.ClanId), donc le couple doit partager un clan -- et
        // depuis que manger n'est plus gaté par un état exclusif, un
        // agent ambiant affamé du MÊME clan peut se remettre à manger dès
        // que ce clan a un pool non vide (ci-dessous), se "dé-neutralisant"
        // silencieusement. Réassigner tous les agents ambiants à un AUTRE
        // clan élimine cette contamination à la racine, plutôt que de
        // compter sur un pool trop petit pour être consommé à temps.
        uint coupleClanId = world.GetAgent(0).ClanId;
        world.SetAgentClanId(1, coupleClanId);
        uint otherClanId = world.GetClan(0).Id == coupleClanId ? world.GetClan(1).Id : world.GetClan(0).Id;
        for (int i = 2; i < world.AliveCount; i++)
        {
            world.SetAgentClanId(i, otherClanId);
        }

        // Neutralise tout le reste de la population ambiante (au-dessus
        // du seuil "bien nourrie" mais loin de la mort de faim) : sans
        // ça, d'autres couples du monde ambiant pourraient aussi se
        // reproduire et fausser les assertions strictes de ces tests.
        for (int i = 2; i < world.AliveCount; i++)
        {
            world.SetAgentHunger(i, 200);
        }

        // Le pool du clan (session 18) est seedé à la construction,
        // INDÉPENDAMMENT de la végétation -- sans ce zérotage, les
        // agents ambiants ci-dessus mangent depuis la réserve bancaire
        // de LEUR clan (désormais distinct de celui du couple) et
        // redeviennent éligibles à la reproduction malgré le hunger=200
        // ci-dessus.
        for (int c = 0; c < world.ClanCount; c++)
        {
            world.SetClanFoodPool(c, 0);
        }

        // Le frein de reproduction (clanPoolRatio, session 18) bloque la
        // conception si le pool du clan est vide -- sans redonner une
        // réserve au clan DU COUPLE après le zérotage global ci-dessus,
        // leur propre conception serait TOUJOURS bloquée elle aussi. Sans
        // risque de contamination désormais : le clan du couple ne
        // contient plus aucun agent ambiant.
        for (int c = 0; c < world.ClanCount; c++)
        {
            if (world.GetClan(c).Id == coupleClanId)
            {
                world.SetClanFoodPool(c, 1_000_000);
                break;
            }
        }

        return (world, world.GetAgent(0).Id, world.GetAgent(1).Id);
    }

    [Fact]
    public void Newborn_HasCorrectStableParentIds()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = MakeFertileSpeciesCatalog();
        var config = LoadFertileConfig();

        var (world, motherId, fatherId) = MakeFertileCouple(catalog, vegetation, species, config, seed: 200);
        int initialCount = world.AliveCount;

        for (int i = 0; i < 100; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.AliveCount > initialCount, "aucune naissance -- la conception n'a pas eu lieu");

        // Retrouvé par filiation (MotherId), pas par position dans le
        // tableau -- robuste même si d'autres agents du monde ambiant
        // se sont aussi reproduits entre-temps.
        uint? newbornId = null;
        for (int i = 0; i < world.AliveCount; i++)
        {
            Agent candidate = world.GetAgent(i);
            if (candidate.MotherId == motherId)
            {
                newbornId = candidate.Id;
                Assert.Equal(fatherId, candidate.FatherId);
                break;
            }
        }
        Assert.NotNull(newbornId);

        // Survit à une compaction : tue un autre agent pour forcer un
        // swap-with-last, puis revérifie par Id, pas par index. Mort par
        // ÂGE (pas par faim, session 19b : AllowStarvationDeath=false par
        // défaut ici, Hunger=255 ne tuerait plus jamais l'agent).
        world.SetAgentLifespan(2, 4);
        world.SetAgentAge(2, 3);
        for (int i = 0; i < 8; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        bool found = false;
        for (int i = 0; i < world.AliveCount; i++)
        {
            if (world.GetAgent(i).Id == newbornId)
            {
                Assert.Equal(motherId, world.GetAgent(i).MotherId);
                Assert.Equal(fatherId, world.GetAgent(i).FatherId);
                found = true;
                break;
            }
        }
        Assert.True(found, "le nouveau-né a disparu après une compaction");
    }

    [Fact]
    public void Immature_CannotReproduce()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        // maturityAge == lifespanTicks : aucun agent vivant ne peut
        // jamais être mature (Age < LifespanTicks est garanti tant que
        // l'agent est vivant, cf. le check de mort de vieillesse).
        var species = SpeciesCatalog.Load("""
        {
          "human": { "id": 0, "lifespanTicks": 2000000, "lifespanVarianceTicks": 0, "maturityAge": 2000000, "gestationTicks": 4 }
        }
        """);
        var config = LoadFertileConfig();

        var (world, _, _) = MakeFertileCouple(catalog, vegetation, species, config, seed: 201);
        int initialCount = world.AliveCount;

        for (int i = 0; i < 200; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(initialCount, world.AliveCount);
    }

    [Fact]
    public void Starving_CannotReproduce()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = MakeFertileSpeciesCatalog();
        var config = LoadFertileConfig();

        var (world, _, _) = MakeFertileCouple(catalog, vegetation, species, config, seed: 202);
        // Au-dessus du seuil "bien nourrie" -- bloque la conception même
        // si un partenaire valide est à portée (cf. TryReproduce).
        world.SetAgentHunger(0, 200);
        int initialCount = world.AliveCount;

        // Depuis la session 19c, manger est un effet PASSIF appliqué à
        // chaque tick réel, indépendamment de l'état -- le clan du couple
        // a un pool généreux (nécessaire pour que MakeFertileCouple ne
        // bloque pas la reproduction des AUTRES tests via clanPoolRatio),
        // donc la mère y puiserait automatiquement et redeviendrait "bien
        // nourrie" en quelques ticks si on ne la re-affame pas ici. Ce
        // test isole spécifiquement le frein Hunger >= HungerSeekThreshold,
        // pas la disponibilité du pool -- réappliqué à chaque tick.
        for (int i = 0; i < 200; i++)
        {
            world.SetAgentHunger(0, 200);
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(initialCount, world.AliveCount);
    }

    private static SimulationConfig LoadFertileConfig()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "simulation.json");
        var baseConfig = SimulationConfig.Load(File.ReadAllText(path));
        // BaseHarvestChance=0 (session 19c) : depuis que manger n'est plus
        // un état exclusif, un agent ambiant affamé (Hunger=200, "neutralisé"
        // ci-dessous) redevient éligible à TryStartHarvesting -- sans ce
        // verrou, il peut récolter, regarnir le pool de SON clan, puis
        // remanger et repasser sous le seuil, se "dé-neutralisant" pendant
        // la fenêtre du test. Le désactiver isole ces tests sur la seule
        // mécanique de reproduction, indépendamment de la récolte.
        return baseConfig with { MateSearchRadius = 10, BaseConceptionChance = 1.0, TargetFoodPerCapita = 0.1, BaseHarvestChance = 0.0 };
    }
}
