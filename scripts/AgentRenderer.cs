using Godot;
using Simulation;

public partial class AgentRenderer : MultiMeshInstance2D
{
    // Asymétrique exprès : le flip gauche/droite (via Facing) doit se voir.
    private static readonly string[] SpriteRows = { ".#.", "##.", ".#." };

    private static readonly Color IdleColor = new(0.9f, 0.15f, 0.15f);
    private static readonly Color SeekingColor = new(0.95f, 0.6f, 0.1f);
    private static readonly Color EatingColor = new(0.2f, 0.8f, 0.3f);

    private World _world = null!;

    public override void _Ready()
    {
        var worldRenderer = GetNode<WorldRenderer>("../WorldSprite");
        _world = worldRenderer.World;

        var mesh = new QuadMesh { Size = new Vector2(3, 3) };
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
        int height = SpriteRows.Length;
        int width = SpriteRows[0].Length;
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);

        for (int y = 0; y < height; y++)
        {
            string row = SpriteRows[y];
            for (int x = 0; x < width; x++)
            {
                Color color = row[x] == '#' ? Colors.White : new Color(0, 0, 0, 0);
                image.SetPixel(x, y, color);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
