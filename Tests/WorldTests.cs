using Simulation;

namespace Tests;

public class WorldTests
{
    private static TerrainCatalog LoadCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "terrain.json");
        return TerrainCatalog.Load(File.ReadAllText(path));
    }

    [Fact]
    public void World_SameSeed_ProducesSameTerrain()
    {
        var catalog = LoadCatalog();

        var a = new World(seed: 42, size: 64, catalog);
        var b = new World(seed: 42, size: 64, catalog);

        Assert.Equal(a.Hash(), b.Hash());
    }

    [Fact]
    public void World_DifferentSeed_ProducesDifferentTerrain()
    {
        var catalog = LoadCatalog();

        var a = new World(seed: 1, size: 64, catalog);
        var b = new World(seed: 2, size: 64, catalog);

        Assert.NotEqual(a.Hash(), b.Hash());
    }

    [Fact]
    public void World_RejectsNonPowerOfTwoSize()
    {
        var catalog = LoadCatalog();

        Assert.Throws<ArgumentException>(() => new World(seed: 1, size: 100, catalog));
    }

    [Fact]
    public void Terrain_LoadsFromJson_AllFourTypesPresent()
    {
        var catalog = LoadCatalog();

        Assert.True(catalog.TryGetId("water", out _));
        Assert.True(catalog.TryGetId("sand", out _));
        Assert.True(catalog.TryGetId("grass", out _));
        Assert.True(catalog.TryGetId("stone", out _));
    }
}
