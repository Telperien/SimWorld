using Simulation;

namespace Tests;

public class TerritoryTests
{
    [Fact]
    public void Territory_ExpandsFromHome()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var baseConfig = TestCatalogs.LoadSimulation();
        // Un seul clan : concentre toute la population initiale sur un
        // seul foyer pour une source d'influence robuste dans un test
        // rapide. TerritoryTickInterval=1 : pas besoin d'attendre des
        // centaines de ticks reels pour voir un tick territoire.
        var config = baseConfig with { InitialClanCount = 1, TerritoryTickInterval = 1 };
        var world = new World(seed: 950, size: 128, catalog, vegetation, species, config);

        Home home = world.GetHome(0);
        uint clanId = world.GetClan(0).Id;

        for (int i = 0; i < 20; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(clanId, world.GetRegionOwnerAt(home.X, home.Y));

        // Une region ADJACENTE (pas seulement celle du foyer) doit
        // aussi avoir ete revendiquee -- preuve d'une vraie expansion
        // par diffusion, pas juste un point source.
        int adjacentX = Math.Min(world.Size - 1, home.X + world.RegionCellSize);
        Assert.Equal(clanId, world.GetRegionOwnerAt(adjacentX, home.Y));
    }

    // Ordre de génération (session territoire) : vérifié AVANT tout
    // Tick() -- le noyau territorial initial (TerritorySystem.
    // SeedInitialTerritory) doit exister dès la construction, pas
    // seulement après le premier tick territoire.
    [Fact]
    public void Territory_InitialCoreExists()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed: 960, size: 128, catalog, vegetation, species, config);

        for (int c = 0; c < world.ClanCount; c++)
        {
            Home home = world.GetHome(c);
            uint clanId = world.GetClan(c).Id;
            Assert.Equal(clanId, world.GetRegionOwnerAt(home.X, home.Y));
        }
    }

    // Ordre de génération (session territoire) : aucun agent ne doit
    // naître hors du territoire de son propre clan -- le noyau initial
    // est semé AVANT le spawn des agents (cf. World constructeur).
    // Vérifié à t=0, sans Tick(), sur plusieurs seeds pour ne pas
    // dépendre d'un tirage RNG chanceux.
    [Theory]
    [InlineData(960)]
    [InlineData(961)]
    [InlineData(962)]
    public void Agents_SpawnInsideTheirTerritory(int seed)
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed: seed, size: 128, catalog, vegetation, species, config);

        Assert.True(world.AliveCount > 0);

        for (int i = 0; i < world.AliveCount; i++)
        {
            Agent agent = world.GetAgent(i);
            int x = (int)agent.X;
            int y = (int)agent.Y;
            Assert.Equal(agent.ClanId, world.GetRegionOwnerAt(x, y));
        }
    }

    [Fact]
    public void Territory_TwoClansFormBorder()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var baseConfig = TestCatalogs.LoadSimulation();
        // Densite boostee (population robuste, pas les ~4/clan par
        // defaut) + 2 clans seulement, foyers forces PROCHES l'un de
        // l'autre (seam SetHomePosition) -- rend le test deterministe
        // sans dependre de la distance de spawn naturelle (qui peut
        // placer les clans n'importe ou sur la carte).
        var config = baseConfig with { InitialClanCount = 2, AgentDensity = 0.01, TerritoryTickInterval = 1 };
        var world = new World(seed: 951, size: 128, catalog, vegetation, species, config);

        // Garantit des regions terrestres autour des deux foyers forces
        // (l'exclusion de l'eau, session rendu bordure, dependrait
        // sinon du terrain reel a cette position pour ce seed).
        catalog.TryGetId("grass", out byte grass);
        for (int y = 48; y <= 80; y++)
        {
            for (int x = 40; x <= 88; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }

        world.SetHomePosition(0, 56, 64);
        world.SetHomePosition(1, 72, 64);

        for (int i = 0; i < 5; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        uint clan0 = world.GetClan(0).Id;
        uint clan1 = world.GetClan(1).Id;

        // Chaque clan possede bien SA region foyer, et elles sont
        // DIFFERENTES -- la base d'une frontiere reelle entre deux
        // territoires, pas juste clan-vs-neutre.
        Assert.Equal(clan0, world.GetRegionOwnerAt(56, 64));
        Assert.Equal(clan1, world.GetRegionOwnerAt(72, 64));

        // Une frontiere reelle : au moins une paire de regions
        // ADJACENTES appartenant aux DEUX clans differents (pas
        // seulement clan-vs-neutre).
        uint[] first = CaptureOwnership(world);
        bool foundRealBorder = false;
        for (int cy = 0; cy < world.RegionGridHeight && !foundRealBorder; cy++)
        {
            for (int cx = 0; cx < world.RegionGridWidth - 1; cx++)
            {
                uint left = first[cy * world.RegionGridWidth + cx];
                uint right = first[cy * world.RegionGridWidth + cx + 1];
                bool bothOwned = left != TerritorySystem.NoOwner && right != TerritorySystem.NoOwner;
                if (bothOwned && left != right)
                {
                    foundRealBorder = true;
                    break;
                }
            }
        }
        Assert.True(foundRealBorder, "aucune frontiere reelle (clan contre clan) trouvee entre les deux territoires");

        for (int i = 0; i < 30; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        uint[] second = CaptureOwnership(world);
        Assert.Equal(first, second);
    }

    private static uint[] CaptureOwnership(World world)
    {
        var result = new uint[world.RegionCount];
        for (int cy = 0; cy < world.RegionGridHeight; cy++)
        {
            for (int cx = 0; cx < world.RegionGridWidth; cx++)
            {
                result[cy * world.RegionGridWidth + cx] = world.GetRegionOwnerAt(cx * world.RegionCellSize, cy * world.RegionCellSize);
            }
        }
        return result;
    }

    [Fact]
    public void Territory_IsDeterministic()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var baseConfig = TestCatalogs.LoadSimulation();
        var config = baseConfig with { TerritoryTickInterval = 1 };

        var worldA = new World(seed: 952, size: 128, catalog, vegetation, species, config);
        var worldB = new World(seed: 952, size: 128, catalog, vegetation, species, config);

        for (int i = 0; i < 30; i++)
        {
            worldA.Tick(World.TickIntervalSeconds);
            worldB.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(CaptureOwnership(worldA), CaptureOwnership(worldB));
    }

    // Remplace Harvester_CanLeaveTerritory (premise inversee, session
    // rendu bordure + confinement) : le SEUL buisson mur de la carte
    // est place loin du foyer, hors du rayon de diffusion du clan --
    // laisse l'IA agir NATURELLEMENT (pas de teleportation via seams,
    // qui contournerait le mecanisme de confinement lui-meme) sur
    // suffisamment de ticks pour plusieurs cycles de recherche.
    // Assertion comportementale : aucune recolte n'a jamais eu lieu.
    [Fact]
    public void Harvester_StaysWithinTerritory()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var baseConfig = TestCatalogs.LoadSimulation();
        var config = baseConfig with { InitialClanCount = 1, TerritoryTickInterval = 1 };
        var world = new World(seed: 954, size: 128, catalog, vegetation, species, config);
        world.ClearAllVegetation();

        for (int i = 0; i < 5; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Home home = world.GetHome(0);

        // Le SEUL buisson mur de la carte, au coin oppose du foyer --
        // hors de portee de la diffusion (rayon mesure de l'ordre de
        // quelques regions, jamais tout l'oppose de la carte).
        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("bush", out byte bushType);
        byte matureStage = (byte)vegetation.Get(bushType).MatureStage;
        int bushX = world.Size - 1 - home.X;
        int bushY = world.Size - 1 - home.Y;
        world.SetTerrainId(bushX, bushY, grass);
        world.ForceSpawnVegetation(bushX, bushY, bushType, matureStage);
        world.SetVegetationFoodRemaining(bushX, bushY, 100_000);
        world.SetClanFoodPool(0, 0);

        for (int i = 0; i < 500; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(0L, world.GetClanFoodHarvestedCumulative(0));
    }

    [Fact]
    public void Territory_DoesNotClaimWater()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var baseConfig = TestCatalogs.LoadSimulation();
        var config = baseConfig with { InitialClanCount = 1, TerritoryTickInterval = 1, TerritoryPopulationWeight = 1000.0 };
        var world = new World(seed: 955, size: 128, catalog, vegetation, species, config);

        // Un bloc d'eau entier (une region complete), foyer force
        // dessus avec un poids de population enorme -- meme une
        // influence ecrasante ne doit jamais revendiquer un lac.
        catalog.TryGetId("water", out byte water);
        for (int y = 16; y <= 31; y++)
        {
            for (int x = 16; x <= 31; x++)
            {
                world.SetTerrainId(x, y, water);
            }
        }
        world.SetHomePosition(0, 20, 20);

        for (int i = 0; i < 5; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(TerritorySystem.NoOwner, world.GetRegionOwnerAt(20, 20));
    }

    [Fact]
    public void Clan_SurvivesFoodlessStart()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var baseConfig = TestCatalogs.LoadSimulation();
        // Scenario du joueur futur : un clan pose sur un territoire
        // sans aucun buisson mur (garantie SeedMinimumBushPerPatch
        // incluse -- ClearAllVegetation() apres construction est le
        // pire cas volontaire), avec de l'herbe disponible.
        var config = baseConfig with { InitialClanCount = 1 };
        var world = new World(seed: 956, size: 128, catalog, vegetation, species, config);
        world.ClearAllVegetation();
        world.SetClanFoodPool(0, 0);

        // Marge large : (a) mesure ~2,6s (78 ticks) une fois le
        // territoire forme (premier tick a 0, tick_counter=0), tres
        // en-dessous de ce budget.
        for (int i = 0; i < 5000; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.AliveCount > 0, "population eteinte malgre la repousse spontanee");
        Assert.True(world.GetClanFoodHarvestedCumulative(0) > 0,
            "aucune recolte reelle n'a eu lieu -- survie accidentelle via le pool banked ?");
    }
}
