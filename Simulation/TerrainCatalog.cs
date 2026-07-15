using System.Text.Json;

namespace Simulation;

public sealed class TerrainCatalog
{
    private readonly TerrainType?[] _byId;
    private readonly Dictionary<string, byte> _idByName;

    private TerrainCatalog(TerrainType?[] byId, Dictionary<string, byte> idByName)
    {
        _byId = byId;
        _idByName = idByName;
    }

    public static TerrainCatalog Load(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dtos = JsonSerializer.Deserialize<Dictionary<string, TerrainDto>>(json, options)
            ?? throw new ArgumentException("terrain JSON is empty or invalid", nameof(json));

        byte maxId = 0;
        foreach (var entry in dtos)
        {
            if (entry.Value.Id > maxId)
            {
                maxId = entry.Value.Id;
            }
        }

        var byId = new TerrainType?[maxId + 1];
        var idByName = new Dictionary<string, byte>(dtos.Count);

        foreach (var (name, dto) in dtos)
        {
            var terrain = new TerrainType
            {
                Name = name,
                Id = dto.Id,
                Color = ParseColor(dto.Color),
                Walkable = dto.Walkable,
                Flammable = dto.Flammable,
            };
            byId[dto.Id] = terrain;
            idByName[name] = dto.Id;
        }

        return new TerrainCatalog(byId, idByName);
    }

    public TerrainType Get(byte id)
    {
        return _byId[id] ?? throw new ArgumentException($"no terrain registered for id {id}", nameof(id));
    }

    public bool TryGetId(string name, out byte id) => _idByName.TryGetValue(name, out id);

    private static uint ParseColor(string hex)
    {
        var trimmed = hex.StartsWith('#') ? hex[1..] : hex;
        return Convert.ToUInt32(trimmed, 16);
    }

    private sealed record TerrainDto(byte Id, string Color, bool Walkable, bool Flammable);
}
