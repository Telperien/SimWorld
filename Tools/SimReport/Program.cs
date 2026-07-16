using System.Diagnostics;
using Simulation;

int seed = 42;
int ticks = 1000;
int size = 512;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed":
            seed = int.Parse(args[++i]);
            break;
        case "--ticks":
            ticks = int.Parse(args[++i]);
            break;
        case "--size":
            size = int.Parse(args[++i]);
            break;
    }
}

string basePath = AppContext.BaseDirectory;
var terrainCatalog = TerrainCatalog.Load(File.ReadAllText(Path.Combine(basePath, "data", "terrain.json")));
var vegetationCatalog = VegetationCatalog.Load(File.ReadAllText(Path.Combine(basePath, "data", "vegetation.json")));
var config = SimulationConfig.Load(File.ReadAllText(Path.Combine(basePath, "data", "simulation.json")));

vegetationCatalog.TryGetId("bush", out byte bushType);
vegetationCatalog.TryGetId("tree", out byte treeType);

var world = new World(seed, size, terrainCatalog, vegetationCatalog, config);

if (world.AgentSpawnCapped)
{
    Console.WriteLine("ATTENTION: le spawn d'agents a atteint sa limite de tentatives (carte quasi sans tuiles walkable ?)");
}

var samples = new List<(int Tick, int Pop, int Bush, int Tree, int Grass, int Ash)>();

void Sample(int tick)
{
    samples.Add((
        tick,
        world.AliveCount,
        world.CountVegetationOfType(bushType),
        world.CountVegetationOfType(treeType),
        world.GrassTileCount,
        world.AshTileCount));
}

Sample(0);

int sampleInterval = Math.Max(1, ticks / 20);
var stopwatch = Stopwatch.StartNew();

for (int i = 0; i < ticks; i++)
{
    world.Tick(World.TickIntervalSeconds);

    if ((i + 1) % sampleInterval == 0 || i == ticks - 1)
    {
        Sample(i + 1);
    }
}

stopwatch.Stop();

Console.WriteLine($"SimReport -- seed={seed} size={size} ticks={ticks}");
Console.WriteLine($"Duree: {stopwatch.Elapsed.TotalSeconds:F2}s");
Console.WriteLine();
Console.WriteLine($"{"tick",8} {"pop",6} {"bush",6} {"tree",6} {"grass",8} {"ash",6}");
foreach (var s in samples)
{
    Console.WriteLine($"{s.Tick,8} {s.Pop,6} {s.Bush,6} {s.Tree,6} {s.Grass,8} {s.Ash,6}");
}

Console.WriteLine();
Console.WriteLine("Morts par cause:");
Console.WriteLine($"  Faim: {world.GetDeathCount(DeathCause.Hunger)}");

Console.WriteLine();
Console.WriteLine($"Hash final: 0x{world.Hash():X16}");
