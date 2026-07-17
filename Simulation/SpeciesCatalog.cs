using System.Text.Json;

namespace Simulation;

public sealed class SpeciesCatalog
{
    private readonly SpeciesType?[] _byId;
    private readonly Dictionary<string, byte> _idByName;

    private SpeciesCatalog(SpeciesType?[] byId, Dictionary<string, byte> idByName)
    {
        _byId = byId;
        _idByName = idByName;
    }

    public static SpeciesCatalog Load(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dtos = JsonSerializer.Deserialize<Dictionary<string, SpeciesDto>>(json, options)
            ?? throw new ArgumentException("species JSON is empty or invalid", nameof(json));

        byte maxId = 0;
        foreach (var entry in dtos)
        {
            if (entry.Value.Id > maxId)
            {
                maxId = entry.Value.Id;
            }
        }

        var byId = new SpeciesType?[maxId + 1];
        var idByName = new Dictionary<string, byte>(dtos.Count);

        foreach (var (name, dto) in dtos)
        {
            if (byId[dto.Id] is not null)
            {
                throw new ArgumentException(
                    $"duplicate species id {dto.Id}: already used by '{byId[dto.Id]!.Name}', conflicting entry '{name}'",
                    nameof(json));
            }

            var species = new SpeciesType
            {
                Name = name,
                Id = dto.Id,
                LifespanTicks = dto.LifespanTicks,
                LifespanVarianceTicks = dto.LifespanVarianceTicks,
                MaturityAge = dto.MaturityAge,
                GestationTicks = dto.GestationTicks,
            };
            byId[dto.Id] = species;
            idByName[name] = dto.Id;
        }

        return new SpeciesCatalog(byId, idByName);
    }

    public SpeciesType Get(byte id)
    {
        return _byId[id] ?? throw new ArgumentException($"no species registered for id {id}", nameof(id));
    }

    public bool TryGetId(string name, out byte id) => _idByName.TryGetValue(name, out id);

    private sealed record SpeciesDto(byte Id, uint LifespanTicks, uint LifespanVarianceTicks, uint MaturityAge, uint GestationTicks);
}
