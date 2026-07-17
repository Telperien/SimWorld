namespace Simulation;

public sealed class VegetationType
{
    public required string Name { get; init; }
    public required byte Id { get; init; }
    public required uint Color { get; init; }

    // Couleur à maturité (session 17b) : par défaut égale à Color si le
    // JSON n'en fournit pas -- seul le buisson doit se distinguer
    // visuellement jeune/mûr, l'arbre grandit plutôt en taille (cf.
    // SpriteGenerator).
    public required uint MatureColor { get; init; }
    public required int MatureStage { get; init; }
    public required bool Flammable { get; init; }
    public required int FoodValue { get; init; }

    // 0 = immortel par l'âge (sort par un autre chemin, ex: consommation).
    public required int LifespanTicks { get; init; }
    public required int LifespanVarianceTicks { get; init; }
}
