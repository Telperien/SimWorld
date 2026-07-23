using Godot;
using Simulation;

public partial class AgentRenderer : MultiMeshInstance2D
{
    // Constante de rendu (comme WorldRenderer.Seed), pas une valeur de
    // gameplay : seed fixe pour la silhouette canonique générée une
    // seule fois au démarrage (session 17b, SpriteGenerator). Le
    // masque reste blanc/transparent -- la teinte affichée vient
    // désormais du CLAN (session rendu), plus jamais de l'état FSM.
    private const ulong SpriteSeed = 777;

    private World _world = null!;
    private Color[] _clanColors = null!;

    public override void _Ready()
    {
        var worldRenderer = GetNode<WorldRenderer>("../WorldSprite");
        _world = worldRenderer.World;

        string paletteJson = FileAccess.GetFileAsString("res://data/palette.json");
        PaletteCatalog paletteCatalog = PaletteCatalog.Load(paletteJson);

        // Couleur par clan (session rendu), même lookup que
        // HomeRenderer.cs -- précalculée une fois, ClanId non compacté
        // cette session donc indexable directement.
        _clanColors = new Color[_world.ClanCount];
        for (int c = 0; c < _world.ClanCount; c++)
        {
            uint hex = paletteCatalog.Get((byte)(_world.GetClan(c).Id % paletteCatalog.Count)).Color;
            _clanColors[c] = ColorFromHex(hex);
        }

        var mesh = new QuadMesh { Size = new Vector2(6, 8) };
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = _world.AgentCapacity,
        };
        Texture = CreateSpriteTexture();
        Modulate = Colors.White;
    }

    public override void _Process(double delta)
    {
        Multimesh.VisibleInstanceCount = _world.AliveCount;

        for (int i = 0; i < _world.AliveCount; i++)
        {
            Agent agent = _world.GetAgent(i);
            float flip = agent.Facing == 1 ? -1f : 1f;
            var transform = new Transform2D(new Vector2(flip, 0), new Vector2(0, 1), new Vector2(agent.X, agent.Y));
            Multimesh.SetInstanceTransform2D(i, transform);
            Multimesh.SetInstanceColor(i, _clanColors[(int)agent.ClanId]);
        }
    }

    // Même patron que WorldRenderer.ColorFromHex -- conversion RRGGBB
    // (PaletteCatalog) vers Godot Color, dupliquée localement (pas de
    // dépendance croisée entre renderers).
    private static Color ColorFromHex(uint hex)
    {
        float r = ((hex >> 16) & 0xFF) / 255f;
        float g = ((hex >> 8) & 0xFF) / 255f;
        float b = (hex & 0xFF) / 255f;
        return new Color(r, g, b);
    }

    private static ImageTexture CreateSpriteTexture()
    {
        // Silhouette blanche (masque), pas la teinte finale -- la
        // couleur affichée reste pilotée par SetInstanceColor (clan,
        // ci-dessus), seule la FORME vient de ce masque.
        SpriteBitmap sprite = SpriteGenerator.GenerateAgentSprite(SpriteSeed, facing: 0, hueColor: 0xFFFFFF);
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
