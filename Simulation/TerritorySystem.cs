namespace Simulation;

// Territoire (session territoire) : grille grossière de régions
// (CLAUDE.md, "32² pour 512²", jamais par tuile), chacune possédée
// par au plus un clan. Dépendance à SENS UNIQUE vers AgentClanSystem
// (lit population + foyers, n'y écrit jamais) -- même patron que
// FireSystem qui lit VegetationSystem/TerrainSystem, aucun cycle.
// Entièrement déterministe (population + position de foyer +
// diffusion géométrique) : aucun flux RNG.
//
// L'expansion réutilise le patron de diffusion Jacobi en double
// buffer déjà établi par VegetationSystem.RebuildFoodGradient : une
// chaîne d'influence PAR CLAN, re-semée depuis les foyers à CHAQUE
// tick territoire (pas de mémoire d'un tick à l'autre -- un clan qui
// décline relâche automatiquement ses régions, sans code spécial),
// diffusée sur un nombre FIXE d'itérations (pas de convergence à
// l'infini). Une région appartient au clan dont l'influence y est la
// plus forte, au-dessus d'un seuil minimal -- la plus forte gagne,
// sans hystérésis (pas de conquête/combat cette session).
//
// Le territoire est un STOCK (état réel, recalculé mais STOCKÉ),
// pas une capacité dérivée à la lecture (cf. CLAUDE.md, IA) -- il
// entre dans Hash() dans cette même session.
public sealed class TerritorySystem
{
    public const uint NoOwner = uint.MaxValue;

    // Fraction minimale de tuiles walkable pour qu'une région soit
    // revendicable -- définition STRUCTURELLE de "qu'est-ce qu'une
    // région" (un lac n'a pas de sens comme territoire), pas un
    // réglage d'équilibrage : reste une constante interne, jamais
    // dans simulation.json. Bas délibérément (pas 0,5) : un foyer
    // valide (toujours sur une tuile walkable) peut légitimement
    // tomber près d'une côte, dans une région à MAJORITÉ eau --
    // n'exclut que les régions VRAIMENT lacustres, jamais une région
    // simplement côtière (vérifié empiriquement : 0,5 rendait des
    // foyers réels non-revendicables, cf. session territoire).
    private const double MinWalkableFractionToClaim = 0.1;

    private readonly SimulationConfig _config;
    private readonly Catalog<TerrainType> _catalog;
    private readonly TerrainSystem _terrainSystem;
    private readonly AgentClanSystem _agentClanSystem;

    private readonly int _size;
    private readonly int _regionCellSize;
    private readonly int _regionGridWidth;
    private readonly int _regionGridHeight;

    private readonly uint[] _regionOwner;
    private readonly bool[] _regionClaimable;

    // Buffers d'influence par clan, ping-pong (comme _foodGradientA/B
    // dans VegetationSystem), indexés clanIndex * regionCount + cell.
    private double[] _influenceCurrent;
    private double[] _influenceNext;

    public int RegionCellSize => _regionCellSize;

    public int RegionGridWidth => _regionGridWidth;

    public int RegionGridHeight => _regionGridHeight;

    public int RegionCount => _regionGridWidth * _regionGridHeight;

    public uint[] RegionOwner => _regionOwner;

    public bool[] RegionClaimable => _regionClaimable;

    public TerritorySystem(int size, SimulationConfig config, Catalog<TerrainType> catalog, TerrainSystem terrainSystem, AgentClanSystem agentClanSystem)
    {
        _config = config;
        _catalog = catalog;
        _terrainSystem = terrainSystem;
        _agentClanSystem = agentClanSystem;
        _size = size;

        _regionCellSize = Math.Max(1, (int)(size / config.TerritoryRegionsAcrossMap));
        _regionGridWidth = (size + _regionCellSize - 1) / _regionCellSize;
        _regionGridHeight = _regionGridWidth;

        int regionCount = _regionGridWidth * _regionGridHeight;
        _regionOwner = new uint[regionCount];
        Array.Fill(_regionOwner, NoOwner);
        _regionClaimable = new bool[regionCount];

        int bufferSize = agentClanSystem.ClanCount * regionCount;
        _influenceCurrent = new double[bufferSize];
        _influenceNext = new double[bufferSize];
    }

    // Eau exclue de l'appropriation (session territoire, rendu +
    // confinement) : recalculé à CHAQUE tick territoire, comme
    // l'appartenance elle-même -- le terrain peut changer après la
    // construction (feu -> cendre, tests -> SetTerrainId), jamais figé.
    private void RecomputeRegionClaimability()
    {
        int regionCount = RegionCount;
        var walkableCount = new int[regionCount];
        var totalCount = new int[regionCount];
        for (int y = 0; y < _size; y++)
        {
            for (int x = 0; x < _size; x++)
            {
                int cell = RegionIndex(x, y);
                totalCount[cell]++;
                if (_catalog.Get(_terrainSystem.Terrain[y * _size + x]).Walkable)
                {
                    walkableCount[cell]++;
                }
            }
        }
        for (int cell = 0; cell < regionCount; cell++)
        {
            _regionClaimable[cell] = totalCount[cell] > 0 && (double)walkableCount[cell] / totalCount[cell] >= MinWalkableFractionToClaim;
        }
    }

    public int RegionIndex(int x, int y)
    {
        int cellX = Math.Clamp(x / _regionCellSize, 0, _regionGridWidth - 1);
        int cellY = Math.Clamp(y / _regionCellSize, 0, _regionGridHeight - 1);
        return cellY * _regionGridWidth + cellX;
    }

    public uint GetRegionOwnerAt(int x, int y) => _regionOwner[RegionIndex(x, y)];

    public bool IsRegionClaimableAt(int x, int y) => _regionClaimable[RegionIndex(x, y)];

    // Diagnostic (valeur d'influence brute post-diffusion, avant
    // seuillage) -- lecture pure, utile pour calibrer
    // TerritoryClaimThreshold empiriquement (cf. SimReport).
    public double GetInfluence(int clanIndex, int x, int y) => _influenceCurrent[clanIndex * RegionCount + RegionIndex(x, y)];

    public int CountRegionsOwnedBy(uint clanId)
    {
        int count = 0;
        for (int i = 0; i < _regionOwner.Length; i++)
        {
            if (_regionOwner[i] == clanId)
            {
                count++;
            }
        }
        return count;
    }

    public int NeutralRegionCount()
    {
        int count = 0;
        for (int i = 0; i < _regionOwner.Length; i++)
        {
            if (_regionOwner[i] == NoOwner)
            {
                count++;
            }
        }
        return count;
    }

    // Territoire initial (session territoire, ordre de génération) :
    // attribue un noyau territorial à chaque clan À LA CONSTRUCTION, avant
    // que ses agents ne soient spawnés -- purement géométrique (distance
    // foyer <-> centre de région), aucun tirage RNG, aucune diffusion.
    // TickTerritory() écrasera entièrement cet état dès le premier tick
    // territoire (re-semé à chaque appel, jamais de mémoire d'un tick à
    // l'autre) : ceci ne comble que la fenêtre entre la construction et ce
    // premier tick, qui laissait sinon toutes les régions à NoOwner pendant
    // le spawn des agents.
    public void SeedInitialTerritory(double radiusFraction)
    {
        RecomputeRegionClaimability();

        double radius = _size * radiusFraction;
        Home[] homes = _agentClanSystem.Homes;

        // Passe 1 : la région contenant le foyer d'un clan lui appartient
        // TOUJOURS, sans comparaison de distance -- deux foyers tirés au
        // hasard pourraient sinon tomber dans la même région, et le clan
        // perdant la comparaison n'aurait plus aucune case où spawner ne
        // serait-ce qu'un seul agent (0 population de départ).
        for (int h = 0; h < homes.Length; h++)
        {
            Home home = homes[h];
            int cell = RegionIndex(home.X, home.Y);
            if (_regionClaimable[cell])
            {
                _regionOwner[cell] = home.ClanId;
            }
        }

        // Passe 2 : étend chaque noyau aux régions revendicables encore
        // neutres, attribuées au foyer le plus proche dans le rayon --
        // égalité stricte -> premier foyer trouvé (même convention que
        // TickTerritory).
        for (int cy = 0; cy < _regionGridHeight; cy++)
        {
            for (int cx = 0; cx < _regionGridWidth; cx++)
            {
                int cell = cy * _regionGridWidth + cx;
                if (!_regionClaimable[cell] || _regionOwner[cell] != NoOwner)
                {
                    continue;
                }

                double centerX = cx * _regionCellSize + _regionCellSize * 0.5;
                double centerY = cy * _regionCellSize + _regionCellSize * 0.5;

                double radiusSquared = radius * radius;
                int bestHome = -1;
                double bestDistanceSquared = double.MaxValue;
                for (int h = 0; h < homes.Length; h++)
                {
                    double dx = centerX - homes[h].X;
                    double dy = centerY - homes[h].Y;
                    double distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared <= radiusSquared && distanceSquared < bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        bestHome = h;
                    }
                }

                if (bestHome >= 0)
                {
                    _regionOwner[cell] = homes[bestHome].ClanId;
                }
            }
        }
    }

    public void TickTerritory()
    {
        RecomputeRegionClaimability();

        int clanCount = _agentClanSystem.ClanCount;
        int regionCount = RegionCount;

        Array.Clear(_influenceCurrent);

        // Source : population du clan, déposée à CHAQUE foyer qu'il
        // possède (boucle générique -- un seul foyer par clan
        // aujourd'hui, mais aucun cas spécial "le premier trouvé").
        Home[] homes = _agentClanSystem.Homes;
        int[] clanPopulation = _agentClanSystem.ClanPopulation;
        for (int h = 0; h < homes.Length; h++)
        {
            Home home = homes[h];
            int clanIndex = _agentClanSystem.ClanIndex(home.ClanId);
            int region = RegionIndex(home.X, home.Y);
            _influenceCurrent[clanIndex * regionCount + region] +=
                clanPopulation[clanIndex] * _config.TerritoryPopulationWeight;
        }

        double[] current = _influenceCurrent;
        double[] next = _influenceNext;

        for (int iter = 0; iter < _config.TerritoryDiffusionIterations; iter++)
        {
            for (int c = 0; c < clanCount; c++)
            {
                int baseIndex = c * regionCount;
                for (int cy = 0; cy < _regionGridHeight; cy++)
                {
                    for (int cx = 0; cx < _regionGridWidth; cx++)
                    {
                        int cell = cy * _regionGridWidth + cx;
                        int index = baseIndex + cell;

                        double neighborSum = 0.0;
                        int neighborCount = 0;

                        if (cx > 0) { neighborSum += current[baseIndex + cell - 1]; neighborCount++; }
                        if (cx < _regionGridWidth - 1) { neighborSum += current[baseIndex + cell + 1]; neighborCount++; }
                        if (cy > 0) { neighborSum += current[baseIndex + cell - _regionGridWidth]; neighborCount++; }
                        if (cy < _regionGridHeight - 1) { neighborSum += current[baseIndex + cell + _regionGridWidth]; neighborCount++; }

                        double avgNeighbors = neighborCount > 0 ? neighborSum / neighborCount : current[index];
                        next[index] = current[index] + _config.TerritoryDiffusionRate * (avgNeighbors - current[index]);
                    }
                }
            }

            (current, next) = (next, current);
        }

        _influenceCurrent = current;
        _influenceNext = next;

        // Une région appartient au clan à l'influence la plus forte,
        // au-dessus du seuil de revendication -- égalité stricte ->
        // premier clan trouvé (ordre 0..ClanCount-1, déterministe).
        for (int cell = 0; cell < regionCount; cell++)
        {
            if (!_regionClaimable[cell])
            {
                _regionOwner[cell] = NoOwner;
                continue;
            }

            int bestClanIndex = -1;
            double bestValue = _config.TerritoryClaimThreshold;
            for (int c = 0; c < clanCount; c++)
            {
                double value = current[c * regionCount + cell];
                if (value > bestValue)
                {
                    bestValue = value;
                    bestClanIndex = c;
                }
            }

            _regionOwner[cell] = bestClanIndex >= 0 ? _agentClanSystem.Clans[bestClanIndex].Id : NoOwner;
        }
    }
}
