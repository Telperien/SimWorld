namespace Simulation;

internal sealed class PerlinNoise
{
    private readonly byte[] _permutation;

    public PerlinNoise(Rng rng)
    {
        var p = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            p[i] = (byte)i;
        }

        for (int i = 255; i > 0; i--)
        {
            int j = (int)(rng.NextUInt64() % (ulong)(i + 1));
            (p[i], p[j]) = (p[j], p[i]);
        }

        _permutation = new byte[512];
        for (int i = 0; i < 512; i++)
        {
            _permutation[i] = p[i & 255];
        }
    }

    // Retourne une valeur approximativement dans [-1, 1].
    public double Sample(double x, double y)
    {
        int xi = (int)Math.Floor(x) & 255;
        int yi = (int)Math.Floor(y) & 255;
        double xf = x - Math.Floor(x);
        double yf = y - Math.Floor(y);

        double u = Fade(xf);
        double v = Fade(yf);

        int aa = _permutation[_permutation[xi] + yi];
        int ab = _permutation[_permutation[xi] + yi + 1];
        int ba = _permutation[_permutation[xi + 1] + yi];
        int bb = _permutation[_permutation[xi + 1] + yi + 1];

        double x1 = Lerp(u, Grad(aa, xf, yf), Grad(ba, xf - 1, yf));
        double x2 = Lerp(u, Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1));

        return Lerp(v, x1, x2);
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static double Lerp(double t, double a, double b) => a + t * (b - a);

    private static double Grad(int hash, double x, double y)
    {
        int h = hash & 7;
        double u = h < 4 ? x : y;
        double v = h < 4 ? y : x;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}
