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

// Herbe par quadrant (etape 3, session 12) : balayage complet des
// tuiles, une seule fois en fin de rapport -- pas dans le tick.
terrainCatalog.TryGetId("grass", out byte grassTerrainId);
int[] grassQuadrants = new int[4];
for (int y = 0; y < size; y++)
{
    for (int x = 0; x < size; x++)
    {
        if (world.GetTerrainId(x, y) == grassTerrainId)
        {
            int quadrant = (x < half ? 0 : 1) + (y < half ? 0 : 2);
            grassQuadrants[quadrant]++;
        }
    }
}

Console.WriteLine($"Herbe par quadrant (fin de run):       HG={grassQuadrants[0]} HD={grassQuadrants[1]} BG={grassQuadrants[2]} BD={grassQuadrants[3]}");

Console.WriteLine();
Console.WriteLine($"Repas cumules: {world.MealsEaten}");
Console.WriteLine("Morts par cause:");
Console.WriteLine($"  Faim: {world.GetDeathCount(DeathCause.Hunger)}");

Console.WriteLine();
Console.WriteLine($"Feu: {world.TilesBurnedCumulative} tuiles brulees (cumule), {world.VegetationLostToFire} vegetation perdue au feu");

int totalHungerDeaths = world.GetDeathCount(DeathCause.Hunger);
if (totalHungerDeaths > 0)
{
    Console.WriteLine();
    Console.WriteLine("--- Autopsie (morts de faim) ---");

    int[] distanceHistogram = world.GetDeathDistanceHistogram();
    var bounds = World.DeathDistanceBucketUpperBounds;
    Console.WriteLine("Distance au buisson mur le plus proche (recherche globale, pas bornee au BFS) :");
    for (int b = 0; b < distanceHistogram.Length; b++)
    {
        string label = b == 0
            ? $"  [0, {bounds[0]})"
            : b < bounds.Count
                ? $"  [{bounds[b - 1]}, {bounds[b]})"
                : $"  [{bounds[^1]}, inf)";
        double pct = 100.0 * distanceHistogram[b] / totalHungerDeaths;
        Console.WriteLine($"{label,14} : {distanceHistogram[b],6}  ({pct,5:F1}%)");
    }

    Console.WriteLine();
    Console.WriteLine("Terrain sous les mourants :");
    int[] terrainHistogram = world.GetDeathTerrainHistogram();
    for (int t = 0; t < terrainHistogram.Length; t++)
    {
        if (terrainHistogram[t] == 0)
        {
            continue;
        }
        string name = $"id={t}";
        try
        {
            name = terrainCatalog.Get((byte)t).Name;
        }
        catch (ArgumentException)
        {
            // id inconnu du catalogue (ne devrait pas arriver) : garde id=N.
        }
        double pct = 100.0 * terrainHistogram[t] / totalHungerDeaths;
        Console.WriteLine($"  {name,-8} : {terrainHistogram[t],6}  ({pct,5:F1}%)");
    }

    Console.WriteLine();
    double lifeTicks = world.AverageDeathTicksIdle + world.AverageDeathTicksMoving
        + world.AverageDeathTicksSeeking + world.AverageDeathTicksEating;
    Console.WriteLine($"Echecs de recherche consecutifs (moyenne avant la mort) : {world.AverageDeathFailureStreak:F1}");
    Console.WriteLine($"Faim au dernier repas commence (moyenne)                : {world.AverageDeathHungerAtLastMeal:F1}");
    if (lifeTicks > 0)
    {
        Console.WriteLine($"Repartition du temps de vie (moyenne) : " +
            $"Idle={100.0 * world.AverageDeathTicksIdle / lifeTicks:F1}% " +
            $"Moving={100.0 * world.AverageDeathTicksMoving / lifeTicks:F1}% " +
            $"Seeking={100.0 * world.AverageDeathTicksSeeking / lifeTicks:F1}% " +
            $"Eating={100.0 * world.AverageDeathTicksEating / lifeTicks:F1}%");
    }
}

Console.WriteLine();
Console.WriteLine($"Hash final: 0x{world.Hash():X16}");
