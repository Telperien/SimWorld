using Godot;
using Simulation;

public partial class AgentRenderer : MultiMeshInstance2D
{
    // Constante de rendu (comme WorldRenderer.Seed), pas une valeur de
    // gameplay : seed fixe pour la silhouette canonique générée une
    // seule fois au démarrage (session 17b, SpriteGenerator). Le
    // masque reste blanc/transparent -- la teinte par état (couleur
    // FSM ci-dessous) reste le mécanisme d'affichage existant, la
    // teinte de palette (préparation multi-race) est un paramètre du
    // générateur mais pas encore branchée par race puisqu'aucune race
    // n'existe -- une seule silhouette blanche pour tous les agents.
    private const ulong SpriteSeed = 777;

    private static readonly Color IdleColor = new(0.9f, 0.15f, 0.15f);
    private static readonly Color SeekingColor = new(0.95f, 0.6f, 0.1f);
    private static readonly Color EatingColor = new(0.2f, 0.8f, 0.3f);

    private World _world = null!;

    public override void _Ready()
    {
        var worldRenderer = GetNode<WorldRenderer>("../WorldSprite");
        _world = worldRenderer.World;

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
            Multimesh.SetInstanceColor(i, ColorForState(agent.State));
        }
    }

    private static Color ColorForState(AgentState state) => state switch
    {
        AgentState.Seeking => SeekingColor,
        AgentState.Eating => EatingColor,
        _ => IdleColor,
    };

    private static ImageTexture CreateSpriteTexture()
    {
        // Silhouette blanche (masque), pas la teinte finale -- la
        // couleur affichée reste pilotée par SetInstanceColor (état
        // FSM) exactement comme avant, seule la FORME change (3x3
        // flèche -> silhouette humanoïde 6x8, session 17b).
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
