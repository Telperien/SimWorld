using System.Text.Json;

namespace Simulation;

public sealed record SimulationConfig
{
    public required double FireSpreadChance { get; init; }
    public required double AgentDensity { get; init; }
    public required double BushDensity { get; init; }
    public required double TreeDensity { get; init; }
    public required int VegetationTickInterval { get; init; }
    public required double IdleMoveChance { get; init; }
    public required double AgentMoveSpeed { get; init; }
    public required byte HungerIncreasePerThink { get; init; }
    public required byte HungerSeekThreshold { get; init; }
    public required byte HungerDecreasePerEatTick { get; init; }
    public required int MaxFoodSearchRadius { get; init; }
    public required double TerrainFeaturesAcrossMap { get; init; }
    public required double TerrainWaterThreshold { get; init; }
    public required double TerrainSandThreshold { get; init; }
    public required double TerrainGrassThreshold { get; init; }
    public required int HarvestAmountPerTick { get; init; }
    public required int VegetationRegrowthDelayTicks { get; init; }
    public required double AshToGrassChance { get; init; }
    public required byte SeekFailureCooldownThinkTicks { get; init; }
    public required double VegetationSpreadChance { get; init; }
    public required double VegetationSpontaneousChance { get; init; }
    public required double TreeSpreadChance { get; init; }
    public required double TreeSpontaneousChance { get; init; }
    public required byte WanderPersistenceTicks { get; init; }
    public required double WanderTurnChance { get; init; }
    public required int MateSearchRadius { get; init; }
    public required int AgentCapacityMultiplier { get; init; }
    public required double BaseConceptionChance { get; init; }
    public required double TargetFoodPerCapita { get; init; }
    public required double FoodGradientDiffusionRate { get; init; }
    public required int FoodGradientDiffusionIterations { get; init; }

    // Clans (session 18).
    public required int InitialClanCount { get; init; }
    public required double ClanSpawnRadiusFraction { get; init; }
    public required double BaseHarvestChance { get; init; }
    public required double TargetFoodPoolPerCapita { get; init; }

    // Plafond de population utilisé dans le calcul du pool CIBLE (pas
    // un plafond de population réel) : au-delà, la réserve visée
    // n'augmente plus avec la taille du clan -- sans ça, un clan qui
    // grandit indéfiniment peut toujours justifier de recruter plus de
    // cueilleurs pour combler un objectif qui grandit avec lui, sans
    // jamais ressentir de vraie pression de rareté (découvert
    // empiriquement, session 18).
    public required int ReferenceClanPopulation { get; init; }

    // World law (session 19b) : la faim tue-t-elle ? Défaut false -- un
    // pool vide bloque la reproduction (déjà le cas via HungerSeekThreshold
    // dans TryReproduce/TryFindMate), jamais un seuil de faim qui tue.
    // Gardé comme flag explicite pour un usage futur (pouvoir divin
    // "famine", race spécifique) -- ne jamais l'activer silencieusement.
    public required bool AllowStarvationDeath { get; init; }

    // Foyers (session foyers) : probabilité qu'un agent qui tire une
    // NOUVELLE direction d'errance de secours (TryStartMoving, aucune
    // cible connue) la choisisse vers son foyer plutôt qu'uniformément
    // au hasard. Une tendance, pas une contrainte -- cf. CLAUDE.md,
    // section Social.
    public required double HomeAnchorChance { get; init; }

    // Territoire (session territoire) : résolution de la grille de
    // régions, dérivée de la taille de la carte (comme
    // TerrainFeaturesAcrossMap) plutôt qu'une constante en dur --
    // cible CLAUDE.md "32² pour 512²".
    public required double TerritoryRegionsAcrossMap { get; init; }

    // Tick lent (géopolitique, pas physique) -- un ordre de grandeur
    // au-dessus de VegetationTickInterval, cohérent avec la
    // hiérarchie d'échelles de temps CLAUDE.md.
    public required int TerritoryTickInterval { get; init; }

    // Diffusion Jacobi de l'influence par clan, même formule que
    // RebuildFoodGradient (VegetationSystem) -- pas de convergence à
    // l'infini, un nombre fixe d'itérations, re-semé depuis les
    // foyers à chaque tick territoire.
    public required double TerritoryDiffusionRate { get; init; }

    public required int TerritoryDiffusionIterations { get; init; }

    // Magnitude de la source d'influence déposée à chaque foyer =
    // population du clan × ce poids.
    public required double TerritoryPopulationWeight { get; init; }

    // Valeur minimale d'influence pour qu'une région soit revendiquée
    // -- sous ce seuil, la région reste neutre.
    public required double TerritoryClaimThreshold { get; init; }

    public static SimulationConfig Load(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<SimulationConfig>(json, options)
            ?? throw new ArgumentException("simulation JSON is empty or invalid", nameof(json));
    }
}
