namespace Simulation;

public enum AgentState : byte
{
    Idle = 0,
    Moving = 1,
    Seeking = 2,
    Eating = 3,
    Dead = 4,
}

public struct Agent
{
    // Parent inconnu. Jamais un index : les tableaux d'agents sont
    // compactés par swap-with-last, seul Id survit à une compaction.
    public const uint UnknownParent = uint.MaxValue;

    // Identité stable et permanente, distincte de la position dans le
    // tableau (qui change à chaque mort via CleanupDeadAgents).
    public uint Id;

    public float X;
    public float Y;
    public int TargetX;
    public int TargetY;
    public uint MotherId;
    public uint FatherId;
    public bool Tracked;
    public AgentState State;
    public byte Species;
    public byte Hunger;
    public byte EatingTimer;

    // 0 = droite, 1 = gauche.
    public byte Facing;
}
