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

    [Fact]
    public void Vegetation_YoungBushes_GrowIntoMature()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 2, size: 32, catalog, vegetation, species, config);

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
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 15, size: 64, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }

        // Sans buisson/arbre existant nulle part, seule la germination
        // spontanée (taux volontairement bas, s13) peut amorcer la
        // végétation -- plus de ticks qu'avant pour laisser une chance
        // raisonnable au tirage sur une carte 64x64.
        for (int i = 0; i < config.VegetationTickInterval * 30; i++)
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
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 50, size: 64, catalog, vegetation, species, config);

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
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        // Capacité large + taux de diffusion haut : test déterministe qui
        // ne dépend pas du tuning réel du jeu (VegetationSpreadChance en
        // conditions normales est bien plus bas).
        var config = LoadSimulationConfig() with
        {
            BushDensity = 1.0,
            TreeDensity = 1.0,
            VegetationSpreadChance = 0.9,
        };

        var world = new World(seed: 40, size: 16, catalog, vegetation, species, config);
        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }

        vegetation.TryGetId("bush", out byte bushType);
        // Buisson source adjacent en (9,8) : la repousse locale (s13) ne
        // remplit plus qu'un slot AILLEURS sur la carte, elle diffuse
        // depuis un buisson existant. Sans source voisine, seule la
        // germination spontanée (bien plus rare) pourrait retomber pile
        // sur (8,8), ce qui rendrait ce test non déterministe.
        world.ForceSpawnVegetation(9, 8, bushType, stage: 1);
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
    public void Bushes_RecolonizeDepletedZone_Locally()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig() with { VegetationSpreadChance = 0.8, VegetationSpontaneousChance = 0.0 };
        var world = new World(seed: 71, size: 64, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }

        vegetation.TryGetId("bush", out byte bushType);

        // Zone rasée 11x11 (rien à l'intérieur), ceinturée de buissons
        // source juste à l'extérieur. La diffusion avance d'au plus un
        // anneau par tick végétation (snapshot de BushCount avant la
        // boucle, cf. World.cs) : après 3 ticks, elle ne peut
        // mécaniquement pas avoir atteint le centre (5 tuiles de la
        // bordure), mais doit avoir atteint l'anneau intérieur immédiat.
        const int zoneMin = 26, zoneMax = 36;
        for (int y = zoneMin - 1; y <= zoneMax + 1; y++)
        {
            for (int x = zoneMin - 1; x <= zoneMax + 1; x++)
            {
                bool onBorder = x == zoneMin - 1 || x == zoneMax + 1 || y == zoneMin - 1 || y == zoneMax + 1;
                bool insideZone = x >= zoneMin && x <= zoneMax && y >= zoneMin && y <= zoneMax;
                if (onBorder && !insideZone)
                {
                    world.ForceSpawnVegetation(x, y, bushType, stage: 1);
                }
            }
        }

        for (int i = 0; i < config.VegetationTickInterval * 3; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        int innerBorderCount = 0;
        for (int x = zoneMin; x <= zoneMax; x++)
        {
            if (world.TryGetVegetationAt(x, zoneMin, out _)) innerBorderCount++;
            if (world.TryGetVegetationAt(x, zoneMax, out _)) innerBorderCount++;
        }
        for (int y = zoneMin + 1; y <= zoneMax - 1; y++)
        {
            if (world.TryGetVegetationAt(zoneMin, y, out _)) innerBorderCount++;
            if (world.TryGetVegetationAt(zoneMax, y, out _)) innerBorderCount++;
        }

        int centerX = (zoneMin + zoneMax) / 2;
        int centerY = (zoneMin + zoneMax) / 2;
        bool centerStillEmpty =
            !world.TryGetVegetationAt(centerX, centerY, out _) &&
            !world.TryGetVegetationAt(centerX - 1, centerY, out _) &&
            !world.TryGetVegetationAt(centerX + 1, centerY, out _) &&
            !world.TryGetVegetationAt(centerX, centerY - 1, out _) &&
            !world.TryGetVegetationAt(centerX, centerY + 1, out _);

        Assert.True(innerBorderCount > 0, "la bordure intérieure de la zone vidée n'a pas repoussé depuis les buissons voisins");
        Assert.True(centerStillEmpty, "le centre de la zone a déjà repoussé -- la diffusion n'est plus locale");
    }

    [Fact]
    public void Bushes_CanRecolonize_FullyClearedRegion()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig() with { VegetationSpontaneousChance = 0.05 };
        var world = new World(seed: 72, size: 32, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }

        // Aucune source de graines nulle part sur la carte : seule la
        // germination spontanée (piège symétrique, s13) peut faire
        // repartir la végétation depuis zéro.
        Assert.Equal(0, world.VegetationCount);

        for (int i = 0; i < config.VegetationTickInterval * 10; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.VegetationCount > 0, "aucune germination spontanée -- une région entièrement rasée resterait stérile pour toujours");
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void Vegetation_SpatialDistribution_IsBalanced(int seed)
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

        catalog.TryGetId("grass", out byte grass);
        int half = world.Size / 2;
        int[] vegQuadrants = new int[4];
        int[] grassQuadrants = new int[4];

        for (int i = 0; i < world.VegetationCount; i++)
        {
            Vegetation veg = world.GetVegetation(i);
            int quadrant = (veg.X < half ? 0 : 1) + (veg.Y < half ? 0 : 2);
            vegQuadrants[quadrant]++;
        }

        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                if (world.GetTerrainId(x, y) == grass)
                {
                    int quadrant = (x < half ? 0 : 1) + (y < half ? 0 : 2);
                    grassQuadrants[quadrant]++;
                }
            }
        }

        Assert.True(world.VegetationCount > 20, "pas assez de végétation pour juger de la répartition");

        // Compare le RATIO végétation/herbe par quadrant, pas les totaux
        // bruts : c'est ce ratio qui doit rester stable si la repousse
        // suit la disponibilité réelle du terrain plutôt que de dériver
        // (cf. diagnostic s12 -- l'ancienne version de ce test mesurait
        // des totaux bruts à court terme et ne voyait pas la dérive).
        double[] ratios = new double[4];
        for (int q = 0; q < 4; q++)
        {
            Assert.True(grassQuadrants[q] > 0, $"quadrant {q} sans herbe -- terrain dégénéré pour ce seed");
            ratios[q] = vegQuadrants[q] / (double)grassQuadrants[q];
        }

        double averageRatio = (ratios[0] + ratios[1] + ratios[2] + ratios[3]) / 4.0;
        foreach (double ratio in ratios)
        {
            Assert.InRange(ratio, averageRatio * 0.5, averageRatio * 1.5);
        }
    }

    [Fact]
    public void Ash_RecoversToGrass_OverTime()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig() with { AshToGrassChance = 0.9 };
        var world = new World(seed: 80, size: 16, catalog, vegetation, species, config);

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

    [Fact]
    public void Tree_Dies_AndFreesSlot()
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed: 55, size: 16, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("tree", out byte treeType);
        world.SetTerrainId(6, 6, grass);
        world.ForceSpawnVegetation(6, 6, treeType, stage: 1);
        world.SetVegetationDeathTick(6, 6, 0);

        world.Tick(World.TickIntervalSeconds);

        Assert.False(world.TryGetVegetationAt(6, 6, out _));
        // Mort de vieillesse != feu : la tuile reste de l'herbe, jamais
        // de la cendre (cf. matrice d'interaction du plan).
        Assert.Equal(grass, world.GetTerrainId(6, 6));
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void Trees_StabilizeOverLongRun(int seed)
    {
        var catalog = LoadCatalog();
        var vegetation = LoadVegetationCatalog();
        var species = LoadSpeciesCatalog();
        var config = LoadSimulationConfig();
        var world = new World(seed, size: 512, catalog, vegetation, species, config);

        vegetation.TryGetId("tree", out byte treeType);

        for (int i = 0; i < 1_000_000; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }
        int treeCountMidway = world.CountVegetationOfType(treeType);

        for (int i = 0; i < 1_000_000; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }
        int treeCountFinal = world.CountVegetationOfType(treeType);

        // Un vrai plateau (à son plafond de capacité ou en dessous, peu
        // importe) : ni extinction (le cliquet inversé introduit par le
        // fix de s11, cause racine = tableau partagé -- corrigé cette
        // session par la séparation bush/tree), ni dérive continue dans
        // un sens ou l'autre entre 1M et 2M ticks.
        Assert.True(treeCountFinal > 20, $"arbres proches de l'extinction : {treeCountFinal}");
        Assert.InRange(treeCountFinal, treeCountMidway * 0.8, treeCountMidway * 1.25);
    }
}
