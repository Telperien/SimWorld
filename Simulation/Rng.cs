namespace Simulation;

// Classe (pas struct) : un struct mutable copié par erreur au lieu d'un
// passage par ref désynchronise silencieusement deux RNG censés être
// identiques, ce qui casserait le déterminisme sans erreur visible.
public sealed class Rng
{
    // L'état 0 est un point fixe du xorshift (reste 0 pour toujours) :
    // un seed de 0 est remplacé par une constante non nulle.
    private ulong _state;

    public Rng(ulong seed)
    {
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    // Lecture seule : sert à couvrir l'état du flux dans World.Hash().
    public ulong State => _state;

    public ulong NextUInt64()
    {
        ulong x = _state;
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        _state = x;
        return x;
    }

    public double NextDouble()
    {
        return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    }
}
