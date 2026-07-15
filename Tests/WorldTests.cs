using Simulation;

namespace Tests;

public class WorldTests
{
    [Fact]
    public void World_CanBeConstructed()
    {
        var world = new World();

        Assert.NotNull(world);
    }
}
