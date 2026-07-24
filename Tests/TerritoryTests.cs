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

    [Fact]
    public void Harvester_CanLeaveTerritory()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var baseConfig = TestCatalogs.LoadSimulation();
        var config = baseConfig with { TerritoryTickInterval = 1 };
        var world = new World(seed: 953, size: 128, catalog, vegetation, species, config);

        for (int i = 0; i < 20; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        // Place un buisson mur loin du foyer de l'agent 0 (hors de son
        // territoire probable), force la recolte, verifie que le pool
        // du clan progresse quand meme -- aucune restriction physique
        // liee au territoire.
        catalog.TryGetId("grass", out byte grass);
        vegetation.TryGetId("bush", out byte bushType);
        byte matureStage = (byte)vegetation.Get(bushType).MatureStage;

        Agent agent = world.GetAgent(0);
        Home home = world.GetHomeById(agent.HomeId);
        int bushX = Math.Clamp(world.Size - 1 - home.X, 0, world.Size - 1);
        int bushY = Math.Clamp(world.Size - 1 - home.Y, 0, world.Size - 1);
        world.SetTerrainId(bushX, bushY, grass);
        world.ForceSpawnVegetation(bushX, bushY, bushType, matureStage);
        world.SetVegetationFoodRemaining(bushX, bushY, 100_000);

        uint clanId = agent.ClanId;
        int clanIndex = -1;
        for (int c = 0; c < world.ClanCount; c++)
        {
            if (world.GetClan(c).Id == clanId)
            {
                clanIndex = c;
                break;
            }
        }
        world.SetClanFoodPool(clanIndex, 0);

        world.SetAgentPosition(0, bushX + 0.5f, bushY + 0.5f);
        world.SetAgentTarget(0, bushX, bushY);
        world.SetAgentState(0, AgentState.Harvesting);

        for (int i = 0; i < 20; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.GetClan(clanIndex).FoodPool > 0,
            "le pool du clan n'a pas progresse -- la recolte hors territoire semble bloquee");
    }
}
