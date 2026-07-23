using Godot;
using Simulation;

// Marqueur de foyer (session foyers) : position FIXE (Home.X/Y ne bouge
// jamais cette session, contrairement aux agents/à la végétation) --
// pas besoin de _Process, une seule pose au démarrage. Un
// MultiMeshInstance2D PAR FOYER (HomeCount est petit, InitialClanCount)
// plutôt qu'un bucket partagé : chaque foyer a sa propre teinte de clan,
// et une texture par instance évite d'introduire UseColors/instance
// colors pour un si petit nombre d'entités.
public partial class HomeRenderer : Node2D
{
    private const ulong SpriteSeed = 555;

    public override void _Ready()
    {
        var worldRenderer = GetNode<WorldRenderer>("../WorldSprite");
        World world = worldRenderer.World;

        string paletteJson = FileAccess.GetFileAsString("res://data/palette.json");
        PaletteCatalog paletteCatalog = PaletteCatalog.Load(paletteJson);

        for (int i = 0; i < world.HomeCount; i++)
        {
            Home home = world.GetHome(i);
            uint hueColor = paletteCatalog.Get((byte)(home.ClanId % paletteCatalog.Count)).Color;
            ulong seed = SpriteGenerator.DeriveTileSeed((int)SpriteSeed, home.X, home.Y);

            SpriteBitmap sprite = SpriteGenerator.GenerateHomeMarkerSprite(seed, hueColor);
            MultiMeshInstance2D node = BuildInstance(sprite);
            var position = new Vector2(home.X + 0.5f, home.Y + 0.5f);
            node.Multimesh.SetInstanceTransform2D(0, new Transform2D(0, position));
            AddChild(node);
        }
    }

    private static MultiMeshInstance2D BuildInstance(SpriteBitmap sprite)
    {
        var node = new MultiMeshInstance2D();
        var mesh = new QuadMesh { Size = new Vector2(sprite.Width, sprite.Height) };
        node.Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            Mesh = mesh,
            InstanceCount = 1,
        };
        node.Texture = BuildTexture(sprite);
        return node;
    }

    private static ImageTexture BuildTexture(SpriteBitmap sprite)
    {
        var image = Image.CreateEmpty(sprite.Width, sprite.Height, false, Image.Format.Rgba8);
        for (int y = 0; y < sprite.Height; y++)
        {
            for (int x = 0; x < sprite.Width; x++)
            {
                int offset = (y * sprite.Width + x) * 4;
                var color = new Color(
                    sprite.Rgba[offset] / 255f,
                    sprite.Rgba[offset + 1] / 255f,
                    sprite.Rgba[offset + 2] / 255f,
                    sprite.Rgba[offset + 3] / 255f);
                image.SetPixel(x, y, color);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
