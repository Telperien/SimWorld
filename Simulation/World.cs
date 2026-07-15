namespace Simulation;

public sealed class World
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const double SpreadChance = 0.5;
    private const double AgentDensity = 0.00076;
    private const double IdleMoveChance = 0.1;
    private const float MoveSpeed = 2f;

    private readonly byte[] _terrain;
    private readonly bool[] _burning;
    private readonly Agent[] _agents;
    private readonly TerrainCatalog _catalog;
    private readonly Rng _rng;
    private readonly byte _ashId;
    private int _tickCounter;

    private List<int> _activeCurrent = new();
    private List<int> _activeNext = new();

    public int Size { get; }

    public int AgentCapacity => _agents.Length;

    public int AliveCount { get; private set; }

    public World(int seed, int size, TerrainCatalog catalog)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException($"size must be a power of two greater than zero, got {size}", nameof(size));
        }

        Size = size;
        _catalog = catalog;
        _terrain = new byte[size * size];
        _burning = new bool[size * size];
        _rng = new Rng((ulong)seed);

        if (!catalog.TryGetId("ash", out _ashId))
        {
            throw new ArgumentException("terrain catalog must define ash", nameof(catalog));
        }

        GenerateTerrain();

        _agents = new Agent[(int)(AgentDensity * size * size)];
        SpawnAgents();
    }

    public byte GetTerrainId(int x, int y) => _terrain[y * Size + x];

    public void SetTerrainId(int x, int y, byte id) => _terrain[y * Size + x] = id;

    public bool IsBurning(int x, int y) => _burning[y * Size + x];

    public Agent GetAgent(int index) => _agents[index];

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
        _tickCounter++;
    }

    public ulong Hash()
    {
        ulong hash = FnvOffsetBasis;

        foreach (byte b in _terrain)
        {
            Mix(ref hash, b);
        }

        for (int i = 0; i < _agents.Length; i++)
        {
            ref Agent agent = ref _agents[i];
            Mix(ref hash, BitConverter.SingleToUInt32Bits(agent.X));
            Mix(ref hash, BitConverter.SingleToUInt32Bits(agent.Y));
            Mix(ref hash, (byte)agent.State);
        }

        return hash;
    }

    private static void Mix(ref ulong hash, ulong value)
    {
        hash ^= value;
        hash *= FnvPrime;
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

        if (_rng.NextDouble() >= SpreadChance)
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

        for (int i = 0; i < _agents.Length; i++)
        {
            ref Agent agent = ref _agents[i];

            if (agent.State == AgentState.Idle)
            {
                if ((i & 3) == group && _rng.NextDouble() < IdleMoveChance)
                {
                    TryStartMoving(ref agent);
                }
                continue;
            }

            float targetCenterX = agent.TargetX + 0.5f;
            float targetCenterY = agent.TargetY + 0.5f;
            float dx = targetCenterX - agent.X;
            float dy = targetCenterY - agent.Y;
            float distanceSquared = dx * dx + dy * dy;

            if (distanceSquared <= step * step)
            {
                agent.X = targetCenterX;
                agent.Y = targetCenterY;
                agent.State = AgentState.Idle;
            }
            else
            {
                float distance = MathF.Sqrt(distanceSquared);
                agent.X += dx / distance * step;
                agent.Y += dy / distance * step;
            }
        }
    }

    private void TryStartMoving(ref Agent agent)
    {
        int currentX = (int)MathF.Floor(agent.X);
        int currentY = (int)MathF.Floor(agent.Y);

        int direction = (int)(_rng.NextUInt64() & 3);
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

        agent.TargetX = targetX;
        agent.TargetY = targetY;
        agent.State = AgentState.Moving;
    }

    private void SpawnAgents()
    {
        int spawned = 0;
        while (spawned < _agents.Length)
        {
            int x = (int)(_rng.NextDouble() * Size);
            int y = (int)(_rng.NextDouble() * Size);

            if (!_catalog.Get(_terrain[y * Size + x]).Walkable)
            {
                continue;
            }

            _agents[spawned] = new Agent
            {
                X = x + 0.5f,
                Y = y + 0.5f,
                TargetX = x,
                TargetY = y,
                MotherId = -1,
                FatherId = -1,
                Tracked = false,
                State = AgentState.Idle,
                Species = 0,
            };
            spawned++;
        }

        AliveCount = spawned;
    }

    private void GenerateTerrain()
    {
        var noise = new PerlinNoise(_rng);

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
