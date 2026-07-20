using Simulation;

namespace Tests;

// Session filet : les quatre chargeurs de catalogue étaient dupliqués
// à l'identique dans 7 fichiers de test. Centralisés ici -- les
// helpers SPÉCIALISÉS (LoadFertileConfig, MakeFertileCouple, etc.)
// restent locaux à leur fichier, ce ne sont pas de simples chargeurs.
internal static class TestCatalogs
{
    // Pas dans /Simulation (CLAUDE.md : AUCUN System.IO dans /Simulation)
    // -- ce petit helper de lecture reste local à /Tests, comme le sera
    // son équivalent dans /Tools/SimReport, /Tools/RenderDump et
    // scripts/ (Game), qui n'ont pas d'assembly partagée entre eux.
    private static string ReadOrThrow(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"fichier de configuration introuvable : '{Path.GetFileName(path)}' attendu à '{path}'", path);
        }
        return File.ReadAllText(path);
    }

    public static Catalog<TerrainType> LoadTerrain()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "terrain.json");
        return TerrainCatalog.Load(ReadOrThrow(path));
    }

    public static Catalog<VegetationType> LoadVegetation()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "vegetation.json");
        return VegetationCatalog.Load(ReadOrThrow(path));
    }

    public static Catalog<SpeciesType> LoadSpecies()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "species.json");
        return SpeciesCatalog.Load(ReadOrThrow(path));
    }

    public static SimulationConfig LoadSimulation()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "simulation.json");
        return SimulationConfig.Load(ReadOrThrow(path));
    }
}
