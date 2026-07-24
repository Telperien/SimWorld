using System.Collections.Generic;
using System.IO;
using Godot;
using Simulation;

public partial class WorldRenderer : Sprite2D
{
    private const int Seed = 42;
    private const int Size = 512;
    private const int FireRadius = 3;
    private const ulong BuildingSpriteSeed = 777;
    private static readonly Color FireColor = new(1f, 0.4f, 0f);

    // Rendu territoire (session territoire, 5e tentative) :
    // abandon des filtres sur le terrain — le sol vert foncé n'a de
    // marge NI en luminosité NI en saturation, aucun filtre ne se voit.
    //
    // Solution : contour ÉPAIS (3 px) et très clair/saturé, dessiné
    // PAR-DESSUS le terrain sur un Sprite2D SÉPARÉ, inséré APRÈS
    // VegetationMultiMesh dans l'arbre de scène — le contour n'est
    // plus écrasé par la végétation.
    // Interpolation bilinéaire des influences pour lisser l'escalier
    // de la grille de régions.
    private const int TerritoryBorderWidth = 4;
    private const float TerritoryBorderAlpha = 0.85f;

    public World World { get; private set; } = null!;

    private Catalog<TerrainType> _catalog = null!;
    private Catalog<VegetationType> _vegetationCatalog = null!;
    private Catalog<SpeciesType> _speciesCatalog = null!;
    private Catalog<BuildingType> _buildingCatalog = null!;
    private Image _image = null!;
    private ImageTexture _texture = null!;
    private double _accumulator;
    private Dictionary<uint, Color> _clanColorByOwnerId = null!;

    // Sprite séparé pour le contour territorial, inséré APRÈS
    // VegetationMultiMesh dans l'arbre (cf. _Ready).
    private Sprite2D _borderSprite = null!;
    private Image _borderImage = null!;
    private ImageTexture _borderTexture = null!;

    // Bâtiments (session bâtiments) : peints dans un sprite séparé
    // entre la végétation et le contour territorial. Pas de MultiMesh,
    // redessinés uniquement au changement (dirty flag).
    private Sprite2D _buildingSprite = null!;
    private Image _buildingImage = null!;
    private ImageTexture _buildingTexture = null!;
    private int _lastBuildingCount;
    private int _lastBuildingVersion; // Somme des tiers pour détecter les upgrades.

    public override void _Ready()
    {
        _catalog = TerrainCatalog.Load(ReadJsonOrThrow("res://data/terrain.json"));
        _vegetationCatalog = VegetationCatalog.Load(ReadJsonOrThrow("res://data/vegetation.json"));
        _speciesCatalog = SpeciesCatalog.Load(ReadJsonOrThrow("res://data/species.json"));
        var config = SimulationConfig.Load(ReadJsonOrThrow("res://data/simulation.json"));

        _buildingCatalog = BuildingCatalog.Load(ReadJsonOrThrow("res://data/buildings.json"));
        World = new World(Seed, Size, _catalog, _vegetationCatalog, _speciesCatalog, _buildingCatalog, config);

        PaletteCatalog paletteCatalog = PaletteCatalog.Load(ReadJsonOrThrow("res://data/palette.json"));
        _clanColorByOwnerId = new Dictionary<uint, Color>();
        for (int c = 0; c < World.ClanCount; c++)
        {
            Clan clan = World.GetClan(c);
            _clanColorByOwnerId[clan.Id] = ColorFromHex(paletteCatalog.Get((byte)(clan.Id % paletteCatalog.Count)).Color);
        }

        // Sprite principal : terrain uniquement.
        _image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        _texture = ImageTexture.CreateFromImage(_image);
        Texture = _texture;

        // Sprite séparé pour le contour territorial.
        // Créé dynamiquement et inséré APRÈS VegetationMultiMesh dans
        // l'arbre de scène, pour que le contour passe par-dessus la
        // végétation (et reste sous les agents).
        _borderImage = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        _borderTexture = ImageTexture.CreateFromImage(_borderImage);
        _borderSprite = new Sprite2D
        {
            Texture = _borderTexture,
            Centered = false,
        };

        // Bâtiments : sprite séparé entre végétation et contour.
        _buildingImage = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        _buildingTexture = ImageTexture.CreateFromImage(_buildingImage);
        _buildingSprite = new Sprite2D
        {
            Texture = _buildingTexture,
            Centered = false,
        };
        // Ordre : _buildingSprite d'abord, puis _borderSprite par-dessus.
        AddChild(_buildingSprite);
        AddChild(_borderSprite);

        Redraw();
    }

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
            RedrawBuildingsIfDirty();
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

    private void Redraw()
    {
        int regionCellSize = World.RegionCellSize;
        int regionGridWidth = World.RegionGridWidth;
        int regionGridHeight = World.RegionGridHeight;

        // Passe 1 : terrain brut sur le sprite principal.
        // Aucun filtre territoire — le sol vert foncé n'a pas de marge
        // pour qu'un filtre soit visible.
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Color color = World.IsBurning(x, y)
                    ? FireColor
                    : ColorFromHex(_catalog.Get(World.GetTerrainId(x, y)).Color);
                _image.SetPixel(x, y, color);
            }
        }
        _texture.Update(_image);

        // Passe 2 : contour de territoire sur le sprite séparé.
        // On efface d'abord le sprite contour (transparent), puis on
        // redessine les frontières.
        _borderImage.Fill(Colors.Transparent);
        DrawTerritoryBorders(regionCellSize, regionGridWidth, regionGridHeight);
        _borderTexture.Update(_borderImage);

        // Diagnostic temporaire : compter les pixels non-transparents.
        int nonTransparentPixels = 0;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (_borderImage.GetPixel(x, y).A > 0f)
                    nonTransparentPixels++;
        GD.Print($"[Territoire] Pixels de contour non-transparents : {nonTransparentPixels}");
    }

    private void DrawTerritoryBorders(int regionCellSize, int regionGridWidth, int regionGridHeight)
    {
        float invCellSize = 1.0f / regionCellSize;

        // Passe 2a : détecter les pixels de frontière (pixels dont le
        // propriétaire interpolé diffère de celui d'un voisin immédiat).
        bool[] borderMask = new bool[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int idx = y * Size + x;

                float rx = (x + 0.5f) * invCellSize;
                float ry = (y + 0.5f) * invCellSize;
                uint owner = World.GetInterpolatedRegionOwner((int)rx, (int)ry);

                if (owner == TerritorySystem.NoOwner)
                    continue;

                // Vérifier les 4 voisins immédiats (connexité 4).
                bool isBorder = false;
                if (x > 0)
                {
                    float nrx = (x - 0.5f) * invCellSize;
                    uint n = World.GetInterpolatedRegionOwner((int)nrx, (int)ry);
                    if (n != owner) isBorder = true;
                }
                if (!isBorder && x < Size - 1)
                {
                    float nrx = (x + 1.5f) * invCellSize;
                    uint n = World.GetInterpolatedRegionOwner((int)nrx, (int)ry);
                    if (n != owner) isBorder = true;
                }
                if (!isBorder && y > 0)
                {
                    float nry = (y - 0.5f) * invCellSize;
                    uint n = World.GetInterpolatedRegionOwner((int)rx, (int)nry);
                    if (n != owner) isBorder = true;
                }
                if (!isBorder && y < Size - 1)
                {
                    float nry = (y + 1.5f) * invCellSize;
                    uint n = World.GetInterpolatedRegionOwner((int)rx, (int)nry);
                    if (n != owner) isBorder = true;
                }

                if (isBorder)
                {
                    borderMask[idx] = true;
                }
            }
        }

        // Passe 2b : épaissir le trait. Pour chaque pixel, chercher le
        // pixel de frontière le plus proche dans un rayon
        // TerritoryBorderWidth. Si trouvé, dessiner la couleur du clan
        // propriétaire sur le sprite contour (fond transparent).
        int bw = TerritoryBorderWidth;
        int bw2 = bw * bw;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int nearestDist2 = bw2 + 1;
                int minBX = Mathf.Max(0, x - bw);
                int maxBX = Mathf.Min(Size - 1, x + bw);
                int minBY = Mathf.Max(0, y - bw);
                int maxBY = Mathf.Min(Size - 1, y + bw);

                for (int by = minBY; by <= maxBY && nearestDist2 > 0; by++)
                {
                    for (int bx = minBX; bx <= maxBX; bx++)
                    {
                        if (!borderMask[by * Size + bx])
                            continue;

                        int dx = bx - x;
                        int dy = by - y;
                        int d2 = dx * dx + dy * dy;
                        if (d2 < nearestDist2)
                            nearestDist2 = d2;
                    }
                }

                if (nearestDist2 <= bw2)
                {
                    float rx = (x + 0.5f) * invCellSize;
                    float ry = (y + 0.5f) * invCellSize;
                    uint owner = World.GetInterpolatedRegionOwner((int)rx, (int)ry);

                    if (owner != TerritorySystem.NoOwner && _clanColorByOwnerId.TryGetValue(owner, out Color clanColor))
                    {
                        _borderImage.SetPixel(x, y, new Color(clanColor, TerritoryBorderAlpha));
                    }
                }
            }
        }
    }

    private void RedrawBuildingsIfDirty()
    {
        int buildingCount = World.BuildingCount;
        int buildingVersion = 0;
        for (int i = 0; i < buildingCount; i++)
        {
            buildingVersion += World.GetBuilding(i).Tier;
        }

        if (buildingCount == _lastBuildingCount && buildingVersion == _lastBuildingVersion)
        {
            return;
        }

        _lastBuildingCount = buildingCount;
        _lastBuildingVersion = buildingVersion;

        _buildingImage.Fill(Colors.Transparent);

        PaletteCatalog paletteCatalog = PaletteCatalog.Load(
            Godot.FileAccess.GetFileAsString("res://data/palette.json"));

        for (int i = 0; i < buildingCount; i++)
        {
            Building b = World.GetBuilding(i);
            uint hueColor = paletteCatalog.Get((byte)(b.ClanId % paletteCatalog.Count)).Color;
            ulong seed = SpriteGenerator.DeriveTileSeed((int)BuildingSpriteSeed, b.X, b.Y);

            SpriteBitmap sprite = SpriteGenerator.GenerateBuildingSprite(seed, b.Tier, hueColor);

            // Centre le sprite sur la tuile.
            int offsetX = b.X - sprite.Width / 2;
            int offsetY = b.Y - sprite.Height / 2;

            for (int sy = 0; sy < sprite.Height; sy++)
            {
                int py = offsetY + sy;
                if (py < 0 || py >= Size)
                {
                    continue;
                }

                for (int sx = 0; sx < sprite.Width; sx++)
                {
                    int px = offsetX + sx;
                    if (px < 0 || px >= Size)
                    {
                        continue;
                    }

                    int rgbaIndex = (sy * sprite.Width + sx) * 4;
                    byte alpha = sprite.Rgba[rgbaIndex + 3];
                    if (alpha == 0)
                    {
                        continue;
                    }

                    var color = new Color(
                        sprite.Rgba[rgbaIndex] / 255f,
                        sprite.Rgba[rgbaIndex + 1] / 255f,
                        sprite.Rgba[rgbaIndex + 2] / 255f,
                        alpha / 255f);
                    _buildingImage.SetPixel(px, py, color);
                }
            }
        }

        _buildingTexture.Update(_buildingImage);
    }

    private static Color ColorFromHex(uint hex)
    {
        float r = ((hex >> 16) & 0xFF) / 255f;
        float g = ((hex >> 8) & 0xFF) / 255f;
        float b = (hex & 0xFF) / 255f;
        return new Color(r, g, b);
    }
}
