using System.IO;
using Godot;
using Simulation;

public partial class WorldRenderer : Sprite2D
{
    private const int Seed = 42;
    private const int Size = 512;
    private const int FireRadius = 3;
    private static readonly Color FireColor = new(1f, 0.4f, 0f);

    public World World { get; private set; } = null!;

    private TerrainCatalog _catalog = null!;
    private VegetationCatalog _vegetationCatalog = null!;
    private SpeciesCatalog _speciesCatalog = null!;
    private Image _image = null!;
    private ImageTexture _texture = null!;
    private double _accumulator;

    public override void _Ready()
    {
        _catalog = TerrainCatalog.Load(ReadJsonOrThrow("res://data/terrain.json"));
        _vegetationCatalog = VegetationCatalog.Load(ReadJsonOrThrow("res://data/vegetation.json"));
        _speciesCatalog = SpeciesCatalog.Load(ReadJsonOrThrow("res://data/species.json"));
        var config = SimulationConfig.Load(ReadJsonOrThrow("res://data/simulation.json"));

        World = new World(Seed, Size, _catalog, _vegetationCatalog, _speciesCatalog, config);

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
    }

    private static Color ColorFromHex(uint hex)
    {
        float r = ((hex >> 16) & 0xFF) / 255f;
        float g = ((hex >> 8) & 0xFF) / 255f;
        float b = (hex & 0xFF) / 255f;
        return new Color(r, g, b);
    }
}
