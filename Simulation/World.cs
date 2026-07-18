namespace Simulation;

public enum DeathCause : byte
{
    Hunger = 0,
    Age = 1,
}

public sealed class World
{
    // Cadence de simulation elle-même, pas un réglage de gameplay : reste
    // un const C#, source unique que le renderer doit consommer.
    public const double TickIntervalSeconds = 1.0 / 30.0;

    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    // Identifiants arbitraires mais fixes pour dériver un seed par flux
    // depuis le seed principal (cf. DeriveSeed). Sel de dérivation, pas
    // un réglage de gameplay.
    private const ulong WorldGenStreamId = 1;
    private const ulong FireStreamId = 2;
    private const ulong AgentsStreamId = 3;
    private const ulong VegetationStreamId = 4;

    // Garde-fou technique (pas un réglage de gameplay) : borne le
    // rejection sampling de SpawnAgents sur une carte quasi dégénérée.
    private const int MaxSpawnAttemptsPerAgent = 10;

    // Une tuile qui n'a jamais porté de végétation doit être
    // immédiatement éligible à la repousse (pas de délai artificiel au
    // démarrage du monde).
    private const int NeverClearedSentinel = int.MinValue / 2;

    private readonly SimulationConfig _config;
    private readonly byte[] _terrain;
    private readonly bool[] _burning;
    private readonly Agent[] _agents;
    private readonly List<int>[] _agentPaths;
    private readonly Vegetation[] _bushes;
    private readonly int[] _bushIndexAt;
    private readonly Vegetation[] _trees;
    private readonly int[] _treeIndexAt;
    private readonly int[] _vegetationClearedTick;
    private readonly TerrainCatalog _catalog;
    private readonly VegetationCatalog _vegetationCatalog;
    private readonly SpeciesCatalog _speciesCatalog;
    private readonly Rng _rngWorldGen;
    private readonly Rng _rngFire;
    private readonly Rng _rngAgents;
    private readonly Rng _rngVegetation;
    private readonly byte _ashId;
    private readonly byte _grassId;
    private readonly byte _bushTypeId;
    private readonly byte _treeTypeId;
    private readonly int _maxSearchRadius;
    private readonly int _boxSide;
    private readonly int[] _searchGeneration;
    private readonly int[] _searchCameFrom;
    private readonly int[] _deathsByCause = new int[2];
    private int _tickCounter;
    private uint _nextAgentId;

    // Clans (session 18) : capacité FIXE cette session (pas de
    // scission/fusion/suppression, donc pas de compaction réelle). La
    // consigne "référence par Id stable, jamais par index" est quand
    // même honorée via _clanIndexById (identité aujourd'hui, câblée
    // pour rester correcte le jour où les scissions arrivent) --
    // même rôle que _bushIndexAt pour la végétation.
    private readonly Clan[] _clans;
    private readonly int[] _clanIndexById;
    private uint _nextClanId;

    // Diagnostic par clan (comme les compteurs globaux existants) :
    // exclus de Hash() sauf FoodPool/ClanId (déjà couverts via le
    // bloc clan et le bloc agent).
    private int[] _clanHungerDeaths = Array.Empty<int>();
    private int[] _clanAgeDeaths = Array.Empty<int>();
    // long : sur un run de plusieurs millions de ticks avec une
    // population nombreuse, le cumul dépasse largement la capacité
    // d'un int32 (observé : débordement vers des valeurs négatives
    // lors du calibrage de cette session).
    private long[] _clanFoodHarvestedCumulative = Array.Empty<long>();
    private long[] _clanFoodConsumedCumulative = Array.Empty<long>();
    private int[] _clanMinAliveEverObserved = Array.Empty<int>();

    // Population par clan (session 18), recalculée une fois par tick
    // réel dans RebuildAgentGrid (déjà O(AliveCount), un incrément de
    // plus ne coûte rien) -- évite un balayage complet par agent
    // candidat à la récolte (cf. TryStartHarvesting).
    private int[] _clanPopulation = Array.Empty<int>();

    // Grille grossière d'agents (session 14) : aucune structure spatiale
    // n'existait pour les agents avant cette session (contrairement à la
    // végétation, indexée par tuile). Reconstruite à chaque tick réel,
    // scratch pur -- exclue de Hash() (même raisonnement que
    // _searchQueue). Sert à la recherche de partenaire (portée
    // MateSearchRadius) ET à la densité locale d'agents pour le frein
    // progressif (item 4).
    //
    // Comptage par bucket sur tableaux plats préalloués (pas de
    // List<int>.Add : la taille d'un bucket varie tick à tick avec les
    // déplacements des agents, un List grossirait et réallouerait --
    // interdit dans le tick). _agentGridCellStart est un prefix-sum de
    // taille cellCount+1 ; _agentGridEntries contient les index d'agent
    // groupés par cellule, [_agentGridCellStart[c], _agentGridCellStart[c+1]).
    private readonly int _agentGridCellSize;
    private readonly int _agentGridWidth;
    private readonly int _agentGridHeight;
    private readonly int[] _agentGridCellCounts;
    private readonly int[] _agentGridCellStart;
    private readonly int[] _agentGridEntries;

    // Densité de nourriture par cellule (même grille), reconstruite une
    // fois par tick végétation seulement (coût déjà budgété, cf.
    // TickVegetationGrowth/Aging qui itèrent déjà tout _bushes chaque
    // tick végétation) -- jamais dans le chemin chaud à 30 Hz.
    private readonly int[] _foodPerCell;

    // Champ de gradient de nourriture (session 14c) : diffusion de
    // _foodPerCell sur la même grille, même principe que TickFire
    // (lire _current, écrire _next, swap) mais dense au lieu de
    // sparse. Un agent affamé dont le BFS local (±MaxFoodSearchRadius)
    // échoue lit sa cellule et ses voisines au lieu de marcher au
    // hasard -- corrige la cécité au-delà de la portée de perception
    // sans BFS élargi. Recalculé au tick végétation seulement, comme
    // _foodPerCell.
    private readonly double[] _foodGradientA;
    private readonly double[] _foodGradientB;
    private double[] _foodGradient;

    // Conductivité par cellule (session 14d) : fraction de tuiles
    // HERBE dans la cellule, plancher 0.05 (jamais nulle -- un signal
    // résiduel doit pouvoir traverser un désert total, même piège
    // symétrique que la repousse de végétation). Le sable/pierre/
    // cendre/eau ne portent jamais de buisson (cf. TrySpreadBushTo,
    // grass uniquement) : sans pondération, la diffusion les traverse
    // comme de l'herbe et peut attirer un agent à travers un désert
    // létal vers un amas lointain plutôt qu'une source proche.
    // Recalculée au tick végétation (même cadence que _foodPerCell).
    private readonly double[] _cellConductivity;
    private readonly int[] _cellGrassCountScratch;
    private readonly int[] _cellTotalCountScratch;

    // Diagnostic (comme MealsEaten) : exclus de Hash().
    private int _birthsTotal;
    private int _birthsRefusedArrayFull;
    private int _birthsLostToUnsafeTile;
    private int _minAliveCountEverObserved = int.MaxValue;

    // Diagnostic feu (session 17b) : taille d'un événement d'incendie
    // (tuiles enflammées entre le moment où la liste active passe de
    // vide à non-vide et le moment où elle revient à vide) et cause
    // d'échec de propagation vers un voisin -- coupe-feu naturel
    // (terrain non-inflammable, quel que soit le tirage RNG) vs
    // extinction par probabilité (terrain inflammable mais tirage
    // raté). Compteurs purs, jamais lus par une décision, exclus de
    // Hash() comme le reste des diagnostics de cette section.
    private int _currentFireEventTiles;
    private long _fireEventSizeSum;
    private int _fireEventCount;
    private int _fireEventMaxSize;
    private int _fireBlockedByTerrainCount;
    private int _fireFizzledCount;

    // --- Diagnostic de mort (session 12) : compteurs cumulés, jamais
    // lus par une décision, exclus de Hash() comme MealsEaten/DeathCause. ---
    // Bornes de buckets : le seuil 33 correspond exactement à _boxSide
    // (portée du BFS de recherche de nourriture), pour lire d'un coup
    // d'œil si les morts sont dans ou hors de portée.
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

    private List<int> _activeCurrent = new();
    private List<int> _activeNext = new();

    // Buffer de travail pour la recherche BFS : entièrement écrasé (via
    // generation-stamp) à chaque appel, jamais lu entre deux appels.
    // Exclu de Hash() volontairement (cf. CLAUDE.md, Déterminisme).
    private readonly List<int> _searchQueue = new();
    private int _searchGenerationCounter;

    public int Size { get; }

    public int AgentCapacity => _agents.Length;

    public int AliveCount { get; private set; }

    // Arbres et buissons vivent dans deux tableaux à capacité indépendante
    // (session 13) : ils ne se disputent plus les slots. VegetationCount/
    // GetVegetation restent une concaténation logique bush-puis-tree pour
    // ne pas casser les appelants (tests, SimReport) qui itèrent "la"
    // végétation sans se soucier du type.
    public int BushCount { get; private set; }

    public int TreeCount { get; private set; }

    public int VegetationCount => BushCount + TreeCount;

    public bool AgentSpawnCapped { get; private set; }

    public int GrassTileCount { get; private set; }

    public int AshTileCount { get; private set; }

    // Compteurs de diagnostic (comme les morts par cause) : n'influencent
    // jamais la simulation, donc exclus de Hash().
    // long (session 19c) : depuis que manger est un effet passif appliqué
    // à CHAQUE tick réel (au lieu d'une fois par "session de repas"),
    // ce compteur incrémente bien plus vite qu'avant et dépasse
    // int.MaxValue en quelques millions de ticks à haute population --
    // même raisonnement que _clanFoodHarvestedCumulative (session 18).
    public long MealsEaten { get; private set; }

    public int TilesBurnedCumulative { get; private set; }

    public int VegetationLostToFire { get; private set; }

    // Diagnostic feu (session 17b), cf. champs privés ci-dessus.
    public double AverageFireEventSize => _fireEventCount > 0 ? (double)_fireEventSizeSum / _fireEventCount : 0.0;

    public int FireEventCount => _fireEventCount;

    public int MaxFireEventSize => _fireEventMaxSize;

    public int FireBlockedByTerrainCount => _fireBlockedByTerrainCount;

    public int FireFizzledCount => _fireFizzledCount;

    public int BirthsTotal => _birthsTotal;

    public int BirthsRefusedArrayFull => _birthsRefusedArrayFull;

    public int BirthsLostToUnsafeTile => _birthsLostToUnsafeTile;

    // Répond au piège de méthode signalé : échantillonner tous les
    // 100k ticks peut rater un creux court. Suivi à CHAQUE tick réel,
    // pas seulement aux points d'échantillonnage.
    public int MinAliveCountEverObserved => _minAliveCountEverObserved;

    // Clans (session 18) : le tableau n'est jamais compacté cette
    // session (pas de scission), donc itérer par index 0..ClanCount
    // est sûr pour un appelant EXTERNE (SimReport, UI). Le code
    // interne passe par GetClanById par principe (cf. champs privés).
    public int ClanCount => _clans.Length;

    public Clan GetClan(int index) => _clans[index];

    public int GetClanHungerDeaths(int index) => _clanHungerDeaths[index];

    public int GetClanAgeDeaths(int index) => _clanAgeDeaths[index];

    public long GetClanFoodHarvestedCumulative(int index) => _clanFoodHarvestedCumulative[index];

    public long GetClanFoodConsumedCumulative(int index) => _clanFoodConsumedCumulative[index];

    public int GetClanMinAliveEverObserved(int index) => _clanMinAliveEverObserved[index];

    public static IReadOnlyList<double> DeathDistanceBucketUpperBounds => DeathDistanceBucketBounds;

    public int[] GetDeathDistanceHistogram() => (int[])_deathDistanceHistogram.Clone();

    public int[] GetDeathTerrainHistogram() => (int[])_deathTerrainHistogram.Clone();

    public int[] GetDeathSeekOutcomeHistogram() => (int[])_deathSeekOutcomeHistogram.Clone();

    // Sous-ensemble des morts de faim où l'agent était Seeking/Harvesting
    // au moment de mourir (session 18) -- dénominateur pertinent pour
    // juger la cécité des CUEILLEURS, distinct du total des morts de
    // faim (qui inclut aussi les morts "pool à sec" en Idle/Moving --
    // manger n'est plus un état depuis la session 19c).
    public int HungerDeathsWhileHarvesting => _hungerDeathsWhileHarvesting;

    public double AverageDeathFailureStreak => AverageOverDeaths(_deathFailureStreakSum);

    public double AverageDeathTicksIdle => AverageOverDeaths(_deathTicksIdleSum);

    public double AverageDeathTicksMoving => AverageOverDeaths(_deathTicksMovingSum);

    public double AverageDeathTicksSeeking => AverageOverDeaths(_deathTicksSeekingSum);

    public double AverageDeathTicksEating => AverageOverDeaths(_deathTicksEatingSum);

    public double AverageDeathHungerAtLastMeal => AverageOverDeaths(_deathHungerAtLastMealSum);

    private double AverageOverDeaths(long sum)
    {
        int deaths = GetDeathCount(DeathCause.Hunger);
        return deaths > 0 ? sum / (double)deaths : 0.0;
    }

    public World(int seed, int size, TerrainCatalog catalog, VegetationCatalog vegetationCatalog, SpeciesCatalog speciesCatalog, SimulationConfig config)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException($"size must be a power of two greater than zero, got {size}", nameof(size));
        }

        Size = size;
        _catalog = catalog;
        _vegetationCatalog = vegetationCatalog;
        _speciesCatalog = speciesCatalog;
        _config = config;
        _terrain = new byte[size * size];
        _burning = new bool[size * size];

        _rngWorldGen = new Rng(DeriveSeed(seed, WorldGenStreamId));
        _rngFire = new Rng(DeriveSeed(seed, FireStreamId));
        _rngAgents = new Rng(DeriveSeed(seed, AgentsStreamId));
        _rngVegetation = new Rng(DeriveSeed(seed, VegetationStreamId));

        if (!catalog.TryGetId("ash", out _ashId))
        {
            throw new ArgumentException("terrain catalog must define ash", nameof(catalog));
        }

        if (!catalog.TryGetId("grass", out _grassId))
        {
            throw new ArgumentException("terrain catalog must define grass", nameof(catalog));
        }

        if (!vegetationCatalog.TryGetId("bush", out _bushTypeId) ||
            !vegetationCatalog.TryGetId("tree", out _treeTypeId))
        {
            throw new ArgumentException("vegetation catalog must define bush and tree", nameof(vegetationCatalog));
        }

        _maxSearchRadius = config.MaxFoodSearchRadius;
        _boxSide = _maxSearchRadius * 2 + 1;
        _searchGeneration = new int[_boxSide * _boxSide];
        _searchCameFrom = new int[_boxSide * _boxSide];

        GenerateTerrain();

        for (int i = 0; i < _terrain.Length; i++)
        {
            if (_terrain[i] == _grassId)
            {
                GrassTileCount++;
            }
        }

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
        _foodPerCell = new int[cellCount];
        _foodGradientA = new double[cellCount];
        _foodGradientB = new double[cellCount];
        _foodGradient = _foodGradientA;
        _cellConductivity = new double[cellCount];
        _cellGrassCountScratch = new int[cellCount];
        _cellTotalCountScratch = new int[cellCount];

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

        SpawnAgents(initialPopulation);

        _bushes = new Vegetation[(int)(config.BushDensity * size * size)];
        _bushIndexAt = new int[size * size];
        Array.Fill(_bushIndexAt, -1);

        _trees = new Vegetation[(int)(config.TreeDensity * size * size)];
        _treeIndexAt = new int[size * size];
        Array.Fill(_treeIndexAt, -1);

        _vegetationClearedTick = new int[size * size];
        Array.Fill(_vegetationClearedTick, NeverClearedSentinel);

        SeedMinimumBushPerPatch();
        SeedInitialVegetation();
    }

    // Garantit qu'aucune poche d'herbe connectee ne demarre totalement
    // sans buisson (session 19). SeedInitialVegetation seul remplit par
    // UN SEUL balayage rotatif jusqu'a la capacite du tableau _bushes --
    // a densite basse (post session 19), cette capacite est atteinte
    // bien avant que le balayage n'ait visite toutes les poches, donc
    // une poche rencontree tardivement n'obtient jamais aucun buisson.
    // Un clan (ou, a terme, un clan place par le joueur) qui nait dans
    // une telle poche est condamne d'office quelle que soit la densite
    // globale -- ce n'est pas un probleme de calibrage, c'est un trou de
    // couverture au world-gen. Flood-fill une fois a la construction
    // (cout ponctuel O(size^2), meme statut qu'AnalyzeGrassConnectivity),
    // plante un unique buisson mur au premier tile libre de chaque poche
    // avant que le remplissage rotatif ne consomme la capacite restante.
    private void SeedMinimumBushPerPatch()
    {
        var visited = new bool[Size * Size];
        var queue = new List<int>();
        int bushMatureStage = _vegetationCatalog.Get(_bushTypeId).MatureStage;

        for (int startIndex = 0; startIndex < Size * Size; startIndex++)
        {
            if (visited[startIndex] || _terrain[startIndex] != _grassId)
            {
                continue;
            }

            queue.Clear();
            queue.Add(startIndex);
            visited[startIndex] = true;

            int head = 0;
            int seedTile = -1;
            while (head < queue.Count)
            {
                int index = queue[head++];
                if (seedTile == -1 && _bushIndexAt[index] == -1 && _treeIndexAt[index] == -1)
                {
                    seedTile = index;
                }

                int x = index % Size;
                int y = index / Size;
                TryEnqueueGrass(x - 1, y, visited, queue);
                TryEnqueueGrass(x + 1, y, visited, queue);
                TryEnqueueGrass(x, y - 1, visited, queue);
                TryEnqueueGrass(x, y + 1, visited, queue);
            }

            if (seedTile != -1 && BushCount < _bushes.Length)
            {
                int x = seedTile % Size;
                int y = seedTile / Size;
                SpawnBush(x, y);
                _bushes[BushCount - 1].Stage = (byte)bushMatureStage;
            }
        }
    }

    // Un monde fraîchement généré doit démarrer avec un écosystème déjà
    // établi, pas une graine (session 15) : depuis que la maturation
    // d'un buisson prend ~50s (échelle de temps ralentie, cf. plan) et
    // que la mort de faim survient à ~34s, un monde vierge condamnait
    // toute la population initiale avant qu'un seul buisson n'ait mûri.
    // Même esprit que la génération de terrain elle-même (déjà
    // "complète" au démarrage, jamais générée par accrétion) :
    // pré-plante directement à maturité, jusqu'à la capacité de
    // chaque tableau, via le même balayage tournant que la
    // germination spontanée (déterministe, pas de biais spatial).
    private void SeedInitialVegetation()
    {
        int bushMatureStage = _vegetationCatalog.Get(_bushTypeId).MatureStage;
        int treeMatureStage = _vegetationCatalog.Get(_treeTypeId).MatureStage;
        int tileCount = _terrain.Length;
        int startIndex = (int)(_rngVegetation.NextDouble() * tileCount);

        for (int offset = 0; offset < tileCount; offset++)
        {
            if (BushCount >= _bushes.Length && TreeCount >= _trees.Length)
            {
                return;
            }

            int index = (startIndex + offset) % tileCount;
            if (_terrain[index] != _grassId || _bushIndexAt[index] != -1 || _treeIndexAt[index] != -1)
            {
                continue;
            }

            int x = index % Size;
            int y = index / Size;

            // Remplit directement jusqu'à la capacité (pas un tirage au
            // taux de germination spontanée, bien trop bas pour amorcer
            // tout un monde -- ce taux reste le filet de sécurité pour
            // une repousse EN COURS DE PARTIE, pas pour le démarrage).
            // La pression de récolte ramènera naturellement la
            // population vers son équilibre réel au fil du temps.
            if (BushCount < _bushes.Length)
            {
                SpawnBush(x, y);
                _bushes[BushCount - 1].Stage = (byte)bushMatureStage;
            }
            else if (TreeCount < _trees.Length)
            {
                SpawnTree(x, y);
                _trees[TreeCount - 1].Stage = (byte)treeMatureStage;
            }
        }
    }

    public byte GetTerrainId(int x, int y) => _terrain[y * Size + x];

    public void SetTerrainId(int x, int y, byte id) => _terrain[y * Size + x] = id;

    public bool IsBurning(int x, int y) => _burning[y * Size + x];

    public Agent GetAgent(int index) => _agents[index];

    public Vegetation GetVegetation(int index) => index < BushCount ? _bushes[index] : _trees[index - BushCount];

    public int GetDeathCount(DeathCause cause) => _deathsByCause[(byte)cause];

    public int CountVegetationOfType(byte type)
    {
        if (type == _bushTypeId)
        {
            return BushCount;
        }
        if (type == _treeTypeId)
        {
            return TreeCount;
        }
        return 0;
    }

    public int CountMatureVegetationOfType(byte type)
    {
        int matureStage = _vegetationCatalog.Get(type).MatureStage;
        if (type == _bushTypeId)
        {
            return CountMature(_bushes, BushCount, matureStage);
        }
        if (type == _treeTypeId)
        {
            return CountMature(_trees, TreeCount, matureStage);
        }
        return 0;
    }

    private static int CountMature(Vegetation[] array, int count, int matureStage)
    {
        int result = 0;
        for (int i = 0; i < count; i++)
        {
            if (array[i].Stage >= matureStage)
            {
                result++;
            }
        }
        return result;
    }

    public bool TryGetVegetationAt(int x, int y, out Vegetation vegetation)
    {
        int index = y * Size + x;

        int bushSlot = _bushIndexAt[index];
        if (bushSlot != -1)
        {
            vegetation = _bushes[bushSlot];
            return true;
        }

        int treeSlot = _treeIndexAt[index];
        if (treeSlot != -1)
        {
            vegetation = _trees[treeSlot];
            return true;
        }

        vegetation = default;
        return false;
    }

    public void ForceSpawnVegetation(int x, int y, byte type, byte stage)
    {
        ClearVegetationAt(x, y);

        // "Force" doit garantir la place même si le tableau (capacité =
        // densité x taille, zéro marge) est déjà plein -- depuis
        // SeedInitialVegetation (s15), c'est le cas dès la construction
        // pour toute tuile qui n'était pas encore de l'herbe à ce
        // moment-là. On libère un slot arbitraire (le premier) du même
        // type plutôt que de planter hors limites.
        if (type == _bushTypeId && BushCount >= _bushes.Length)
        {
            RemoveBushAt(0);
        }
        else if (type == _treeTypeId && TreeCount >= _trees.Length)
        {
            RemoveTreeAt(0);
        }

        SpawnVegetationOfType(x, y, type);

        int index = y * Size + x;
        if (type == _bushTypeId)
        {
            _bushes[_bushIndexAt[index]].Stage = stage;
        }
        else if (type == _treeTypeId)
        {
            _trees[_treeIndexAt[index]].Stage = stage;
        }
    }

    public void SetVegetationFoodRemaining(int x, int y, int amount)
    {
        int index = y * Size + x;

        int bushSlot = _bushIndexAt[index];
        if (bushSlot != -1)
        {
            _bushes[bushSlot].FoodRemaining = amount;
            return;
        }

        int treeSlot = _treeIndexAt[index];
        if (treeSlot != -1)
        {
            _trees[treeSlot].FoodRemaining = amount;
        }
    }

    public void SetVegetationDeathTick(int x, int y, int deathTick)
    {
        int index = y * Size + x;

        int bushSlot = _bushIndexAt[index];
        if (bushSlot != -1)
        {
            _bushes[bushSlot].DeathTick = deathTick;
            return;
        }

        int treeSlot = _treeIndexAt[index];
        if (treeSlot != -1)
        {
            _trees[treeSlot].DeathTick = deathTick;
        }
    }

    // Seam de test : retire la végétation présente (si il y en a) pour
    // poser l'horodatage de délai de repousse sans dépendre d'un agent
    // qui mange ou d'un feu.
    public void ClearVegetationAt(int x, int y)
    {
        int index = y * Size + x;

        int bushSlot = _bushIndexAt[index];
        if (bushSlot != -1)
        {
            RemoveBushAt(bushSlot);
            return;
        }

        int treeSlot = _treeIndexAt[index];
        if (treeSlot != -1)
        {
            RemoveTreeAt(treeSlot);
        }
    }

    // Seam de test : vide toute la végétation posée par
    // SeedInitialVegetation (s15) -- nécessaire pour les scénarios qui
    // exigent une carte réellement sans nourriture (RemoveBushAt/
    // RemoveTreeAt en boucle plutôt qu'un simple Clear() du tableau, le
    // swap-with-last laisse toujours le tableau dans un état valide).
    public void ClearAllVegetation()
    {
        while (BushCount > 0)
        {
            RemoveBushAt(BushCount - 1);
        }

        while (TreeCount > 0)
        {
            RemoveTreeAt(TreeCount - 1);
        }
    }

    // Distance euclidienne au buisson mûr le plus proche, SANS la limite
    // de portée du BFS de gameplay (_maxSearchRadius) : balaie tout
    // _bushes[0..BushCount), la "vraie" distance. Utilisée par le
    // diagnostic de mort (s12) et par la mesure de clusterisation de
    // SimReport (s13). double.PositiveInfinity si aucun buisson mûr.
    public double DistanceToNearestMatureBush(int x, int y)
    {
        int matureStage = _vegetationCatalog.Get(_bushTypeId).MatureStage;
        double best = double.PositiveInfinity;

        for (int i = 0; i < BushCount; i++)
        {
            ref Vegetation veg = ref _bushes[i];
            if (veg.Stage < matureStage)
            {
                continue;
            }

            double dx = veg.X - x;
            double dy = veg.Y - y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < best)
            {
                best = distance;
            }
        }

        return best;
    }

    // Diagnostic (session 14b) : écart-type du nombre d'agents par
    // cellule de la grille grossière déjà reconstruite chaque tick
    // (cf. RebuildAgentGrid) -- mesure la clusterisation des AGENTS,
    // distincte de celle des buissons (déjà mesurée par SimReport en
    // s13). Lecture pure, exclue de Hash() (comme DistanceToNearestMatureBush).
    public double AgentDensityStdDev()
    {
        int cellCount = _agentGridWidth * _agentGridHeight;
        if (cellCount == 0)
        {
            return 0.0;
        }

        double mean = (double)AliveCount / cellCount;
        double sumSquaredDiff = 0.0;
        for (int c = 0; c < cellCount; c++)
        {
            double diff = AgentCountInCell(c) - mean;
            sumSquaredDiff += diff * diff;
        }
        return Math.Sqrt(sumSquaredDiff / cellCount);
    }

    // Diagnostic (session 17b) : connectivité des poches d'herbe --
    // sable/eau/pierre/cendre ne portent jamais d'herbe (cf.
    // GenerateTerrain), donc chaque lac ceinturé de sable isole
    // potentiellement l'herbe autour en îlots : chacun son propre
    // coupe-feu naturel, et une poche broutée à zéro n'a plus de
    // graines locales pour la repousse (cf. TrySpreadBushTo, herbe
    // uniquement). Flood-fill classique sur un buffer visited/queue
    // frais -- jamais appelée depuis Tick(), allocation acceptée
    // (même statut que AgentDensityStdDev/DistanceToNearestMatureBush).
    // Lecture pure, exclue de Hash().
    public GrassConnectivityReport AnalyzeGrassConnectivity()
    {
        var visited = new bool[Size * Size];
        var queue = new List<int>();
        var sizes = new List<int>();
        int half = Size / 2;
        var patchCountByQuadrant = new int[4];
        var noBushByQuadrant = new int[4];
        int patchesWithNoBush = 0;

        for (int startIndex = 0; startIndex < Size * Size; startIndex++)
        {
            if (visited[startIndex] || _terrain[startIndex] != _grassId)
            {
                continue;
            }

            queue.Clear();
            queue.Add(startIndex);
            visited[startIndex] = true;

            int quadrant = (startIndex % Size < half ? 0 : 1) + (startIndex / Size < half ? 0 : 2);
            int size = 0;
            bool hasBush = false;

            int head = 0;
            while (head < queue.Count)
            {
                int index = queue[head++];
                size++;

                if (_bushIndexAt[index] != -1)
                {
                    hasBush = true;
                }

                int x = index % Size;
                int y = index / Size;
                TryEnqueueGrass(x - 1, y, visited, queue);
                TryEnqueueGrass(x + 1, y, visited, queue);
                TryEnqueueGrass(x, y - 1, visited, queue);
                TryEnqueueGrass(x, y + 1, visited, queue);
            }

            sizes.Add(size);
            patchCountByQuadrant[quadrant]++;
            if (!hasBush)
            {
                patchesWithNoBush++;
                noBushByQuadrant[quadrant]++;
            }
        }

        sizes.Sort();
        int count = sizes.Count;
        int min = count > 0 ? sizes[0] : 0;
        int max = count > 0 ? sizes[count - 1] : 0;
        int median = count > 0 ? sizes[count / 2] : 0;

        return new GrassConnectivityReport(count, min, median, max, patchesWithNoBush, patchCountByQuadrant, noBushByQuadrant);
    }

    private void TryEnqueueGrass(int x, int y, bool[] visited, List<int> queue)
    {
        if (x < 0 || x >= Size || y < 0 || y >= Size)
        {
            return;
        }

        int index = y * Size + x;
        if (visited[index] || _terrain[index] != _grassId)
        {
            return;
        }

        visited[index] = true;
        queue.Add(index);
    }

    public void SetAgentHunger(int index, byte hunger) => _agents[index].Hunger = hunger;

    // Seams de test (session 14) : même statut que SetAgentHunger --
    // permettent de forcer un scénario déterministe sans dépendre du
    // hasard du spawn.
    public void SetAgentAge(int index, uint age) => _agents[index].Age = age;

    public void SetAgentLifespan(int index, uint lifespan) => _agents[index].LifespanTicks = lifespan;

    public void SetAgentSex(int index, byte sex) => _agents[index].Sex = sex;

    public void SetAgentPosition(int index, float x, float y)
    {
        _agents[index].X = x;
        _agents[index].Y = y;
    }

    // Seams de test (session 18) : même statut que les seams ci-dessus.
    public void SetAgentClanId(int index, uint clanId) => _agents[index].ClanId = clanId;

    public void SetClanFoodPool(int clanIndex, int amount) => _clans[clanIndex].FoodPool = amount;

    public void SetAgentState(int index, AgentState state) => _agents[index].State = state;

    public void SetAgentTarget(int index, int x, int y)
    {
        _agents[index].TargetX = x;
        _agents[index].TargetY = y;
    }

    public void Execute(ICommand command) => command.Execute(this);

    public void IgniteArea(int centerX, int centerY, int radius)
    {
        int radiusSquared = radius * radius;
        int minX = Math.Max(0, centerX - radius);
        int maxX = Math.Min(Size - 1, centerX + radius);
        int minY = Math.Max(0, centerY - radius);
        int maxY = Math.Min(Size - 1, centerY + radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy > radiusSquared)
                {
                    continue;
                }

                TryIgnite(x, y, _activeCurrent);
            }
        }
    }

    public void Tick(double delta)
    {
        TickFire();
        TickAgents(delta);
        CleanupDeadAgents();

        if (_tickCounter % _config.VegetationTickInterval == 0)
        {
            TickVegetationGrowth();
            TickVegetationAging();
            TickVegetationSpread();
            TickAshRecovery();
            RebuildFoodDensityGrid();
            RebuildCellConductivity();
            RebuildFoodGradient();
        }

        if (AliveCount < _minAliveCountEverObserved)
        {
            _minAliveCountEverObserved = AliveCount;
        }

        UpdateClanMinAliveObserved();

        _tickCounter++;
    }

    public ulong Hash()
    {
        ulong hash = FnvOffsetBasis;

        foreach (byte b in _terrain)
        {
            Mix(ref hash, b);
        }

        foreach (bool burning in _burning)
        {
            Mix(ref hash, burning ? 1UL : 0UL);
        }

        foreach (int clearedTick in _vegetationClearedTick)
        {
            Mix(ref hash, unchecked((uint)clearedTick));
        }

        Mix(ref hash, (ulong)_tickCounter);
        Mix(ref hash, _nextAgentId);
        Mix(ref hash, _rngWorldGen.State);
        Mix(ref hash, _rngFire.State);
        Mix(ref hash, _rngAgents.State);
        Mix(ref hash, _rngVegetation.State);

        Mix(ref hash, (ulong)_activeCurrent.Count);
        foreach (int index in _activeCurrent)
        {
            Mix(ref hash, (uint)index);
        }

        Mix(ref hash, (ulong)AliveCount);

        for (int i = 0; i < AliveCount; i++)
        {
            ref Agent agent = ref _agents[i];
            Mix(ref hash, agent.Id);
            Mix(ref hash, BitConverter.SingleToUInt32Bits(agent.X));
            Mix(ref hash, BitConverter.SingleToUInt32Bits(agent.Y));
            Mix(ref hash, (uint)agent.TargetX);
            Mix(ref hash, (uint)agent.TargetY);
            Mix(ref hash, agent.MotherId);
            Mix(ref hash, agent.FatherId);
            Mix(ref hash, agent.Tracked ? 1UL : 0UL);
            Mix(ref hash, (byte)agent.State);
            Mix(ref hash, agent.Species);
            Mix(ref hash, agent.Hunger);
            Mix(ref hash, agent.Facing);
            Mix(ref hash, agent.SeekCooldown);
            Mix(ref hash, agent.WanderDirection);
            Mix(ref hash, agent.WanderTicksRemaining);
            Mix(ref hash, agent.Age);
            Mix(ref hash, agent.LifespanTicks);
            Mix(ref hash, agent.Sex);
            Mix(ref hash, agent.PregnantUntil);
            Mix(ref hash, agent.PendingFatherId);
            Mix(ref hash, agent.ClanId);

            List<int> path = _agentPaths[i];
            Mix(ref hash, (ulong)path.Count);
            foreach (int waypoint in path)
            {
                Mix(ref hash, (uint)waypoint);
            }
        }

        Mix(ref hash, (ulong)BushCount);
        for (int i = 0; i < BushCount; i++)
        {
            ref Vegetation veg = ref _bushes[i];
            Mix(ref hash, (uint)veg.X);
            Mix(ref hash, (uint)veg.Y);
            Mix(ref hash, veg.Type);
            Mix(ref hash, veg.Stage);
            Mix(ref hash, (uint)veg.FoodRemaining);
            Mix(ref hash, unchecked((uint)veg.DeathTick));
        }

        Mix(ref hash, (ulong)TreeCount);
        for (int i = 0; i < TreeCount; i++)
        {
            ref Vegetation veg = ref _trees[i];
            Mix(ref hash, (uint)veg.X);
            Mix(ref hash, (uint)veg.Y);
            Mix(ref hash, veg.Type);
            Mix(ref hash, veg.Stage);
            Mix(ref hash, (uint)veg.FoodRemaining);
            Mix(ref hash, unchecked((uint)veg.DeathTick));
        }

        // Clans (session 18) : capacité fixe, jamais compactée cette
        // session, itération directe par index sûre ici (code interne
        // uniquement -- les appelants externes passent par GetClan).
        Mix(ref hash, (ulong)_clans.Length);
        for (int i = 0; i < _clans.Length; i++)
        {
            ref Clan clan = ref _clans[i];
            Mix(ref hash, clan.Id);
            Mix(ref hash, unchecked((uint)clan.ParentClanId));
            Mix(ref hash, clan.Species);
            Mix(ref hash, unchecked((uint)clan.FoodPool));
        }

        // Champ de gradient de nourriture (session 14c) : dérivé
        // déterministe de _foodPerCell (déjà couvert via _bushes
        // ci-dessus), donc redondant en théorie -- inclus quand même
        // par prudence, cf. plan.
        foreach (double value in _foodGradient)
        {
            Mix(ref hash, BitConverter.DoubleToUInt64Bits(value));
        }

        // Conductivité (session 14d) : même raisonnement de prudence
        // que _foodGradient -- dérivée déterministe de _terrain (déjà
        // hashé), incluse quand même.
        foreach (double value in _cellConductivity)
        {
            Mix(ref hash, BitConverter.DoubleToUInt64Bits(value));
        }

        return hash;
    }

    private static void Mix(ref ulong hash, ulong value)
    {
        hash ^= value;
        hash *= FnvPrime;
    }

    private static ulong DeriveSeed(int seed, ulong streamId)
    {
        ulong derived = (ulong)seed;
        Mix(ref derived, streamId);
        return derived;
    }

    private void TickFire()
    {
        _activeNext.Clear();

        foreach (int index in _activeCurrent)
        {
            int x = index % Size;
            int y = index / Size;

            TrySpreadTo(x - 1, y);
            TrySpreadTo(x + 1, y);
            TrySpreadTo(x, y - 1);
            TrySpreadTo(x, y + 1);

            _burning[index] = false;
            _terrain[index] = _ashId;
            GrassTileCount--;
            AshTileCount++;
            TilesBurnedCumulative++;

            int bushSlot = _bushIndexAt[index];
            if (bushSlot != -1)
            {
                if (_vegetationCatalog.Get(_bushes[bushSlot].Type).Flammable)
                {
                    RemoveBushAt(bushSlot);
                    VegetationLostToFire++;
                }
            }
            else
            {
                int treeSlot = _treeIndexAt[index];
                if (treeSlot != -1 && _vegetationCatalog.Get(_trees[treeSlot].Type).Flammable)
                {
                    RemoveTreeAt(treeSlot);
                    VegetationLostToFire++;
                }
            }
        }

        List<int> swap = _activeCurrent;
        _activeCurrent = _activeNext;
        _activeNext = swap;

        // Un événement d'incendie se termine quand la liste active
        // (après swap) redevient vide -- flush dans les accumulateurs
        // avant de remettre le compteur à zéro pour le prochain feu.
        if (_activeCurrent.Count == 0 && _currentFireEventTiles > 0)
        {
            _fireEventSizeSum += _currentFireEventTiles;
            _fireEventCount++;
            _fireEventMaxSize = Math.Max(_fireEventMaxSize, _currentFireEventTiles);
            _currentFireEventTiles = 0;
        }
    }

    private void TrySpreadTo(int x, int y)
    {
        if (x < 0 || x >= Size || y < 0 || y >= Size)
        {
            return;
        }

        int index = y * Size + x;
        bool neighborFlammable = _catalog.Get(_terrain[index]).Flammable;

        // Lecture pure (catalogue + terrain), aucune consommation de
        // _rngFire : le tirage ci-dessous reste le seul et unique appel
        // RNG de cette méthode, dans le même ordre qu'avant -- le
        // comportement/déterminisme ne change pas, seule la
        // classification diagnostique est nouvelle.
        if (_rngFire.NextDouble() >= _config.FireSpreadChance)
        {
            if (neighborFlammable && !_burning[index])
            {
                _fireFizzledCount++;
            }
            return;
        }

        if (!neighborFlammable)
        {
            _fireBlockedByTerrainCount++;
        }

        TryIgnite(x, y, _activeNext);
    }

    private void TryIgnite(int x, int y, List<int> active)
    {
        int index = y * Size + x;
        if (_burning[index])
        {
            return;
        }

        byte terrainId = _terrain[index];
        if (!_catalog.Get(terrainId).Flammable)
        {
            return;
        }

        _burning[index] = true;
        active.Add(index);
        _currentFireEventTiles++;
    }

    private void TickAgents(double delta)
    {
        RebuildAgentGrid();

        int group = _tickCounter & 3;
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
                ThinkAgent(ref agent, i);
                if (agent.State == AgentState.Dead)
                {
                    continue;
                }
            }

            MoveAgent(ref agent, i, step);
        }
    }

    // Grille grossière d'agents (session 14) : reconstruite en un seul
    // passage O(AliveCount) au début de chaque tick réel, avant que les
    // agents ne pensent -- ils la consultent (recherche de partenaire,
    // densité locale) mais ne la modifient jamais pendant leur propre
    // tick de pensée (staleness d'un tick, acceptable, même esprit que
    // le snapshot de végétation pour la diffusion).
    private void RebuildAgentGrid()
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

    private int AgentCountInCell(int cell) => _agentGridCellStart[cell + 1] - _agentGridCellStart[cell];

    private int AgentCellIndex(float x, float y)
    {
        int cellX = Math.Clamp((int)(x / _agentGridCellSize), 0, _agentGridWidth - 1);
        int cellY = Math.Clamp((int)(y / _agentGridCellSize), 0, _agentGridHeight - 1);
        return cellY * _agentGridWidth + cellX;
    }

    // Densité de nourriture locale (session 14), même grille que les
    // agents -- reconstruite une fois par tick végétation en scannant
    // _bushes[0..BushCount) une fois (coût déjà budgété : du même ordre
    // que TickVegetationGrowth/Aging qui itèrent déjà tout le tableau
    // chaque tick végétation). Seuls les buissons MÛRS comptent comme
    // nourriture disponible, même critère que CountMatureVegetationOfType.
    private void RebuildFoodDensityGrid()
    {
        Array.Clear(_foodPerCell);

        int matureStage = _vegetationCatalog.Get(_bushTypeId).MatureStage;
        for (int i = 0; i < BushCount; i++)
        {
            ref Vegetation bush = ref _bushes[i];
            if (bush.Stage < matureStage)
            {
                continue;
            }

            int cell = AgentCellIndex(bush.X, bush.Y);
            _foodPerCell[cell]++;
        }
    }

    // Conductivité par cellule (session 14d) : fraction de tuiles HERBE
    // parmi toutes les tuiles de la cellule, plancher 0.05. Balaie tout
    // _terrain une fois (coût du même ordre que TickAshRecovery, déjà
    // un scan complet à cette cadence).
    private void RebuildCellConductivity()
    {
        Array.Clear(_cellGrassCountScratch);
        Array.Clear(_cellTotalCountScratch);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int cell = AgentCellIndex(x, y);
                _cellTotalCountScratch[cell]++;
                if (_terrain[y * Size + x] == _grassId)
                {
                    _cellGrassCountScratch[cell]++;
                }
            }
        }

        for (int c = 0; c < _cellConductivity.Length; c++)
        {
            double fraction = _cellTotalCountScratch[c] > 0 ? (double)_cellGrassCountScratch[c] / _cellTotalCountScratch[c] : 0.0;
            _cellConductivity[c] = Math.Max(0.05, fraction);
        }
    }

    // Diffusion du champ de nourriture (session 14c, terrain-aware
    // depuis s14d) : même principe que TickFire (lire _current, écrire
    // _next, swap), mais dense -- chaque cellule diffuse vers ses 4
    // voisines à chaque passe, sur FoodGradientDiffusionIterations
    // passes. Pondérée par _cellConductivity : le sable/pierre/cendre/
    // eau ne portent jamais de buisson (cf. TrySpreadBushTo, grass
    // uniquement) et conduisent donc mal le signal -- sans ça, le
    // gradient d'un gros amas lointain traverserait un désert intact et
    // attirerait un agent à travers une zone stérile plutôt que vers
    // une source proche (diagnostic s14c/s14d : morts sur sable 18,7%→61%).
    private void RebuildFoodGradient()
    {
        int cellCount = _agentGridWidth * _agentGridHeight;
        for (int c = 0; c < cellCount; c++)
        {
            _foodGradientA[c] = _foodPerCell[c];
        }

        double[] current = _foodGradientA;
        double[] next = _foodGradientB;

        for (int iter = 0; iter < _config.FoodGradientDiffusionIterations; iter++)
        {
            for (int cy = 0; cy < _agentGridHeight; cy++)
            {
                for (int cx = 0; cx < _agentGridWidth; cx++)
                {
                    int c = cy * _agentGridWidth + cx;
                    double weightedSum = 0.0;
                    double weightSum = 0.0;

                    if (cx > 0) { int n = c - 1; weightedSum += current[n] * _cellConductivity[n]; weightSum += _cellConductivity[n]; }
                    if (cx < _agentGridWidth - 1) { int n = c + 1; weightedSum += current[n] * _cellConductivity[n]; weightSum += _cellConductivity[n]; }
                    if (cy > 0) { int n = c - _agentGridWidth; weightedSum += current[n] * _cellConductivity[n]; weightSum += _cellConductivity[n]; }
                    if (cy < _agentGridHeight - 1) { int n = c + _agentGridWidth; weightedSum += current[n] * _cellConductivity[n]; weightSum += _cellConductivity[n]; }

                    double avgNeighbors = weightSum > 0 ? weightedSum / weightSum : current[c];
                    next[c] = current[c] + _config.FoodGradientDiffusionRate * _cellConductivity[c] * (avgNeighbors - current[c]);
                }
            }

            (current, next) = (next, current);
        }

        _foodGradient = current;
    }

    private void ThinkAgent(ref Agent agent, int index)
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
        if (agent.PregnantUntil != 0 && (uint)_tickCounter >= agent.PregnantUntil)
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
            TryReproduce(ref agent, index);
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

    // Récolte (session 18) : déclenchée par le VIDE du pool du CLAN,
    // jamais par agent.Hunger -- c'est le point de la session, la
    // décision de cueillir est une décision de clan, pas individuelle.
    // Chance progressive (jamais un seuil dur, même philosophie que le
    // frein de reproduction) : proportionnelle au vide du pool rapporté
    // à un pool cible par tête. BFS/gradient réutilisés tels quels
    // (inchangés depuis s14c).
    private void TryStartHarvesting(ref Agent agent, int index)
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

        if (TryFindNearestMatureBush(currentX, currentY, _agentPaths[index]))
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

    // Reproduction (session 14) : rencontre par RAYON, pas par adjacence
    // -- aucun déplacement, aucun nouvel état FSM, aucune réservation de
    // partenaire. Le frein est PROGRESSIF (jamais un seuil dur) pour
    // éviter les dents de scie boom/famine/effondrement : la chance de
    // conception décroît linéairement avec la nourriture locale
    // rapportée à la population locale.
    private void TryReproduce(ref Agent agent, int index)
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
        int foodInCell = _foodPerCell[cell];
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

        agent.PregnantUntil = (uint)_tickCounter + species.GestationTicks;
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
    private void TryGiveBirth(ref Agent mother)
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
        if (x >= 0 && x < Size && y >= 0 && y < Size && IsSafeForBirth(x, y))
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
        return _catalog.Get(_terrain[y * Size + x]).Walkable && !_burning[y * Size + x];
    }

    private void MoveAgent(ref Agent agent, int index, float step)
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
            HarvestTick(ref agent);
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

        if (TryGetVegetationAt(agent.TargetX, agent.TargetY, out Vegetation bush) &&
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

    // Récolte étalée sur plusieurs ticks (session 18) : chaque tick
    // retire au stock du buisson et dépose DIRECTEMENT dans le pool du
    // CLAN -- jamais dans la faim du cueilleur (récolter et manger sont
    // deux actions distinctes désormais). Aucune cargaison en transit :
    // ce qui est extrait est déjà dans le pool au tick où c'est extrait
    // (cf. plan, CLAUDE.md section Ressources). Générique par
    // conception (servira plus tard au bois/à la pierre).
    private void HarvestTick(ref Agent agent)
    {
        int index = agent.TargetY * Size + agent.TargetX;
        int slot = _bushIndexAt[index];

        if (slot == -1)
        {
            // Le buisson a disparu (vidé ou brûlé) pendant que l'agent
            // récoltait déjà (concurrence sans réservation, hors scope).
            agent.State = AgentState.Idle;
            return;
        }

        int harvested = Math.Min(_config.HarvestAmountPerTick, _bushes[slot].FoodRemaining);
        _bushes[slot].FoodRemaining -= harvested;

        ref Clan clan = ref GetClanById(agent.ClanId);
        clan.FoodPool += harvested;
        _clanFoodHarvestedCumulative[ClanIndex(agent.ClanId)] += harvested;

        if (_bushes[slot].FoodRemaining <= 0)
        {
            RemoveBushAt(slot);
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

    // Manger depuis le pool du clan (session 18, refondu en effet passif
    // session 19c) : AUCUN déplacement, où que soit l'agent, quel que
    // soit son état -- appelé inconditionnellement depuis MoveAgent
    // chaque tick réel, pour tout agent vivant. Si le pool est vide,
    // l'agent ne mange pas ce tick (continue d'avoir faim, retentera au
    // suivant) -- SANS jamais bloquer son état : contrairement à
    // l'ancien EatFromPoolTick, il n'y a plus de transition vers/depuis
    // un état "en train de manger" (supprimé, cf. plan s19c), donc plus
    // aucun moyen pour toute une population affamée de se retrouver
    // collectivement incapable de repartir cueillir.
    private void ApplyPassiveEating(ref Agent agent)
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

    private void SetWaypoint(ref Agent agent, int waypointIndex)
    {
        int targetX = waypointIndex % Size;
        int targetY = waypointIndex / Size;
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
    private bool TryFollowFoodGradient(ref Agent agent)
    {
        int currentX = (int)MathF.Floor(agent.X);
        int currentY = (int)MathF.Floor(agent.Y);
        int cell = AgentCellIndex(agent.X, agent.Y);
        int cellX = cell % _agentGridWidth;
        int cellY = cell / _agentGridWidth;

        double bestValue = _foodGradient[cell];
        int bestDx = 0;
        int bestDy = 0;
        bool found = false;

        if (cellX > 0 && _foodGradient[cell - 1] > bestValue)
        {
            bestValue = _foodGradient[cell - 1];
            bestDx = -1;
            bestDy = 0;
            found = true;
        }
        if (cellX < _agentGridWidth - 1 && _foodGradient[cell + 1] > bestValue)
        {
            bestValue = _foodGradient[cell + 1];
            bestDx = 1;
            bestDy = 0;
            found = true;
        }
        if (cellY > 0 && _foodGradient[cell - _agentGridWidth] > bestValue)
        {
            bestValue = _foodGradient[cell - _agentGridWidth];
            bestDx = 0;
            bestDy = -1;
            found = true;
        }
        if (cellY < _agentGridHeight - 1 && _foodGradient[cell + _agentGridWidth] > bestValue)
        {
            bestValue = _foodGradient[cell + _agentGridWidth];
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

        if (targetX < 0 || targetX >= Size || targetY < 0 || targetY >= Size ||
            !_catalog.Get(_terrain[targetY * Size + targetX]).Walkable)
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
    private void TryStartMoving(ref Agent agent)
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
            direction = (int)(_rngAgents.NextUInt64() >> 62);
            agent.WanderDirection = (byte)direction;
            agent.WanderTicksRemaining = _config.WanderPersistenceTicks;
        }

        int dx = direction == 0 ? -1 : direction == 1 ? 1 : 0;
        int dy = direction == 2 ? -1 : direction == 3 ? 1 : 0;

        int targetX = currentX + dx;
        int targetY = currentY + dy;

        if (targetX < 0 || targetX >= Size || targetY < 0 || targetY >= Size ||
            !_catalog.Get(_terrain[targetY * Size + targetX]).Walkable)
        {
            // Direction bloquée (bord de carte / obstacle) : force un
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

    private bool TryFindNearestMatureBush(int startX, int startY, List<int> outputPath)
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

            int bushSlot = _bushIndexAt[worldY * Size + worldX];
            if (bushSlot != -1 && _bushes[bushSlot].Stage >= matureStage)
            {
                ReconstructPath(current, originX, originY, outputPath);
                return true;
            }

            TryEnqueue(lx - 1, ly, current, originX, originY);
            TryEnqueue(lx + 1, ly, current, originX, originY);
            TryEnqueue(lx, ly - 1, current, originX, originY);
            TryEnqueue(lx, ly + 1, current, originX, originY);
        }

        return false;
    }

    private void TryEnqueue(int lx, int ly, int fromLocal, int originX, int originY)
    {
        if (lx < 0 || lx >= _boxSide || ly < 0 || ly >= _boxSide)
        {
            return;
        }

        int worldX = originX + lx;
        int worldY = originY + ly;
        if (worldX < 0 || worldX >= Size || worldY < 0 || worldY >= Size)
        {
            return;
        }

        if (!_catalog.Get(_terrain[worldY * Size + worldX]).Walkable)
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
            outputPath.Add((originY + ly) * Size + (originX + lx));
            node = _searchCameFrom[node];
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
        tileX = Math.Clamp(tileX, 0, Size - 1);
        tileY = Math.Clamp(tileY, 0, Size - 1);

        double distance = DistanceToNearestMatureBush(tileX, tileY);
        _deathDistanceHistogram[DistanceBucket(distance)]++;

        byte terrainId = _terrain[tileY * Size + tileX];
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

    private void CleanupDeadAgents()
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
    // même raisonnement que _minAliveCountEverObserved (un
    // échantillonnage périodique peut rater un creux court, cf. s14c).
    // stackalloc : ClanCount est petit et fixe cette session, aucune
    // allocation tas dans le tick.
    private void UpdateClanMinAliveObserved()
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

    // Un clan par race disponible, cyclé (une seule race aujourd'hui,
    // donc tous les clans démarrent avec la même -- reste conforme à
    // "un clan = une race", qui n'exige pas l'inverse).
    private Clan[] CreateClans(int count, SpeciesCatalog speciesCatalog)
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

    private int ClanIndex(uint id) => _clanIndexById[id];

    private ref Clan GetClanById(uint id) => ref _clans[ClanIndex(id)];

    // Population utilisée pour calculer le pool CIBLE du clan (récolte,
    // frein de reproduction) -- plafonnée à ReferenceClanPopulation
    // (cf. commentaire sur le champ de config) pour qu'un clan qui
    // dépasse cette taille ressente une vraie pression de rareté au
    // lieu de voir son objectif grandir indéfiniment avec lui.
    private int ClanPopulationForTarget(uint clanId)
    {
        int actual = Math.Max(1, _clanPopulation[ClanIndex(clanId)]);
        return Math.Min(actual, _config.ReferenceClanPopulation);
    }

    // Spawn groupé par clan (session 18) : disperser uniformément sur
    // toute la carte comme avant diviserait la densité de partenaires
    // DU MÊME CLAN par InitialClanCount (les autres agents proches sont
    // désormais hors-clan et filtrés par TryFindMate) -- effet Allee
    // quasi garanti. Chaque clan est concentré sur un disque de rayon
    // Size*ClanSpawnRadiusFraction autour d'un centre tiré au hasard,
    // pour restaurer une densité locale par clan comparable au cas déjà
    // validé (199 agents dispersés sur toute la carte, s15) -- calcul
    // détaillé dans le plan de session.
    private void SpawnAgents(int count)
    {
        int clanCount = _clans.Length;
        int perClan = count / clanCount;
        int remainder = count - perClan * clanCount;
        double radius = Size * _config.ClanSpawnRadiusFraction;

        int spawned = 0;

        for (int c = 0; c < clanCount && spawned < _agents.Length; c++)
        {
            int clanTarget = perClan + (c < remainder ? 1 : 0);
            if (clanTarget == 0 || !TryPickClusterCenter(out int centerX, out int centerY))
            {
                continue;
            }

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
                if (x < 0 || x >= Size || y < 0 || y >= Size || !_catalog.Get(_terrain[y * Size + x]).Walkable)
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

    // Garde-fou technique (pas un réglage de gameplay), même esprit que
    // MaxSpawnAttemptsPerAgent : borne le rejection sampling du centre
    // de grappe sur une carte quasi dégénérée.
    private const int MaxClusterCenterAttempts = 100;

    private bool TryPickClusterCenter(out int x, out int y)
    {
        for (int attempt = 0; attempt < MaxClusterCenterAttempts; attempt++)
        {
            int candidateX = (int)(_rngWorldGen.NextDouble() * Size);
            int candidateY = (int)(_rngWorldGen.NextDouble() * Size);
            if (_catalog.Get(_terrain[candidateY * Size + candidateX]).Walkable)
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

    private uint RollLifespan(SpeciesType species)
    {
        if (species.LifespanVarianceTicks == 0)
        {
            return species.LifespanTicks;
        }

        double roll = (_rngAgents.NextDouble() * (species.LifespanVarianceTicks * 2 + 1)) - species.LifespanVarianceTicks;
        double lifespan = species.LifespanTicks + roll;
        return (uint)Math.Max(1, lifespan);
    }

    private int ComputeDeathTick(VegetationType typeInfo)
    {
        if (typeInfo.LifespanTicks <= 0)
        {
            return -1;
        }

        int variance = typeInfo.LifespanVarianceTicks;
        int roll = variance > 0 ? (int)(_rngVegetation.NextDouble() * (variance * 2 + 1)) - variance : 0;
        int lifespan = Math.Max(1, typeInfo.LifespanTicks + roll);
        return _tickCounter + lifespan;
    }

    private void SpawnVegetationOfType(int x, int y, byte type)
    {
        if (type == _bushTypeId)
        {
            SpawnBush(x, y);
        }
        else if (type == _treeTypeId)
        {
            SpawnTree(x, y);
        }
    }

    private void SpawnBush(int x, int y)
    {
        int index = y * Size + x;
        int slot = BushCount;
        VegetationType typeInfo = _vegetationCatalog.Get(_bushTypeId);

        _bushes[slot] = new Vegetation
        {
            X = x,
            Y = y,
            Type = _bushTypeId,
            Stage = 0,
            FoodRemaining = typeInfo.FoodValue,
            DeathTick = ComputeDeathTick(typeInfo),
        };
        _bushIndexAt[index] = slot;
        BushCount++;
    }

    private void SpawnTree(int x, int y)
    {
        int index = y * Size + x;
        int slot = TreeCount;
        VegetationType typeInfo = _vegetationCatalog.Get(_treeTypeId);

        _trees[slot] = new Vegetation
        {
            X = x,
            Y = y,
            Type = _treeTypeId,
            Stage = 0,
            FoodRemaining = typeInfo.FoodValue,
            DeathTick = ComputeDeathTick(typeInfo),
        };
        _treeIndexAt[index] = slot;
        TreeCount++;
    }

    private void RemoveBushAt(int slot)
    {
        Vegetation removed = _bushes[slot];
        int removedIndex = removed.Y * Size + removed.X;
        _bushIndexAt[removedIndex] = -1;
        _vegetationClearedTick[removedIndex] = _tickCounter;

        BushCount--;
        if (slot != BushCount)
        {
            Vegetation moved = _bushes[BushCount];
            _bushes[slot] = moved;
            _bushIndexAt[moved.Y * Size + moved.X] = slot;
        }
    }

    private void RemoveTreeAt(int slot)
    {
        Vegetation removed = _trees[slot];
        int removedIndex = removed.Y * Size + removed.X;
        _treeIndexAt[removedIndex] = -1;
        _vegetationClearedTick[removedIndex] = _tickCounter;

        TreeCount--;
        if (slot != TreeCount)
        {
            Vegetation moved = _trees[TreeCount];
            _trees[slot] = moved;
            _treeIndexAt[moved.Y * Size + moved.X] = slot;
        }
    }

    private void TickVegetationGrowth()
    {
        GrowArray(_bushes, BushCount, _bushTypeId);
        GrowArray(_trees, TreeCount, _treeTypeId);
    }

    private void GrowArray(Vegetation[] array, int count, byte type)
    {
        int matureStage = _vegetationCatalog.Get(type).MatureStage;
        for (int i = 0; i < count; i++)
        {
            ref Vegetation veg = ref array[i];
            if (veg.Stage < matureStage)
            {
                veg.Stage++;
            }
        }
    }

    private void TickVegetationAging()
    {
        int i = 0;
        while (i < BushCount)
        {
            if (_bushes[i].DeathTick != -1 && _tickCounter >= _bushes[i].DeathTick)
            {
                RemoveBushAt(i);
            }
            else
            {
                i++;
            }
        }

        i = 0;
        while (i < TreeCount)
        {
            if (_trees[i].DeathTick != -1 && _tickCounter >= _trees[i].DeathTick)
            {
                RemoveTreeAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    // Repousse locale (session 13) : remplace l'ancien scan global i.i.d.
    // (une repousse pouvait apparaître n'importe où sur la carte, jamais
    // préférentiellement près d'un buisson existant -- exactement le bug
    // diagnostiqué en s12). Même pattern de diffusion que le feu, mais un
    // seul passage par tick végétation (pas de chaîne dans le même tick :
    // BushCount/TreeCount sont capturés en snapshot avant chaque boucle).
    private void TickVegetationSpread()
    {
        SpreadBushesLocally();
        SpreadTreesLocally();
        SpawnSpontaneously();
    }

    private void SpreadBushesLocally()
    {
        int count = BushCount;
        for (int i = 0; i < count; i++)
        {
            int x = _bushes[i].X;
            int y = _bushes[i].Y;
            TrySpreadBushTo(x - 1, y);
            TrySpreadBushTo(x + 1, y);
            TrySpreadBushTo(x, y - 1);
            TrySpreadBushTo(x, y + 1);
        }
    }

    private void TrySpreadBushTo(int x, int y)
    {
        if (x < 0 || x >= Size || y < 0 || y >= Size || BushCount >= _bushes.Length)
        {
            return;
        }

        int index = y * Size + x;
        if (_terrain[index] != _grassId || _bushIndexAt[index] != -1 || _treeIndexAt[index] != -1)
        {
            return;
        }

        if (_tickCounter - _vegetationClearedTick[index] < _config.VegetationRegrowthDelayTicks)
        {
            return;
        }

        if (_rngVegetation.NextDouble() >= _config.VegetationSpreadChance)
        {
            return;
        }

        SpawnBush(x, y);
    }

    private void SpreadTreesLocally()
    {
        int count = TreeCount;
        for (int i = 0; i < count; i++)
        {
            int x = _trees[i].X;
            int y = _trees[i].Y;
            TrySpreadTreeTo(x - 1, y);
            TrySpreadTreeTo(x + 1, y);
            TrySpreadTreeTo(x, y - 1);
            TrySpreadTreeTo(x, y + 1);
        }
    }

    private void TrySpreadTreeTo(int x, int y)
    {
        if (x < 0 || x >= Size || y < 0 || y >= Size || TreeCount >= _trees.Length)
        {
            return;
        }

        int index = y * Size + x;
        if (_terrain[index] != _grassId || _bushIndexAt[index] != -1 || _treeIndexAt[index] != -1)
        {
            return;
        }

        if (_tickCounter - _vegetationClearedTick[index] < _config.VegetationRegrowthDelayTicks)
        {
            return;
        }

        if (_rngVegetation.NextDouble() >= _config.TreeSpreadChance)
        {
            return;
        }

        SpawnTree(x, y);
    }

    // Germination spontanée résiduelle (piège symétrique) : sans elle,
    // une région entièrement rasée (aucun buisson/arbre voisin pour
    // diffuser) ne pourrait jamais repartir. Taux volontairement bas par
    // rapport à la diffusion locale -- un filet de sécurité, pas le
    // mécanisme principal de repousse. Même scan tournant que l'ancien
    // mécanisme (évite le biais spatial fixé en s11).
    private void SpawnSpontaneously()
    {
        int tileCount = _terrain.Length;
        int startIndex = (int)(_rngVegetation.NextDouble() * tileCount);

        for (int offset = 0; offset < tileCount; offset++)
        {
            if (BushCount >= _bushes.Length && TreeCount >= _trees.Length)
            {
                return;
            }

            int index = (startIndex + offset) % tileCount;

            if (_terrain[index] != _grassId || _bushIndexAt[index] != -1 || _treeIndexAt[index] != -1)
            {
                continue;
            }

            if (_tickCounter - _vegetationClearedTick[index] < _config.VegetationRegrowthDelayTicks)
            {
                continue;
            }

            int x = index % Size;
            int y = index / Size;

            if (BushCount < _bushes.Length && _rngVegetation.NextDouble() < _config.VegetationSpontaneousChance)
            {
                SpawnBush(x, y);
            }
            else if (TreeCount < _trees.Length && _rngVegetation.NextDouble() < _config.TreeSpontaneousChance)
            {
                SpawnTree(x, y);
            }
        }
    }

    private void TickAshRecovery()
    {
        for (int i = 0; i < _terrain.Length; i++)
        {
            if (_terrain[i] != _ashId)
            {
                continue;
            }

            if (_rngVegetation.NextDouble() < _config.AshToGrassChance)
            {
                _terrain[i] = _grassId;
                AshTileCount--;
                GrassTileCount++;
            }
        }
    }

    private void GenerateTerrain()
    {
        var noise = new PerlinNoise(_rngWorldGen);

        if (!_catalog.TryGetId("water", out byte water) ||
            !_catalog.TryGetId("sand", out byte sand) ||
            !_catalog.TryGetId("grass", out byte grass) ||
            !_catalog.TryGetId("stone", out byte stone))
        {
            throw new ArgumentException("terrain catalog must define water, sand, grass and stone", nameof(_catalog));
        }

        double frequency = 1.0 / (Size / _config.TerrainFeaturesAcrossMap);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double elevation = noise.Sample(x * frequency, y * frequency);
                byte terrain =
                    elevation < _config.TerrainWaterThreshold ? water :
                    elevation < _config.TerrainSandThreshold ? sand :
                    elevation < _config.TerrainGrassThreshold ? grass :
                    stone;
                _terrain[y * Size + x] = terrain;
            }
        }
    }
}
