using System.Diagnostics;
using Simulation;

int seed = 42;
int ticks = 1000;
int size = 512;
bool scarcity = false;
bool fire = false;
int fireInterval = 5000;
int fireRadius = 5;

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
        case "--scarcity":
            scarcity = true;
            break;
        case "--fire":
            fire = true;
            break;
        case "--fire-interval":
            fireInterval = int.Parse(args[++i]);
            break;
        case "--fire-radius":
            fireRadius = int.Parse(args[++i]);
            break;
    }
}

string basePath = AppContext.BaseDirectory;
var terrainCatalog = TerrainCatalog.Load(File.ReadAllText(Path.Combine(basePath, "data", "terrain.json")));
var vegetationCatalog = VegetationCatalog.Load(File.ReadAllText(Path.Combine(basePath, "data", "vegetation.json")));
var baseConfig = SimulationConfig.Load(File.ReadAllText(Path.Combine(basePath, "data", "simulation.json")));

// Densite d'agents en hausse, capacite vegetale en baisse : force une
// vraie pression (declin qui ralentit nettement, pas un massacre total).
var config = scarcity
    ? baseConfig with { AgentDensity = 0.0011, VegetationDensity = 0.03 }
    : baseConfig;

vegetationCatalog.TryGetId("bush", out byte bushType);
vegetationCatalog.TryGetId("tree", out byte treeType);

var world = new World(seed, size, terrainCatalog, vegetationCatalog, config);

// Stimulus externe (comme un clic joueur) : Rng local au rapport, seede
// sur --seed pour rester reproductible run-a-run, mais hors de World
// (jamais dans Hash()).
var fireRng = new Rng((ulong)seed);

if (world.AgentSpawnCapped)
{
    Console.WriteLine("ATTENTION: le spawn d'agents a atteint sa limite de tentatives (carte quasi sans tuiles walkable ?)");
}

var samples = new List<(int Tick, int Pop, int BushYoung, int BushMature, int Tree, int Grass, int Ash)>();

void Sample(int tick)
{
    int bushMature = world.CountMatureVegetationOfType(bushType);
    int bushTotal = world.CountVegetationOfType(bushType);
    samples.Add((
        tick,
        world.AliveCount,
        bushTotal - bushMature,
        bushMature,
        world.CountVegetationOfType(treeType),
        world.GrassTileCount,
        world.AshTileCount));
}

Sample(0);

int sampleInterval = Math.Max(1, ticks / 20);
var stopwatch = Stopwatch.StartNew();

for (int i = 0; i < ticks; i++)
{
    if (fire && i % fireInterval == 0)
    {
        int fireX = (int)(fireRng.NextDouble() * size);
        int fireY = (int)(fireRng.NextDouble() * size);
        world.Execute(new SpawnFire(fireX, fireY, fireRadius));
    }

    world.Tick(World.TickIntervalSeconds);

    if ((i + 1) % sampleInterval == 0 || i == ticks - 1)
    {
        Sample(i + 1);
    }
}

stopwatch.Stop();

string flags = (scarcity ? " --scarcity" : "") + (fire ? $" --fire (interval={fireInterval} radius={fireRadius})" : "");
Console.WriteLine($"SimReport -- seed={seed} size={size} ticks={ticks}{flags}");
Console.WriteLine($"Duree: {stopwatch.Elapsed.TotalSeconds:F2}s");
Console.WriteLine();
Console.WriteLine($"{"tick",8} {"pop",6} {"bjeune",7} {"bmur",6} {"arbre",6} {"herbe",8} {"cendre",7}");
foreach (var s in samples)
{
    Console.WriteLine($"{s.Tick,8} {s.Pop,6} {s.BushYoung,7} {s.BushMature,6} {s.Tree,6} {s.Grass,8} {s.Ash,7}");
}

int idle = 0, moving = 0, seeking = 0, eating = 0;
for (int i = 0; i < world.AliveCount; i++)
{
    switch (world.GetAgent(i).State)
    {
        case AgentState.Idle: idle++; break;
        case AgentState.Moving: moving++; break;
        case AgentState.Seeking: seeking++; break;
        case AgentState.Eating: eating++; break;
    }
}

Console.WriteLine();
Console.WriteLine($"Etats agents (fin de run): Idle={idle} Moving={moving} Seeking={seeking} Eating={eating}");

int half = size / 2;
int[] quadrants = new int[4];
for (int i = 0; i < world.VegetationCount; i++)
{
    Vegetation veg = world.GetVegetation(i);
    int quadrant = (veg.X < half ? 0 : 1) + (veg.Y < half ? 0 : 2);
    quadrants[quadrant]++;
}

Console.WriteLine($"Vegetation par quadrant (fin de run): HG={quadrants[0]} HD={quadrants[1]} BG={quadrants[2]} BD={quadrants[3]}");

Console.WriteLine();
Console.WriteLine($"Repas cumules: {world.MealsEaten}");
Console.WriteLine("Morts par cause:");
Console.WriteLine($"  Faim: {world.GetDeathCount(DeathCause.Hunger)}");

Console.WriteLine();
Console.WriteLine($"Feu: {world.TilesBurnedCumulative} tuiles brulees (cumule), {world.VegetationLostToFire} vegetation perdue au feu");

Console.WriteLine();
Console.WriteLine($"Hash final: 0x{world.Hash():X16}");
