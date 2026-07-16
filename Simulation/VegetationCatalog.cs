using System.Text.Json;

namespace Simulation;

public sealed class VegetationCatalog
{
    private readonly VegetationType?[] _byId;
    private readonly Dictionary<string, byte> _idByName;

    private VegetationCatalog(VegetationType?[] byId, Dictionary<string, byte> idByName)
    {
        _byId = byId;
        _idByName = idByName;
    }

    public static VegetationCatalog Load(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dtos = JsonSerializer.Deserialize<Dictionary<string, VegetationDto>>(json, options)
            ?? throw new ArgumentException("vegetation JSON is empty or invalid", nameof(json));

        byte maxId = 0;
        foreach (var entry in dtos)
        {
            if (entry.Value.Id > maxId)
            {
                maxId = entry.Value.Id;
            }
        }

        var byId = new VegetationType?[maxId + 1];
        var idByName = new Dictionary<string, byte>(dtos.Count);

        foreach (var (name, dto) in dtos)
        {
            if (byId[dto.Id] is not null)
            {
                throw new ArgumentException(
                    $"duplicate vegetation id {dto.Id}: already used by '{byId[dto.Id]!.Name}', conflicting entry '{name}'",
                    nameof(json));
            }

            var vegetation = new VegetationType
            {
                Name = name,
                Id = dto.Id,
                Color = ParseColor(dto.Color),
                MatureStage = dto.MatureStage,
                Flammable = dto.Flammable,
                FoodValue = dto.FoodValue,
                LifespanTicks = dto.LifespanTicks,
                LifespanVarianceTicks = dto.LifespanVarianceTicks,
            };
            byId[dto.Id] = vegetation;
            idByName[name] = dto.Id;
        }

        return new VegetationCatalog(byId, idByName);
    }

    public VegetationType Get(byte id)
    {
        return _byId[id] ?? throw new ArgumentException($"no vegetation registered for id {id}", nameof(id));
    }

    public bool TryGetId(string name, out byte id) => _idByName.TryGetValue(name, out id);

    private static uint ParseColor(string hex)
    {
        var trimmed = hex.StartsWith('#') ? hex[1..] : hex;
        return Convert.ToUInt32(trimmed, 16);
    }

    private sealed record VegetationDto(byte Id, string Color, int MatureStage, bool Flammable, int FoodValue, int LifespanTicks, int LifespanVarianceTicks);
}
