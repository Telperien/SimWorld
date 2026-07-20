namespace Simulation;

public static class TerrainCatalog
{
    public static Catalog<TerrainType> Load(string json)
    {
        return Catalog<TerrainType>.Load<TerrainDto>(
            json,
            "terrain",
            dto => dto.Id,
            (name, dto) => new TerrainType
            {
                Name = name,
                Id = dto.Id,
                Color = CatalogColor.ParseColor(dto.Color),
                Walkable = dto.Walkable,
                Flammable = dto.Flammable,
            });
    }

    private sealed record TerrainDto(byte Id, string Color, bool Walkable, bool Flammable);
}
