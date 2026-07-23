namespace Simulation;

public enum AgentState : byte
{
    Idle = 0,
    Moving = 1,

    // Marche vers une cible via BFS/gradient (inchangé, s14c) --
    // emprunté EXCLUSIVEMENT par les cueilleurs depuis la session 18
    // (manger ne déplace plus jamais un agent, cf. Eating ci-dessous).
    Seeking = 2,

    // Récolte (session 18) : arrivé au buisson, en train d'en extraire
    // la nourriture tick par tick DANS LE POOL DU CLAN (jamais dans
    // Hunger directement -- récolter et manger sont deux actions
    // distinctes).
    Harvesting = 5,

    // Pas de valeur 3 : manger n'est PLUS un état FSM (session 19c,
    // suppression du deadlock Eating/Harvest). C'était un besoin
    // modélisé comme une occupation exclusive, alors que c'est un effet
    // passif sans condition spatiale -- voir ApplyPassiveEating dans
    // World.cs, appelé chaque tick réel pour tout agent affamé, quel que
    // soit son état. CLAUDE.md, section IA : un besoin n'est jamais un
    // état FSM exclusif.

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

    // État FSM juste AVANT la mort (session 18) -- capturé avant que
    // ThinkAgent n'écrase State en Dead, pour distinguer "mourait en
    // route/en récolte" (Seeking/Harvesting) de "mourait le pool à sec"
    // (Eating, sans déplacement). Même statut que CauseOfDeath : ne
    // persiste jamais au-delà du tick où c'est écrit, exclu de Hash().
    public byte StateAtDeath;

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

    // Clan (session 18) : JAMAIS de valeur "pas de clan", contrairement
    // à MotherId/FatherId (Agent.UnknownParent). Hérité de la mère à la
    // naissance. Influence le comportement (cueillette, reproduction
    // inter-clan interdite) -> inclus dans Hash().
    public uint ClanId;

    // Diagnostic (session 18) : temps passé en Harvesting (récolte),
    // distinct de TicksEating (repas depuis le pool, sans déplacement)
    // -- écrits uniquement, jamais lus par une décision, exclus de
    // Hash() comme le reste des compteurs de la section diagnostic.
    public uint TicksHarvesting;

    // Foyer (session foyers) : JAMAIS de valeur "pas de foyer", même
    // raisonnement que ClanId. Hérité de la mère à la naissance ; au
    // spawn initial, celui du clan créé au même point que le centre de
    // grappe. Influence le comportement (ancrage d'errance) -> inclus
    // dans Hash().
    public uint HomeId;
}

public static class SeekOutcome
{
    public const byte NeverSearched = 0;
    public const byte FoundBush = 1;
    public const byte FollowingGradient = 2;
    public const byte BlindWander = 3;
}
