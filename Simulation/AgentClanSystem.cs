namespace Simulation;

// Refactor : dernier système extrait de World.cs (étape 5/10, lots
// vérifiés). Agents et clans sont extraits ENSEMBLE, pas séparément :
// un agent appartient toujours à un clan, la reproduction/récolte
// sont des décisions d'agent qui écrivent dans le pool du clan -- ce
// couplage est réel (domaine), pas un accident d'implémentation à
// corriger en le séparant artificiellement. _tickCounter et tout état
// partagé de World restent des paramètres, jamais dupliqués ici.
public sealed class AgentClanSystem
{
    private readonly Agent[] _agents;
    private readonly List<int>[] _agentPaths;
    private uint _nextAgentId;

    // Buffer de travail pour la recherche BFS : entièrement écrasé (via
    // generation-stamp) à chaque appel, jamais lu entre deux appels.
    // Exclu de Hash() volontairement (cf. CLAUDE.md, Déterminisme).
    private readonly int _maxSearchRadius;
    private readonly int _boxSide;
    private readonly int[] _searchGeneration;
    private readonly int[] _searchCameFrom;
    private readonly List<int> _searchQueue;
    private int _searchGenerationCounter;

    private readonly int[] _deathsByCause = new int[2];

    // Clans (session 18) : capacité FIXE (pas de scission/fusion/
    // suppression, donc pas de compaction réelle). Référence par Id
    // stable via _clanIndexById (identité aujourd'hui, câblée pour
    // rester correcte le jour où les scissions arrivent).
    private readonly Clan[] _clans;
    private readonly int[] _clanIndexById;
    private uint _nextClanId;

    // Foyers (session foyers) : un par clan, même patron de capacité
    // fixe + indirection par Id stable que Clan[] ci-dessus.
    private readonly Home[] _homes;
    private readonly int[] _homeIndexById;
    private uint _nextHomeId;

    // Diagnostic par clan : exclus de Hash() sauf FoodPool/ClanId
    // (déjà couverts via le bloc clan et le bloc agent).
    private readonly int[] _clanHungerDeaths;
    private readonly int[] _clanAgeDeaths;
    private readonly long[] _clanFoodHarvestedCumulative;
    private readonly long[] _clanFoodConsumedCumulative;
    private readonly int[] _clanMinAliveEverObserved;
    private readonly int[] _clanPopulation;

    // Grille grossière d'agents (session 14) : reconstruite à chaque
    // tick réel, scratch pur -- exclue de Hash().
    private readonly int _agentGridCellSize;
    private readonly int _agentGridWidth;
    private readonly int _agentGridHeight;
    private readonly int[] _agentGridCellCounts;
    private readonly int[] _agentGridCellStart;
    private readonly int[] _agentGridEntries;

    // Diagnostic (comme MealsEaten) : exclus de Hash().
    private int _birthsTotal;
    private int _birthsRefusedArrayFull;
    private int _birthsLostToUnsafeTile;
    private int _minAliveCountEverObserved = int.MaxValue;

    private static readonly double[] DeathDistanceBucketBounds = { 5, 10, 15, 20, 25, 33, 50, 100, 200 };
    private readonly int[] _deathDistanceHistogram = new int[DeathDistanceBucketBounds.Length + 1];
    private readonly int[] _deathTerrainHistogram = new int[256];
    private readonly int[] _deathSeekOutcomeHistogram = new int[4];
    private int _hungerDeathsWhileHarvesting;
    private long _deathFailureStreakSum;
    private long _deathTicksIdleSum;
    private long _deathTicksMovingSum;
    private long _deathTicksSeekingSum;
    private long _deathTicksEatingSum;
    private long _deathHungerAtLastMealSum;

    public Agent[] Agents => _agents;

    public List<int>[] AgentPaths => _agentPaths;

    public Clan[] Clans => _clans;

    public int[] ClanIndexById => _clanIndexById;

    public int[] ClanHungerDeaths => _clanHungerDeaths;

    public int[] ClanAgeDeaths => _clanAgeDeaths;

    public long[] ClanFoodHarvestedCumulative => _clanFoodHarvestedCumulative;

    public long[] ClanFoodConsumedCumulative => _clanFoodConsumedCumulative;

    public int[] ClanMinAliveEverObserved => _clanMinAliveEverObserved;

    public int[] ClanPopulation => _clanPopulation;

    public int AgentGridCellSize => _agentGridCellSize;

    public int AgentGridWidth => _agentGridWidth;

    public int AgentGridHeight => _agentGridHeight;

    public int[] AgentGridCellCounts => _agentGridCellCounts;

    public int[] AgentGridCellStart => _agentGridCellStart;

    public int[] AgentGridEntries => _agentGridEntries;

    public int MaxSearchRadius => _maxSearchRadius;

    public int BoxSide => _boxSide;

    public int[] SearchGeneration => _searchGeneration;

    public int[] SearchCameFrom => _searchCameFrom;

    public List<int> SearchQueue => _searchQueue;

    public int SearchGenerationCounter { get => _searchGenerationCounter; set => _searchGenerationCounter = value; }

    public int[] DeathsByCause => _deathsByCause;

    public int AgentCapacity => _agents.Length;

    public int AliveCount { get; set; }

    public bool AgentSpawnCapped { get; set; }

    public long MealsEaten { get; set; }

    public int ClanCount => _clans.Length;

    public Home[] Homes => _homes;

    public int[] HomeIndexById => _homeIndexById;

    public int HomeCount => _homes.Length;

    public uint NextHomeId { get => _nextHomeId; set => _nextHomeId = value; }

    public uint NextAgentId { get => _nextAgentId; set => _nextAgentId = value; }

    public uint NextClanId { get => _nextClanId; set => _nextClanId = value; }

    public int BirthsTotal { get => _birthsTotal; set => _birthsTotal = value; }

    public int BirthsRefusedArrayFull { get => _birthsRefusedArrayFull; set => _birthsRefusedArrayFull = value; }

    public int BirthsLostToUnsafeTile { get => _birthsLostToUnsafeTile; set => _birthsLostToUnsafeTile = value; }

    public int MinAliveCountEverObserved { get => _minAliveCountEverObserved; set => _minAliveCountEverObserved = value; }

    public static IReadOnlyList<double> DeathDistanceBucketUpperBounds => DeathDistanceBucketBounds;

    public static double[] DeathDistanceBucketBoundsArray => DeathDistanceBucketBounds;

    public int[] DeathDistanceHistogram => _deathDistanceHistogram;

    public int[] DeathTerrainHistogram => _deathTerrainHistogram;

    public int[] DeathSeekOutcomeHistogram => _deathSeekOutcomeHistogram;

    public int HungerDeathsWhileHarvesting { get => _hungerDeathsWhileHarvesting; set => _hungerDeathsWhileHarvesting = value; }

    public long DeathFailureStreakSum { get => _deathFailureStreakSum; set => _deathFailureStreakSum = value; }

    public long DeathTicksIdleSum { get => _deathTicksIdleSum; set => _deathTicksIdleSum = value; }

    public long DeathTicksMovingSum { get => _deathTicksMovingSum; set => _deathTicksMovingSum = value; }

    public long DeathTicksSeekingSum { get => _deathTicksSeekingSum; set => _deathTicksSeekingSum = value; }

    public long DeathTicksEatingSum { get => _deathTicksEatingSum; set => _deathTicksEatingSum = value; }

    public long DeathHungerAtLastMealSum { get => _deathHungerAtLastMealSum; set => _deathHungerAtLastMealSum = value; }

    // Garde-fous techniques (pas des réglages de gameplay) : bornent le
    // rejection sampling sur une carte quasi dégénérée.
    private const int MaxSpawnAttemptsPerAgent = 10;
    private const int MaxClusterCenterAttempts = 100;

    private readonly int _size;
    private readonly SimulationConfig _config;
    private readonly Catalog<TerrainType> _catalog;
    private readonly Catalog<SpeciesType> _speciesCatalog;
    private readonly Catalog<VegetationType> _vegetationCatalog;
    private readonly byte _bushTypeId;
    private readonly TerrainSystem _terrainSystem;
    private readonly Rng _rngWorldGen;
    private readonly Rng _rngAgents;

    // Résout le cycle Agent↔Vegetation : VegetationSystem a besoin des
    // dimensions de la grille agent (ci-dessus) pour se construire,
    // mais le mouvement/BFS agent a besoin d'une référence VIVANTE vers
    // VegetationSystem (FoodGradient, Bushes -- mutés après
    // construction). Construction en deux temps, même patron prévu
    // pour Agent↔Clan à l'origine du plan : World construit
    // AgentClanSystem, lit ses dimensions de grille, construit
    // VegetationSystem, puis appelle AttachVegetationSystem UNE FOIS,
    // avant tout Tick(). Aucune méthode de mouvement n'est appelée
    // entre la construction et cet attachement.
    private VegetationSystem? _vegetationSystem;

    public void AttachVegetationSystem(VegetationSystem vegetationSystem) => _vegetationSystem = vegetationSystem;

    // Même patron pour le cycle Agent↔Territoire (session territoire,
    // confinement) : TerritorySystem a besoin d'AgentClanSystem
    // (population, foyers) pour se construire, mais la recherche/
    // errance agent a besoin d'une référence VIVANTE vers
    // TerritorySystem (propriétaire de région, mis à jour au tick
    // territoire). World construit AgentClanSystem, puis
    // TerritorySystem, puis appelle AttachTerritorySystem UNE FOIS,
    // avant tout Tick().
    private TerritorySystem? _territorySystem;

    public void AttachTerritorySystem(TerritorySystem territorySystem) => _territorySystem = territorySystem;

    public AgentClanSystem(int size, SimulationConfig config, Catalog<TerrainType> catalog, Catalog<SpeciesType> speciesCatalog,
        Catalog<VegetationType> vegetationCatalog, TerrainSystem terrainSystem, Rng rngWorldGen, Rng rngAgents)
    {
        _size = size;
        _config = config;
        _catalog = catalog;
        _speciesCatalog = speciesCatalog;
        _vegetationCatalog = vegetationCatalog;
        _terrainSystem = terrainSystem;
        _rngWorldGen = rngWorldGen;
        _rngAgents = rngAgents;

        if (!vegetationCatalog.TryGetId("bush", out _bushTypeId))
        {
            throw new ArgumentException("vegetation catalog must define bush", nameof(vegetationCatalog));
        }

        int searchBoxSide = 2 * config.MaxFoodSearchRadius + 1;
        _searchQueue = new List<int>(searchBoxSide * searchBoxSide);

        _maxSearchRadius = config.MaxFoodSearchRadius;
        _boxSide = _maxSearchRadius * 2 + 1;
        _searchGeneration = new int[_boxSide * _boxSide];
        _searchCameFrom = new int[_boxSide * _boxSide];

        int initialPopulation = (int)(config.AgentDensity * size * size);
        int agentCapacity = initialPopulation * config.AgentCapacityMultiplier;
        _agents = new Agent[agentCapacity];
        _agentPaths = new List<int>[_agents.Length];
        for (int i = 0; i < _agentPaths.Length; i++)
        {
            _agentPaths[i] = new List<int>();
        }

        _agentGridCellSize = Math.Max(1, config.MateSearchRadius);
        _agentGridWidth = (size + _agentGridCellSize - 1) / _agentGridCellSize;
        _agentGridHeight = _agentGridWidth;
        int cellCount = _agentGridWidth * _agentGridHeight;
        _agentGridCellCounts = new int[cellCount];
        _agentGridCellStart = new int[cellCount + 1];
        _agentGridEntries = new int[agentCapacity];

        _clans = CreateClans(config.InitialClanCount, speciesCatalog);
        _clanIndexById = new int[_clans.Length];
        for (int i = 0; i < _clanIndexById.Length; i++)
        {
            _clanIndexById[i] = i;
        }
        _clanHungerDeaths = new int[_clans.Length];
        _clanAgeDeaths = new int[_clans.Length];
        _clanFoodHarvestedCumulative = new long[_clans.Length];
        _clanFoodConsumedCumulative = new long[_clans.Length];
        _clanMinAliveEverObserved = new int[_clans.Length];
        Array.Fill(_clanMinAliveEverObserved, int.MaxValue);
        _clanPopulation = new int[_clans.Length];

        // Un foyer par clan (capacité fixe, même raisonnement que
        // _clans ci-dessus). Id/ClanId assignés ICI, inconditionnellement
        // (comme CreateClans) -- l'identité _homeIndexById[i]==i doit
        // tenir même si un clan échoue à trouver un centre de grappe
        // (SpawnAgents ci-dessous ne fait alors que remplir X/Y, jamais
        // sauter la création elle-même). Position (X, Y) posée à la
        // même passe que le centre de grappe du clan dans SpawnAgents --
        // pas de second tirage RNG.
        _homes = new Home[_clans.Length];
        _homeIndexById = new int[_clans.Length];
        for (int i = 0; i < _homes.Length; i++)
        {
            _homes[i] = new Home { Id = _nextHomeId++, ClanId = _clans[i].Id, X = 0, Y = 0 };
            _homeIndexById[i] = i;
        }

        SpawnAgents(initialPopulation);
    }

    // Un clan par race disponible, cyclé (une seule race aujourd'hui,
    // donc tous les clans démarrent avec la même -- reste conforme à
    // "un clan = une race", qui n'exige pas l'inverse).
    private Clan[] CreateClans(int count, Catalog<SpeciesType> speciesCatalog)
    {
        var clans = new Clan[Math.Max(1, count)];
        int speciesCount = Math.Max(1, speciesCatalog.Count);
        for (int i = 0; i < clans.Length; i++)
        {
            clans[i] = new Clan
            {
                Id = _nextClanId++,
                ParentClanId = -1,
                Species = (byte)(i % speciesCount),
                FoodPool = 0,
            };
        }

        return clans;
    }

    public int ClanIndex(uint id) => _clanIndexById[id];

    public ref Clan GetClanById(uint id) => ref _clans[ClanIndex(id)];

    public int HomeIndex(uint id) => _homeIndexById[id];

    public ref Home GetHomeById(uint id) => ref _homes[HomeIndex(id)];

    public Home GetHome(int index) => _homes[index];

    // Diagnostic (session foyers) : distance euclidienne moyenne entre
    // chaque agent vivant et le foyer de son clan -- mesure la
    // clusterisation réelle produite par l'ancrage, jamais lue par une
    // décision. Lecture pure, exclue de Hash() (même statut que
    // AgentDensityStdDev/DistanceToNearestMatureBush).
    public double AverageDistanceToHome()
    {
        if (AliveCount == 0)
        {
            return 0.0;
        }

        double sum = 0.0;
        for (int i = 0; i < AliveCount; i++)
        {
            ref Agent agent = ref _agents[i];
            Home home = GetHomeById(agent.HomeId);
            double dx = agent.X - (home.X + 0.5);
            double dy = agent.Y - (home.Y + 0.5);
            sum += Math.Sqrt(dx * dx + dy * dy);
        }
        return sum / AliveCount;
    }

    private void SpawnAgents(int count)
    {
        int clanCount = _clans.Length;
        int perClan = count / clanCount;
        int remainder = count - perClan * clanCount;
        double radius = _size * _config.ClanSpawnRadiusFraction;

        int spawned = 0;

        for (int c = 0; c < clanCount && spawned < _agents.Length; c++)
        {
            int clanTarget = perClan + (c < remainder ? 1 : 0);
            if (clanTarget == 0 || !TryPickClusterCenter(out int centerX, out int centerY))
            {
                continue;
            }

            // Position du foyer du clan : même point que le centre de
            // grappe utilisé pour le spawn groupé ci-dessus, aucun
            // tirage RNG supplémentaire. Id/ClanId déjà posés au
            // constructeur (identité de _homeIndexById préservée même
            // si ce clan échoue à trouver un centre).
            _homes[c].X = centerX;
            _homes[c].Y = centerY;

            SpeciesType species = _speciesCatalog.Get(_clans[c].Species);
            int clanSpawned = 0;
            int attempts = 0;
            int maxAttempts = clanTarget * MaxSpawnAttemptsPerAgent;

            while (clanSpawned < clanTarget && attempts < maxAttempts && spawned < _agents.Length)
            {
                attempts++;

                double dx = (_rngWorldGen.NextDouble() * 2.0 - 1.0) * radius;
                double dy = (_rngWorldGen.NextDouble() * 2.0 - 1.0) * radius;
                if (dx * dx + dy * dy > radius * radius)
                {
                    continue;
                }

                int x = centerX + (int)dx;
                int y = centerY + (int)dy;
                if (x < 0 || x >= _size || y < 0 || y >= _size || !_catalog.Get(_terrainSystem.Terrain[y * _size + x]).Walkable)
                {
                    continue;
                }

                uint lifespan = RollLifespan(species);

                _agents[spawned] = new Agent
                {
                    Id = _nextAgentId++,
                    X = x + 0.5f,
                    Y = y + 0.5f,
                    TargetX = x,
                    TargetY = y,
                    MotherId = Agent.UnknownParent,
                    FatherId = Agent.UnknownParent,
                    Tracked = false,
                    State = AgentState.Idle,
                    Species = _clans[c].Species,
                    ClanId = _clans[c].Id,
                    HomeId = _homes[c].Id,
                    // Étalée sur [0, HungerSeekThreshold) -- même
                    // raisonnement que l'âge de départ étalé ci-dessous
                    // (s11/s14) : Hunger=0 pour tout le monde ferait
                    // franchir le seuil de repas EN MÊME TEMPS à toute
                    // la population initiale, vidant le pool du clan en
                    // une seule rafale synchronisée au lieu d'un flux
                    // lissé dans le temps (découvert en s18 : sans ça,
                    // toute la population initiale meurt vers le tick
                    // 1500, le pool encaisse la rafale puis reste à sec).
                    Hunger = (byte)(_rngAgents.NextDouble() * _config.HungerSeekThreshold),
                    Facing = 0,
                    SeekCooldown = 0,
                    WanderDirection = 0,
                    WanderTicksRemaining = 0,
                    // Âge de départ étalé sur toute l'espérance de vie de CET
                    // agent (pas 0 pour tout le monde) : sans ça, les agents
                    // initiaux meurent tous ensemble, et leurs enfants aussi
                    // -- exactement le piège de vague de cohorte des arbres
                    // (s11), transposé aux agents.
                    Age = (uint)(_rngAgents.NextDouble() * lifespan),
                    LifespanTicks = lifespan,
                    Sex = (byte)(_rngAgents.NextDouble() < 0.5 ? 0 : 1),
                    PregnantUntil = 0,
                    PendingFatherId = Agent.UnknownParent,
                    CauseOfDeath = 0,
                    SearchFailureStreak = 0,
                    TicksIdle = 0,
                    TicksMoving = 0,
                    TicksSeeking = 0,
                    TicksEating = 0,
                    HungerAtLastMealStart = 0,
                    LastSeekOutcome = SeekOutcome.NeverSearched,
                };
                spawned++;
                clanSpawned++;
            }

            // Un clan neuf démarre avec un pool ÉTABLI, pas vide (même
            // esprit que SeedInitialVegetation, s15) : tous les agents
            // spawnent avec Hunger=0 simultanément, donc ils franchiront
            // le seuil de faim au même moment -- sans réserve de départ,
            // toute la population initiale meurt avant qu'un premier
            // cueilleur n'ait eu le temps de faire l'aller-retour
            // (~165 ticks, cf. plan). Démarre exactement au niveau cible
            // par tête pour cette population.
            _clans[c].FoodPool = (int)(_config.TargetFoodPoolPerCapita * clanSpawned);
        }

        AliveCount = spawned;
        AgentSpawnCapped = spawned < count;
    }

    private bool TryPickClusterCenter(out int x, out int y)
    {
        for (int attempt = 0; attempt < MaxClusterCenterAttempts; attempt++)
        {
            int candidateX = (int)(_rngWorldGen.NextDouble() * _size);
            int candidateY = (int)(_rngWorldGen.NextDouble() * _size);
            if (_catalog.Get(_terrainSystem.Terrain[candidateY * _size + candidateX]).Walkable)
            {
                x = candidateX;
                y = candidateY;
                return true;
            }
        }

        x = 0;
        y = 0;
        return false;
    }

    // Grille grossière d'agents (session 14) : reconstruite en un seul
    // passage O(AliveCount) au début de chaque tick réel, avant que les
    // agents ne pensent -- ils la consultent (recherche de partenaire,
    // densité locale) mais ne la modifient jamais pendant leur propre
    // tick de pensée (staleness d'un tick, acceptable, même esprit que
    // le snapshot de végétation pour la diffusion).
    public void RebuildAgentGrid()
    {
        Array.Clear(_agentGridCellCounts);
        Array.Clear(_clanPopulation);

        for (int i = 0; i < AliveCount; i++)
        {
            ref Agent agent = ref _agents[i];
            _agentGridCellCounts[AgentCellIndex(agent.X, agent.Y)]++;
            _clanPopulation[ClanIndex(agent.ClanId)]++;
        }

        _agentGridCellStart[0] = 0;
        for (int c = 0; c < _agentGridCellCounts.Length; c++)
        {
            _agentGridCellStart[c + 1] = _agentGridCellStart[c] + _agentGridCellCounts[c];
        }

        // Réutilisé comme curseur d'écriture par cellule (repart de 0).
        Array.Clear(_agentGridCellCounts);
        for (int i = 0; i < AliveCount; i++)
        {
            ref Agent agent = ref _agents[i];
            int cell = AgentCellIndex(agent.X, agent.Y);
            int slot = _agentGridCellStart[cell] + _agentGridCellCounts[cell];
            _agentGridEntries[slot] = i;
            _agentGridCellCounts[cell]++;
        }
    }

    public int AgentCountInCell(int cell) => _agentGridCellStart[cell + 1] - _agentGridCellStart[cell];

    public int AgentCellIndex(float x, float y)
    {
        int cellX = Math.Clamp((int)(x / _agentGridCellSize), 0, _agentGridWidth - 1);
        int cellY = Math.Clamp((int)(y / _agentGridCellSize), 0, _agentGridHeight - 1);
        return cellY * _agentGridWidth + cellX;
    }

    public void SetWaypoint(ref Agent agent, int waypointIndex)
    {
        int targetX = waypointIndex % _size;
        int targetY = waypointIndex / _size;
        float targetCenterX = targetX + 0.5f;

        if (targetCenterX != agent.X)
        {
            agent.Facing = (byte)(targetCenterX < agent.X ? 1 : 0);
        }

        agent.TargetX = targetX;
        agent.TargetY = targetY;
    }

    // Suit le champ de nourriture diffusé (session 14c) : lit la
    // cellule courante et ses 4 voisines cardinales, avance d'une
    // tuile vers celle dont la valeur est la plus haute. Coût O(1) --
    // pas de BFS. Retourne false si aucune voisine ne dépasse la
    // cellule courante (gradient plat, région jamais atteinte par la
    // diffusion) ou si la tuile visée n'est pas franchissable :
    // l'appelant retombe alors sur l'errance dirigée existante.
    public bool TryFollowFoodGradient(ref Agent agent)
    {
        int currentX = (int)MathF.Floor(agent.X);
        int currentY = (int)MathF.Floor(agent.Y);
        int cell = AgentCellIndex(agent.X, agent.Y);
        int cellX = cell % _agentGridWidth;
        int cellY = cell / _agentGridWidth;

        double[] foodGradient = _vegetationSystem!.FoodGradient;
        double bestValue = foodGradient[cell];
        int bestDx = 0;
        int bestDy = 0;
        bool found = false;

        if (cellX > 0 && foodGradient[cell - 1] > bestValue)
        {
            bestValue = foodGradient[cell - 1];
            bestDx = -1;
            bestDy = 0;
            found = true;
        }
        if (cellX < _agentGridWidth - 1 && foodGradient[cell + 1] > bestValue)
        {
            bestValue = foodGradient[cell + 1];
            bestDx = 1;
            bestDy = 0;
            found = true;
        }
        if (cellY > 0 && foodGradient[cell - _agentGridWidth] > bestValue)
        {
            bestValue = foodGradient[cell - _agentGridWidth];
            bestDx = 0;
            bestDy = -1;
            found = true;
        }
        if (cellY < _agentGridHeight - 1 && foodGradient[cell + _agentGridWidth] > bestValue)
        {
            bestValue = foodGradient[cell + _agentGridWidth];
            bestDx = 0;
            bestDy = 1;
            found = true;
        }

        if (!found)
        {
            return false;
        }

        int targetX = currentX + bestDx;
        int targetY = currentY + bestDy;

        if (targetX < 0 || targetX >= _size || targetY < 0 || targetY >= _size ||
            !_catalog.Get(_terrainSystem.Terrain[targetY * _size + targetX]).Walkable ||
            IsRivalTerritory(targetX, targetY, agent.ClanId))
        {
            return false;
        }

        if (bestDx != 0)
        {
            agent.Facing = (byte)(bestDx < 0 ? 1 : 0);
        }

        agent.TargetX = targetX;
        agent.TargetY = targetY;
        agent.State = AgentState.Moving;

        // Un retour ultérieur à l'errance pure repart d'un tirage
        // frais, pas d'un vieux compteur de persistance directionnelle.
        agent.WanderTicksRemaining = 0;
        return true;
    }

    // Errance dirigée (session 13) : conserve une direction sur plusieurs
    // essais (marche corrélée, déplacement net linéaire en N) plutôt que
    // de retirer une direction à chaque pas (marche aléatoire pure, √N —
    // incapable d'échapper à une zone stérile plus large que quelques
    // tuiles, cf. CLAUDE.md).
    public void TryStartMoving(ref Agent agent)
    {
        int currentX = (int)MathF.Floor(agent.X);
        int currentY = (int)MathF.Floor(agent.Y);

        bool keepDirection = agent.WanderTicksRemaining > 0 && _rngAgents.NextDouble() >= _config.WanderTurnChance;

        int direction;
        if (keepDirection)
        {
            direction = agent.WanderDirection;
            agent.WanderTicksRemaining--;
        }
        else
        {
            // Ancrage foyer (session foyers) : une nouvelle direction a
            // une chance HomeAnchorChance d'être choisie vers le foyer
            // plutôt qu'uniformément au hasard -- une TENDANCE, jamais
            // une contrainte : ce tirage ne touche que la marche
            // d'errance de secours (aucun effet sur Seeking/Harvesting,
            // cf. CLAUDE.md section Social). Si l'agent est déjà sur la
            // tuile de son foyer (dx=dy=0), rien à biaiser -- retombe
            // sur le tirage uniforme.
            if (agent.HomeId != Home.NoHome && _rngAgents.NextDouble() < _config.HomeAnchorChance)
            {
                Home home = GetHomeById(agent.HomeId);
                int dxHome = home.X - currentX;
                int dyHome = home.Y - currentY;
                direction = dxHome == 0 && dyHome == 0
                    ? (int)(_rngAgents.NextUInt64() >> 62)
                    : Math.Abs(dxHome) >= Math.Abs(dyHome)
                        ? (dxHome < 0 ? 0 : 1)
                        : (dyHome < 0 ? 2 : 3);
            }
            else
            {
                direction = (int)(_rngAgents.NextUInt64() >> 62);
            }

            agent.WanderDirection = (byte)direction;
            agent.WanderTicksRemaining = _config.WanderPersistenceTicks;
        }

        int dx = direction == 0 ? -1 : direction == 1 ? 1 : 0;
        int dy = direction == 2 ? -1 : direction == 3 ? 1 : 0;

        int targetX = currentX + dx;
        int targetY = currentY + dy;

        if (targetX < 0 || targetX >= _size || targetY < 0 || targetY >= _size ||
            !_catalog.Get(_terrainSystem.Terrain[targetY * _size + targetX]).Walkable ||
            IsRivalTerritory(targetX, targetY, agent.ClanId))
        {
            // Direction bloquée (bord de carte / obstacle / territoire
            // rival, session territoire -- confinement) : force un
            // nouveau tirage au prochain essai plutôt que de re-cogner
            // indéfiniment contre le même mur (même esprit que le fix
            // anti-gel du cooldown de faim, s10).
            agent.WanderTicksRemaining = 0;
            return;
        }

        if (dx != 0)
        {
            agent.Facing = (byte)(dx < 0 ? 1 : 0);
        }

        agent.TargetX = targetX;
        agent.TargetY = targetY;
        agent.State = AgentState.Moving;
    }

    // Confinement (session territoire, révisé après mesure) : le
    // MOUVEMENT (errance, suivi de gradient) reste possible en terrain
    // NEUTRE, bloqué seulement en territoire RIVAL -- pas "doit être
    // mon clan strictement". Une région change de main à chaque tick
    // territoire (re-semé depuis la population courante, sans
    // mémoire) ; une confinement strict à "mon clan uniquement"
    // échouait empiriquement : un agent debout sur une région qui
    // vient de repasser neutre (aucun cas rare -- observé sur un run
    // ordinaire) n'avait plus AUCUNE case candidate valide et restait
    // figé en permanence. La RESSOURCE (récolte, TryEnqueue ci-dessous)
    // reste strictement bornée au territoire du clan -- c'est elle qui
    // porte "les ressources ne sont accessibles que dans le territoire
    // du clan", pas le simple déplacement.
    private bool IsRivalTerritory(int x, int y, uint clanId)
    {
        uint owner = _territorySystem!.GetRegionOwnerAt(x, y);
        return owner != TerritorySystem.NoOwner && owner != clanId;
    }

    public bool TryFindNearestMatureBush(int startX, int startY, uint clanId, List<int> outputPath)
    {
        outputPath.Clear();
        _searchGenerationCounter++;
        _searchQueue.Clear();

        int originX = startX - _maxSearchRadius;
        int originY = startY - _maxSearchRadius;
        int startLocal = (startY - originY) * _boxSide + (startX - originX);

        _searchGeneration[startLocal] = _searchGenerationCounter;
        _searchCameFrom[startLocal] = -1;
        _searchQueue.Add(startLocal);

        int matureStage = _vegetationCatalog.Get(_bushTypeId).MatureStage;

        int head = 0;
        while (head < _searchQueue.Count)
        {
            int current = _searchQueue[head];
            head++;

            int lx = current % _boxSide;
            int ly = current / _boxSide;
            int worldX = originX + lx;
            int worldY = originY + ly;

            int bushSlot = _vegetationSystem!.BushIndexAt[worldY * _size + worldX];
            if (bushSlot != -1 && _vegetationSystem.Bushes[bushSlot].Stage >= matureStage)
            {
                ReconstructPath(current, originX, originY, outputPath);
                return true;
            }

            TryEnqueue(lx - 1, ly, current, originX, originY, clanId);
            TryEnqueue(lx + 1, ly, current, originX, originY, clanId);
            TryEnqueue(lx, ly - 1, current, originX, originY, clanId);
            TryEnqueue(lx, ly + 1, current, originX, originY, clanId);
        }

        return false;
    }

    private void TryEnqueue(int lx, int ly, int fromLocal, int originX, int originY, uint clanId)
    {
        if (lx < 0 || lx >= _boxSide || ly < 0 || ly >= _boxSide)
        {
            return;
        }

        int worldX = originX + lx;
        int worldY = originY + ly;
        if (worldX < 0 || worldX >= _size || worldY < 0 || worldY >= _size)
        {
            return;
        }

        if (!_catalog.Get(_terrainSystem.Terrain[worldY * _size + worldX]).Walkable)
        {
            return;
        }

        // Confinement territorial (session territoire) : un cueilleur
        // ne peut jamais quitter les régions possédées par son clan --
        // la ressource n'est accessible que dans le territoire.
        if (_territorySystem!.GetRegionOwnerAt(worldX, worldY) != clanId)
        {
            return;
        }

        int local = ly * _boxSide + lx;
        if (_searchGeneration[local] == _searchGenerationCounter)
        {
            return;
        }

        _searchGeneration[local] = _searchGenerationCounter;
        _searchCameFrom[local] = fromLocal;
        _searchQueue.Add(local);
    }

    private void ReconstructPath(int endLocal, int originX, int originY, List<int> outputPath)
    {
        int node = endLocal;
        while (_searchCameFrom[node] != -1)
        {
            int lx = node % _boxSide;
            int ly = node / _boxSide;
            outputPath.Add((originY + ly) * _size + (originX + lx));
            node = _searchCameFrom[node];
        }
    }

    public void ApplyPassiveEating(ref Agent agent)
    {
        if (agent.Hunger == 0)
        {
            return;
        }

        ref Clan clan = ref GetClanById(agent.ClanId);
        int amount = Math.Min(_config.HungerDecreasePerEatTick, Math.Min((int)agent.Hunger, clan.FoodPool));

        if (amount > 0)
        {
            agent.HungerAtLastMealStart = agent.Hunger;
            clan.FoodPool -= amount;
            agent.Hunger = (byte)Math.Max(0, agent.Hunger - amount);
            _clanFoodConsumedCumulative[ClanIndex(agent.ClanId)] += amount;
            agent.TicksEating++;
            MealsEaten++;
        }
    }

    // Population utilisée pour calculer le pool CIBLE du clan (récolte,
    // frein de reproduction) -- plafonnée à ReferenceClanPopulation
    // pour qu'un clan qui dépasse cette taille ressente une vraie
    // pression de rareté au lieu de voir son objectif grandir
    // indéfiniment avec lui.
    public int ClanPopulationForTarget(uint clanId)
    {
        int actual = Math.Max(1, _clanPopulation[ClanIndex(clanId)]);
        return Math.Min(actual, _config.ReferenceClanPopulation);
    }

    public void TryStartHarvesting(ref Agent agent, int index)
    {
        if (agent.SeekCooldown > 0)
        {
            agent.SeekCooldown--;
            return;
        }

        ref Clan clan = ref GetClanById(agent.ClanId);
        int clanPopulation = ClanPopulationForTarget(agent.ClanId);
        double targetPool = _config.TargetFoodPoolPerCapita * clanPopulation;
        double emptiness = targetPool > 0 ? 1.0 - Math.Clamp(clan.FoodPool / targetPool, 0.0, 1.0) : 1.0;
        double harvestChance = _config.BaseHarvestChance * emptiness;

        if (_rngAgents.NextDouble() >= harvestChance)
        {
            return;
        }

        int currentX = (int)MathF.Floor(agent.X);
        int currentY = (int)MathF.Floor(agent.Y);

        if (TryFindNearestMatureBush(currentX, currentY, agent.ClanId, _agentPaths[index]))
        {
            List<int> path = _agentPaths[index];
            if (path.Count == 0)
            {
                agent.State = AgentState.Harvesting;
                agent.SeekCooldown = 0;
                agent.SearchFailureStreak = 0;
                agent.LastSeekOutcome = SeekOutcome.FoundBush;
            }
            else
            {
                SetWaypoint(ref agent, path[^1]);
                path.RemoveAt(path.Count - 1);
                agent.State = AgentState.Seeking;
                agent.SearchFailureStreak = 0;
                agent.LastSeekOutcome = SeekOutcome.FoundBush;
            }
            return;
        }

        agent.SeekCooldown = _config.SeekFailureCooldownThinkTicks;
        agent.SearchFailureStreak++;

        // Le BFS local a échoué : même repli sur le gradient de
        // nourriture qu'avant (s14c), inchangé -- seul le contexte
        // d'appel (récolte pour le clan, pas repas individuel) diffère.
        if (TryFollowFoodGradient(ref agent))
        {
            agent.LastSeekOutcome = SeekOutcome.FollowingGradient;
        }
        else
        {
            TryStartMoving(ref agent);
            agent.LastSeekOutcome = SeekOutcome.BlindWander;
        }
    }

    public void HarvestTick(ref Agent agent, int tickCounter)
    {
        int index = agent.TargetY * _size + agent.TargetX;
        int slot = _vegetationSystem!.BushIndexAt[index];

        if (slot == -1)
        {
            // Le buisson a disparu (vidé ou brûlé) pendant que l'agent
            // récoltait déjà (concurrence sans réservation, hors scope).
            agent.State = AgentState.Idle;
            return;
        }

        int harvested = Math.Min(_config.HarvestAmountPerTick, _vegetationSystem.Bushes[slot].FoodRemaining);
        _vegetationSystem.Bushes[slot].FoodRemaining -= harvested;

        ref Clan clan = ref GetClanById(agent.ClanId);
        clan.FoodPool += harvested;
        _clanFoodHarvestedCumulative[ClanIndex(agent.ClanId)] += harvested;

        if (_vegetationSystem.Bushes[slot].FoodRemaining <= 0)
        {
            _vegetationSystem.RemoveBushAt(slot, tickCounter);
            agent.State = AgentState.Idle;
            return;
        }

        // S'arrête quand le pool cible du clan est atteint -- un
        // cueilleur ne vide pas systématiquement tout le buisson si le
        // clan n'a plus besoin de plus, laissant le reste pour plus
        // tard/un autre cueilleur (le buisson n'est pas réservé, cf.
        // matrice d'interaction).
        int clanPopulation = ClanPopulationForTarget(agent.ClanId);
        if (clan.FoodPool >= _config.TargetFoodPoolPerCapita * clanPopulation)
        {
            agent.State = AgentState.Idle;
        }
    }

    // Reproduction (session 14) : rencontre par RAYON, pas par adjacence
    // -- aucun déplacement, aucun nouvel état FSM, aucune réservation de
    // partenaire. Le frein est PROGRESSIF (jamais un seuil dur) pour
    // éviter les dents de scie boom/famine/effondrement : la chance de
    // conception décroît linéairement avec la nourriture locale
    // rapportée à la population locale.
    public void TryReproduce(ref Agent agent, int index, int tickCounter)
    {
        if (agent.Sex != 0 || agent.PregnantUntil != 0)
        {
            return;
        }

        SpeciesType species = _speciesCatalog.Get(agent.Species);
        if (agent.Age < species.MaturityAge || agent.Hunger >= _config.HungerSeekThreshold)
        {
            return;
        }

        if (!TryFindMate(index, out int maleIndex))
        {
            return;
        }

        int cell = AgentCellIndex(agent.X, agent.Y);
        int agentsInCell = AgentCountInCell(cell);
        int foodInCell = _vegetationSystem!.FoodPerCell[cell];
        double localFoodPerCapita = foodInCell / (double)(agentsInCell + 1);
        double ratio = Math.Clamp(localFoodPerCapita / _config.TargetFoodPerCapita, 0.0, 1.0);

        // Frein additionnel sur la santé du POOL du clan (session 18,
        // découvert empiriquement) : le frein local (densité de
        // buissons) ne réagit jamais au stress du clan -- une
        // grossesse en cours ne coûte rien tant qu'on ne regarde QUE la
        // végétation locale, alors que la faim (retardée par le pool)
        // continue à monter. Sans ce second facteur, les naissances
        // dépassent largement ce que la récolte peut soutenir avant que
        // la faim ne freine quoi que ce soit (rétroaction retardée,
        // dépassement puis effondrement plutôt qu'un plateau).
        ref Clan clan = ref GetClanById(agent.ClanId);
        int clanPopulation = ClanPopulationForTarget(agent.ClanId);
        double clanPoolRatio = Math.Clamp(clan.FoodPool / (_config.TargetFoodPoolPerCapita * clanPopulation), 0.0, 1.0);

        double conceptionChance = _config.BaseConceptionChance * ratio * clanPoolRatio;

        if (_rngAgents.NextDouble() >= conceptionChance)
        {
            return;
        }

        agent.PregnantUntil = (uint)tickCounter + species.GestationTicks;
        agent.PendingFatherId = _agents[maleIndex].Id;
    }

    // Scanne les cellules de la grille recouvrant MateSearchRadius autour
    // de la femelle, dans un ordre géométrique fixe (lignes puis
    // colonnes) et, à l'intérieur de chaque case, dans l'ordre
    // d'insertion (= index croissant, cf. RebuildAgentGrid) -- pleinement
    // déterministe. Ne garantit PAS le plus petit index global (un mâle
    // valide dans une case scannée plus tard pourrait avoir un index
    // plus petit qu'un candidat déjà retenu), mais c'est reproductible
    // pour un même état, ce qu'exige réellement CLAUDE.md.
    private bool TryFindMate(int femaleIndex, out int maleIndex)
    {
        ref Agent female = ref _agents[femaleIndex];
        int radius = _config.MateSearchRadius;
        double radiusSquared = (double)radius * radius;

        int centerCellX = Math.Clamp((int)(female.X / _agentGridCellSize), 0, _agentGridWidth - 1);
        int centerCellY = Math.Clamp((int)(female.Y / _agentGridCellSize), 0, _agentGridHeight - 1);
        int cellReach = (radius + _agentGridCellSize - 1) / _agentGridCellSize;

        int minCellY = Math.Max(0, centerCellY - cellReach);
        int maxCellY = Math.Min(_agentGridHeight - 1, centerCellY + cellReach);
        int minCellX = Math.Max(0, centerCellX - cellReach);
        int maxCellX = Math.Min(_agentGridWidth - 1, centerCellX + cellReach);

        for (int cy = minCellY; cy <= maxCellY; cy++)
        {
            for (int cx = minCellX; cx <= maxCellX; cx++)
            {
                int cell = cy * _agentGridWidth + cx;
                int start = _agentGridCellStart[cell];
                int end = _agentGridCellStart[cell + 1];
                for (int b = start; b < end; b++)
                {
                    int candidateIndex = _agentGridEntries[b];
                    if (candidateIndex == femaleIndex)
                    {
                        continue;
                    }

                    ref Agent candidate = ref _agents[candidateIndex];
                    if (candidate.Sex != 1 || candidate.State != AgentState.Idle)
                    {
                        continue;
                    }

                    // Pas de reproduction inter-clans (session 18).
                    if (candidate.ClanId != female.ClanId)
                    {
                        continue;
                    }

                    SpeciesType candidateSpecies = _speciesCatalog.Get(candidate.Species);
                    if (candidate.Age < candidateSpecies.MaturityAge || candidate.Hunger >= _config.HungerSeekThreshold)
                    {
                        continue;
                    }

                    double dx = candidate.X - female.X;
                    double dy = candidate.Y - female.Y;
                    if (dx * dx + dy * dy > radiusSquared)
                    {
                        continue;
                    }

                    maleIndex = candidateIndex;
                    return true;
                }
            }
        }

        maleIndex = -1;
        return false;
    }

    // Naissance (session 14). Deux échecs distincts, comptés
    // séparément pour ne pas polluer le signal "tableau plein" :
    // tuile non sûre (feu/non walkable) et capacité du tableau Agent[]
    // atteinte. "REFUSER" = définitif, jamais une file d'attente --
    // JAMAIS agrandir le tableau en cours de tick (allocation interdite).
    public void TryGiveBirth(ref Agent mother)
    {
        int motherTileX = (int)MathF.Floor(mother.X);
        int motherTileY = (int)MathF.Floor(mother.Y);

        if (!TryFindBirthTile(motherTileX, motherTileY, out int birthX, out int birthY))
        {
            _birthsLostToUnsafeTile++;
            mother.PregnantUntil = 0;
            mother.PendingFatherId = Agent.UnknownParent;
            return;
        }

        if (AliveCount >= _agents.Length)
        {
            _birthsRefusedArrayFull++;
            mother.PregnantUntil = 0;
            mother.PendingFatherId = Agent.UnknownParent;
            return;
        }

        SpeciesType species = _speciesCatalog.Get(mother.Species);
        uint lifespan = RollLifespan(species);
        int newIndex = AliveCount;

        _agentPaths[newIndex].Clear();
        _agents[newIndex] = new Agent
        {
            Id = _nextAgentId++,
            X = birthX + 0.5f,
            Y = birthY + 0.5f,
            TargetX = birthX,
            TargetY = birthY,
            MotherId = mother.Id,
            FatherId = mother.PendingFatherId,
            Tracked = false,
            State = AgentState.Idle,
            Species = mother.Species,
            // Hérité de la mère (session 18) : un enfant naît dans le
            // clan de ses parents, jamais sans clan.
            ClanId = mother.ClanId,
            // Hérité de la mère (session foyers), même raisonnement que
            // ClanId ci-dessus.
            HomeId = mother.HomeId,
            Hunger = 0,
            Facing = 0,
            SeekCooldown = 0,
            WanderDirection = 0,
            WanderTicksRemaining = 0,
            // Pas besoin d'étaler l'âge des naissances : elles sont déjà
            // étalées dans le temps par construction (contrairement au
            // spawn initial, cf. SpawnAgents).
            Age = 0,
            LifespanTicks = lifespan,
            Sex = (byte)(_rngAgents.NextDouble() < 0.5 ? 0 : 1),
            PregnantUntil = 0,
            PendingFatherId = Agent.UnknownParent,
            CauseOfDeath = 0,
            SearchFailureStreak = 0,
            TicksIdle = 0,
            TicksMoving = 0,
            TicksSeeking = 0,
            TicksEating = 0,
            HungerAtLastMealStart = 0,
            LastSeekOutcome = SeekOutcome.NeverSearched,
        };
        AliveCount++;
        _birthsTotal++;

        mother.PregnantUntil = 0;
        mother.PendingFatherId = Agent.UnknownParent;
    }

    private bool TryFindBirthTile(int motherX, int motherY, out int birthX, out int birthY)
    {
        if (IsSafeForBirth(motherX, motherY))
        {
            birthX = motherX;
            birthY = motherY;
            return true;
        }

        if (TryBirthOffset(motherX - 1, motherY, out birthX, out birthY)) return true;
        if (TryBirthOffset(motherX + 1, motherY, out birthX, out birthY)) return true;
        if (TryBirthOffset(motherX, motherY - 1, out birthX, out birthY)) return true;
        if (TryBirthOffset(motherX, motherY + 1, out birthX, out birthY)) return true;

        birthX = 0;
        birthY = 0;
        return false;
    }

    private bool TryBirthOffset(int x, int y, out int birthX, out int birthY)
    {
        if (x >= 0 && x < _size && y >= 0 && y < _size && IsSafeForBirth(x, y))
        {
            birthX = x;
            birthY = y;
            return true;
        }
        birthX = 0;
        birthY = 0;
        return false;
    }

    private bool IsSafeForBirth(int x, int y)
    {
        return _catalog.Get(_terrainSystem.Terrain[y * _size + x]).Walkable && !_terrainSystem.Burning[y * _size + x];
    }

    public void TickAgents(double delta, int tickCounter)
    {
        RebuildAgentGrid();

        int group = tickCounter & 3;
        float step = (float)(_config.AgentMoveSpeed * delta);

        for (int i = 0; i < AliveCount; i++)
        {
            ref Agent agent = ref _agents[i];

            if (agent.State == AgentState.Dead)
            {
                continue;
            }

            switch (agent.State)
            {
                case AgentState.Idle: agent.TicksIdle++; break;
                case AgentState.Moving: agent.TicksMoving++; break;
                case AgentState.Seeking: agent.TicksSeeking++; break;
                case AgentState.Harvesting: agent.TicksHarvesting++; break;
            }
            // TicksEating (session 19c) : incrémenté depuis ApplyPassiveEating
            // sur une bouchée effective, pas ici -- manger n'est plus un état.

            if ((agent.Id & 3) == group)
            {
                ThinkAgent(ref agent, i, tickCounter);
                if (agent.State == AgentState.Dead)
                {
                    continue;
                }
            }

            MoveAgent(ref agent, i, step, tickCounter);
        }
    }

    private void ThinkAgent(ref Agent agent, int index, int tickCounter)
    {
        agent.Hunger = (byte)Math.Min(255, agent.Hunger + _config.HungerIncreasePerThink);

        // World law (session 19b) : la faim ne tue que si explicitement
        // autorisée. Par défaut, Hunger plafonne à 255 (Math.Min ci-dessus)
        // -- l'agent reste vivant, affamé indéfiniment tant que le pool du
        // clan reste vide, mais continue de penser normalement (session
        // 19c : manger n'est plus un état qui l'immobilise).
        if (_config.AllowStarvationDeath && agent.Hunger >= 255)
        {
            agent.StateAtDeath = (byte)agent.State;
            agent.State = AgentState.Dead;
            agent.CauseOfDeath = (byte)DeathCause.Hunger;
            return;
        }

        // Vérifiée après la faim (ordre existant inchangé) : si les deux
        // seuils sont franchis le même tick, la cause enregistrée est la
        // Faim -- tranchage arbitraire mais déterministe (cf. plan,
        // matrice d'interaction).
        agent.Age++;
        if (agent.Age >= agent.LifespanTicks)
        {
            agent.StateAtDeath = (byte)agent.State;
            agent.State = AgentState.Dead;
            agent.CauseOfDeath = (byte)DeathCause.Age;
            return;
        }

        // Gestation NE bloque PAS la recherche de nourriture (cf. plan,
        // matrice d'interaction) : vérifiée ici, inconditionnellement,
        // avant le retour anticipé Seeking/Harvesting ci-dessous.
        if (agent.PregnantUntil != 0 && (uint)tickCounter >= agent.PregnantUntil)
        {
            TryGiveBirth(ref agent);
        }

        // Seeking/Harvesting sont des occupations réelles (déplacement,
        // extraction) -- seuls états qui bloquent la réévaluation. Manger
        // n'est PLUS un état (session 19c) : un agent affamé continue
        // normalement ci-dessous, reste exclu de la reproduction (déjà
        // gaté par Hunger < HungerSeekThreshold dans TryReproduce/
        // TryFindMate, inchangé) MAIS reste éligible à TryStartHarvesting
        // -- c'est ce qui élimine le deadlock Eating/Harvest (cf. plan
        // s19c) : un agent affamé peut désormais partir cueillir.
        if (agent.State == AgentState.Seeking || agent.State == AgentState.Harvesting)
        {
            return;
        }

        if (agent.State == AgentState.Idle)
        {
            TryReproduce(ref agent, index, tickCounter);
        }

        // Récolte (session 18) : déclenchée par le VIDE du pool du
        // clan, jamais par la faim individuelle -- un agent Idle,
        // affamé ou non, peut devenir cueilleur (session 19c : un
        // agent affamé n'est plus immobilisé par un état "en train de
        // manger").
        if (agent.State == AgentState.Idle)
        {
            TryStartHarvesting(ref agent, index);
        }

        // Errance : atteinte quand l'agent n'est pas affamé, ou qu'il
        // l'est mais patiente son cooldown après une recherche ratée —
        // jamais figé en attendant (cf. plan, cooldown de famine).
        if (agent.State == AgentState.Idle && _rngAgents.NextDouble() < _config.IdleMoveChance)
        {
            TryStartMoving(ref agent);
        }
    }

    private void MoveAgent(ref Agent agent, int index, float step, int tickCounter)
    {
        // Manger n'est plus un état exclusif (session 19c) : effet passif
        // appliqué à CHAQUE tick réel, à TOUT agent affamé, quel que soit
        // son état (y compris Harvesting -- un cueilleur affamé mange sans
        // jamais quitter sa récolte). C'est ce qui rend le deadlock
        // Eating/Harvest structurellement impossible : plus aucun état ne
        // peut "avaler" toute la population sans issue.
        ApplyPassiveEating(ref agent);

        if (agent.State == AgentState.Harvesting)
        {
            HarvestTick(ref agent, tickCounter);
            return;
        }

        if (agent.State != AgentState.Moving && agent.State != AgentState.Seeking)
        {
            return;
        }

        float targetCenterX = agent.TargetX + 0.5f;
        float targetCenterY = agent.TargetY + 0.5f;
        float dx = targetCenterX - agent.X;
        float dy = targetCenterY - agent.Y;
        float distanceSquared = dx * dx + dy * dy;

        if (distanceSquared > step * step)
        {
            float distance = MathF.Sqrt(distanceSquared);
            agent.X += dx / distance * step;
            agent.Y += dy / distance * step;
            return;
        }

        agent.X = targetCenterX;
        agent.Y = targetCenterY;

        if (agent.State == AgentState.Moving)
        {
            agent.State = AgentState.Idle;
            return;
        }

        List<int> path = _agentPaths[index];
        if (path.Count > 0)
        {
            SetWaypoint(ref agent, path[^1]);
            path.RemoveAt(path.Count - 1);
            return;
        }

        if (_vegetationSystem!.TryGetVegetationAt(agent.TargetX, agent.TargetY, out Vegetation bush) &&
            bush.Type == _bushTypeId &&
            bush.Stage >= _vegetationCatalog.Get(_bushTypeId).MatureStage)
        {
            agent.State = AgentState.Harvesting;
        }
        else
        {
            agent.State = AgentState.Idle;
        }
    }

    // Diagnostic de mort (session 12) : appelé UNIQUEMENT à la mort d'un
    // agent (rare relativement au nombre de ticks), jamais dans le
    // chemin chaud. Capture avant que CleanupDeadAgents n'écrase le
    // slot par swap-with-last.
    private void RecordDeathDiagnostics(ref Agent agent)
    {
        int tileX = (int)MathF.Floor(agent.X);
        int tileY = (int)MathF.Floor(agent.Y);
        tileX = Math.Clamp(tileX, 0, _size - 1);
        tileY = Math.Clamp(tileY, 0, _size - 1);

        double distance = _vegetationSystem!.DistanceToNearestMatureBush(tileX, tileY);
        _deathDistanceHistogram[DistanceBucket(distance)]++;

        byte terrainId = _terrainSystem.Terrain[tileY * _size + tileX];
        _deathTerrainHistogram[terrainId]++;

        // LastSeekOutcome ne veut dire quelque chose que pour un agent
        // qui CHERCHAIT/RÉCOLTAIT au moment de sa mort (session 18) --
        // un agent mort simplement affamé en Idle/Moving (pool à sec,
        // session 19c : manger n'est plus un état) n'a rien cherché ce
        // tick-là, LastSeekOutcome y serait une valeur périmée d'un
        // voyage de récolte passé, pas un signe de cécité actuelle.
        if (agent.StateAtDeath == (byte)AgentState.Seeking || agent.StateAtDeath == (byte)AgentState.Harvesting)
        {
            _hungerDeathsWhileHarvesting++;
            _deathSeekOutcomeHistogram[agent.LastSeekOutcome]++;
        }

        _deathFailureStreakSum += agent.SearchFailureStreak;
        _deathTicksIdleSum += agent.TicksIdle;
        _deathTicksMovingSum += agent.TicksMoving;
        _deathTicksSeekingSum += agent.TicksSeeking;
        _deathTicksEatingSum += agent.TicksEating;
        _deathHungerAtLastMealSum += agent.HungerAtLastMealStart;
    }

    private static int DistanceBucket(double distance)
    {
        for (int i = 0; i < DeathDistanceBucketBounds.Length; i++)
        {
            if (distance < DeathDistanceBucketBounds[i])
            {
                return i;
            }
        }
        return DeathDistanceBucketBounds.Length;
    }

    public void CleanupDeadAgents()
    {
        int aliveCount = AliveCount;
        int i = 0;
        while (i < aliveCount)
        {
            if (_agents[i].State == AgentState.Dead)
            {
                _deathsByCause[_agents[i].CauseOfDeath]++;

                int clanIndex = ClanIndex(_agents[i].ClanId);
                if (_agents[i].CauseOfDeath == (byte)DeathCause.Hunger)
                {
                    _clanHungerDeaths[clanIndex]++;
                    RecordDeathDiagnostics(ref _agents[i]);
                }
                else if (_agents[i].CauseOfDeath == (byte)DeathCause.Age)
                {
                    _clanAgeDeaths[clanIndex]++;
                }

                aliveCount--;
                _agents[i] = _agents[aliveCount];

                List<int> path = _agentPaths[i];
                _agentPaths[i] = _agentPaths[aliveCount];
                _agentPaths[aliveCount] = path;
            }
            else
            {
                i++;
            }
        }

        AliveCount = aliveCount;
    }

    // Minimum de population JAMAIS observé, PAR CLAN (session 18) --
    // même raisonnement que MinAliveCountEverObserved (un échantillonnage
    // périodique peut rater un creux court, cf. s14c). stackalloc :
    // ClanCount est petit et fixe cette session, aucune allocation tas
    // dans le tick.
    public void UpdateClanMinAliveObserved()
    {
        Span<int> counts = stackalloc int[_clans.Length];
        for (int i = 0; i < AliveCount; i++)
        {
            counts[ClanIndex(_agents[i].ClanId)]++;
        }

        for (int c = 0; c < _clans.Length; c++)
        {
            if (counts[c] < _clanMinAliveEverObserved[c])
            {
                _clanMinAliveEverObserved[c] = counts[c];
            }
        }
    }

    public uint RollLifespan(SpeciesType species)
    {
        if (species.LifespanVarianceTicks == 0)
        {
            return species.LifespanTicks;
        }

        double roll = (_rngAgents.NextDouble() * (species.LifespanVarianceTicks * 2 + 1)) - species.LifespanVarianceTicks;
        double lifespan = species.LifespanTicks + roll;
        return (uint)Math.Max(1, lifespan);
    }
}
