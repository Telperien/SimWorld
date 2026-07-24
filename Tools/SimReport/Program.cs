using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Simulation;

int seed = 42;
int ticks = 1000;
int size = 512;
bool scarcity = false;
bool fire = false;
int fireInterval = 5000;
int fireRadius = 5;
bool bench = false;
string? seedsArg = null;
double? bushDensityOverride = null;
double? poolPerCapitaOverride = null;
double? conceptionChanceOverride = null;
double? harvestChanceOverride = null;
double? treeSpreadChanceOverride = null;
double? treeSpontaneousChanceOverride = null;
int? agentCapacityMultiplierOverride = null;
bool allowStarvationDeathOverride = false;

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
        case "--bench":
            bench = true;
            break;
        case "--seeds":
            seedsArg = args[++i];
            break;
        case "--bush-density":
            bushDensityOverride = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--pool-per-capita":
            poolPerCapitaOverride = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--conception-chance":
            conceptionChanceOverride = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--harvest-chance":
            harvestChanceOverride = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--tree-spread-chance":
            treeSpreadChanceOverride = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--tree-spontaneous-chance":
            treeSpontaneousChanceOverride = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--agent-capacity-multiplier":
            agentCapacityMultiplierOverride = int.Parse(args[++i]);
            break;
        case "--allow-starvation-death":
            allowStarvationDeathOverride = true;
            break;
    }
}

string basePath = AppContext.BaseDirectory;
var terrainCatalog = TerrainCatalog.Load(ReadJsonOrThrow(Path.Combine(basePath, "data", "terrain.json")));
var vegetationCatalog = VegetationCatalog.Load(ReadJsonOrThrow(Path.Combine(basePath, "data", "vegetation.json")));
var speciesCatalog = SpeciesCatalog.Load(ReadJsonOrThrow(Path.Combine(basePath, "data", "species.json")));
var baseConfig = SimulationConfig.Load(ReadJsonOrThrow(Path.Combine(basePath, "data", "simulation.json")));

// Session filet : pas dans /Simulation (CLAUDE.md interdit System.IO
// la-dedans) -- message d'erreur lisible (chemin + nom de fichier)
// au lieu d'une FileNotFoundException brute si un JSON de boot manque.
static string ReadJsonOrThrow(string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException(
            $"fichier de configuration introuvable : '{Path.GetFileName(path)}' attendu a '{path}'", path);
    }
    return File.ReadAllText(path);
}

// --bench : diagnostic de perf (session 18 suite) -- la simulation est-elle
// superlineaire en population ? Construit des mondes a population CONTROLEE
// (via AgentDensity), chauffe, chronometre un lot de ticks, rapporte le
// cout par agent par tick. Sort avant le rapport normal (mode exclusif).
if (bench)
{
    RunPopulationBenchmark(terrainCatalog, vegetationCatalog, speciesCatalog, baseConfig, seed, size);
    return;
}

// --seeds 42,7 : deux World independants, aucun etat partage -- lance
// des processus enfants en parallele (chacun re-invoque ce meme
// executable avec --seed unique) plutot que des threads dans ce
// process, pour rester simple/surete memoire sans toucher a la regle
// mono-thread de /Simulation (qui ne s'applique qu'a la sim elle-meme,
// pas au harnais /Tools). x2 (ou plus) gratuit en temps d'horloge murale.
if (seedsArg != null)
{
    RunParallelSeeds(seedsArg, args);
    return;
}

static void RunParallelSeeds(string seedsArg, string[] originalArgs)
{
    int[] seedList = seedsArg.Split(',').Select(int.Parse).ToArray();
    string hostPath = Process.GetCurrentProcess().MainModule!.FileName!;
    string assemblyPath = Environment.GetCommandLineArgs()[0];

    var tasks = new List<(int Seed, Process Process, Task<string> Output)>();
    foreach (int s in seedList)
    {
        var childArgs = new List<string> { assemblyPath };
        for (int i = 0; i < originalArgs.Length; i++)
        {
            if (originalArgs[i] == "--seeds")
            {
                i++;
                continue;
            }
            childArgs.Add(originalArgs[i]);
        }
        childArgs.Add("--seed");
        childArgs.Add(s.ToString());

        var psi = new ProcessStartInfo(hostPath) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string a in childArgs)
        {
            psi.ArgumentList.Add(a);
        }

        var process = new Process { StartInfo = psi };
        process.Start();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        tasks.Add((s, process, outputTask));
    }

    foreach (var (s, process, outputTask) in tasks)
    {
        string output = outputTask.GetAwaiter().GetResult();
        process.WaitForExit();
        Console.WriteLine($"===== seed {s} =====");
        Console.WriteLine(output);
    }
}

static void RunPopulationBenchmark(Catalog<TerrainType> terrainCatalog, Catalog<VegetationType> vegetationCatalog,
    Catalog<SpeciesType> speciesCatalog, SimulationConfig baseConfig, int seed, int size)
{
    const int warmupTicks = 2000;
    const int measureTicks = 3000;
    int[] targetPopulations = { 100, 500, 1000, 2000, 5000, 15000, 30000 };

    Console.WriteLine($"SimReport --bench -- seed={seed} size={size} warmup={warmupTicks} measure={measureTicks}");
    Console.WriteLine();
    Console.WriteLine($"{"popCible",8} {"popDebut",8} {"popFin",7} {"msTotal",8} {"us/tick",9} {"us/agent/tick",14}");

    foreach (int targetPop in targetPopulations)
    {
        double density = (double)targetPop / (size * size);
        // Multiplicateur de capacite reduit pour le benchmark : a
        // grande population cible, le multiplicateur par defaut (200)
        // ferait exploser la memoire du tableau Agent[] pour rien (le
        // benchmark ne teste pas la croissance de population).
        var config = baseConfig with { AgentDensity = density, AgentCapacityMultiplier = 3 };
        var world = new World(seed, size, terrainCatalog, vegetationCatalog, speciesCatalog, config);

        for (int i = 0; i < warmupTicks; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }

        int popStart = world.AliveCount;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measureTicks; i++)
        {
            world.Tick(World.TickIntervalSeconds);
        }
        sw.Stop();
        int popEnd = world.AliveCount;

        double avgPop = Math.Max(1, (popStart + popEnd) / 2.0);
        double usPerTick = sw.Elapsed.TotalMilliseconds * 1000.0 / measureTicks;
        double usPerAgentPerTick = usPerTick / avgPop;

        Console.WriteLine($"{targetPop,8} {popStart,8} {popEnd,7} {sw.Elapsed.TotalMilliseconds,8:F0} {usPerTick,9:F2} {usPerAgentPerTick,14:F4}");
    }
}

// Densite d'agents en hausse, capacite vegetale en baisse : force une
// vraie pression (declin qui ralentit nettement, pas un massacre total).
var config = scarcity
    ? baseConfig with { AgentDensity = 0.0011, BushDensity = 0.03, TreeDensity = 0.012 }
    : baseConfig;

// Balayage de densite (session 19) : override ponctuel sans toucher au
// fichier de config, pour sweeper plusieurs valeurs sans rebuild entre
// chaque run.
if (bushDensityOverride.HasValue)
{
    config = config with { BushDensity = bushDensityOverride.Value };
}
if (poolPerCapitaOverride.HasValue)
{
    config = config with { TargetFoodPoolPerCapita = poolPerCapitaOverride.Value };
}
if (conceptionChanceOverride.HasValue)
{
    config = config with { BaseConceptionChance = conceptionChanceOverride.Value };
}
if (harvestChanceOverride.HasValue)
{
    config = config with { BaseHarvestChance = harvestChanceOverride.Value };
}
if (treeSpreadChanceOverride.HasValue)
{
    config = config with { TreeSpreadChance = treeSpreadChanceOverride.Value };
}
if (treeSpontaneousChanceOverride.HasValue)
{
    config = config with { TreeSpontaneousChance = treeSpontaneousChanceOverride.Value };
}
if (agentCapacityMultiplierOverride.HasValue)
{
    config = config with { AgentCapacityMultiplier = agentCapacityMultiplierOverride.Value };
}
if (allowStarvationDeathOverride)
{
    config = config with { AllowStarvationDeath = true };
}

vegetationCatalog.TryGetId("bush", out byte bushType);
vegetationCatalog.TryGetId("tree", out byte treeType);

var overallStopwatch = Stopwatch.StartNew();
var world = new World(seed, size, terrainCatalog, vegetationCatalog, speciesCatalog, config);

// Stimulus externe (comme un clic joueur) : Rng local au rapport, seede
// sur --seed pour rester reproductible run-a-run, mais hors de World
// (jamais dans Hash()).
var fireRng = new Rng((ulong)seed);

if (world.AgentSpawnCapped)
{
    Console.WriteLine("ATTENTION: le spawn d'agents a atteint sa limite de tentatives (carte quasi sans tuiles walkable ?)");
}

// Ordre de generation (session territoire) : mesure la proportion
// d'agents hors du territoire de leur PROPRE clan -- capturee a t=0 (juste
// apres construction, avant tout Tick) et a nouveau en fin de run. Si ce
// chiffre reste bas a t=0 mais grimpe en cours de partie, le territoire
// RECULE sous des agents deja en place (probleme distinct, pas un defaut
// de generation).
static (int OutsideCount, int AliveCount) CountAgentsOutsideOwnTerritory(World world)
{
    int outsideCount = 0;
    for (int i = 0; i < world.AliveCount; i++)
    {
        Agent agent = world.GetAgent(i);
        if (world.GetRegionOwnerAt((int)agent.X, (int)agent.Y) != agent.ClanId)
        {
            outsideCount++;
        }
    }
    return (outsideCount, world.AliveCount);
}

var (outsideAtStart, aliveAtStart) = CountAgentsOutsideOwnTerritory(world);

const int ageBuckets = 10;

var samples = new List<(int Tick, int Pop, int BushYoung, int BushMature, int Tree, int Grass, int Ash,
    int HungerDeathsCum, int AgeDeathsCum, int BirthsCum, int BirthsRefusedCum, double AgentStdDev, int[] AgeHistogram,
    int[] PoolPerClan)>();

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

    var poolPerClan = new int[world.ClanCount];
    for (int c = 0; c < world.ClanCount; c++)
    {
        poolPerClan[c] = world.GetClan(c).FoodPool;
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
        ageHistogram,
        poolPerClan));
}

Sample(0);

int sampleInterval = Math.Max(1, ticks / 20);
var stopwatch = Stopwatch.StartNew();

// Chronometrage separe simulation vs instrumentation (diagnostic de
// perf, cf. plan) : tickStopwatch n'entoure QUE world.Tick -- rien
// d'autre. Jamais dans /Simulation (interdit par CLAUDE.md), acceptable
// ici : /Tools est le harnais, pas la sim.
var tickStopwatch = new Stopwatch();
var connectivityStopwatch = new Stopwatch();
var sampleStopwatch = new Stopwatch();

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
    connectivityStopwatch.Start();
    connectivitySamples.Add((0, world.AnalyzeGrassConnectivity()));
    connectivityStopwatch.Stop();
}

for (int i = 0; i < ticks; i++)
{
    if (fire && i % fireInterval == 0)
    {
        int fireX = (int)(fireRng.NextDouble() * size);
        int fireY = (int)(fireRng.NextDouble() * size);
        world.Execute(new SpawnFire(fireX, fireY, fireRadius));
    }

    tickStopwatch.Start();
    world.Tick(World.TickIntervalSeconds);
    tickStopwatch.Stop();

    if ((i + 1) % sampleInterval == 0 || i == ticks - 1)
    {
        sampleStopwatch.Start();
        Sample(i + 1);
        sampleStopwatch.Stop();
    }

    if (connectivityCheckpointTicks.Contains(i + 1))
    {
        connectivityStopwatch.Start();
        connectivitySamples.Add((i + 1, world.AnalyzeGrassConnectivity()));
        connectivityStopwatch.Stop();
    }
}

stopwatch.Stop();

string flags = (scarcity ? " --scarcity" : "") + (fire ? $" --fire (interval={fireInterval} radius={fireRadius})" : "");
Console.WriteLine($"SimReport -- seed={seed} size={size} ticks={ticks}{flags}");
Console.WriteLine($"Duree: {stopwatch.Elapsed.TotalSeconds:F2}s");
Console.WriteLine();
string poolHeader = string.Concat(Enumerable.Range(0, world.ClanCount).Select(c => $" {"pool" + c,8}"));
Console.WriteLine($"{"tick",8} {"pop",6} {"bjeune",7} {"bmur",6} {"arbre",6} {"herbe",8} {"cendre",7} " +
    $"{"faimD",6} {"ageD",5} {"naisD",6} {"refusD",7} {"stdAgt",7}{poolHeader}");
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
    string poolCols = string.Concat(cur.PoolPerClan.Select(p => $" {p,8}"));
    Console.WriteLine($"{cur.Tick,8} {cur.Pop,6} {cur.BushYoung,7} {cur.BushMature,6} {cur.Tree,6} {cur.Grass,8} {cur.Ash,7} " +
        $"{hungerD,6} {ageD,5} {birthsD,6} {refusedD,7} {cur.AgentStdDev,7:F2}{poolCols}");
}

int idle = 0, moving = 0, seeking = 0, harvesting = 0;
for (int i = 0; i < world.AliveCount; i++)
{
    switch (world.GetAgent(i).State)
    {
        case AgentState.Idle: idle++; break;
        case AgentState.Moving: moving++; break;
        case AgentState.Seeking: seeking++; break;
        case AgentState.Harvesting: harvesting++; break;
    }
}

Console.WriteLine();
// Manger n'est plus un etat depuis la session 19c (effet passif, ne
// figure donc plus dans cette repartition d'etats FSM).
Console.WriteLine($"Etats agents (fin de run): Idle={idle} Moving={moving} Seeking={seeking} Harvesting={harvesting}");
Console.WriteLine($"Cueilleurs actifs (Seeking-vers-buisson + Harvesting) : {seeking + harvesting}");

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

// Suspect n°1 du diagnostic de perf (cf. plan) : DistanceToNearestMatureBush
// est O(BushCount) par appel, jusqu'a 2000 points -> jusqu'a ~2000×BushCount
// operations. Chronometre a part pour verifier si c'est bien negligeable
// face au cout total du tick (appelee UNE FOIS en fin de rapport, pas par
// tick).
var clusteringStopwatch = Stopwatch.StartNew();
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
clusteringStopwatch.Stop();

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

// --- Clans (session 18) ---
Console.WriteLine();
Console.WriteLine("--- Clans (session 18) ---");
int[] clanPopulation = new int[world.ClanCount];
for (int i = 0; i < world.AliveCount; i++)
{
    clanPopulation[world.GetAgent(i).ClanId]++;
}

long totalHarvested = 0, totalConsumed = 0;
for (int c = 0; c < world.ClanCount; c++)
{
    Clan clan = world.GetClan(c);
    long harvestedCum = world.GetClanFoodHarvestedCumulative(c);
    long consumedCum = world.GetClanFoodConsumedCumulative(c);
    totalHarvested += harvestedCum;
    totalConsumed += consumedCum;
    Console.WriteLine($"  Clan {c} (espece {clan.Species}) : pop={clanPopulation[c],5} (creux min jamais observe={world.GetClanMinAliveEverObserved(c),5})  pool={clan.FoodPool,6}  " +
        $"recolte cumulee={harvestedCum,8} consommee cumulee={consumedCum,8}  morts faim={world.GetClanHungerDeaths(c),5} morts age={world.GetClanAgeDeaths(c),5}");
}
Console.WriteLine($"  Total : recolte cumulee={totalHarvested} consommee cumulee={totalConsumed}");

// --- Foyers (session foyers) ---
Console.WriteLine();
Console.WriteLine("--- Foyers (session foyers) ---");
int[] nearHomePopulation = new int[world.ClanCount];
for (int i = 0; i < world.AliveCount; i++)
{
    Agent agent = world.GetAgent(i);
    Home agentHome = world.GetHomeById(agent.HomeId);
    double dx = agent.X - (agentHome.X + 0.5);
    double dy = agent.Y - (agentHome.Y + 0.5);
    if (dx * dx + dy * dy <= (double)config.MateSearchRadius * config.MateSearchRadius)
    {
        nearHomePopulation[agent.ClanId]++;
    }
}
for (int c = 0; c < world.HomeCount; c++)
{
    Home home = world.GetHome(c);
    Console.WriteLine($"  Foyer clan {c} : position=({home.X},{home.Y})  pop dans rayon {config.MateSearchRadius}={nearHomePopulation[c],5}/{clanPopulation[c],5}");
}
Console.WriteLine($"  Distance moyenne agent->foyer de son clan : {world.AverageDistanceToHome(),8:F2}");

// --- Territoire (session territoire) ---
Console.WriteLine();
Console.WriteLine("--- Territoire (session territoire) ---");
int largestClanRegions = 0;
for (int c = 0; c < world.ClanCount; c++)
{
    int regions = world.CountRegionsOwnedBy(world.GetClan(c).Id);
    largestClanRegions = Math.Max(largestClanRegions, regions);
    Console.WriteLine($"  Clan {c} : regions={regions,5} / {world.RegionCount,5}");
}
int neutralRegions = world.NeutralRegionCount();
double largestShare = world.RegionCount > 0 ? (double)largestClanRegions / world.RegionCount : 0.0;
Console.WriteLine($"  Regions neutres : {neutralRegions,5} / {world.RegionCount,5}");
Console.WriteLine($"  Part du plus gros clan : {largestShare,6:P1}");

var (outsideAtEnd, aliveAtEnd) = CountAgentsOutsideOwnTerritory(world);
double outsideAtStartPct = aliveAtStart > 0 ? 100.0 * outsideAtStart / aliveAtStart : 0.0;
double outsideAtEndPct = aliveAtEnd > 0 ? 100.0 * outsideAtEnd / aliveAtEnd : 0.0;
Console.WriteLine($"  Agents hors du territoire de leur propre clan a t=0    : {outsideAtStart,5} / {aliveAtStart,5} ({outsideAtStartPct,5:F1}%)");
Console.WriteLine($"  Agents hors du territoire de leur propre clan en fin  : {outsideAtEnd,5} / {aliveAtEnd,5} ({outsideAtEndPct,5:F1}%)");

// Buissons murs ACCESSIBLES par clan (session confinement) : le
// chiffre qui compte maintenant que la recolte est bornee au
// territoire -- un buisson mur existe peut-etre, mais s'il n'est pas
// dans le territoire du clan, il ne nourrit personne.
vegetationCatalog.TryGetId("bush", out byte bushTypeForAccess);
int bushMatureStage = vegetationCatalog.Get(bushTypeForAccess).MatureStage;
int[] accessibleMatureBushes = new int[world.ClanCount];
for (int i = 0; i < world.BushCount; i++)
{
    Vegetation bush = world.GetVegetation(i);
    if (bush.Stage < bushMatureStage)
    {
        continue;
    }
    uint owner = world.GetRegionOwnerAt(bush.X, bush.Y);
    for (int c = 0; c < world.ClanCount; c++)
    {
        if (world.GetClan(c).Id == owner)
        {
            accessibleMatureBushes[c]++;
            break;
        }
    }
}
for (int c = 0; c < world.ClanCount; c++)
{
    Console.WriteLine($"  Clan {c} : buissons murs accessibles={accessibleMatureBushes[c],5}");
}

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
    // Depuis la session 19c, manger n'est plus un etat exclusif (effet
    // passif applique a tout etat) -- Eating peut donc se CHEVAUCHER avec
    // Idle/Moving/Seeking (un Harvesting affame mange sans quitter son
    // etat). Ce n'est plus une partition exclusive du temps de vie, donc
    // les pourcentages ci-dessous ne somment plus a 100%.
    double lifeTicks = world.AverageDeathTicksIdle + world.AverageDeathTicksMoving
        + world.AverageDeathTicksSeeking + world.AverageDeathTicksEating;
    Console.WriteLine($"Echecs de recherche consecutifs (moyenne avant la mort) : {world.AverageDeathFailureStreak:F1}");
    Console.WriteLine($"Faim au dernier repas commence (moyenne)                : {world.AverageDeathHungerAtLastMeal:F1}");
    if (lifeTicks > 0)
    {
        Console.WriteLine($"Repartition du temps de vie (moyenne, chevauchement possible avec Eating) : " +
            $"Idle={100.0 * world.AverageDeathTicksIdle / lifeTicks:F1}% " +
            $"Moving={100.0 * world.AverageDeathTicksMoving / lifeTicks:F1}% " +
            $"Seeking={100.0 * world.AverageDeathTicksSeeking / lifeTicks:F1}% " +
            $"Eating={100.0 * world.AverageDeathTicksEating / lifeTicks:F1}%");
    }
}

Console.WriteLine();
Console.WriteLine($"Hash final: 0x{world.Hash():X16}");

// --- Perf : simulation vs instrumentation (diagnostic, cf. plan) ---
overallStopwatch.Stop();
double totalMs = overallStopwatch.Elapsed.TotalMilliseconds;
double tickMs = tickStopwatch.Elapsed.TotalMilliseconds;
double connectivityMs = connectivityStopwatch.Elapsed.TotalMilliseconds;
double sampleMs = sampleStopwatch.Elapsed.TotalMilliseconds;
double clusteringMs = clusteringStopwatch.Elapsed.TotalMilliseconds;
double otherMs = Math.Max(0, totalMs - tickMs - connectivityMs - sampleMs - clusteringMs);

Console.WriteLine();
Console.WriteLine("--- Perf (diagnostic) ---");
Console.WriteLine($"  Total run                         : {totalMs,10:F0} ms (100,0%)");
Console.WriteLine($"  world.Tick (simulation pure)       : {tickMs,10:F0} ms ({100.0 * tickMs / totalMs,5:F1}%)  -- {tickMs / ticks * 1000.0:F2} us/tick, {tickMs / ticks / Math.Max(1, world.AliveCount) * 1000.0:F3} us/agent/tick (population fin de run)");
Console.WriteLine($"  Connectivite herbe ({connectivitySamples.Count} appels)     : {connectivityMs,10:F0} ms ({100.0 * connectivityMs / totalMs,5:F1}%)");
Console.WriteLine($"  Clusterisation (1 appel, {clusterSamples} points): {clusteringMs,10:F0} ms ({100.0 * clusteringMs / totalMs,5:F1}%)");
Console.WriteLine($"  Echantillonnage ({samples.Count} appels)        : {sampleMs,10:F0} ms ({100.0 * sampleMs / totalMs,5:F1}%)");
Console.WriteLine($"  Autre (rapport final, etc.)        : {otherMs,10:F0} ms ({100.0 * otherMs / totalMs,5:F1}%)");
