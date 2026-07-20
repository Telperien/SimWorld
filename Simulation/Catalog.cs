using System.Text.Json;

namespace Simulation;

// Refactor : Catalog<TEntry> generique, factorise la forme identique
// (parse JSON -> dictionnaire de DTO -> tableau byId -> detection de
// doublon -> Dictionary<string, byte> par nom) partagee par
// TerrainCatalog/VegetationCatalog/SpeciesCatalog (et les catalogues a
// venir : castes, traits, batiments, techs, materiaux). Le mapping
// DTO->type reste propre a chaque type appelant, pas factorisable ici.
public sealed class Catalog<TEntry> where TEntry : class
{
    private readonly TEntry?[] _byId;
    private readonly Dictionary<string, byte> _idByName;

    private Catalog(TEntry?[] byId, Dictionary<string, byte> idByName)
    {
        _byId = byId;
        _idByName = idByName;
    }

    public static Catalog<TEntry> Load<TDto>(
        string json,
        string catalogLabel,
        Func<TDto, byte> idOf,
        Func<string, TDto, TEntry> build)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dtos = JsonSerializer.Deserialize<Dictionary<string, TDto>>(json, options)
            ?? throw new ArgumentException($"{catalogLabel} JSON is empty or invalid", nameof(json));

        byte maxId = 0;
        foreach (var entry in dtos)
        {
            byte id = idOf(entry.Value);
            if (id > maxId)
            {
                maxId = id;
            }
        }

        var byId = new TEntry?[maxId + 1];
        var idByName = new Dictionary<string, byte>(dtos.Count);

        foreach (var (name, dto) in dtos)
        {
            byte id = idOf(dto);
            if (byId[id] is not null)
            {
                throw new ArgumentException(
                    $"duplicate {catalogLabel} id {id}: conflicting entry '{name}'",
                    nameof(json));
            }

            byId[id] = build(name, dto);
            idByName[name] = id;
        }

        return new Catalog<TEntry>(byId, idByName);
    }

    public TEntry Get(byte id)
    {
        return _byId[id] ?? throw new ArgumentException($"no {typeof(TEntry).Name} registered for id {id}", nameof(id));
    }

    public bool TryGetId(string name, out byte id) => _idByName.TryGetValue(name, out id);

    public int Count => _byId.Length;
}

internal static class CatalogColor
{
    public static uint ParseColor(string hex)
    {
        var trimmed = hex.StartsWith('#') ? hex[1..] : hex;
        return Convert.ToUInt32(trimmed, 16);
    }
}
