using System.Text.Json;

namespace Simulation;

public static class BuildingCatalog
{
    public static Catalog<BuildingType> Load(string json)
    {
        return Catalog<BuildingType>.Load<BuildingDto>(
            json,
            "buildings",
            dto => dto.Id,
            (name, dto) => new BuildingType
            {
                Name = name,
                Id = dto.Id,
                Tier = dto.Tier,
                PopThreshold = dto.PopThreshold,
                Sprite = dto.Sprite,
                Cost = new ResourceCost { Wood = dto.Cost.Wood, Stone = dto.Cost.Stone },
                Material = dto.Material,
                Provides = dto.Provides ?? [],
            });
    }

    private sealed record BuildingDto(byte Id, byte Tier, int PopThreshold, string Sprite, CostDto Cost, string Material, string[]? Provides);
    private sealed record CostDto(int Wood, int Stone);
}