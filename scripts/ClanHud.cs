using Godot;
using Simulation;

// Affichage simple (session 18, pas de polish) : le stock de nourriture
// de chaque clan, en direct -- seule fenêtre du joueur sur la santé
// d'un clan tant que le territoire/l'UI détaillée n'existent pas.
public partial class ClanHud : Label
{
    private World _world = null!;
    private Catalog<SpeciesType> _speciesCatalog = null!;

    public override void _Ready()
    {
        // ClanHud est un enfant de Hud (CanvasLayer), lui-même sibling
        // de WorldSprite sous Main -- un niveau plus profond que
        // AgentRenderer/VegetationRenderer, d'où le chemin plus long.
        var worldRenderer = GetNode<WorldRenderer>("../../WorldSprite");
        _world = worldRenderer.World;

        string speciesJson = FileAccess.GetFileAsString("res://data/species.json");
        _speciesCatalog = SpeciesCatalog.Load(speciesJson);

        Position = new Vector2(8, 8);
        AddThemeColorOverride("font_color", Colors.White);
        AddThemeColorOverride("font_outline_color", Colors.Black);
        AddThemeConstantOverride("outline_size", 3);
    }

    public override void _Process(double delta)
    {
        var lines = new System.Text.StringBuilder();
        for (int i = 0; i < _world.ClanCount; i++)
        {
            Clan clan = _world.GetClan(i);
            string speciesName = _speciesCatalog.Get(clan.Species).Name;
            lines.AppendLine($"Clan {i} ({speciesName}) : {clan.FoodPool}");
        }

        Text = lines.ToString();
    }
}
