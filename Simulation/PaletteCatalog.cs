using System.Text.Json;

namespace Simulation;

// Catalogue de teintes pour les sprites d'agents (session 17b),
// préparation multi-race/clan sans construire le système lui-même --
// même patron de chargement que TerrainCatalog/VegetationCatalog.
public sealed class PaletteCatalog
{
    private readonly PaletteEntry?[] _byId;
    private readonly Dictionary<string, byte> _idByName;

    private PaletteCatalog(PaletteEntry?[] byId, Dictionary<string, byte> idByName)
    {
        _byId = byId;
        _idByName = idByName;
    }

    public static PaletteCatalog Load(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dtos = JsonSerializer.Deserialize<Dictionary<string, PaletteDto>>(json, options)
            ?? throw new ArgumentException("palette JSON is empty or invalid", nameof(json));

        byte maxId = 0;
        foreach (var entry in dtos)
        {
            if (entry.Value.Id > maxId)
            {
                maxId = entry.Value.Id;
            }
        }

        var byId = new PaletteEntry?[maxId + 1];
        var idByName = new Dictionary<string, byte>(dtos.Count);

        foreach (var (name, dto) in dtos)
        {
            if (byId[dto.Id] is not null)
            {
                throw new ArgumentException(
                    $"duplicate palette id {dto.Id}: already used by '{byId[dto.Id]!.Name}', conflicting entry '{name}'",
                    nameof(json));
            }

            var entry = new PaletteEntry
            {
                Name = name,
                Id = dto.Id,
                Color = ParseColor(dto.Color),
            };
            byId[dto.Id] = entry;
            idByName[name] = dto.Id;
        }

        return new PaletteCatalog(byId, idByName);
    }

    public PaletteEntry Get(byte id)
    {
        return _byId[id] ?? throw new ArgumentException($"no palette entry registered for id {id}", nameof(id));
    }

    public bool TryGetId(string name, out byte id) => _idByName.TryGetValue(name, out id);

    public int Count => _byId.Length;

    private static uint ParseColor(string hex)
    {
        var trimmed = hex.StartsWith('#') ? hex[1..] : hex;
        return Convert.ToUInt32(trimmed, 16);
    }

    private sealed record PaletteDto(byte Id, string Color);
}
