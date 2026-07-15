using Simulation;

namespace Tests;

public class FireTests
{
    private static TerrainCatalog LoadCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "terrain.json");
        return TerrainCatalog.Load(File.ReadAllText(path));
    }

    private static void FillGrass(World world, TerrainCatalog catalog, int minX, int minY, int maxX, int maxY)
    {
        catalog.TryGetId("grass", out byte grass);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                world.SetTerrainId(x, y, grass);
            }
        }
    }

    [Fact]
    public void Fire_SpreadsOnFlammableTerrain()
    {
        var catalog = LoadCatalog();
        var world = new World(seed: 7, size: 32, catalog);
        FillGrass(world, catalog, 10, 10, 20, 20);
        catalog.TryGetId("ash", out byte ash);

        world.Execute(new SpawnFire(15, 15, radius: 0));
        world.Tick(0);

        bool spread = false;
        for (int y = 14; y <= 16; y++)
        {
            for (int x = 14; x <= 16; x++)
            {
                if (x == 15 && y == 15)
                {
                    continue;
                }

                if (world.IsBurning(x, y) || world.GetTerrainId(x, y) == ash)
                {
                    spread = true;
                }
            }
        }

        Assert.True(spread);
    }

    [Fact]
    public void Fire_StopsAtWater()
    {
        var catalog = LoadCatalog();
        var world = new World(seed: 7, size: 16, catalog);
        catalog.TryGetId("grass", out byte grass);
        catalog.TryGetId("water", out byte water);

        world.SetTerrainId(8, 8, grass);
        world.SetTerrainId(9, 8, water);

        world.Execute(new SpawnFire(8, 8, radius: 0));
        for (int i = 0; i < 5; i++)
        {
            world.Tick(0);
        }

        Assert.False(world.IsBurning(9, 8));
        Assert.Equal(water, world.GetTerrainId(9, 8));
    }

    [Fact]
    public void Fire_BecomesAsh_AfterBurning()
    {
        var catalog = LoadCatalog();
        var world = new World(seed: 3, size: 16, catalog);
        catalog.TryGetId("grass", out byte grass);
        catalog.TryGetId("ash", out byte ash);
        world.SetTerrainId(5, 5, grass);

        world.Execute(new SpawnFire(5, 5, radius: 0));
        Assert.True(world.IsBurning(5, 5));

        world.Tick(0);

        Assert.False(world.IsBurning(5, 5));
        Assert.Equal(ash, world.GetTerrainId(5, 5));
    }

    [Fact]
    public void Fire_Propagation_IsDeterministic_ForSameSeed()
    {
        var catalog = LoadCatalog();

        var a = new World(seed: 11, size: 32, catalog);
        var b = new World(seed: 11, size: 32, catalog);
        FillGrass(a, catalog, 5, 5, 25, 25);
        FillGrass(b, catalog, 5, 5, 25, 25);

        a.Execute(new SpawnFire(15, 15, radius: 2));
        b.Execute(new SpawnFire(15, 15, radius: 2));

        for (int i = 0; i < 10; i++)
        {
            a.Tick(0);
            b.Tick(0);
        }

        Assert.Equal(a.Hash(), b.Hash());
    }
}
