using Godot;
using Simulation;

public partial class WorldRenderer : Sprite2D
{
    private const int Seed = 42;
    private const int Size = 512;
    private const double TickInterval = 1.0 / 30.0;
    private const int FireRadius = 3;
    private static readonly Color FireColor = new(1f, 0.4f, 0f);

    private World _world = null!;
    private TerrainCatalog _catalog = null!;
    private Image _image = null!;
    private ImageTexture _texture = null!;
    private double _accumulator;

    public override void _Ready()
    {
        string json = FileAccess.GetFileAsString("res://data/terrain.json");
        _catalog = TerrainCatalog.Load(json);
        _world = new World(Seed, Size, _catalog);

        _image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        _texture = ImageTexture.CreateFromImage(_image);
        Texture = _texture;

        Redraw();
    }

    public override void _Process(double delta)
    {
        _accumulator += delta;
        bool ticked = false;

        while (_accumulator >= TickInterval)
        {
            _world.Tick(TickInterval);
            _accumulator -= TickInterval;
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
            _world.Execute(new SpawnFire(x, y, FireRadius));
        }
    }

    private void Redraw()
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Color color = _world.IsBurning(x, y)
                    ? FireColor
                    : ColorFromHex(_catalog.Get(_world.GetTerrainId(x, y)).Color);
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
