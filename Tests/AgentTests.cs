using Simulation;

namespace Tests;

public class AgentTests
{
    private static TerrainCatalog LoadCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "terrain.json");
        return TerrainCatalog.Load(File.ReadAllText(path));
    }

    [Fact]
    public void Agents_SpawnOnlyOnWalkableTiles()
    {
        var catalog = LoadCatalog();
        var world = new World(seed: 5, size: 128, catalog);

        for (int i = 0; i < world.AgentCapacity; i++)
        {
            Agent agent = world.GetAgent(i);
            int x = (int)MathF.Floor(agent.X);
            int y = (int)MathF.Floor(agent.Y);
            byte terrainId = world.GetTerrainId(x, y);
            Assert.True(catalog.Get(terrainId).Walkable);
        }
    }

    [Fact]
    public void Agents_Count_MatchesRequestedDensity()
    {
        var catalog = LoadCatalog();
        var world = new World(seed: 1, size: 512, catalog);

        Assert.InRange(world.AgentCapacity, 150, 250);
        Assert.Equal(world.AgentCapacity, world.AliveCount);
    }

    [Fact]
    public void Agents_Movement_IsDeterministic_ForSameSeed()
    {
        var catalog = LoadCatalog();

        var a = new World(seed: 21, size: 128, catalog);
        var b = new World(seed: 21, size: 128, catalog);

        for (int i = 0; i < 40; i++)
        {
            a.Tick(1.0 / 30.0);
            b.Tick(1.0 / 30.0);
        }

        Assert.Equal(a.Hash(), b.Hash());
    }

    [Fact]
    public void Tick_StillAllocatesNothing()
    {
        var catalog = LoadCatalog();
        var world = new World(seed: 9, size: 128, catalog);

        for (int i = 0; i < 5; i++)
        {
            world.Tick(1.0 / 30.0);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            world.Tick(1.0 / 30.0);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
