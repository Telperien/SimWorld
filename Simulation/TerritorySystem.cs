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

    private readonly SimulationConfig _config;
    private readonly AgentClanSystem _agentClanSystem;

    private readonly int _regionCellSize;
    private readonly int _regionGridWidth;
    private readonly int _regionGridHeight;

    private readonly uint[] _regionOwner;

    // Buffers d'influence par clan, ping-pong (comme _foodGradientA/B
    // dans VegetationSystem), indexés clanIndex * regionCount + cell.
    private double[] _influenceCurrent;
    private double[] _influenceNext;

    public int RegionCellSize => _regionCellSize;

    public int RegionGridWidth => _regionGridWidth;

    public int RegionGridHeight => _regionGridHeight;

    public int RegionCount => _regionGridWidth * _regionGridHeight;

    public uint[] RegionOwner => _regionOwner;

    public TerritorySystem(int size, SimulationConfig config, AgentClanSystem agentClanSystem)
    {
        _config = config;
        _agentClanSystem = agentClanSystem;

        _regionCellSize = Math.Max(1, (int)(size / config.TerritoryRegionsAcrossMap));
        _regionGridWidth = (size + _regionCellSize - 1) / _regionCellSize;
        _regionGridHeight = _regionGridWidth;

        int regionCount = _regionGridWidth * _regionGridHeight;
        _regionOwner = new uint[regionCount];
        Array.Fill(_regionOwner, NoOwner);

        int bufferSize = agentClanSystem.ClanCount * regionCount;
        _influenceCurrent = new double[bufferSize];
        _influenceNext = new double[bufferSize];
    }

    public int RegionIndex(int x, int y)
    {
        int cellX = Math.Clamp(x / _regionCellSize, 0, _regionGridWidth - 1);
        int cellY = Math.Clamp(y / _regionCellSize, 0, _regionGridHeight - 1);
        return cellY * _regionGridWidth + cellX;
    }

    public uint GetRegionOwnerAt(int x, int y) => _regionOwner[RegionIndex(x, y)];

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

    public void TickTerritory()
    {
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
