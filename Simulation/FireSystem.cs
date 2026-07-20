namespace Simulation;

// Refactor : système feu, extrait de World.cs (étape 4/10 du
// découpage). Dépend de TerrainSystem (BurnToAsh, Terrain, Burning) et
// VegetationSystem (détruit les buissons/arbres inflammables sur son
// passage). _tickCounter reste un état PARTAGÉ de World -- reçu en
// paramètre. Catalogues terrain/végétation dupliqués comme référence
// (même patron que TerrainSystem._catalog), pas de dépendance ajoutée.
public sealed class FireSystem
{
    private readonly int _size;
    private readonly SimulationConfig _config;
    private readonly Catalog<TerrainType> _catalog;
    private readonly Catalog<VegetationType> _vegetationCatalog;
    private readonly TerrainSystem _terrainSystem;
    private readonly VegetationSystem _vegetationSystem;
    private readonly Rng _rngFire;

    // Capacité initiale (cf. Simulation filet, session filet) : sans
    // ça, List.Add réalloue en plein tick dès qu'un incendie dépasse
    // la capacité par défaut -- une allocation tas à 30 Hz, invisible
    // tant qu'aucun test zéro-alloc n'allume de feu.
    private List<int> _activeCurrent = new(512);
    private List<int> _activeNext = new(512);

    // Diagnostic feu (session 17b) : jamais lu par une décision, exclu
    // de Hash() comme le reste des diagnostics.
    private int _currentFireEventTiles;
    private long _fireEventSizeSum;
    private int _fireEventCount;
    private int _fireEventMaxSize;
    private int _fireBlockedByTerrainCount;
    private int _fireFizzledCount;

    public IReadOnlyList<int> ActiveCurrent => _activeCurrent;

    public double AverageFireEventSize => _fireEventCount > 0 ? (double)_fireEventSizeSum / _fireEventCount : 0.0;

    public int FireEventCount => _fireEventCount;

    public int MaxFireEventSize => _fireEventMaxSize;

    public int FireBlockedByTerrainCount => _fireBlockedByTerrainCount;

    public int FireFizzledCount => _fireFizzledCount;

    public FireSystem(int size, SimulationConfig config, Catalog<TerrainType> catalog, Catalog<VegetationType> vegetationCatalog,
        TerrainSystem terrainSystem, VegetationSystem vegetationSystem, Rng rngFire)
    {
        _size = size;
        _config = config;
        _catalog = catalog;
        _vegetationCatalog = vegetationCatalog;
        _terrainSystem = terrainSystem;
        _vegetationSystem = vegetationSystem;
        _rngFire = rngFire;
    }

    public void IgniteArea(int centerX, int centerY, int radius)
    {
        int radiusSquared = radius * radius;
        int minX = Math.Max(0, centerX - radius);
        int maxX = Math.Min(_size - 1, centerX + radius);
        int minY = Math.Max(0, centerY - radius);
        int maxY = Math.Min(_size - 1, centerY + radius);

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

    public void TickFire(int tickCounter)
    {
        _activeNext.Clear();

        foreach (int index in _activeCurrent)
        {
            int x = index % _size;
            int y = index / _size;

            TrySpreadTo(x - 1, y);
            TrySpreadTo(x + 1, y);
            TrySpreadTo(x, y - 1);
            TrySpreadTo(x, y + 1);

            _terrainSystem.BurnToAsh(index);

            int bushSlot = _vegetationSystem.BushIndexAt[index];
            if (bushSlot != -1)
            {
                if (_vegetationCatalog.Get(_vegetationSystem.Bushes[bushSlot].Type).Flammable)
                {
                    _vegetationSystem.RemoveBushAt(bushSlot, tickCounter);
                    _vegetationSystem.RecordLostToFire();
                }
            }
            else
            {
                int treeSlot = _vegetationSystem.TreeIndexAt[index];
                if (treeSlot != -1 && _vegetationCatalog.Get(_vegetationSystem.Trees[treeSlot].Type).Flammable)
                {
                    _vegetationSystem.RemoveTreeAt(treeSlot, tickCounter);
                    _vegetationSystem.RecordLostToFire();
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
        if (x < 0 || x >= _size || y < 0 || y >= _size)
        {
            return;
        }

        int index = y * _size + x;
        bool neighborFlammable = _catalog.Get(_terrainSystem.Terrain[index]).Flammable;

        // Lecture pure (catalogue + terrain), aucune consommation de
        // _rngFire : le tirage ci-dessous reste le seul et unique appel
        // RNG de cette méthode, dans le même ordre qu'avant.
        if (_rngFire.NextDouble() >= _config.FireSpreadChance)
        {
            if (neighborFlammable && !_terrainSystem.Burning[index])
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
        int index = y * _size + x;
        if (_terrainSystem.Burning[index])
        {
            return;
        }

        byte terrainId = _terrainSystem.Terrain[index];
        if (!_catalog.Get(terrainId).Flammable)
        {
            return;
        }

        _terrainSystem.Burning[index] = true;
        active.Add(index);
        _currentFireEventTiles++;
    }
}
