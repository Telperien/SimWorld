namespace Simulation;

public static class VegetationCatalog
{
    public static Catalog<VegetationType> Load(string json)
    {
        return Catalog<VegetationType>.Load<VegetationDto>(
            json,
            "vegetation",
            dto => dto.Id,
            (name, dto) => new VegetationType
            {
                Name = name,
                Id = dto.Id,
                Color = CatalogColor.ParseColor(dto.Color),
                MatureColor = dto.MatureColor is not null ? CatalogColor.ParseColor(dto.MatureColor) : CatalogColor.ParseColor(dto.Color),
                MatureStage = dto.MatureStage,
                Flammable = dto.Flammable,
                FoodValue = dto.FoodValue,
                LifespanTicks = dto.LifespanTicks,
                LifespanVarianceTicks = dto.LifespanVarianceTicks,
            });
    }

    private sealed record VegetationDto(byte Id, string Color, string? MatureColor, int MatureStage, bool Flammable, int FoodValue, int LifespanTicks, int LifespanVarianceTicks);
}
