using Simulation;

namespace Tests;

public class DeterminismTests
{

    [Fact]
    public void Agent_Id_RemainsValid_AfterMultipleDeathsAndCompactions()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var buildings = TestCatalogs.LoadBuildings();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed: 30, size: 64, catalog, vegetation, species, buildings, config);

        int trackedIndex = world.AliveCount - 1;
        uint trackedId = world.GetAgent(trackedIndex).Id;

        // Mort par ÂGE (pas par faim, session 19b : AllowStarvationDeath
        // =false par défaut, Hunger=254 ne tuerait plus jamais l'agent).
        int killed = 0;
        for (int i = 0; i < world.AliveCount && killed < 3; i++)
        {
            if (i == trackedIndex)
            {
                continue;
            }
            world.SetAgentLifespan(i, 4);
            world.SetAgentAge(i, 3);
            killed++;
        }

        int aliveBefore = world.AliveCount;

        for (int i = 0; i < 8; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.True(world.AliveCount < aliveBefore);

        int matches = 0;
        for (int i = 0; i < world.AliveCount; i++)
        {
            if (world.GetAgent(i).Id == trackedId)
            {
                matches++;
            }
        }

        Assert.Equal(1, matches);
    }

    [Fact]
    public void Golden_Hash_MatchesCommittedValue()
    {
        var catalog = TestCatalogs.LoadTerrain();
        var vegetation = TestCatalogs.LoadVegetation();
        var species = TestCatalogs.LoadSpecies();
        var buildings = TestCatalogs.LoadBuildings();
        var config = TestCatalogs.LoadSimulation();
        var world = new World(seed: 12345, size: 128, catalog, vegetation, species, buildings, config);

        world.Execute(new SpawnFire(64, 64, radius: 3));

        for (int i = 0; i < 5000; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        Assert.Equal(13502697128949866843UL, world.Hash());
    }
}
