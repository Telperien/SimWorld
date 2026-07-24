using Simulation;

namespace Tests;

public class WorldTests
{

    [Fact]
    public void World_SameSeed_ProducesSameTerrain()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var buildings = TestCatalogs.LoadBuildings();

        var a = new World(seed: 42, size: 64, catalog, vegetation, species, buildings, config);
        var b = new World(seed: 42, size: 64, catalog, vegetation, species, buildings, config);

        Assert.Equal(a.Hash(), b.Hash());
    }

    [Fact]
    public void World_DifferentSeed_ProducesDifferentTerrain()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var buildings = TestCatalogs.LoadBuildings();

        var a = new World(seed: 1, size: 64, catalog, vegetation, species, buildings, config);
        var b = new World(seed: 2, size: 64, catalog, vegetation, species, buildings, config);

        Assert.NotEqual(a.Hash(), b.Hash());
    }

    [Fact]
    public void World_RejectsNonPowerOfTwoSize()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var config = TestCatalogs.LoadSimulation();
        var buildings = TestCatalogs.LoadBuildings();

        Assert.Throws<ArgumentException>(() => new World(seed: 1, size: 100, catalog, vegetation, species, buildings, config));
    }

    [Fact]
    public void Terrain_LoadsFromJson_AllFourTypesPresent()
    {
        var catalog = TestCatalogs.LoadTerrain();

        Assert.True(catalog.TryGetId("water", out _));
        Assert.True(catalog.TryGetId("sand", out _));
        Assert.True(catalog.TryGetId("grass", out _));
        Assert.True(catalog.TryGetId("stone", out _));
    }
}
