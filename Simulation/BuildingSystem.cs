namespace Simulation;

// Système bâtiments (session bâtiments) : préalloué, compaction par
// swap-with-last, Id stable monotone. Les bâtiments apparaissent autour
// du foyer de leur clan, dans le territoire du clan, sur tuile walkable.
// Montée en tier quand la population du foyer dépasse le seuil du tier
// supérieur (défini dans buildings.json).
public sealed class BuildingSystem
{
    private readonly int _size;
    private readonly SimulationConfig _config;
    private readonly Catalog<TerrainType> _terrainCatalog;
    private readonly Catalog<BuildingType> _buildingCatalog;
    private readonly TerrainSystem _terrainSystem;
    private readonly TerritorySystem _territorySystem;
    private readonly AgentClanSystem _agentClanSystem;
    private readonly Rng _rngBuildings;

    private Building[] _buildings;
    private int _count;
    private uint _nextId;

    // Population par foyer, recalculée chaque Tick().
    private int[] _homePopulation;

    // Cache pour éviter d'allouer un tableau chaque tick.
    private int[] _homeIndexById;

    // Compteur de ticks depuis la dernière construction (cooldown).
    private int _buildCooldown;

    // Flag de test : quand true, RebuildHomePopulation() ne recalcule pas
    // la population à partir des agents réels — la valeur posée par
    // SetHomePopulationForTest est préservée.
    private bool _populationOverridden;

    public int Count => _count;

    public Building[] Buildings => _buildings;

    public int BuildingCapacity => _buildings.Length;

    public uint NextId => _nextId;

    public BuildingSystem(int size, SimulationConfig config, Catalog<TerrainType> terrainCatalog,
        Catalog<BuildingType> buildingCatalog, TerrainSystem terrainSystem,
        TerritorySystem territorySystem, AgentClanSystem agentClanSystem, Rng rngBuildings)
    {
        _size = size;
        _config = config;
        _terrainCatalog = terrainCatalog;
        _buildingCatalog = buildingCatalog;
        _terrainSystem = terrainSystem;
        _territorySystem = territorySystem;
        _agentClanSystem = agentClanSystem;
        _rngBuildings = rngBuildings;

        // Capacité initiale : 8 bâtiments par foyer, comme les agents.
        int capacity = config.InitialClanCount * config.BuildingCapacityPerHome;
        _buildings = new Building[Math.Max(1, capacity)];
        _homePopulation = new int[agentClanSystem.Homes.Length];
        _homeIndexById = agentClanSystem.HomeIndexById;
    }

    // Compte la population par foyer en itérant sur les agents vivants.
    // Appelé au début de TickBuildings(). Si _populationOverridden est
    // true, on saute le recalcul pour préserver la valeur injectée par
    // SetHomePopulationForTest.
    private void RebuildHomePopulation()
    {
        if (_populationOverridden)
        {
            return;
        }

        Array.Clear(_homePopulation, 0, _homePopulation.Length);
        Agent[] agents = _agentClanSystem.Agents;
        int alive = _agentClanSystem.AliveCount;
        for (int i = 0; i < alive; i++)
        {
            uint homeId = agents[i].HomeId;
            if (homeId == Home.NoHome)
            {
                continue;
            }

            int homeIndex = _homeIndexById[homeId];
            if (homeIndex >= 0 && homeIndex < _homePopulation.Length)
            {
                _homePopulation[homeIndex]++;
            }
        }
    }

    // Cherche un BuildingType par tier. Retourne null si aucun.
    private BuildingType? FindTypeByTier(byte tier)
    {
        for (int i = 0; i < _buildingCatalog.Count; i++)
        {
            BuildingType? candidate = _buildingCatalog.Get((byte)i);
            if (candidate != null && candidate.Tier == tier)
            {
                return candidate;
            }
        }

        return null;
    }

    // Compte le nombre de BuildingType (tous tiers confondus) dans le
    // catalogue. On parcourt les slots et on compte ceux non-null.
    private int CatalogTypeCount()
    {
        int count = 0;
        for (int i = 0; i < _buildingCatalog.Count; i++)
        {
            BuildingType? candidate = _buildingCatalog.Get((byte)i);
            if (candidate != null)
            {
                count++;
            }
        }

        return count;
    }

    public void TickBuildings()
    {
        if (CatalogTypeCount() == 0)
        {
            return;
        }

        RebuildHomePopulation();

        int homeCount = _agentClanSystem.Homes.Length;

        // Cooldown : on ne construit pas tous les ticks, sinon ça va trop
        // vite et le calibrage visuel est impossible. L'upgrade, elle, est
        // instantanée (pas derrière le cooldown) -- sinon le test
        // Building_UpgradesAtPopThreshold ne voit jamais de tier > 0 en
        // 1000 ticks.
        bool canBuild = false;
        _buildCooldown--;
        if (_buildCooldown <= 0)
        {
            _buildCooldown = _config.BuildingBuildCooldownTicks;
            canBuild = true;
        }

        for (int h = 0; h < homeCount; h++)
        {
            int pop = _homePopulation[h];
            ref Home home = ref _agentClanSystem.Homes[h];

            // Upgrade des bâtiments existants si la population le permet.
            // L'upgrade n'est PAS derrière le cooldown de construction --
            // la montée en tier est instantanée dès que le seuil est
            // franchi (pas de temps de construction ni coût pour l'instant).
            for (int b = 0; b < _count; b++)
            {
                if (_buildings[b].HomeId != home.Id)
                {
                    continue;
                }

                byte currentTier = _buildings[b].Tier;
                byte nextTier = (byte)(currentTier + 1);

                BuildingType? nextType = FindTypeByTier(nextTier);
                if (nextType == null)
                {
                    continue;
                }

                // Si la population dépasse le seuil du tier supérieur,
                // on upgrade.
                if (pop >= nextType.PopThreshold)
                {
                    _buildings[b].Tier = nextTier;
                    _buildings[b].Type = nextType.Id;
                }
            }

            if (!canBuild)
            {
                continue;
            }

            // Compte les bâtiments existants pour ce foyer.
            int existingCount = 0;
            for (int b = 0; b < _count; b++)
            {
                if (_buildings[b].HomeId == home.Id)
                {
                    existingCount++;
                }
            }

            // Nombre de bâtiments cible : proportionnel à la population.
            int targetCount = pop / _config.BuildingPopPerBuilding;
            if (targetCount < 1)
            {
                targetCount = 1; // Au moins une hutte si population > 0.
            }

            // Plafond : pas plus que la capacité par foyer.
            if (targetCount > _config.BuildingMaxPerHome)
            {
                targetCount = _config.BuildingMaxPerHome;
            }

            // Construire de nouveaux bâtiments si nécessaire.
            while (existingCount < targetCount && _count < _buildings.Length)
            {
                if (TryPlaceBuilding(home, out Building newBuilding))
                {
                    if (_count == _buildings.Length)
                    {
                        GrowArray();
                    }

                    newBuilding.Id = _nextId++;
                    _buildings[_count++] = newBuilding;
                    existingCount++;
                }
                else
                {
                    // Pas de place trouvée, on abandonne pour ce foyer.
                    break;
                }
            }
        }
    }

    private bool TryPlaceBuilding(Home home, out Building building)
    {
        building = default;

        uint clanId = home.ClanId;
        int homeX = home.X;
        int homeY = home.Y;

        // Type de bâtiment initial (tier 0).
        BuildingType? tier0Type = FindTypeByTier(0);
        if (tier0Type == null)
        {
            return false;
        }

        // Rayon de recherche autour du foyer.
        int searchRadius = _config.BuildingPlacementRadius;

        // Collecte les positions candidates.
        // On utilise un petit buffer sur la pile pour éviter les allocations.
        Span<int> candidatesX = stackalloc int[64];
        Span<int> candidatesY = stackalloc int[64];
        int candidateCount = 0;

        int minX = Math.Max(0, homeX - searchRadius);
        int maxX = Math.Min(_size - 1, homeX + searchRadius);
        int minY = Math.Max(0, homeY - searchRadius);
        int maxY = Math.Min(_size - 1, homeY + searchRadius);

        for (int y = minY; y <= maxY && candidateCount < candidatesX.Length; y++)
        {
            for (int x = minX; x <= maxX && candidateCount < candidatesX.Length; x++)
            {
                // Dans le territoire du clan ?
                if (_territorySystem.GetRegionOwnerAt(x, y) != clanId)
                {
                    continue;
                }

                // Tuile walkable ?
                byte terrainId = _terrainSystem.Terrain[y * _size + x];
                if (!_terrainCatalog.Get(terrainId).Walkable)
                {
                    continue;
                }

                // Pas déjà un bâtiment ici ?
                bool occupied = false;
                for (int b = 0; b < _count; b++)
                {
                    if (_buildings[b].X == x && _buildings[b].Y == y)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (occupied)
                {
                    continue;
                }

                candidatesX[candidateCount] = x;
                candidatesY[candidateCount] = y;
                candidateCount++;
            }
        }

        if (candidateCount == 0)
        {
            return false;
        }

        // Choix déterministe via RNG.
        int chosen = (int)(_rngBuildings.NextDouble() * candidateCount);

        building = new Building
        {
            Id = 0, // Remplacé par l'appelant.
            HomeId = home.Id,
            ClanId = clanId,
            X = candidatesX[chosen],
            Y = candidatesY[chosen],
            Type = tier0Type.Id,
            Tier = 0,
        };

        return true;
    }

    private void GrowArray()
    {
        int newCapacity = _buildings.Length * 2;
        var newArray = new Building[newCapacity];
        Array.Copy(_buildings, newArray, _count);
        _buildings = newArray;
    }

    // Seam de test : force la population d'un foyer (index dans le
    // tableau Homes, pas HomeId). Active le flag _populationOverridden
    // pour que RebuildHomePopulation() ne l'écrase pas.
    public void SetHomePopulationForTest(int homeIndex, int pop)
    {
        if (homeIndex >= 0 && homeIndex < _homePopulation.Length)
        {
            _homePopulation[homeIndex] = pop;
            _populationOverridden = true;
        }
    }

    // Seam de test : vide tous les bâtiments et réinitialise le
    // compteur d'Id pour que les tests soient prédictibles.
    public void ClearAll()
    {
        _count = 0;
        _nextId = 0;
        _buildCooldown = 0;
    }

    // --- Public API ---

    public Building Get(int index)
    {
        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _buildings[index];
    }

    // Seam de test : ajoute un bâtiment directement, sans validation.
    public void AddDirect(Building building)
    {
        if (_count == _buildings.Length)
        {
            GrowArray();
        }

        if (building.Id == 0)
        {
            building.Id = _nextId++;
        }
        else if (building.Id >= _nextId)
        {
            _nextId = building.Id + 1;
        }

        _buildings[_count++] = building;
    }

    // Seam de test : change le tier d'un bâtiment directement.
    public void SetTier(int index, byte tier)
    {
        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _buildings[index].Tier = tier;
    }
}
