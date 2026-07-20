namespace Simulation;

// Refactor : premier système extrait de World.cs (le plus bas niveau,
// aucune dépendance vers les autres systèmes). Possède le tableau de
// terrain et l'état de combustion -- les autres systèmes (Fire,
// Vegetation, Agent) restent pour l'instant dans World.cs et lisent/
// écrivent ce système via les propriétés exposées ici, à l'identique
// du comportement précédent (mêmes tableaux, même ordre d'opérations).
public sealed class TerrainSystem
{
    private readonly int _size;
    private readonly Catalog<TerrainType> _catalog;
    private readonly byte[] _terrain;
    private readonly bool[] _burning;
    private readonly byte _ashId;
    private readonly byte _grassId;

    public byte[] Terrain => _terrain;

    public bool[] Burning => _burning;

    public byte AshId => _ashId;

    public byte GrassId => _grassId;

    public int GrassTileCount { get; private set; }

    public int AshTileCount { get; private set; }

    public int TilesBurnedCumulative { get; private set; }

    public TerrainSystem(int size, Catalog<TerrainType> catalog, SimulationConfig config, Rng rngWorldGen)
    {
        _size = size;
        _catalog = catalog;
        _terrain = new byte[size * size];
        _burning = new bool[size * size];

        if (!catalog.TryGetId("ash", out _ashId))
        {
            throw new ArgumentException("terrain catalog must define ash", nameof(catalog));
        }

        if (!catalog.TryGetId("grass", out _grassId))
        {
            throw new ArgumentException("terrain catalog must define grass", nameof(catalog));
        }

        GenerateTerrain(config, rngWorldGen);

        for (int i = 0; i < _terrain.Length; i++)
        {
            if (_terrain[i] == _grassId)
            {
                GrassTileCount++;
            }
        }
    }

    public byte GetTerrainId(int x, int y) => _terrain[y * _size + x];

    public void SetTerrainId(int x, int y, byte id) => _terrain[y * _size + x] = id;

    public bool IsBurning(int x, int y) => _burning[y * _size + x];

    // BFS grass (déplacé de World.cs, refactor VegetationSystem lot 1) :
    // ne touche que Terrain/GrassId, domaine terrain pur -- partagé par
    // SeedMinimumBushPerPatch (VegetationSystem) et AnalyzeGrassConnectivity
    // (World.cs, diagnostic terrain).
    public void TryEnqueueGrass(int x, int y, bool[] visited, List<int> queue)
    {
        if (x < 0 || x >= _size || y < 0 || y >= _size)
        {
            return;
        }

        int index = y * _size + x;
        if (visited[index] || _terrain[index] != _grassId)
        {
            return;
        }

        visited[index] = true;
        queue.Add(index);
    }

    // Appelé par TickFire (World.cs) quand une tuile finit de brûler --
    // reproduit exactement l'ancien bloc inline (session 17b : compteurs
    // GrassTileCount/AshTileCount/TilesBurnedCumulative).
    public void BurnToAsh(int index)
    {
        _burning[index] = false;
        _terrain[index] = _ashId;
        GrassTileCount--;
        AshTileCount++;
        TilesBurnedCumulative++;
    }

    // Appelé par TickAshRecovery (World.cs, cadence tick végétation) --
    // reproduit exactement l'ancien bloc inline.
    public void RecoverAshToGrass(int index)
    {
        _terrain[index] = _grassId;
        AshTileCount--;
        GrassTileCount++;
    }

    private void GenerateTerrain(SimulationConfig config, Rng rngWorldGen)
    {
        var noise = new PerlinNoise(rngWorldGen);

        if (!_catalog.TryGetId("water", out byte water) ||
            !_catalog.TryGetId("sand", out byte sand) ||
            !_catalog.TryGetId("grass", out byte grass) ||
            !_catalog.TryGetId("stone", out byte stone))
        {
            throw new ArgumentException("terrain catalog must define water, sand, grass and stone", nameof(_catalog));
        }

        double frequency = 1.0 / (_size / config.TerrainFeaturesAcrossMap);

        for (int y = 0; y < _size; y++)
        {
            for (int x = 0; x < _size; x++)
            {
                double elevation = noise.Sample(x * frequency, y * frequency);
                byte terrain =
                    elevation < config.TerrainWaterThreshold ? water :
                    elevation < config.TerrainSandThreshold ? sand :
                    elevation < config.TerrainGrassThreshold ? grass :
                    stone;
                _terrain[y * _size + x] = terrain;
            }
        }
    }
}
