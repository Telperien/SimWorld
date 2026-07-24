namespace Simulation;

// Refactor : système végétation, extrait par lots vérifiés (build +
// golden-hash après chaque lot, cf. plan de session). _tickCounter est
// un état PARTAGÉ de World (le temps est global) -- reçu en paramètre
// sur les méthodes qui en ont besoin, jamais possédé ici. La grille
// spatiale des agents (agentGridCellSize/Width/Height) n'existe pas
// encore comme système séparé (arrive à l'étape AgentSpatialGrid du
// découpage) -- reçue en entiers au constructeur en attendant.
public sealed class VegetationSystem
{
    private readonly SimulationConfig _config;

    // Une tuile qui n'a jamais porté de végétation doit être
    // immédiatement éligible à la repousse (pas de délai artificiel au
    // démarrage du monde) -- même constante que World.NeverClearedSentinel
    // avant le déplacement (refactor lot 2).
    private const int NeverClearedSentinel = int.MinValue / 2;

    private readonly int _size;
    private readonly TerrainSystem _terrainSystem;
    private readonly Catalog<VegetationType> _vegetationCatalog;
    private readonly Rng _rngVegetation;
    private readonly byte _bushTypeId;
    private readonly byte _treeTypeId;

    private readonly Vegetation[] _bushes;
    private readonly int[] _bushIndexAt;
    private readonly Vegetation[] _trees;
    private readonly int[] _treeIndexAt;
    private readonly int[] _vegetationClearedTick;

    // Grille spatiale des agents (pas encore un système séparé, cf.
    // note d'en-tête) -- reçue en entiers pour dimensionner/indexer les
    // grilles food/gradient/conductivité, qui vivent ici car elles sont
    // des signaux DÉRIVÉS de la végétation projetés sur cette grille.
    private readonly int _agentGridCellSize;
    private readonly int _agentGridWidth;
    private readonly int _agentGridHeight;
    private readonly int[] _foodPerCell;
    private readonly double[] _foodGradientA;
    private readonly double[] _foodGradientB;
    private double[] _foodGradient;
    private readonly double[] _cellConductivity;
    private readonly int[] _cellGrassCountScratch;
    private readonly int[] _cellTotalCountScratch;

    public int BushCount { get; private set; }

    public int TreeCount { get; private set; }

    public int VegetationLostToFire { get; private set; }

    public Vegetation[] Bushes => _bushes;

    public int[] BushIndexAt => _bushIndexAt;

    public Vegetation[] Trees => _trees;

    public int[] TreeIndexAt => _treeIndexAt;

    public int[] VegetationClearedTick => _vegetationClearedTick;

    public int[] FoodPerCell => _foodPerCell;

    public double[] FoodGradient => _foodGradient;

    public double[] CellConductivity => _cellConductivity;

    public VegetationSystem(int size, Catalog<VegetationType> vegetationCatalog, SimulationConfig config,
        TerrainSystem terrainSystem, Rng rngVegetation, int agentGridCellSize, int agentGridWidth, int agentGridHeight)
    {
        _size = size;
        _config = config;
        _terrainSystem = terrainSystem;
        _vegetationCatalog = vegetationCatalog;
        _rngVegetation = rngVegetation;
        _agentGridCellSize = agentGridCellSize;
        _agentGridWidth = agentGridWidth;
        _agentGridHeight = agentGridHeight;

        if (!vegetationCatalog.TryGetId("bush", out _bushTypeId) ||
            !vegetationCatalog.TryGetId("tree", out _treeTypeId))
        {
            throw new ArgumentException("vegetation catalog must define bush and tree", nameof(vegetationCatalog));
        }

        _bushes = new Vegetation[(int)(config.BushDensity * size * size)];
        _bushIndexAt = new int[size * size];
        Array.Fill(_bushIndexAt, -1);

        _trees = new Vegetation[(int)(config.TreeDensity * size * size)];
        _treeIndexAt = new int[size * size];
        Array.Fill(_treeIndexAt, -1);

        _vegetationClearedTick = new int[size * size];
        Array.Fill(_vegetationClearedTick, NeverClearedSentinel);

        int cellCount = agentGridWidth * agentGridHeight;
        _foodPerCell = new int[cellCount];
        _foodGradientA = new double[cellCount];
        _foodGradientB = new double[cellCount];
        _foodGradient = _foodGradientA;
        _cellConductivity = new double[cellCount];
        _cellGrassCountScratch = new int[cellCount];
        _cellTotalCountScratch = new int[cellCount];

        SeedMinimumBushPerPatch();
        SeedInitialVegetation();
    }

    private int AgentCellIndex(float x, float y)
    {
        int cellX = Math.Clamp((int)(x / _agentGridCellSize), 0, _agentGridWidth - 1);
        int cellY = Math.Clamp((int)(y / _agentGridCellSize), 0, _agentGridHeight - 1);
        return cellY * _agentGridWidth + cellX;
    }

    public void RecordLostToFire() => VegetationLostToFire++;

    // Garantit qu'aucune poche d'herbe connectee ne demarre totalement
    // sans buisson (session 19). SeedInitialVegetation seul remplit par
    // UN SEUL balayage rotatif jusqu'a la capacite du tableau _bushes --
    // a densite basse (post session 19), cette capacite est atteinte
    // bien avant que le balayage n'ait visite toutes les poches, donc
    // une poche rencontree tardivement n'obtient jamais aucun buisson.
    // Un clan (ou, a terme, un clan place par le joueur) qui nait dans
    // une telle poche est condamne d'office quelle que soit la densite
    // globale -- ce n'est pas un probleme de calibrage, c'est un trou de
    // couverture au world-gen. Flood-fill une fois a la construction
    // (cout ponctuel O(size^2), meme statut qu'AnalyzeGrassConnectivity),
    // plante un unique buisson mur au premier tile libre de chaque poche
    // avant que le remplissage rotatif ne consomme la capacite restante.
    private void SeedMinimumBushPerPatch()
    {
        var visited = new bool[_size * _size];
        var queue = new List<int>();
        int bushMatureStage = _vegetationCatalog.Get(_bushTypeId).MatureStage;

        for (int startIndex = 0; startIndex < _size * _size; startIndex++)
        {
            if (visited[startIndex] || _terrainSystem.Terrain[startIndex] != _terrainSystem.GrassId)
            {
                continue;
            }

            queue.Clear();
            queue.Add(startIndex);
            visited[startIndex] = true;

            int head = 0;
            int seedTile = -1;
            while (head < queue.Count)
            {
                int index = queue[head++];
                if (seedTile == -1 && _bushIndexAt[index] == -1 && _treeIndexAt[index] == -1)
                {
                    seedTile = index;
                }

                int x = index % _size;
                int y = index / _size;
                _terrainSystem.TryEnqueueGrass(x - 1, y, visited, queue);
                _terrainSystem.TryEnqueueGrass(x + 1, y, visited, queue);
                _terrainSystem.TryEnqueueGrass(x, y - 1, visited, queue);
                _terrainSystem.TryEnqueueGrass(x, y + 1, visited, queue);
            }

            if (seedTile != -1 && BushCount < _bushes.Length)
            {
                int x = seedTile % _size;
                int y = seedTile / _size;
                SpawnBush(x, y, 0);
                _bushes[BushCount - 1].Stage = (byte)bushMatureStage;
            }
        }
    }

    // Un monde fraîchement généré doit démarrer avec un écosystème déjà
    // établi, pas une graine (session 15) : depuis que la maturation
    // d'un buisson prend ~50s (échelle de temps ralentie, cf. plan) et
    // que la mort de faim survient à ~34s, un monde vierge condamnait
    // toute la population initiale avant qu'un seul buisson n'ait mûri.
    // Même esprit que la génération de terrain elle-même (déjà
    // "complète" au démarrage, jamais générée par accrétion) :
    // pré-plante directement à maturité, jusqu'à la capacité de
    // chaque tableau.
    //
    // Ordre de balayage MÉLANGÉ (session fix ensemencement), pas une
    // simple rotation linéaire : une rotation visite les tuiles en
    // ordre raster (y*Size+x), donc la portion "buissons puis arbres
    // pour le reste" tombe systématiquement dans une plage CONTIGUË de
    // cet ordre (bande visible + biais spatial réel : la nourriture
    // initiale dépendait de l'ordre de parcours, pas du terrain -- un
    // clan qui spawnait tard dans le balayage démarrait désavantagé
    // sans rapport avec sa géographie). Une permutation complète
    // (Fisher-Yates, déterministe via _rngVegetation) rend le
    // sous-ensemble déjà visité au moment où la capacité buisson
    // sature ÉPARS sur toute la carte -- la portion "arbres seulement"
    // qui suit est donc elle aussi éparse, plus de bande possible par
    // construction. Le corps de boucle (bush-puis-arbre, capacité) est
    // inchangé, seul l'ordre de visite change.
    private void SeedInitialVegetation()
    {
        int bushMatureStage = _vegetationCatalog.Get(_bushTypeId).MatureStage;
        int treeMatureStage = _vegetationCatalog.Get(_treeTypeId).MatureStage;
        int[] order = BuildShuffledTileOrder();

        for (int offset = 0; offset < order.Length; offset++)
        {
            if (BushCount >= _bushes.Length && TreeCount >= _trees.Length)
            {
                return;
            }

            int index = order[offset];
            if (_terrainSystem.Terrain[index] != _terrainSystem.GrassId || _bushIndexAt[index] != -1 || _treeIndexAt[index] != -1)
            {
                continue;
            }

            int x = index % _size;
            int y = index / _size;

            // Remplit directement jusqu'à la capacité (pas un tirage au
            // taux de germination spontanée, bien trop bas pour amorcer
            // tout un monde -- ce taux reste le filet de sécurité pour
            // une repousse EN COURS DE PARTIE, pas pour le démarrage).
            // La pression de récolte ramènera naturellement la
            // population vers son équilibre réel au fil du temps.
            if (BushCount < _bushes.Length)
            {
                SpawnBush(x, y, 0);
                _bushes[BushCount - 1].Stage = (byte)bushMatureStage;
            }
            else if (TreeCount < _trees.Length)
            {
                SpawnTree(x, y, 0);
                _trees[TreeCount - 1].Stage = (byte)treeMatureStage;
            }
        }
    }

    // Permutation complète des indices de tuile (Fisher-Yates),
    // déterministe via _rngVegetation -- même flux déjà utilisé dans ce
    // fichier, aucun nouveau flux introduit. Coût O(size²) une fois à
    // la construction, même ordre de grandeur que le flood-fill déjà
    // présent dans SeedMinimumBushPerPatch (déjà accepté).
    private int[] BuildShuffledTileOrder()
    {
        int tileCount = _size * _size;
        var order = new int[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            order[i] = i;
        }

        for (int i = tileCount - 1; i > 0; i--)
        {
            int j = (int)(_rngVegetation.NextDouble() * (i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    private int ComputeDeathTick(VegetationType typeInfo, int tickCounter)
    {
        if (typeInfo.LifespanTicks <= 0)
        {
            return -1;
        }

        int variance = typeInfo.LifespanVarianceTicks;
        int roll = variance > 0 ? (int)(_rngVegetation.NextDouble() * (variance * 2 + 1)) - variance : 0;
        int lifespan = Math.Max(1, typeInfo.LifespanTicks + roll);
        return tickCounter + lifespan;
    }

    public void SpawnVegetationOfType(int x, int y, byte type, int tickCounter)
    {
        if (type == _bushTypeId)
        {
            SpawnBush(x, y, tickCounter);
        }
        else if (type == _treeTypeId)
        {
            SpawnTree(x, y, tickCounter);
        }
    }

    public void SpawnBush(int x, int y, int tickCounter)
    {
        int index = y * _size + x;
        int slot = BushCount;
        VegetationType typeInfo = _vegetationCatalog.Get(_bushTypeId);

        _bushes[slot] = new Vegetation
        {
            X = x,
            Y = y,
            Type = _bushTypeId,
            Stage = 0,
            FoodRemaining = typeInfo.FoodValue,
            DeathTick = ComputeDeathTick(typeInfo, tickCounter),
        };
        _bushIndexAt[index] = slot;
        BushCount++;
    }

    public void SpawnTree(int x, int y, int tickCounter)
    {
        int index = y * _size + x;
        int slot = TreeCount;
        VegetationType typeInfo = _vegetationCatalog.Get(_treeTypeId);

        _trees[slot] = new Vegetation
        {
            X = x,
            Y = y,
            Type = _treeTypeId,
            Stage = 0,
            FoodRemaining = typeInfo.FoodValue,
            DeathTick = ComputeDeathTick(typeInfo, tickCounter),
        };
        _treeIndexAt[index] = slot;
        TreeCount++;
    }

    public void RemoveBushAt(int slot, int tickCounter)
    {
        Vegetation removed = _bushes[slot];
        int removedIndex = removed.Y * _size + removed.X;
        _bushIndexAt[removedIndex] = -1;
        _vegetationClearedTick[removedIndex] = tickCounter;

        BushCount--;
        if (slot != BushCount)
        {
            Vegetation moved = _bushes[BushCount];
            _bushes[slot] = moved;
            _bushIndexAt[moved.Y * _size + moved.X] = slot;
        }
    }

    public void RemoveTreeAt(int slot, int tickCounter)
    {
        Vegetation removed = _trees[slot];
        int removedIndex = removed.Y * _size + removed.X;
        _treeIndexAt[removedIndex] = -1;
        _vegetationClearedTick[removedIndex] = tickCounter;

        TreeCount--;
        if (slot != TreeCount)
        {
            Vegetation moved = _trees[TreeCount];
            _trees[slot] = moved;
            _treeIndexAt[moved.Y * _size + moved.X] = slot;
        }
    }

    public Vegetation GetVegetation(int index) => index < BushCount ? _bushes[index] : _trees[index - BushCount];

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
        int index = y * _size + x;

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

    public void ForceSpawnVegetation(int x, int y, byte type, byte stage, int tickCounter)
    {
        // Si la densité configurée pour ce type est nulle à cette taille
        // de monde (ex. treeDensity 0.0004 × 16² = 0 → tableau vide),
        // on ne peut rien forcer. Le monde n'a pas de capacité pour ce
        // type de végétation.
        if ((type == _bushTypeId && _bushes.Length == 0) ||
            (type == _treeTypeId && _trees.Length == 0))
        {
            return;
        }

        ClearVegetationAt(x, y, tickCounter);

        // "Force" doit garantir la place même si le tableau (capacité =
        // densité x taille, zéro marge) est déjà plein -- depuis
        // SeedInitialVegetation (s15), c'est le cas dès la construction
        // pour toute tuile qui n'était pas encore de l'herbe à ce
        // moment-là. On libère un slot arbitraire (le premier) du même
        // type plutôt que de planter hors limites.
        if (type == _bushTypeId && BushCount >= _bushes.Length)
        {
            RemoveBushAt(0, tickCounter);
        }
        else if (type == _treeTypeId && TreeCount >= _trees.Length)
        {
            RemoveTreeAt(0, tickCounter);
        }

        SpawnVegetationOfType(x, y, type, tickCounter);

        int index = y * _size + x;
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
        int index = y * _size + x;

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
        int index = y * _size + x;

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
    public void ClearVegetationAt(int x, int y, int tickCounter)
    {
        int index = y * _size + x;

        int bushSlot = _bushIndexAt[index];
        if (bushSlot != -1)
        {
            RemoveBushAt(bushSlot, tickCounter);
            return;
        }

        int treeSlot = _treeIndexAt[index];
        if (treeSlot != -1)
        {
            RemoveTreeAt(treeSlot, tickCounter);
        }
    }

    // Seam de test : vide toute la végétation posée par
    // SeedInitialVegetation (s15) -- nécessaire pour les scénarios qui
    // exigent une carte réellement sans nourriture (RemoveBushAt/
    // RemoveTreeAt en boucle plutôt qu'un simple Clear() du tableau, le
    // swap-with-last laisse toujours le tableau dans un état valide).
    public void ClearAllVegetation(int tickCounter)
    {
        while (BushCount > 0)
        {
            RemoveBushAt(BushCount - 1, tickCounter);
        }

        while (TreeCount > 0)
        {
            RemoveTreeAt(TreeCount - 1, tickCounter);
        }
    }

    // Distance euclidienne au buisson mûr le plus proche, SANS la limite
    // de portée du BFS de gameplay : balaie tout _bushes[0..BushCount),
    // la "vraie" distance. double.PositiveInfinity si aucun buisson mûr.
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

    public void TickVegetationGrowth()
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

    public void TickVegetationAging(int tickCounter)
    {
        int i = 0;
        while (i < BushCount)
        {
            if (_bushes[i].DeathTick != -1 && tickCounter >= _bushes[i].DeathTick)
            {
                RemoveBushAt(i, tickCounter);
            }
            else
            {
                i++;
            }
        }

        i = 0;
        while (i < TreeCount)
        {
            if (_trees[i].DeathTick != -1 && tickCounter >= _trees[i].DeathTick)
            {
                RemoveTreeAt(i, tickCounter);
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
    public void TickVegetationSpread(int tickCounter)
    {
        SpreadBushesLocally(tickCounter);
        SpreadTreesLocally(tickCounter);
        SpawnSpontaneously(tickCounter);
    }

    private void SpreadBushesLocally(int tickCounter)
    {
        int count = BushCount;
        for (int i = 0; i < count; i++)
        {
            int x = _bushes[i].X;
            int y = _bushes[i].Y;
            TrySpreadBushTo(x - 1, y, tickCounter);
            TrySpreadBushTo(x + 1, y, tickCounter);
            TrySpreadBushTo(x, y - 1, tickCounter);
            TrySpreadBushTo(x, y + 1, tickCounter);
        }
    }

    private void TrySpreadBushTo(int x, int y, int tickCounter)
    {
        if (x < 0 || x >= _size || y < 0 || y >= _size || BushCount >= _bushes.Length)
        {
            return;
        }

        int index = y * _size + x;
        if (_terrainSystem.Terrain[index] != _terrainSystem.GrassId || _bushIndexAt[index] != -1 || _treeIndexAt[index] != -1)
        {
            return;
        }

        if (tickCounter - _vegetationClearedTick[index] < _config.VegetationRegrowthDelayTicks)
        {
            return;
        }

        if (_rngVegetation.NextDouble() >= _config.VegetationSpreadChance)
        {
            return;
        }

        SpawnBush(x, y, tickCounter);
    }

    private void SpreadTreesLocally(int tickCounter)
    {
        int count = TreeCount;
        for (int i = 0; i < count; i++)
        {
            int x = _trees[i].X;
            int y = _trees[i].Y;
            TrySpreadTreeTo(x - 1, y, tickCounter);
            TrySpreadTreeTo(x + 1, y, tickCounter);
            TrySpreadTreeTo(x, y - 1, tickCounter);
            TrySpreadTreeTo(x, y + 1, tickCounter);
        }
    }

    private void TrySpreadTreeTo(int x, int y, int tickCounter)
    {
        if (x < 0 || x >= _size || y < 0 || y >= _size || TreeCount >= _trees.Length)
        {
            return;
        }

        int index = y * _size + x;
        if (_terrainSystem.Terrain[index] != _terrainSystem.GrassId || _bushIndexAt[index] != -1 || _treeIndexAt[index] != -1)
        {
            return;
        }

        if (tickCounter - _vegetationClearedTick[index] < _config.VegetationRegrowthDelayTicks)
        {
            return;
        }

        if (_rngVegetation.NextDouble() >= _config.TreeSpreadChance)
        {
            return;
        }

        SpawnTree(x, y, tickCounter);
    }

    // Germination spontanée résiduelle (piège symétrique) : sans elle,
    // une région entièrement rasée (aucun buisson/arbre voisin pour
    // diffuser) ne pourrait jamais repartir. Taux volontairement bas par
    // rapport à la diffusion locale -- un filet de sécurité, pas le
    // mécanisme principal de repousse. Même scan tournant que l'ancien
    // mécanisme (évite le biais spatial fixé en s11).
    private void SpawnSpontaneously(int tickCounter)
    {
        int tileCount = _terrainSystem.Terrain.Length;
        int startIndex = (int)(_rngVegetation.NextDouble() * tileCount);

        for (int offset = 0; offset < tileCount; offset++)
        {
            if (BushCount >= _bushes.Length && TreeCount >= _trees.Length)
            {
                return;
            }

            int index = (startIndex + offset) % tileCount;

            if (_terrainSystem.Terrain[index] != _terrainSystem.GrassId || _bushIndexAt[index] != -1 || _treeIndexAt[index] != -1)
            {
                continue;
            }

            if (tickCounter - _vegetationClearedTick[index] < _config.VegetationRegrowthDelayTicks)
            {
                continue;
            }

            int x = index % _size;
            int y = index / _size;

            if (BushCount < _bushes.Length && _rngVegetation.NextDouble() < _config.VegetationSpontaneousChance)
            {
                SpawnBush(x, y, tickCounter);
            }
            else if (TreeCount < _trees.Length && _rngVegetation.NextDouble() < _config.TreeSpontaneousChance)
            {
                SpawnTree(x, y, tickCounter);
            }
        }
    }

    public void TickAshRecovery()
    {
        for (int i = 0; i < _terrainSystem.Terrain.Length; i++)
        {
            if (_terrainSystem.Terrain[i] != _terrainSystem.AshId)
            {
                continue;
            }

            if (_rngVegetation.NextDouble() < _config.AshToGrassChance)
            {
                _terrainSystem.RecoverAshToGrass(i);
            }
        }
    }

    // Densité de nourriture locale (session 14), même grille que les
    // agents -- reconstruite une fois par tick végétation en scannant
    // _bushes[0..BushCount) une fois. Seuls les buissons MÛRS comptent
    // comme nourriture disponible, même critère que CountMatureVegetationOfType.
    public void RebuildFoodDensityGrid()
    {
        Array.Clear(_foodPerCell);

        int matureStage = _vegetationCatalog.Get(_bushTypeId).MatureStage;
        for (int i = 0; i < BushCount; i++)
        {
            ref Vegetation bush = ref _bushes[i];
            if (bush.Stage < matureStage)
            {
                continue;
            }

            int cell = AgentCellIndex(bush.X, bush.Y);
            _foodPerCell[cell]++;
        }
    }

    // Conductivité par cellule (session 14d) : fraction de tuiles HERBE
    // parmi toutes les tuiles de la cellule, plancher 0.05. Balaie tout
    // le terrain une fois (coût du même ordre que TickAshRecovery, déjà
    // un scan complet à cette cadence).
    public void RebuildCellConductivity()
    {
        Array.Clear(_cellGrassCountScratch);
        Array.Clear(_cellTotalCountScratch);

        for (int y = 0; y < _size; y++)
        {
            for (int x = 0; x < _size; x++)
            {
                int cell = AgentCellIndex(x, y);
                _cellTotalCountScratch[cell]++;
                if (_terrainSystem.Terrain[y * _size + x] == _terrainSystem.GrassId)
                {
                    _cellGrassCountScratch[cell]++;
                }
            }
        }

        for (int c = 0; c < _cellConductivity.Length; c++)
        {
            double fraction = _cellTotalCountScratch[c] > 0 ? (double)_cellGrassCountScratch[c] / _cellTotalCountScratch[c] : 0.0;
            _cellConductivity[c] = Math.Max(0.05, fraction);
        }
    }

    // Diffusion du champ de nourriture (session 14c, terrain-aware
    // depuis s14d) : même principe que TickFire (lire _current, écrire
    // _next, swap), mais dense -- chaque cellule diffuse vers ses 4
    // voisines à chaque passe, sur FoodGradientDiffusionIterations
    // passes. Pondérée par _cellConductivity : le sable/pierre/cendre/
    // eau ne portent jamais de buisson et conduisent donc mal le
    // signal -- sans ça, le gradient d'un gros amas lointain traverserait
    // un désert intact et attirerait un agent à travers une zone
    // stérile plutôt que vers une source proche.
    public void RebuildFoodGradient()
    {
        int cellCount = _agentGridWidth * _agentGridHeight;
        for (int c = 0; c < cellCount; c++)
        {
            _foodGradientA[c] = _foodPerCell[c];
        }

        double[] current = _foodGradientA;
        double[] next = _foodGradientB;

        for (int iter = 0; iter < _config.FoodGradientDiffusionIterations; iter++)
        {
            for (int cy = 0; cy < _agentGridHeight; cy++)
            {
                for (int cx = 0; cx < _agentGridWidth; cx++)
                {
                    int c = cy * _agentGridWidth + cx;
                    double weightedSum = 0.0;
                    double weightSum = 0.0;

                    if (cx > 0) { int n = c - 1; weightedSum += current[n] * _cellConductivity[n]; weightSum += _cellConductivity[n]; }
                    if (cx < _agentGridWidth - 1) { int n = c + 1; weightedSum += current[n] * _cellConductivity[n]; weightSum += _cellConductivity[n]; }
                    if (cy > 0) { int n = c - _agentGridWidth; weightedSum += current[n] * _cellConductivity[n]; weightSum += _cellConductivity[n]; }
                    if (cy < _agentGridHeight - 1) { int n = c + _agentGridWidth; weightedSum += current[n] * _cellConductivity[n]; weightSum += _cellConductivity[n]; }

                    double avgNeighbors = weightSum > 0 ? weightedSum / weightSum : current[c];
                    next[c] = current[c] + _config.FoodGradientDiffusionRate * _cellConductivity[c] * (avgNeighbors - current[c]);
                }
            }

            (current, next) = (next, current);
        }

        _foodGradient = current;
    }
}
