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

    private readonly SimulationConfig _config;
    private readonly TerrainSystem _terrainSystem;
    private readonly VegetationSystem _vegetationSystem;
    private readonly FireSystem _fireSystem;
    private readonly AgentClanSystem _agentClanSystem;
    private readonly TerritorySystem _territorySystem;
    private readonly Catalog<TerrainType> _catalog;
    private readonly Catalog<VegetationType> _vegetationCatalog;
    private readonly Catalog<SpeciesType> _speciesCatalog;
    private readonly Rng _rngWorldGen;
    private readonly Rng _rngFire;
    private readonly Rng _rngAgents;
    private readonly Rng _rngVegetation;
    private readonly byte _bushTypeId;
    private readonly byte _treeTypeId;
    private int _tickCounter;

    public int Size { get; }

    public int AgentCapacity => _agentClanSystem.AgentCapacity;

    public int AliveCount { get => _agentClanSystem.AliveCount; private set => _agentClanSystem.AliveCount = value; }

    // Arbres et buissons vivent dans deux tableaux à capacité indépendante
    // (session 13) : ils ne se disputent plus les slots. VegetationCount/
    // GetVegetation restent une concaténation logique bush-puis-tree pour
    // ne pas casser les appelants (tests, SimReport) qui itèrent "la"
    // végétation sans se soucier du type.
    public int BushCount => _vegetationSystem.BushCount;

    public int TreeCount => _vegetationSystem.TreeCount;

    public int VegetationCount => BushCount + TreeCount;

    public bool AgentSpawnCapped { get => _agentClanSystem.AgentSpawnCapped; private set => _agentClanSystem.AgentSpawnCapped = value; }

    public int GrassTileCount => _terrainSystem.GrassTileCount;

    public int AshTileCount => _terrainSystem.AshTileCount;

    // Compteurs de diagnostic (comme les morts par cause) : n'influencent
    // jamais la simulation, donc exclus de Hash().
    // long (session 19c) : depuis que manger est un effet passif appliqué
    // à CHAQUE tick réel (au lieu d'une fois par "session de repas"),
    // ce compteur incrémente bien plus vite qu'avant et dépasse
    // int.MaxValue en quelques millions de ticks à haute population --
    // même raisonnement que _agentClanSystem.ClanFoodHarvestedCumulative (session 18).
    public long MealsEaten { get => _agentClanSystem.MealsEaten; private set => _agentClanSystem.MealsEaten = value; }

    public int TilesBurnedCumulative => _terrainSystem.TilesBurnedCumulative;

    public int VegetationLostToFire => _vegetationSystem.VegetationLostToFire;

    // Diagnostic feu (session 17b), délégué à FireSystem.
    public double AverageFireEventSize => _fireSystem.AverageFireEventSize;

    public int FireEventCount => _fireSystem.FireEventCount;

    public int MaxFireEventSize => _fireSystem.MaxFireEventSize;

    public int FireBlockedByTerrainCount => _fireSystem.FireBlockedByTerrainCount;

    public int FireFizzledCount => _fireSystem.FireFizzledCount;

    public int BirthsTotal => _agentClanSystem.BirthsTotal;

    public int BirthsRefusedArrayFull => _agentClanSystem.BirthsRefusedArrayFull;

    public int BirthsLostToUnsafeTile => _agentClanSystem.BirthsLostToUnsafeTile;

    // Répond au piège de méthode signalé : échantillonner tous les
    // 100k ticks peut rater un creux court. Suivi à CHAQUE tick réel,
    // pas seulement aux points d'échantillonnage.
    public int MinAliveCountEverObserved => _agentClanSystem.MinAliveCountEverObserved;

    // Clans (session 18) : le tableau n'est jamais compacté cette
    // session (pas de scission), donc itérer par index 0..ClanCount
    // est sûr pour un appelant EXTERNE (SimReport, UI). Le code
    // interne passe par GetClanById par principe (cf. champs privés).
    public int ClanCount => _agentClanSystem.ClanCount;

    public Clan GetClan(int index) => _agentClanSystem.Clans[index];

    public int GetClanHungerDeaths(int index) => _agentClanSystem.ClanHungerDeaths[index];

    public int GetClanAgeDeaths(int index) => _agentClanSystem.ClanAgeDeaths[index];

    public long GetClanFoodHarvestedCumulative(int index) => _agentClanSystem.ClanFoodHarvestedCumulative[index];

    public long GetClanFoodConsumedCumulative(int index) => _agentClanSystem.ClanFoodConsumedCumulative[index];

    public int GetClanMinAliveEverObserved(int index) => _agentClanSystem.ClanMinAliveEverObserved[index];

    // Foyers (session foyers) : même raisonnement de sûreté d'itération
    // externe que les clans ci-dessus (capacité fixe, jamais compactée).
    public int HomeCount => _agentClanSystem.HomeCount;

    public Home GetHome(int index) => _agentClanSystem.GetHome(index);

    public Home GetHomeById(uint id) => _agentClanSystem.GetHomeById(id);

    public double AverageDistanceToHome() => _agentClanSystem.AverageDistanceToHome();

    // Territoire (session territoire) : grille grossière de régions,
    // même raisonnement de sûreté d'itération externe que Clans/Homes
    // ci-dessus (capacité fixe, jamais compactée).
    public int RegionCellSize => _territorySystem.RegionCellSize;

    public int RegionGridWidth => _territorySystem.RegionGridWidth;

    public int RegionGridHeight => _territorySystem.RegionGridHeight;

    public int RegionCount => _territorySystem.RegionCount;

    public uint GetRegionOwnerAt(int x, int y) => _territorySystem.GetRegionOwnerAt(x, y);

    public bool IsRegionClaimableAt(int x, int y) => _territorySystem.IsRegionClaimableAt(x, y);

    public int CountRegionsOwnedBy(uint clanId) => _territorySystem.CountRegionsOwnedBy(clanId);

    public int NeutralRegionCount() => _territorySystem.NeutralRegionCount();

    public double GetTerritoryInfluence(uint clanId, int x, int y) => _territorySystem.GetInfluence(_agentClanSystem.ClanIndex(clanId), x, y);

    public static IReadOnlyList<double> DeathDistanceBucketUpperBounds => AgentClanSystem.DeathDistanceBucketUpperBounds;

    public int[] GetDeathDistanceHistogram() => (int[])_agentClanSystem.DeathDistanceHistogram.Clone();

    public int[] GetDeathTerrainHistogram() => (int[])_agentClanSystem.DeathTerrainHistogram.Clone();

    public int[] GetDeathSeekOutcomeHistogram() => (int[])_agentClanSystem.DeathSeekOutcomeHistogram.Clone();

    // Sous-ensemble des morts de faim où l'agent était Seeking/Harvesting
    // au moment de mourir (session 18) -- dénominateur pertinent pour
    // juger la cécité des CUEILLEURS, distinct du total des morts de
    // faim (qui inclut aussi les morts "pool à sec" en Idle/Moving --
    // manger n'est plus un état depuis la session 19c).
    public int HungerDeathsWhileHarvesting => _agentClanSystem.HungerDeathsWhileHarvesting;

    public double AverageDeathFailureStreak => AverageOverDeaths(_agentClanSystem.DeathFailureStreakSum);

    public double AverageDeathTicksIdle => AverageOverDeaths(_agentClanSystem.DeathTicksIdleSum);

    public double AverageDeathTicksMoving => AverageOverDeaths(_agentClanSystem.DeathTicksMovingSum);

    public double AverageDeathTicksSeeking => AverageOverDeaths(_agentClanSystem.DeathTicksSeekingSum);

    public double AverageDeathTicksEating => AverageOverDeaths(_agentClanSystem.DeathTicksEatingSum);

    public double AverageDeathHungerAtLastMeal => AverageOverDeaths(_agentClanSystem.DeathHungerAtLastMealSum);

    private double AverageOverDeaths(long sum)
    {
        int deaths = GetDeathCount(DeathCause.Hunger);
        return deaths > 0 ? sum / (double)deaths : 0.0;
    }

    public World(int seed, int size, Catalog<TerrainType> catalog, Catalog<VegetationType> vegetationCatalog, Catalog<SpeciesType> speciesCatalog, SimulationConfig config)
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

        _rngWorldGen = new Rng(DeriveSeed(seed, WorldGenStreamId));
        _rngFire = new Rng(DeriveSeed(seed, FireStreamId));
        _rngAgents = new Rng(DeriveSeed(seed, AgentsStreamId));
        _rngVegetation = new Rng(DeriveSeed(seed, VegetationStreamId));

        _terrainSystem = new TerrainSystem(size, catalog, config, _rngWorldGen);

        if (!vegetationCatalog.TryGetId("bush", out _bushTypeId) ||
            !vegetationCatalog.TryGetId("tree", out _treeTypeId))
        {
            throw new ArgumentException("vegetation catalog must define bush and tree", nameof(vegetationCatalog));
        }

        // Ordre de génération (session territoire) : AgentClanSystem ne
        // crée ici que les clans et leurs positions de foyer -- ses
        // agents ne sont spawnés qu'après que TerritorySystem a semé un
        // noyau territorial initial (SpawnInitialAgents ci-dessous),
        // garantissant qu'aucun agent ne naît hors du territoire de son
        // propre clan.
        _agentClanSystem = new AgentClanSystem(size, config, catalog, speciesCatalog, vegetationCatalog, _terrainSystem, _rngWorldGen, _rngAgents);

        _vegetationSystem = new VegetationSystem(size, vegetationCatalog, config, _terrainSystem, _rngVegetation,
            _agentClanSystem.AgentGridCellSize, _agentClanSystem.AgentGridWidth, _agentClanSystem.AgentGridHeight);

        _agentClanSystem.AttachVegetationSystem(_vegetationSystem);

        _fireSystem = new FireSystem(size, config, catalog, vegetationCatalog, _terrainSystem, _vegetationSystem, _rngFire);

        _territorySystem = new TerritorySystem(size, config, catalog, _terrainSystem, _agentClanSystem);
        _territorySystem.SeedInitialTerritory(config.TerritoryInitialRadiusFraction);
        _agentClanSystem.AttachTerritorySystem(_territorySystem);
        _agentClanSystem.SpawnInitialAgents();
    }

    public byte GetTerrainId(int x, int y) => _terrainSystem.Terrain[y * Size + x];

    public void SetTerrainId(int x, int y, byte id) => _terrainSystem.Terrain[y * Size + x] = id;

    public bool IsBurning(int x, int y) => _terrainSystem.Burning[y * Size + x];

    public Agent GetAgent(int index) => _agentClanSystem.Agents[index];

    public Vegetation GetVegetation(int index) => _vegetationSystem.GetVegetation(index);

    public int GetDeathCount(DeathCause cause) => _agentClanSystem.DeathsByCause[(byte)cause];

    public int CountVegetationOfType(byte type) => _vegetationSystem.CountVegetationOfType(type);

    public int CountMatureVegetationOfType(byte type) => _vegetationSystem.CountMatureVegetationOfType(type);

    public bool TryGetVegetationAt(int x, int y, out Vegetation vegetation) => _vegetationSystem.TryGetVegetationAt(x, y, out vegetation);

    public void ForceSpawnVegetation(int x, int y, byte type, byte stage) => _vegetationSystem.ForceSpawnVegetation(x, y, type, stage, _tickCounter);

    public void SetVegetationFoodRemaining(int x, int y, int amount) => _vegetationSystem.SetVegetationFoodRemaining(x, y, amount);

    public void SetVegetationDeathTick(int x, int y, int deathTick) => _vegetationSystem.SetVegetationDeathTick(x, y, deathTick);

    // Seam de test : retire la végétation présente (si il y en a) pour
    // poser l'horodatage de délai de repousse sans dépendre d'un agent
    // qui mange ou d'un feu.
    public void ClearVegetationAt(int x, int y) => _vegetationSystem.ClearVegetationAt(x, y, _tickCounter);

    // Seam de test : vide toute la végétation posée par
    // SeedInitialVegetation (s15) -- nécessaire pour les scénarios qui
    // exigent une carte réellement sans nourriture.
    public void ClearAllVegetation() => _vegetationSystem.ClearAllVegetation(_tickCounter);

    // Distance euclidienne au buisson mûr le plus proche, SANS la limite
    // de portée du BFS de gameplay -- utilisée par le diagnostic de mort
    // (s12) et par la mesure de clusterisation de SimReport (s13).
    public double DistanceToNearestMatureBush(int x, int y) => _vegetationSystem.DistanceToNearestMatureBush(x, y);

    // Diagnostic (session 14b) : écart-type du nombre d'agents par
    // cellule de la grille grossière déjà reconstruite chaque tick
    // (cf. RebuildAgentGrid) -- mesure la clusterisation des AGENTS,
    // distincte de celle des buissons (déjà mesurée par SimReport en
    // s13). Lecture pure, exclue de Hash() (comme DistanceToNearestMatureBush).
    public double AgentDensityStdDev()
    {
        int cellCount = _agentClanSystem.AgentGridWidth * _agentClanSystem.AgentGridHeight;
        if (cellCount == 0)
        {
            return 0.0;
        }

        double mean = (double)AliveCount / cellCount;
        double sumSquaredDiff = 0.0;
        for (int c = 0; c < cellCount; c++)
        {
            double diff = _agentClanSystem.AgentCountInCell(c) - mean;
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
            if (visited[startIndex] || _terrainSystem.Terrain[startIndex] != _terrainSystem.GrassId)
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

                if (_vegetationSystem.BushIndexAt[index] != -1)
                {
                    hasBush = true;
                }

                int x = index % Size;
                int y = index / Size;
                _terrainSystem.TryEnqueueGrass(x - 1, y, visited, queue);
                _terrainSystem.TryEnqueueGrass(x + 1, y, visited, queue);
                _terrainSystem.TryEnqueueGrass(x, y - 1, visited, queue);
                _terrainSystem.TryEnqueueGrass(x, y + 1, visited, queue);
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

    public void SetAgentHunger(int index, byte hunger) => _agentClanSystem.Agents[index].Hunger = hunger;

    // Seams de test (session 14) : même statut que SetAgentHunger --
    // permettent de forcer un scénario déterministe sans dépendre du
    // hasard du spawn.
    public void SetAgentAge(int index, uint age) => _agentClanSystem.Agents[index].Age = age;

    public void SetAgentLifespan(int index, uint lifespan) => _agentClanSystem.Agents[index].LifespanTicks = lifespan;

    public void SetAgentSex(int index, byte sex) => _agentClanSystem.Agents[index].Sex = sex;

    public void SetAgentPosition(int index, float x, float y)
    {
        _agentClanSystem.Agents[index].X = x;
        _agentClanSystem.Agents[index].Y = y;
    }

    // Seams de test (session 18) : même statut que les seams ci-dessus.
    public void SetAgentClanId(int index, uint clanId) => _agentClanSystem.Agents[index].ClanId = clanId;

    public void SetClanFoodPool(int clanIndex, int amount) => _agentClanSystem.Clans[clanIndex].FoodPool = amount;

    // Seam de test (session territoire) : force la position d'un
    // foyer -- même statut que SetClanFoodPool/SetAgentPosition,
    // jamais utilisé par la simulation elle-même.
    public void SetHomePosition(int homeIndex, int x, int y)
    {
        _agentClanSystem.Homes[homeIndex].X = x;
        _agentClanSystem.Homes[homeIndex].Y = y;
    }

    public void SetAgentState(int index, AgentState state) => _agentClanSystem.Agents[index].State = state;

    public void SetAgentTarget(int index, int x, int y)
    {
        _agentClanSystem.Agents[index].TargetX = x;
        _agentClanSystem.Agents[index].TargetY = y;
    }

    public void Execute(ICommand command) => command.Execute(this);

    public void IgniteArea(int centerX, int centerY, int radius) => _fireSystem.IgniteArea(centerX, centerY, radius);

    public void Tick(double delta)
    {
        _fireSystem.TickFire(_tickCounter);
        _agentClanSystem.TickAgents(delta, _tickCounter);
        _agentClanSystem.CleanupDeadAgents();

        if (_tickCounter % _config.VegetationTickInterval == 0)
        {
            _vegetationSystem.TickVegetationGrowth();
            _vegetationSystem.TickVegetationAging(_tickCounter);
            _vegetationSystem.TickVegetationSpread(_tickCounter);
            _vegetationSystem.TickAshRecovery();
            _vegetationSystem.RebuildFoodDensityGrid();
            _vegetationSystem.RebuildCellConductivity();
            _vegetationSystem.RebuildFoodGradient();
        }

        if (_tickCounter % _config.TerritoryTickInterval == 0)
        {
            _territorySystem.TickTerritory();
        }

        if (AliveCount < _agentClanSystem.MinAliveCountEverObserved)
        {
            _agentClanSystem.MinAliveCountEverObserved = AliveCount;
        }

        _agentClanSystem.UpdateClanMinAliveObserved();

        _tickCounter++;
    }

    public ulong Hash()
    {
        ulong hash = FnvOffsetBasis;

        foreach (byte b in _terrainSystem.Terrain)
        {
            Mix(ref hash, b);
        }

        foreach (bool burning in _terrainSystem.Burning)
        {
            Mix(ref hash, burning ? 1UL : 0UL);
        }

        foreach (int clearedTick in _vegetationSystem.VegetationClearedTick)
        {
            Mix(ref hash, unchecked((uint)clearedTick));
        }

        Mix(ref hash, (ulong)_tickCounter);
        Mix(ref hash, _agentClanSystem.NextAgentId);
        Mix(ref hash, _rngWorldGen.State);
        Mix(ref hash, _rngFire.State);
        Mix(ref hash, _rngAgents.State);
        Mix(ref hash, _rngVegetation.State);

        Mix(ref hash, (ulong)_fireSystem.ActiveCurrent.Count);
        foreach (int index in _fireSystem.ActiveCurrent)
        {
            Mix(ref hash, (uint)index);
        }

        Mix(ref hash, (ulong)AliveCount);

        for (int i = 0; i < AliveCount; i++)
        {
            ref Agent agent = ref _agentClanSystem.Agents[i];
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
            Mix(ref hash, agent.HomeId);

            List<int> path = _agentClanSystem.AgentPaths[i];
            Mix(ref hash, (ulong)path.Count);
            foreach (int waypoint in path)
            {
                Mix(ref hash, (uint)waypoint);
            }
        }

        Mix(ref hash, (ulong)BushCount);
        for (int i = 0; i < BushCount; i++)
        {
            ref Vegetation veg = ref _vegetationSystem.Bushes[i];
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
            ref Vegetation veg = ref _vegetationSystem.Trees[i];
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
        Mix(ref hash, (ulong)_agentClanSystem.Clans.Length);
        for (int i = 0; i < _agentClanSystem.Clans.Length; i++)
        {
            ref Clan clan = ref _agentClanSystem.Clans[i];
            Mix(ref hash, clan.Id);
            Mix(ref hash, unchecked((uint)clan.ParentClanId));
            Mix(ref hash, clan.Species);
            Mix(ref hash, unchecked((uint)clan.FoodPool));
        }

        // Foyers (session foyers) : capacité fixe, jamais compactée,
        // même raisonnement que le bloc Clan ci-dessus.
        Mix(ref hash, (ulong)_agentClanSystem.Homes.Length);
        for (int i = 0; i < _agentClanSystem.Homes.Length; i++)
        {
            ref Home home = ref _agentClanSystem.Homes[i];
            Mix(ref hash, home.Id);
            Mix(ref hash, home.ClanId);
            Mix(ref hash, (uint)home.X);
            Mix(ref hash, (uint)home.Y);
        }

        // Champ de gradient de nourriture (session 14c) : dérivé
        // déterministe de _vegetationSystem.FoodPerCell (déjà couvert via _vegetationSystem.Bushes
        // ci-dessus), donc redondant en théorie -- inclus quand même
        // par prudence, cf. plan.
        foreach (double value in _vegetationSystem.FoodGradient)
        {
            Mix(ref hash, BitConverter.DoubleToUInt64Bits(value));
        }

        // Conductivité (session 14d) : même raisonnement de prudence
        // que _vegetationSystem.FoodGradient -- dérivée déterministe de _terrainSystem.Terrain (déjà
        // hashé), incluse quand même.
        foreach (double value in _vegetationSystem.CellConductivity)
        {
            Mix(ref hash, BitConverter.DoubleToUInt64Bits(value));
        }

        // Territoire (session territoire) : STOCK réel, pas une
        // capacité dérivée à la lecture -- inclus explicitement.
        Mix(ref hash, (ulong)_territorySystem.RegionGridWidth);
        Mix(ref hash, (ulong)_territorySystem.RegionGridHeight);
        foreach (uint owner in _territorySystem.RegionOwner)
        {
            Mix(ref hash, owner);
        }

        // Revendicabilité (session territoire, eau exclue) : dérivée
        // du terrain (déjà hashé), incluse quand même par le même
        // raisonnement de prudence que FoodGradient/CellConductivity.
        foreach (bool claimable in _territorySystem.RegionClaimable)
        {
            Mix(ref hash, claimable ? 1UL : 0UL);
        }

        return hash;
    }

    private static void Mix(ref ulong hash, ulong value)
    {
        hash ^= value;
        hash *= FnvPrime;
    }

    // Session filet : un seul tour de Mix (FNV) sur le seed brut ne
    // décorrèle pas suffisamment des seeds bruts proches (ex. 42 vs 43)
    // -- les bits bas restent trop similaires après une seule passe.
    // Finaliseur SplitMix64 complet (algorithme public, constantes
    // standard) appliqué au seed brut AVANT de dériver les flux, pour
    // un avalanche correct même sur des seeds faibles/proches. Mix()
    // lui-même reste inchangé (accumulateur de hash chaîné correct sur
    // de nombreux appels, cf. Hash()).
    private static ulong SplitMix64(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong DeriveSeed(int seed, ulong streamId)
    {
        ulong derived = SplitMix64((ulong)seed);
        Mix(ref derived, streamId);
        return derived;
    }

    private int ClanIndex(uint id) => _agentClanSystem.ClanIndex(id);

    private ref Clan GetClanById(uint id) => ref _agentClanSystem.GetClanById(id);

    private int ClanPopulationForTarget(uint clanId) => _agentClanSystem.ClanPopulationForTarget(clanId);

}
