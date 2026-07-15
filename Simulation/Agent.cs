namespace Simulation;

public enum AgentState : byte
{
    Idle = 0,
    Moving = 1,
}

public struct Agent
{
    public float X;
    public float Y;
    public int TargetX;
    public int TargetY;
    public int MotherId;
    public int FatherId;
    public bool Tracked;
    public AgentState State;
    public byte Species;
}
