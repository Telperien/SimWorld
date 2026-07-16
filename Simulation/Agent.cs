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

    // 0 = droite, 1 = gauche.
    public byte Facing;

    // Ticks de réflexion à attendre avant de retenter une recherche de
    // nourriture après un échec (cf. cooldown de famine, session 10).
    public byte SeekCooldown;

    // Errance dirigée (session 13) : marche corrélée plutôt qu'aléatoire
    // pure, pour un déplacement net linéaire en N plutôt qu'en √N.
    // Influence le comportement -> incluse dans Hash().
    public byte WanderDirection;
    public byte WanderTicksRemaining;

    // --- Diagnostic (session 12) ---
    // Écrits uniquement, jamais lus par une décision : n'influencent
    // jamais le comportement, donc exclus de World.Hash().
    public uint SearchFailureStreak;
    public uint TicksIdle;
    public uint TicksMoving;
    public uint TicksSeeking;
    public uint TicksEating;
    public byte HungerAtLastMealStart;
}
