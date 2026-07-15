using Godot;
using Simulation;

public partial class AgentRenderer : MultiMeshInstance2D
{
    private World _world = null!;

    public override void _Ready()
    {
        var worldRenderer = GetNode<WorldRenderer>("../WorldSprite");
        _world = worldRenderer.World;

        var mesh = new QuadMesh { Size = new Vector2(3, 3) };
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            Mesh = mesh,
            InstanceCount = _world.AgentCapacity,
        };
        Modulate = new Color(0.9f, 0.15f, 0.15f);
    }

    public override void _Process(double delta)
    {
        for (int i = 0; i < _world.AgentCapacity; i++)
        {
            Agent agent = _world.GetAgent(i);
            Multimesh.SetInstanceTransform2D(i, new Transform2D(0, new Vector2(agent.X, agent.Y)));
        }
    }
}
