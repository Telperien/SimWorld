namespace Simulation;

// Bâtiment statique, rattaché à un foyer, uniquement dans le territoire
// de son clan. Id stable monotone, référence par Id jamais par index.
// Type et Tier évoluent ensemble (upgrade au même moment).
public struct Building
{
    public uint Id;
    public uint HomeId;
    public uint ClanId;
    public int X;
    public int Y;
    public byte Type;  // BuildingType.Id
    public byte Tier;

    public static readonly Building None = default;
}