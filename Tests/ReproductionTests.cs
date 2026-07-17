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

        // Neutralise tout le reste de la population ambiante (au-dessus
        // du seuil "bien nourrie" mais loin de la mort de faim) : sans
        // ça, d'autres couples du monde ambiant pourraient aussi se
        // reproduire et fausser les assertions strictes de ces tests.
        for (int i = 2; i < world.AliveCount; i++)
        {
            world.SetAgentHunger(i, 200);
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
        // swap-with-last, puis revérifie par Id, pas par index.
        world.SetAgentHunger(2, 255);
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

        for (int i = 0; i < 200; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(initialCount, world.AliveCount);
    }

    private static SimulationConfig LoadFertileConfig()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "simulation.json");
        var baseConfig = SimulationConfig.Load(File.ReadAllText(path));
        return baseConfig with { MateSearchRadius = 10, BaseConceptionChance = 1.0, TargetFoodPerCapita = 0.1 };
    }
}
