namespace Simulation;

public sealed class World
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const double SpreadChance = 0.5;
    private const double AgentDensity = 0.00076;
    private const double VegetationDensity = 0.05;
    private const int VegetationTickInterval = 30;
    private const double IdleMoveChance = 0.1;
    private const float MoveSpeed = 4f;
    private const byte HungerIncreasePerThink = 1;
    private const byte HungerSeekThreshold = 150;
    private const byte HungerDecreasePerEatTick = 8;
    private const int MaxSearchRadius = 16;
    private const int BoxSide = MaxSearchRadius * 2 + 1;

    // Identifiants arbitraires mais fixes pour dériver un seed par flux
    // depuis le seed principal (cf. DeriveSeed).
    private const ulong WorldGenStreamId = 1;
    private const ulong FireStreamId = 2;
    private const ulong AgentsStreamId = 3;
    private const ulong VegetationStreamId = 4;

    private readonly byte[] _terrain;
    private readonly bool[] _burning;
    private readonly Agent[] _agents;
    private readonly List<int>[] _agentPaths;
    private readonly Vegetation[] _vegetation;
    private readonly int[] _vegetationIndexAt;
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
    private int _tickCounter;
    private uint _nextAgentId;

    private List<int> _activeCurrent = new();
    private List<int> _activeNext = new();

    // Buffers de travail pour la recherche BFS : entièrement écrasés (via
    // generation-stamp) à chaque appel, jamais lus entre deux appels.
    // Exclus de Hash() volontairement (cf. CLAUDE.md, Déterminisme).
    private readonly int[] _searchGeneration = new int[BoxSide * BoxSide];
    private readonly int[] _searchCameFrom = new int[BoxSide * BoxSide];
    private readonly List<int> _searchQueue = new();
    private int _searchGenerationCounter;

    public int Size { get; }

    public int AgentCapacity => _agents.Length;

    public int AliveCount { get; private set; }

    public int VegetationCount { get; private set; }

    public World(int seed, int size, TerrainCatalog catalog, VegetationCatalog vegetationCatalog)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException($"size must be a power of two greater than zero, got {size}", nameof(size));
        }

        Size = size;
        _catalog = catalog;
        _vegetationCatalog = vegetationCatalog;
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

        GenerateTerrain();

        _agents = new Agent[(int)(AgentDensity * size * size)];
        _agentPaths = new List<int>[_agents.Length];
        for (int i = 0; i < _agentPaths.Length; i++)
        {
            _agentPaths[i] = new List<int>();
        }

        SpawnAgents();

        _vegetation = new Vegetation[(int)(VegetationDensity * size * size)];
        _vegetationIndexAt = new int[size * size];
        Array.Fill(_vegetationIndexAt, -1);
    }

    public byte GetTerrainId(int x, int y) => _terrain[y * Size + x];

    public void SetTerrainId(int x, int y, byte id) => _terrain[y * Size + x] = id;

    public bool IsBurning(int x, int y) => _burning[y * Size + x];

    public Agent GetAgent(int index) => _agents[index];

    public Vegetation GetVegetation(int index) => _vegetation[index];

    public bool TryGetVegetationAt(int x, int y, out Vegetation vegetation)
    {
        int slot = _vegetationIndexAt[y * Size + x];
        if (slot == -1)
        {
            vegetation = default;
            return false;
        }

        vegetation = _vegetation[slot];
        return true;
    }

    public void ForceSpawnVegetation(int x, int y, byte type, byte stage)
    {
        int index = y * Size + x;
        int existingSlot = _vegetationIndexAt[index];
        if (existingSlot != -1)
        {
            RemoveVegetationAt(existingSlot);
        }

        SpawnVegetation(x, y, type);
        _vegetation[_vegetationIndexAt[index]].Stage = stage;
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

        if (_tickCounter % VegetationTickInterval == 0)
        {
            TickVegetationGrowth();
            TickVegetationSpread();
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
            Mix(ref hash, agent.EatingTimer);
            Mix(ref hash, agent.Facing);

            List<int> path = _agentPaths[i];
            Mix(ref hash, (ulong)path.Count);
            foreach (int waypoint in path)
            {
                Mix(ref hash, (uint)waypoint);
            }
        }

        Mix(ref hash, (ulong)VegetationCount);

        for (int i = 0; i < VegetationCount; i++)
        {
            ref Vegetation veg = ref _vegetation[i];
            Mix(ref hash, (uint)veg.X);
            Mix(ref hash, (uint)veg.Y);
            Mix(ref hash, veg.Type);
            Mix(ref hash, veg.Stage);
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

            int vegSlot = _vegetationIndexAt[index];
            if (vegSlot != -1 && _vegetationCatalog.Get(_vegetation[vegSlot].Type).Flammable)
            {
                RemoveVegetationAt(vegSlot);
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

        if (_rngFire.NextDouble() >= SpreadChance)
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
        float step = MoveSpeed * (float)delta;

        for (int i = 0; i < AliveCount; i++)
        {
            ref Agent agent = ref _agents[i];

            if (agent.State == AgentState.Dead)
            {
                continue;
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
        agent.Hunger = (byte)Math.Min(255, agent.Hunger + HungerIncreasePerThink);

        if (agent.Hunger >= 255)
        {
            agent.State = AgentState.Dead;
            return;
        }

        if (agent.State == AgentState.Seeking || agent.State == AgentState.Eating)
        {
            return;
        }

        if (agent.Hunger >= HungerSeekThreshold)
        {
            int currentX = (int)MathF.Floor(agent.X);
            int currentY = (int)MathF.Floor(agent.Y);

            if (TryFindNearestMatureBush(currentX, currentY, _agentPaths[index]))
            {
                List<int> path = _agentPaths[index];
                if (path.Count == 0)
                {
                    StartEatingAt(ref agent, currentX, currentY);
                }
                else
                {
                    SetWaypoint(ref agent, path[^1]);
                    path.RemoveAt(path.Count - 1);
                    agent.State = AgentState.Seeking;
                }
            }

            return;
        }

        if (agent.State == AgentState.Idle && _rngAgents.NextDouble() < IdleMoveChance)
        {
            TryStartMoving(ref agent);
        }
    }

    private void MoveAgent(ref Agent agent, int index, float step)
    {
        if (agent.State == AgentState.Eating)
        {
            agent.Hunger = (byte)Math.Max(0, agent.Hunger - HungerDecreasePerEatTick);
            agent.EatingTimer--;
            if (agent.EatingTimer == 0)
            {
                agent.State = AgentState.Idle;
            }
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
            StartEatingAt(ref agent, agent.TargetX, agent.TargetY);
        }
        else
        {
            agent.State = AgentState.Idle;
        }
    }

    private void StartEatingAt(ref Agent agent, int x, int y)
    {
        int slot = _vegetationIndexAt[y * Size + x];
        int foodValue = 0;
        if (slot != -1)
        {
            foodValue = _vegetationCatalog.Get(_vegetation[slot].Type).FoodValue;
            _vegetation[slot].Stage = 0;
        }

        agent.State = AgentState.Eating;
        agent.EatingTimer = (byte)Math.Clamp(foodValue / HungerDecreasePerEatTick, 1, 255);
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

    private void TryStartMoving(ref Agent agent)
    {
        int currentX = (int)MathF.Floor(agent.X);
        int currentY = (int)MathF.Floor(agent.Y);

        int direction = (int)(_rngAgents.NextUInt64() >> 62);
        int dx = direction == 0 ? -1 : direction == 1 ? 1 : 0;
        int dy = direction == 2 ? -1 : direction == 3 ? 1 : 0;

        int targetX = currentX + dx;
        int targetY = currentY + dy;

        if (targetX < 0 || targetX >= Size || targetY < 0 || targetY >= Size)
        {
            return;
        }

        if (!_catalog.Get(_terrain[targetY * Size + targetX]).Walkable)
        {
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

        int originX = startX - MaxSearchRadius;
        int originY = startY - MaxSearchRadius;
        int startLocal = (startY - originY) * BoxSide + (startX - originX);

        _searchGeneration[startLocal] = _searchGenerationCounter;
        _searchCameFrom[startLocal] = -1;
        _searchQueue.Add(startLocal);

        int matureStage = _vegetationCatalog.Get(_bushTypeId).MatureStage;

        int head = 0;
        while (head < _searchQueue.Count)
        {
            int current = _searchQueue[head];
            head++;

            int lx = current % BoxSide;
            int ly = current / BoxSide;
            int worldX = originX + lx;
            int worldY = originY + ly;

            int vegSlot = _vegetationIndexAt[worldY * Size + worldX];
            if (vegSlot != -1 && _vegetation[vegSlot].Type == _bushTypeId && _vegetation[vegSlot].Stage >= matureStage)
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
        if (lx < 0 || lx >= BoxSide || ly < 0 || ly >= BoxSide)
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

        int local = ly * BoxSide + lx;
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
            int lx = node % BoxSide;
            int ly = node / BoxSide;
            outputPath.Add((originY + ly) * Size + (originX + lx));
            node = _searchCameFrom[node];
        }
    }

    private void CleanupDeadAgents()
    {
        int aliveCount = AliveCount;
        int i = 0;
        while (i < aliveCount)
        {
            if (_agents[i].State == AgentState.Dead)
            {
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
        while (spawned < _agents.Length)
        {
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
                EatingTimer = 0,
                Facing = 0,
            };
            spawned++;
        }

        AliveCount = spawned;
    }

    private void SpawnVegetation(int x, int y, byte type)
    {
        int index = y * Size + x;
        int slot = VegetationCount;
        _vegetation[slot] = new Vegetation { X = x, Y = y, Type = type, Stage = 0 };
        _vegetationIndexAt[index] = slot;
        VegetationCount++;
    }

    private void RemoveVegetationAt(int slot)
    {
        Vegetation removed = _vegetation[slot];
        _vegetationIndexAt[removed.Y * Size + removed.X] = -1;

        VegetationCount--;
        if (slot != VegetationCount)
        {
            Vegetation moved = _vegetation[VegetationCount];
            _vegetation[slot] = moved;
            _vegetationIndexAt[moved.Y * Size + moved.X] = slot;
        }
    }

    private void TickVegetationGrowth()
    {
        for (int i = 0; i < VegetationCount; i++)
        {
            ref Vegetation veg = ref _vegetation[i];
            int matureStage = _vegetationCatalog.Get(veg.Type).MatureStage;
            if (veg.Stage < matureStage)
            {
                veg.Stage++;
            }
        }
    }

    private void TickVegetationSpread()
    {
        double bushChance = _vegetationCatalog.Get(_bushTypeId).SpawnChance;
        double treeChance = _vegetationCatalog.Get(_treeTypeId).SpawnChance;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (VegetationCount >= _vegetation.Length)
                {
                    return;
                }

                int index = y * Size + x;
                if (_terrain[index] != _grassId || _vegetationIndexAt[index] != -1)
                {
                    continue;
                }

                if (_rngVegetation.NextDouble() < bushChance)
                {
                    SpawnVegetation(x, y, _bushTypeId);
                }
                else if (_rngVegetation.NextDouble() < treeChance)
                {
                    SpawnVegetation(x, y, _treeTypeId);
                }
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

        double frequency = 1.0 / (Size / 8.0);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double elevation = noise.Sample(x * frequency, y * frequency);
                byte terrain =
                    elevation < -0.1 ? water :
                    elevation < 0.0 ? sand :
                    elevation < 0.5 ? grass :
                    stone;
                _terrain[y * Size + x] = terrain;
            }
        }
    }
}
