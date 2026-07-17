namespace Simulation;

// Résultat de World.AnalyzeGrassConnectivity() (session 17b, diagnostic
// pur). Le quadrant d'une poche est celui de sa tuile de départ du
// flood-fill (approximation simple, pas un centroïde exact) -- correct
// pour juger si le déficit de végétation d'un quadrant suit son nombre
// de poches, pas pour un calcul géométrique précis.
public sealed class GrassConnectivityReport
{
    public int PatchCount { get; }
    public int MinSize { get; }
    public int MedianSize { get; }
    public int MaxSize { get; }
    public int PatchesWithNoBush { get; }
    public IReadOnlyList<int> PatchCountByQuadrant { get; }
    public IReadOnlyList<int> PatchesWithNoBushByQuadrant { get; }

    public GrassConnectivityReport(
        int patchCount,
        int minSize,
        int medianSize,
        int maxSize,
        int patchesWithNoBush,
        int[] patchCountByQuadrant,
        int[] patchesWithNoBushByQuadrant)
    {
        PatchCount = patchCount;
        MinSize = minSize;
        MedianSize = medianSize;
        MaxSize = maxSize;
        PatchesWithNoBush = patchesWithNoBush;
        PatchCountByQuadrant = patchCountByQuadrant;
        PatchesWithNoBushByQuadrant = patchesWithNoBushByQuadrant;
    }
}
