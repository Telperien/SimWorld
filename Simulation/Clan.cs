namespace Simulation;

// Unité politique (session 18) : ressources, reproduction, et plus
// tard diplomatie/guerre/territoire. Un clan = une race (Species) ;
// plusieurs clans peuvent partager la même race.
public struct Clan
{
    // Identité stable, distincte de la position dans le tableau --
    // même raisonnement que Agent.Id (cf. World._clanIndexById).
    public uint Id;

    // -1 = racine. NON UTILISÉ cette session (les scissions viendront
    // plus tard) mais posé dès maintenant : sans lui, une scission
    // future produirait un clan sans ascendance, l'erreur qu'on a
    // failli faire avec MotherId/FatherId.
    public int ParentClanId;

    public byte Species;

    // Pool commun de nourriture du clan (session 18) : un cueilleur y
    // dépose directement au fil de la récolte, un agent affamé y
    // puise directement, sans déplacement. Influence le comportement
    // (déclencheur de récolte, capacité à manger) -> inclus dans
    // Hash().
    public int FoodPool;
}
