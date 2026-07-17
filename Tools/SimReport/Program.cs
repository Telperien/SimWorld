using System.Diagnostics;
using System.Linq;
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
var speciesCatalog = SpeciesCatalog.Load(File.ReadAllText(Path.Combine(basePath, "data", "species.json")));
var baseConfig = SimulationConfig.Load(File.ReadAllText(Path.Combine(basePath, "data", "simulation.json")));

// Densite d'agents en hausse, capacite vegetale en baisse : force une
// vraie pression (declin qui ralentit nettement, pas un massacre total).
var config = scarcity
    ? baseConfig with { AgentDensity = 0.0011, BushDensity = 0.03, TreeDensity = 0.012 }
    : baseConfig;

vegetationCatalog.TryGetId("bush", out byte bushType);
vegetationCatalog.TryGetId("tree", out byte treeType);

var world = new World(seed, size, terrainCatalog, vegetationCatalog, speciesCatalog, config);

// Stimulus externe (comme un clic joueur) : Rng local au rapport, seede
// sur --seed pour rester reproductible run-a-run, mais hors de World
// (jamais dans Hash()).
var fireRng = new Rng((ulong)seed);

if (world.AgentSpawnCapped)
{
    Console.WriteLine("ATTENTION: le spawn d'agents a atteint sa limite de tentatives (carte quasi sans tuiles walkable ?)");
}

const int ageBuckets = 10;

var samples = new List<(int Tick, int Pop, int BushYoung, int BushMature, int Tree, int Grass, int Ash,
    int HungerDeathsCum, int AgeDeathsCum, int BirthsCum, int BirthsRefusedCum, double AgentStdDev, int[] AgeHistogram)>();

void Sample(int tick)
{
    int bushMature = world.CountMatureVegetationOfType(bushType);
    int bushTotal = world.CountVegetationOfType(bushType);

    SpeciesType speciesForHistogram = speciesCatalog.Get(0);
    int[] ageHistogram = new int[ageBuckets];
    if (world.AliveCount > 0)
    {
        uint bucketWidth = Math.Max(1, speciesForHistogram.LifespanTicks / ageBuckets);
        for (int i = 0; i < world.AliveCount; i++)
        {
            uint age = world.GetAgent(i).Age;
            int bucket = (int)Math.Min(ageBuckets - 1, age / bucketWidth);
            ageHistogram[bucket]++;
        }
    }

    samples.Add((
        tick,
        world.AliveCount,
        bushTotal - bushMature,
        bushMature,
        world.CountVegetationOfType(treeType),
        world.GrassTileCount,
        world.AshTileCount,
        world.GetDeathCount(DeathCause.Hunger),
        world.GetDeathCount(DeathCause.Age),
        world.BirthsTotal,
        world.BirthsRefusedArrayFull,
        world.AgentDensityStdDev(),
        ageHistogram));
}

Sample(0);

int sampleInterval = Math.Max(1, ticks / 20);
var stopwatch = Stopwatch.StartNew();

// Points de controle pour l'evolution de la connectivite d'herbe
// (session 17b, partie 1.2) : uniquement les points demandes qui
// tombent dans la duree reelle du run, plus toujours le tick final.
var connectivityCheckpointTicks = new List<int> { 0, 500_000, 1_000_000, 2_000_000 }
    .Where(t => t <= ticks)
    .Distinct()
    .OrderBy(t => t)
    .ToList();
if (connectivityCheckpointTicks.Count == 0 || connectivityCheckpointTicks[^1] != ticks)
{
    connectivityCheckpointTicks.Add(ticks);
}
var connectivitySamples = new List<(int Tick, GrassConnectivityReport Report)>();
if (connectivityCheckpointTicks.Contains(0))
{
    connectivitySamples.Add((0, world.AnalyzeGrassConnectivity()));
}

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

    if (connectivityCheckpointTicks.Contains(i + 1))
    {
        connectivitySamples.Add((i + 1, world.AnalyzeGrassConnectivity()));
    }
}

stopwatch.Stop();

string flags = (scarcity ? " --scarcity" : "") + (fire ? $" --fire (interval={fireInterval} radius={fireRadius})" : "");
Console.WriteLine($"SimReport -- seed={seed} size={size} ticks={ticks}{flags}");
Console.WriteLine($"Duree: {stopwatch.Elapsed.TotalSeconds:F2}s");
Console.WriteLine();
Console.WriteLine($"{"tick",8} {"pop",6} {"bjeune",7} {"bmur",6} {"arbre",6} {"herbe",8} {"cendre",7} " +
    $"{"faimD",6} {"ageD",5} {"naisD",6} {"refusD",7} {"stdAgt",7}");
for (int s = 0; s < samples.Count; s++)
{
    var cur = samples[s];
    // Deltas par intervalle (pas cumules) : montre ce qui tue/nait
    // PENDANT chaque fenetre, pas seulement le total sur tout le run
    // (session 14b, diagnostic boom-bust).
    int hungerD = s == 0 ? cur.HungerDeathsCum : cur.HungerDeathsCum - samples[s - 1].HungerDeathsCum;
    int ageD = s == 0 ? cur.AgeDeathsCum : cur.AgeDeathsCum - samples[s - 1].AgeDeathsCum;
    int birthsD = s == 0 ? cur.BirthsCum : cur.BirthsCum - samples[s - 1].BirthsCum;
    int refusedD = s == 0 ? cur.BirthsRefusedCum : cur.BirthsRefusedCum - samples[s - 1].BirthsRefusedCum;
    Console.WriteLine($"{cur.Tick,8} {cur.Pop,6} {cur.BushYoung,7} {cur.BushMature,6} {cur.Tree,6} {cur.Grass,8} {cur.Ash,7} " +
        $"{hungerD,6} {ageD,5} {birthsD,6} {refusedD,7} {cur.AgentStdDev,7:F2}");
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

// Agents par quadrant (session 14d, question 2b) : correle-t-on le
// deficit de vegetation d'un quadrant a sa densite d'agents (broutage)
// plutot qu'a un artefact de repousse ?
int[] agentQuadrants = new int[4];
for (int i = 0; i < world.AliveCount; i++)
{
    Agent agent = world.GetAgent(i);
    int quadrant = (agent.X < half ? 0 : 1) + (agent.Y < half ? 0 : 2);
    agentQuadrants[quadrant]++;
}
Console.WriteLine($"Agents par quadrant (fin de run):      HG={agentQuadrants[0]} HD={agentQuadrants[1]} BG={agentQuadrants[2]} BD={agentQuadrants[3]}");

// Mesure de clusterisation (etape 6, session 13) : distance moyenne au
// buisson mur le plus proche pour un point d'herbe TIRE AU HASARD (pas
// seulement les mourants) -- Rng local au rapport, hors de World, jamais
// dans Hash(), meme esprit que fireRng.
var clusterRng = new Rng((ulong)seed ^ 0xC1AA5D1FUL);
const int clusterSampleTarget = 2000;
int clusterSamples = 0;
double clusterDistanceSum = 0.0;
int clusterAttempts = 0;
int clusterMaxAttempts = clusterSampleTarget * 50;

while (clusterSamples < clusterSampleTarget && clusterAttempts < clusterMaxAttempts)
{
    clusterAttempts++;
    int sx = (int)(clusterRng.NextDouble() * size);
    int sy = (int)(clusterRng.NextDouble() * size);
    if (world.GetTerrainId(sx, sy) != grassTerrainId)
    {
        continue;
    }

    double distance = world.DistanceToNearestMatureBush(sx, sy);
    if (double.IsFinite(distance))
    {
        clusterDistanceSum += distance;
        clusterSamples++;
    }
}

Console.WriteLine();
if (clusterSamples > 0)
{
    Console.WriteLine($"Clusterisation -- distance moyenne au buisson mur le plus proche pour {clusterSamples} points d'herbe tires au hasard : {clusterDistanceSum / clusterSamples:F2}");
}

// --- Diagnostic terrain/vegetation/feu (session 17b, partie 1) ---
// Hypothese : chaque lac ceinture de sable isole l'herbe en ilots
// (sable/eau/pierre/cendre jamais inflammables ni porteurs de
// vegetation) -- chacun son propre coupe-feu, chacun sa propre
// disponibilite de graines locales pour la repousse.
Console.WriteLine();
Console.WriteLine("--- Connectivite de l'herbe (session 17b) ---");
foreach (var (tick, report) in connectivitySamples)
{
    Console.WriteLine($"  tick {tick,8} : {report.PatchCount,5} poches (tailles min={report.MinSize} median={report.MedianSize} max={report.MaxSize}), " +
        $"{report.PatchesWithNoBush,5} sans aucun buisson");
}

var lastConnectivity = connectivitySamples[^1].Report;
Console.WriteLine();
Console.WriteLine("Poches sans buisson par quadrant (dernier point de controle), a comparer au deficit de vegetation par quadrant ci-dessus :");
Console.WriteLine($"  HG={lastConnectivity.PatchesWithNoBushByQuadrant[0]}/{lastConnectivity.PatchCountByQuadrant[0]} " +
    $"HD={lastConnectivity.PatchesWithNoBushByQuadrant[1]}/{lastConnectivity.PatchCountByQuadrant[1]} " +
    $"BG={lastConnectivity.PatchesWithNoBushByQuadrant[2]}/{lastConnectivity.PatchCountByQuadrant[2]} " +
    $"BD={lastConnectivity.PatchesWithNoBushByQuadrant[3]}/{lastConnectivity.PatchCountByQuadrant[3]}  (poches sans buisson / total poches)");

Console.WriteLine();
Console.WriteLine("--- Feu : taille d'evenement et cause d'extinction (session 17b) ---");
Console.WriteLine($"  Evenements termines: {world.FireEventCount}  Taille moyenne: {world.AverageFireEventSize:F1} tuiles  Taille max: {world.MaxFireEventSize} tuiles");
int fireBlockTotal = world.FireBlockedByTerrainCount + world.FireFizzledCount;
if (fireBlockTotal > 0)
{
    double blockedPct = 100.0 * world.FireBlockedByTerrainCount / fireBlockTotal;
    double fizzledPct = 100.0 * world.FireFizzledCount / fireBlockTotal;
    Console.WriteLine($"  Tentatives de propagation bloquees : {world.FireBlockedByTerrainCount} par terrain non-inflammable ({blockedPct:F1}%), " +
        $"{world.FireFizzledCount} par tirage rate sur terrain inflammable ({fizzledPct:F1}%)");
}

Console.WriteLine();
Console.WriteLine("--- Couplage arbre/buisson et repousse cendre (session 17b) ---");
double treeShareOfGrass = world.GrassTileCount > 0 ? 100.0 * world.TreeCount / world.GrassTileCount : 0.0;
Console.WriteLine($"  Tuiles d'herbe occupees par un arbre (indisponibles pour un buisson) : {world.TreeCount} / {world.GrassTileCount} herbe ({treeShareOfGrass:F1}%)");
double ashRecoveryEligibleTicks = config.AshToGrassChance > 0 ? 1.0 / config.AshToGrassChance : double.PositiveInfinity;
double ashRecoveryRealTicks = ashRecoveryEligibleTicks * config.VegetationTickInterval;
Console.WriteLine($"  Guerison cendre->herbe attendue : ~{ashRecoveryRealTicks:F0} ticks reels ({ashRecoveryRealTicks / 30.0:F1}s a 30Hz), " +
    $"a comparer aux bandes temporelles s15 (repousse buisson 9000 ticks/300s, maturation arbre 900 ticks/30s)");

Console.WriteLine();
Console.WriteLine($"Repas cumules: {world.MealsEaten}");
Console.WriteLine("Morts par cause:");
Console.WriteLine($"  Faim: {world.GetDeathCount(DeathCause.Hunger)}");
Console.WriteLine($"  Age : {world.GetDeathCount(DeathCause.Age)}");

Console.WriteLine();
Console.WriteLine($"Naissances cumulees: {world.BirthsTotal}");
Console.WriteLine($"Naissances refusees (tableau plein): {world.BirthsRefusedArrayFull}");
Console.WriteLine($"Naissances perdues (tuile non sure): {world.BirthsLostToUnsafeTile}");

// Histogrammes des ages a 4 instants choisis APRES COUP sur la courbe
// de population deja collectee (session 14b, diagnostic boom-bust) :
// baseline (premier echantillon), pic (argmax pop), creux suivant le
// pic (argmin pop apres l'index du pic), fin de run. Revele les vagues
// de cohorte si un echo demographique est la cause du crash.
{
    SpeciesType speciesForHistogram = speciesCatalog.Get(0);
    uint bucketWidth = Math.Max(1, speciesForHistogram.LifespanTicks / ageBuckets);

    int peakIdx = 0;
    for (int s = 1; s < samples.Count; s++)
    {
        if (samples[s].Pop > samples[peakIdx].Pop)
        {
            peakIdx = s;
        }
    }

    int troughIdx = peakIdx;
    for (int s = peakIdx + 1; s < samples.Count; s++)
    {
        if (samples[s].Pop < samples[troughIdx].Pop)
        {
            troughIdx = s;
        }
    }

    var picks = new (string Label, int Index)[]
    {
        ("Baseline (premier echantillon)", 0),
        ("Pic de population", peakIdx),
        ("Creux apres le pic", troughIdx),
        ("Fin de run", samples.Count - 1),
    };

    Console.WriteLine();
    Console.WriteLine("--- Histogrammes des ages (session 14b, diagnostic) ---");
    foreach (var (label, idx) in picks)
    {
        var s = samples[idx];
        Console.WriteLine();
        Console.WriteLine($"{label} -- tick {s.Tick}, pop {s.Pop} :");
        for (int b = 0; b < ageBuckets; b++)
        {
            uint lower = (uint)b * bucketWidth;
            uint upper = (uint)(b + 1) * bucketWidth;
            double pct = s.Pop > 0 ? 100.0 * s.AgeHistogram[b] / s.Pop : 0.0;
            Console.WriteLine($"  [{lower,7}, {upper,7}) : {s.AgeHistogram[b],6}  ({pct,5:F1}%)");
        }
    }
}

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
    Console.WriteLine("Issue du dernier cycle de recherche avant la mort (session 14d) :");
    int[] seekOutcomeHistogram = world.GetDeathSeekOutcomeHistogram();
    string[] seekOutcomeLabels = { "Jamais cherche", "Buisson trouve (BFS)", "Suivait le gradient", "Errance aveugle" };
    for (int o = 0; o < seekOutcomeHistogram.Length; o++)
    {
        double pct = 100.0 * seekOutcomeHistogram[o] / totalHungerDeaths;
        Console.WriteLine($"  {seekOutcomeLabels[o],-22} : {seekOutcomeHistogram[o],6}  ({pct,5:F1}%)");
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
