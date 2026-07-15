using Godot;
using Simulation;

public partial class WorldRenderer : Sprite2D
{
    private const int Seed = 42;
    private const int Size = 512;

    public override void _Ready()
    {
        string json = FileAccess.GetFileAsString("res://data/terrain.json");
        var catalog = TerrainCatalog.Load(json);
        var world = new World(Seed, Size, catalog);

        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                byte terrainId = world.GetTerrainId(x, y);
                uint colorHex = catalog.Get(terrainId).Color;
                image.SetPixel(x, y, ColorFromHex(colorHex));
            }
        }

        Texture = ImageTexture.CreateFromImage(image);
    }

    private static Color ColorFromHex(uint hex)
    {
        float r = ((hex >> 16) & 0xFF) / 255f;
        float g = ((hex >> 8) & 0xFF) / 255f;
        float b = (hex & 0xFF) / 255f;
        return new Color(r, g, b);
    }
}
