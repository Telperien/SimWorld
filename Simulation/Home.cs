namespace Simulation;

// Foyer (session foyers) : point d'ancrage spatial d'un clan. Aucune
// logique propre (pas de tick, pas de croissance, pas de FSM) -- son
// seul effet s'exprime dans le comportement d'errance d'un agent
// (cf. AgentClanSystem.TryStartMoving). Un foyer par clan, capacité
// fixe cette session (pas de scission/fusion), même statut que Clan[]
// en s18.
public struct Home
{
    // Aucun foyer valide -- réservé pour un futur mécanisme qui
    // retirerait un foyer. Non utilisé cette session : tout agent
    // vivant a toujours un HomeId résolvable, comme ClanId.
    public const uint NoHome = uint.MaxValue;

    // Identité stable, distincte de la position dans le tableau --
    // même raisonnement que Clan.Id (cf. AgentClanSystem._homeIndexById).
    public uint Id;

    public uint ClanId;

    public int X;
    public int Y;
}
