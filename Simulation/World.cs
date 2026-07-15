namespace Simulation;

public sealed class World
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private readonly byte[] _terrain;

    public int Size { get; }

    public World(int seed, int size, TerrainCatalog catalog)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException($"size must be a power of two greater than zero, got {size}", nameof(size));
        }

        Size = size;
        _terrain = new byte[size * size];
        GenerateTerrain(seed, catalog);
    }

    public byte GetTerrainId(int x, int y) => _terrain[y * Size + x];

    public ulong Hash()
    {
        ulong hash = FnvOffsetBasis;
        foreach (byte b in _terrain)
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return hash;
    }

    private void GenerateTerrain(int seed, TerrainCatalog catalog)
    {
        var rng = new Rng((ulong)seed);
        var noise = new PerlinNoise(rng);

        if (!catalog.TryGetId("water", out byte water) ||
            !catalog.TryGetId("sand", out byte sand) ||
            !catalog.TryGetId("grass", out byte grass) ||
            !catalog.TryGetId("stone", out byte stone))
        {
            throw new ArgumentException("terrain catalog must define water, sand, grass and stone", nameof(catalog));
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
