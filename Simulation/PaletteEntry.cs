namespace Simulation;

// Une teinte nommée (préparation multi-race/clan, session 17b) : la
// donnée seule, aucune logique de sélection de race/clan construite
// cette session.
public sealed class PaletteEntry
{
    public required string Name { get; init; }
    public required byte Id { get; init; }
    public required uint Color { get; init; }
}
