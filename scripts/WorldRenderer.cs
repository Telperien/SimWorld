using System.Collections.Generic;
using System.IO;
using Godot;
using Simulation;

public partial class WorldRenderer : Sprite2D
{
    private const int Seed = 42;
    private const int Size = 512;
    private const int FireRadius = 3;
    private static readonly Color FireColor = new(1f, 0.4f, 0f);

    // Teinte territoire (session territoire) : constantes de rendu, pas
    // des valeurs de gameplay -- opacité FAIBLE pour laisser agents/
    // buissons/arbres parfaitement lisibles par-dessus, plus marquée à
    // 1 tuile d'une frontière pour un liseré visible sans géométrie
    // séparée.
    private const float TerritoryTintAlpha = 0.22f;
    private const float TerritoryBorderAlpha = 0.45f;
    private const int TerritoryBorderMarginTiles = 1;

    public World World { get; private set; } = null!;

    private Catalog<TerrainType> _catalog = null!;
    private Catalog<VegetationType> _vegetationCatalog = null!;
    private Catalog<SpeciesType> _speciesCatalog = null!;
    private Image _image = null!;
    private ImageTexture _texture = null!;
    private double _accumulator;
    private Dictionary<uint, Color> _clanColorByOwnerId = null!;

    public override void _Ready()
    {
        _catalog = TerrainCatalog.Load(ReadJsonOrThrow("res://data/terrain.json"));
        _vegetationCatalog = VegetationCatalog.Load(ReadJsonOrThrow("res://data/vegetation.json"));
        _speciesCatalog = SpeciesCatalog.Load(ReadJsonOrThrow("res://data/species.json"));
        var config = SimulationConfig.Load(ReadJsonOrThrow("res://data/simulation.json"));

        World = new World(Seed, Size, _catalog, _vegetationCatalog, _speciesCatalog, config);

        // Couleur de territoire par clan (session territoire) : même
        // convention que chaque renderer existant -- charge son propre
        // PaletteCatalog, pas de dépendance croisée entre renderers
        // (cf. AgentRenderer.cs/HomeRenderer.cs).
        PaletteCatalog paletteCatalog = PaletteCatalog.Load(ReadJsonOrThrow("res://data/palette.json"));
        _clanColorByOwnerId = new Dictionary<uint, Color>();
        for (int c = 0; c < World.ClanCount; c++)
        {
            Clan clan = World.GetClan(c);
            _clanColorByOwnerId[clan.Id] = ColorFromHex(paletteCatalog.Get((byte)(clan.Id % paletteCatalog.Count)).Color);
        }

        _image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        _texture = ImageTexture.CreateFromImage(_image);
        Texture = _texture;

        Redraw();
    }

    // Session filet : FileAccess.GetFileAsString (Godot) renvoie une
    // chaine vide sans lever si le fichier manque -- Load() echouerait
    // alors plus loin avec un message generique ("JSON is empty or
    // invalid") sans dire QUEL fichier. Verifie l'existence d'abord
    // pour un message qui nomme le fichier attendu.
    private static string ReadJsonOrThrow(string resPath)
    {
        if (!Godot.FileAccess.FileExists(resPath))
        {
            throw new FileNotFoundException($"fichier de configuration introuvable : '{resPath}'", resPath);
        }
        return Godot.FileAccess.GetFileAsString(resPath);
    }

    public override void _Process(double delta)
    {
        _accumulator += delta;
        bool ticked = false;

        while (_accumulator >= World.TickIntervalSeconds)
        {
            World.Tick(World.TickIntervalSeconds);
            _accumulator -= World.TickIntervalSeconds;
            ticked = true;
        }

        if (ticked)
        {
            Redraw();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Vector2 worldPosition = GetGlobalMousePosition();
            int x = (int)Mathf.Floor(worldPosition.X);
            int y = (int)Mathf.Floor(worldPosition.Y);
            World.Execute(new SpawnFire(x, y, FireRadius));
        }
    }

    // La végétation (buisson/arbre) est rendue par VegetationRenderer
    // (MultiMeshInstance2D GPU-instancié, session 17b) -- peindre un
    // pixel par plante ici, à chaque tick, pour des dizaines de milliers
    // d'entités serait un vrai risque de performance (SetPixel est lent,
    // et un buisson/arbre fait maintenant 4x4 à 14x14, pas 1 pixel).
    // Ce redraw ne peint plus que le TERRAIN.
    private void Redraw()
    {
        int regionCellSize = World.RegionCellSize;
        int regionGridWidth = World.RegionGridWidth;
        int regionGridHeight = World.RegionGridHeight;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Color color = World.IsBurning(x, y)
                    ? FireColor
                    : ColorFromHex(_catalog.Get(World.GetTerrainId(x, y)).Color);

                uint owner = World.GetRegionOwnerAt(x, y);
                if (owner != TerritorySystem.NoOwner && _clanColorByOwnerId.TryGetValue(owner, out Color clanColor))
                {
                    bool nearBorder = IsNearTerritoryBorder(x, y, owner, regionCellSize, regionGridWidth, regionGridHeight);
                    color = color.Lerp(clanColor, nearBorder ? TerritoryBorderAlpha : TerritoryTintAlpha);
                }

                _image.SetPixel(x, y, color);
            }
        }

        _texture.Update(_image);
    }

    // Frontière (session territoire) : un pixel à moins de
    // TerritoryBorderMarginTiles du bord de SA cellule région, dont la
    // cellule voisine dans cette direction a un propriétaire DIFFÉRENT
    // (y compris neutre), reçoit une teinte plus marquée -- crée un
    // liseré visible sans géométrie séparée.
    private bool IsNearTerritoryBorder(int x, int y, uint owner, int regionCellSize, int regionGridWidth, int regionGridHeight)
    {
        int cellX = x / regionCellSize;
        int cellY = y / regionCellSize;
        int localX = x - cellX * regionCellSize;
        int localY = y - cellY * regionCellSize;

        if (localX < TerritoryBorderMarginTiles && cellX > 0 && World.GetRegionOwnerAt(x - regionCellSize, y) != owner)
        {
            return true;
        }
        if (localX >= regionCellSize - TerritoryBorderMarginTiles && cellX < regionGridWidth - 1 && World.GetRegionOwnerAt(x + regionCellSize, y) != owner)
        {
            return true;
        }
        if (localY < TerritoryBorderMarginTiles && cellY > 0 && World.GetRegionOwnerAt(x, y - regionCellSize) != owner)
        {
            return true;
        }
        if (localY >= regionCellSize - TerritoryBorderMarginTiles && cellY < regionGridHeight - 1 && World.GetRegionOwnerAt(x, y + regionCellSize) != owner)
        {
            return true;
        }

        return false;
    }

    private static Color ColorFromHex(uint hex)
    {
        float r = ((hex >> 16) & 0xFF) / 255f;
        float g = ((hex >> 8) & 0xFF) / 255f;
        float b = (hex & 0xFF) / 255f;
        return new Color(r, g, b);
    }
}
