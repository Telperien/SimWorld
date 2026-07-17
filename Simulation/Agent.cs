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

    // Âge et reproduction (session 14). Influencent le comportement
    // futur -> inclus dans Hash().
    public uint Age;

    // Roulé UNE FOIS à la naissance (espérance ± variance de l'espèce),
    // jamais retiré -- même patron que Vegetation.DeathTick.
    public uint LifespanTicks;

    // 0 = femelle, 1 = mâle.
    public byte Sex;

    // 0 = non gestante, sinon tick absolu de mise bas.
    public uint PregnantUntil;

    // Id stable du père, valide seulement pendant la gestation --
    // influence le FatherId du futur nouveau-né.
    public uint PendingFatherId;

    // Cause de la mort ce tick (Faim ou Âge), lue par CleanupDeadAgents
    // pour router vers le bon compteur. Ne persiste jamais au-delà du
    // tick où elle est écrite (l'agent est compacté hors du tableau
    // avant qu'un Hash() puisse la voir) -> exclue de Hash().
    public byte CauseOfDeath;

    // --- Diagnostic (session 12) ---
    // Écrits uniquement, jamais lus par une décision : n'influencent
    // jamais le comportement, donc exclus de World.Hash().
    public uint SearchFailureStreak;
    public uint TicksIdle;
    public uint TicksMoving;
    public uint TicksSeeking;
    public uint TicksEating;
    public byte HungerAtLastMealStart;

    // Issue du dernier cycle de décision de recherche de nourriture
    // (session 14d) : 0 = jamais cherché, 1 = buisson trouvé par BFS
    // direct, 2 = suit le gradient de nourriture, 3 = repli errance
    // aveugle. Distingue "en route vers une source connue" (légitime)
    // de "aucun signal, marche au hasard" (le vrai signe d'aveuglement).
    public byte LastSeekOutcome;
}

public static class SeekOutcome
{
    public const byte NeverSearched = 0;
    public const byte FoundBush = 1;
    public const byte FollowingGradient = 2;
    public const byte BlindWander = 3;
}
