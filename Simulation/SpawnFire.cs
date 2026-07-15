namespace Simulation;

public sealed class SpawnFire : ICommand
{
    private readonly int _x;
    private readonly int _y;
    private readonly int _radius;

    public SpawnFire(int x, int y, int radius)
    {
        _x = x;
        _y = y;
        _radius = radius;
    }

    public void Execute(World world) => world.IgniteArea(_x, _y, _radius);
}
