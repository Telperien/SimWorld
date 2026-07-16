namespace Simulation;

public enum DeathCause : byte
{
    Hunger = 0,
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
    private readonly int[] _deathsByCause = new int[1];
    private int _tickCounter;
    private uint _nextAgentId;

    // --- Diagnostic de mort (session 12) : compteurs cumulés, jamais
    // lus par une décision, exclus de Hash() comme MealsEaten/DeathCause. ---
    // Bornes de buckets : le seuil 33 correspond exactement à _boxSide
    // (portée du BFS de recherche de nourriture), pour lire d'un coup
    // d'œil si les morts sont dans ou hors de portée.
    private static readonly double[] DeathDistanceBucketBounds = { 5, 10, 15, 20, 25, 33, 50, 100, 200 };
    private readonly int[] _deathDistanceHistogram = new int[DeathDistanceBucketBounds.Length + 1];
    private readonly int[] _deathTerrainHistogram = new int[256];
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
    public int MealsEaten { get; private set; }

    public int TilesBurnedCumulative { get; private set; }

    public int VegetationLostToFire { get; private set; }

    public static IReadOnlyList<double> DeathDistanceBucketUpperBounds => DeathDistanceBucketBounds;

    public int[] GetDeathDistanceHistogram() => (int[])_deathDistanceHistogram.Clone();

    public int[] GetDeathTerrainHistogram() => (int[])_deathTerrainHistogram.Clone();

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

    public World(int seed, int size, TerrainCatalog catalog, VegetationCatalog vegetationCatalog, SimulationConfig config)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException($"size must be a power of two greater than zero, got {size}", nameof(size));
        }

        Size = size;
        _catalog = catalog;
        _vegetationCatalog = vegetationCatalog;
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

        _agents = new Agent[(int)(config.AgentDensity * size * size)];
        _agentPaths = new List<int>[_agents.Length];
        for (int i = 0; i < _agentPaths.Length; i++)
        {
            _agentPaths[i] = new List<int>();
        }

        SpawnAgents();

        _bushes = new Vegetation[(int)(config.BushDensity * size * size)];
        _bushIndexAt = new int[size * size];
        Array.Fill(_bushIndexAt, -1);

        _trees = new Vegetation[(int)(config.TreeDensity * size * size)];
        _treeIndexAt = new int[size * size];
        Array.Fill(_treeIndexAt, -1);

        _vegetationClearedTick = new int[size * size];
        Array.Fill(_vegetationClearedTick, NeverClearedSentinel);
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

    public void SetAgentHunger(int index, byte hunger) => _agents[index].Hunger = hunger;

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
        }

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
    }

    private void TrySpreadTo(int x, int y)
    {
        if (x < 0 || x >= Size || y < 0 || y >= Size)
        {
            return;
        }

        if (_rngFire.NextDouble() >= _config.FireSpreadChance)
        {
            return;
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
    }

    private void TickAgents(double delta)
    {
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
                case AgentState.Eating: agent.TicksEating++; break;
            }

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

    private void ThinkAgent(ref Agent agent, int index)
    {
        agent.Hunger = (byte)Math.Min(255, agent.Hunger + _config.HungerIncreasePerThink);

        if (agent.Hunger >= 255)
        {
            agent.State = AgentState.Dead;
            return;
        }

        if (agent.State == AgentState.Seeking || agent.State == AgentState.Eating)
        {
            return;
        }

        if (agent.Hunger >= _config.HungerSeekThreshold)
        {
            if (agent.SeekCooldown > 0)
            {
                agent.SeekCooldown--;
            }
            else
            {
                int currentX = (int)MathF.Floor(agent.X);
                int currentY = (int)MathF.Floor(agent.Y);

                if (TryFindNearestMatureBush(currentX, currentY, _agentPaths[index]))
                {
                    List<int> path = _agentPaths[index];
                    if (path.Count == 0)
                    {
                        agent.State = AgentState.Eating;
                        agent.SeekCooldown = 0;
                        agent.SearchFailureStreak = 0;
                        agent.HungerAtLastMealStart = agent.Hunger;
                    }
                    else
                    {
                        SetWaypoint(ref agent, path[^1]);
                        path.RemoveAt(path.Count - 1);
                        agent.State = AgentState.Seeking;
                        agent.SearchFailureStreak = 0;
                    }
                    return;
                }

                agent.SeekCooldown = _config.SeekFailureCooldownThinkTicks;
                agent.SearchFailureStreak++;
            }
        }

        // Errance : atteinte quand l'agent n'est pas affamé, ou qu'il
        // l'est mais patiente son cooldown après une recherche ratée —
        // jamais figé en attendant (cf. plan, cooldown de famine).
        if (agent.State == AgentState.Idle && _rngAgents.NextDouble() < _config.IdleMoveChance)
        {
            TryStartMoving(ref agent);
        }
    }

    private void MoveAgent(ref Agent agent, int index, float step)
    {
        if (agent.State == AgentState.Eating)
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
            agent.State = AgentState.Eating;
            agent.HungerAtLastMealStart = agent.Hunger;
            MealsEaten++;
        }
        else
        {
            agent.State = AgentState.Idle;
        }
    }

    // Récolte étalée sur plusieurs ticks : chaque tick retire la même
    // quantité au stock du buisson ET à la faim de l'agent (récolter et
    // se nourrir sont le même geste). Générique par conception (servira
    // plus tard au bois/à la pierre, pas codé cette session).
    private void HarvestTick(ref Agent agent)
    {
        int index = agent.TargetY * Size + agent.TargetX;
        int slot = _bushIndexAt[index];

        if (slot == -1)
        {
            // Le buisson a disparu (vidé ou brûlé) pendant que l'agent
            // mangeait déjà (concurrence sans réservation, hors scope).
            agent.State = AgentState.Idle;
            return;
        }

        int harvested = Math.Min(_config.HarvestAmountPerTick, _bushes[slot].FoodRemaining);
        _bushes[slot].FoodRemaining -= harvested;
        agent.Hunger = (byte)Math.Max(0, agent.Hunger - harvested);

        if (_bushes[slot].FoodRemaining <= 0)
        {
            RemoveBushAt(slot);
            agent.State = AgentState.Idle;
            return;
        }

        if (agent.Hunger == 0)
        {
            agent.State = AgentState.Idle;
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
                _deathsByCause[(byte)DeathCause.Hunger]++;
                RecordDeathDiagnostics(ref _agents[i]);

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

    private void SpawnAgents()
    {
        int spawned = 0;
        int attempts = 0;
        int maxAttempts = _agents.Length * MaxSpawnAttemptsPerAgent;

        while (spawned < _agents.Length && attempts < maxAttempts)
        {
            attempts++;

            int x = (int)(_rngWorldGen.NextDouble() * Size);
            int y = (int)(_rngWorldGen.NextDouble() * Size);

            if (!_catalog.Get(_terrain[y * Size + x]).Walkable)
            {
                continue;
            }

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
                Species = 0,
                Hunger = 0,
                Facing = 0,
                SeekCooldown = 0,
                WanderDirection = 0,
                WanderTicksRemaining = 0,
                SearchFailureStreak = 0,
                TicksIdle = 0,
                TicksMoving = 0,
                TicksSeeking = 0,
                TicksEating = 0,
                HungerAtLastMealStart = 0,
            };
            spawned++;
        }

        AliveCount = spawned;
        AgentSpawnCapped = spawned < _agents.Length;
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

        if (_rngVegetation.NextDouble() >= _config.VegetationSpreadChance)
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
            else if (TreeCount < _trees.Length && _rngVegetation.NextDouble() < _config.VegetationSpontaneousChance)
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
