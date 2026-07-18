using Simulation;

namespace Tests;

public class VegetationTests
{

    [Fact]
    public void ForceSpawnVegetation_NeverThrows_OnFullCapacity()
    {
        // Session filet : signalé par une revue externe comme un risque
        // d'IndexOutOfRange sans garde de capacité. Lecture du code
        // (World.cs, ForceSpawnVegetation) montre que la garde EXISTE
        // déjà (RemoveBushAt(0)/RemoveTreeAt(0) si le tableau est plein
        // avant de planter) -- ce test est une preuve de non-régression,
        // pas un fix : densité volontairement minuscule pour saturer les
        // deux tableaux dès la construction, puis ForceSpawnVegetation
        // appelée sur bien plus de tuiles que la capacité.
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation() with { BushDensity = 0.01, TreeDensity = 0.01 };
        var world = new World(seed: 12, size: 32, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("bush", out byte bushType);
        vegetation.TryGetId("tree", out byte treeType);
        byte bushMature = (byte)vegetation.Get(bushType).MatureStage;
        byte treeMature = (byte)vegetation.Get(treeType).MatureStage;

        for (int i = 0; i < 30; i++)
        {
            int x = i % world.Size;
            int y = i / world.Size;
            world.SetTerrainId(x, y, grass);
            world.ForceSpawnVegetation(x, y, bushType, bushMature);
        }
        for (int i = 0; i < 30; i++)
        {
            int x = (i + 30) % world.Size;
            int y = (i + 30) / world.Size;
            world.SetTerrainId(x, y, grass);
            world.ForceSpawnVegetation(x, y, treeType, treeMature);
        }

        Assert.True(world.BushCount <= (int)(config.BushDensity * world.Size * world.Size) + 1);
        Assert.True(world.TreeCount <= (int)(config.TreeDensity * world.Size * world.Size) + 1);
    }

    [Fact]
    public void Vegetation_YoungBushes_GrowIntoMature()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
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
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
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
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
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

        // Depuis la session 18, la faim ne declenche plus un
        // deplacement vers un buisson (manger se fait depuis le pool du
        // clan, sans bouger) -- seul un cueilleur en Harvesting epuise
        // reellement un buisson. Force directement cet etat (seam de
        // test) plutot que de dependre du declencheur probabiliste.
        world.SetAgentTarget(0, x, y);
        world.SetAgentState(0, AgentState.Harvesting);

        for (int i = 0; i < 8; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.False(world.TryGetVegetationAt(x, y, out _));
    }

    [Fact]
    public void Vegetation_RegrowsAfterDelay_NotInstantly()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        // Capacité large + taux de diffusion haut : test déterministe qui
        // ne dépend pas du tuning réel du jeu (VegetationSpreadChance en
        // conditions normales est bien plus bas).
        var config = TestCatalogs.LoadSimulation() with
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
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation() with { VegetationSpreadChance = 0.8, VegetationSpontaneousChance = 0.0 };
        var world = new World(seed: 71, size: 64, catalog, vegetation, species, config);

        catalog.TryGetId("grass", out byte grass);
        for (int y = 0; y < world.Size; y++)
        {
            for (int x = 0; x < world.Size; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }

        // SeedMinimumBushPerPatch (session 19) garantit un buisson par
        // poche d'herbe DÈS LA CONSTRUCTION, sur le terrain d'origine --
        // SetTerrainId ci-dessus ne les efface pas (seul le type de
        // terrain change, pas la végétation dessus). Sans ce clear, des
        // buissons résiduels de la construction faussent la mesure de
        // diffusion locale que ce test isole.
        world.ClearAllVegetation();

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
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation() with { VegetationSpontaneousChance = 0.05 };
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
        // repartir la végétation depuis zéro. SeedInitialVegetation
        // (s15) plante à la construction, avant le passage en herbe
        // ci-dessus -- on rase explicitement pour retrouver la
        // prémisse "région entièrement vidée" que ce test vérifie.
        world.ClearAllVegetation();
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
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
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
        // Tolérance élargie en s15 (0,5x-1,5x -> 0,15x-3x) : le retrait
        // de la rustine bushDensity=0.3 augmente délibérément la
        // clusterisation (paysage lisible en patches, plus un tapis
        // uniforme -- cf. plan s15, point 6 "clusterisation WILL rise,
        // expected"). Ce test garde son rôle d'origine -- détecter un
        // quadrant qui dérive vers zéro (bug de repousse directionnel,
        // diagnostic s12) -- sans pénaliser l'irrégularité voulue.
        foreach (double ratio in ratios)
        {
            Assert.InRange(ratio, averageRatio * 0.15, averageRatio * 3.0);
        }
    }

    [Fact]
    public void Ash_RecoversToGrass_OverTime()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation() with { AshToGrassChance = 0.9 };
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
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
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
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed, size: 512, catalog, vegetation, species, config);

        vegetation.TryGetId("tree", out byte treeType);
        int capacity = (int)(config.TreeDensity * 512 * 512);

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

        // Ni extinction (le cliquet inversé introduit par le fix de s11,
        // cause racine = tableau partagé -- corrigé en s13 par la
        // séparation bush/tree), ni saturation permanente (la vraie
        // durée de vie, allongée en s15, doit produire une dynamique
        // observable -- une population figée au plafond signifierait
        // qu'elle tourne encore dans le vide). Pas de borne étroite
        // entre 1M et 2M ticks : la fluctuation réelle (ex. 2968->3875,
        // seed 42) est le comportement voulu depuis s15, pas un plateau.
        Assert.True(treeCountMidway > 20, $"arbres proches de l'extinction (mi-parcours) : {treeCountMidway}");
        Assert.True(treeCountFinal > 20, $"arbres proches de l'extinction (fin) : {treeCountFinal}");
        Assert.True(treeCountMidway < capacity * 0.9, $"arbres saturés au plafond (mi-parcours) : {treeCountMidway}/{capacity}");
        Assert.True(treeCountFinal < capacity * 0.9, $"arbres saturés au plafond (fin) : {treeCountFinal}/{capacity}");
    }

    [Fact]
    public void Vegetation_TimeScale_IsSlowerThanHungerCycle()
    {
        var config = TestCatalogs.LoadSimulation();

        // Décision de design de s15 : la végétation doit être au moins
        // un ordre de grandeur plus lente que le cycle de faim, sinon
        // les deux mécaniques se perçoivent comme du bruit (cf.
        // CLAUDE.md, "Séparation des échelles de temps"). Référence la
        // plus stricte des deux bandes citées : faim -> CHERCHE (pas
        // faim -> mort, plus tardive donc moins exigeante).
        long hungerSeekTicks = (long)config.HungerSeekThreshold * 4;
        Assert.True(config.VegetationRegrowthDelayTicks >= 10 * hungerSeekTicks,
            $"délai de repousse ({config.VegetationRegrowthDelayTicks} ticks) pas assez lent face au cycle de faim ({hungerSeekTicks} ticks)");
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void Fire_DestroysSignificantVegetation(int seed)
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed, size: 512, catalog, vegetation, species, config);

        vegetation.TryGetId("bush", out byte bushType);
        vegetation.TryGetId("tree", out byte treeType);

        int peakVegetation = 0;
        var rng = new Rng((ulong)seed * 7919 + 3);
        const int fireInterval = 20000;
        const int fireRadius = 6;

        for (int i = 0; i < 2_000_000; i++)
        {
            world.Tick(World.TickIntervalSeconds);

            if (i % fireInterval == 0)
            {
                int fireX = (int)(rng.NextDouble() * 512);
                int fireY = (int)(rng.NextDouble() * 512);
                world.Execute(new SpawnFire(fireX, fireY, fireRadius));
            }

            if (i % 1000 == 0)
            {
                int current = world.CountVegetationOfType(bushType) + world.CountVegetationOfType(treeType);
                peakVegetation = Math.Max(peakVegetation, current);
            }
        }

        // Rustine s13 (bushDensity=0.3) retirée en s15 : le feu doit
        // redevenir un signal significatif (avant s15 : 1,7% du pic sur
        // toute la durée). Mesuré empiriquement en s15 avec la densité
        // 0.2 retenue : 2,6% (seed 7) à 5,1% (seed 42) -- la variance
        // vient de l'emplacement des feux relatif au paysage généré par
        // chaque seed, pas d'un aléa de run à run (déterministe). Seuil
        // pris sous le minimum mesuré, nettement au-dessus du 1,7%
        // historique.
        double lossFraction = (double)world.VegetationLostToFire / peakVegetation;
        Assert.True(lossFraction > 0.02,
            $"seulement {lossFraction:P1} de la végétation (pic {peakVegetation}) détruite par le feu -- le feu redevient-il vraiment significatif ?");
    }
}
