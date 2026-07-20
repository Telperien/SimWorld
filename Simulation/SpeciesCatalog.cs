namespace Simulation;

public static class SpeciesCatalog
{
    public static Catalog<SpeciesType> Load(string json)
    {
        return Catalog<SpeciesType>.Load<SpeciesDto>(
            json,
            "species",
            dto => dto.Id,
            (name, dto) => new SpeciesType
            {
                Name = name,
                Id = dto.Id,
                LifespanTicks = dto.LifespanTicks,
                LifespanVarianceTicks = dto.LifespanVarianceTicks,
                MaturityAge = dto.MaturityAge,
                GestationTicks = dto.GestationTicks,
            });
    }

    private sealed record SpeciesDto(byte Id, uint LifespanTicks, uint LifespanVarianceTicks, uint MaturityAge, uint GestationTicks);
}
