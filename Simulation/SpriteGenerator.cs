namespace Simulation;

// Génération procédurale de sprites (session 17b) : pixels calculés par
// formule + bruit RNG seedé, jamais de trigonométrie (MathF.Sin/Cos
// interdits en /Simulation, cf. CLAUDE.md) -- les cercles utilisent un
// test de distance (Sqrt autorisé) plutôt qu'un balayage d'angle.
// Pure C#, aucun using Godot : /Game convertit le SpriteBitmap retourné
// en Image/ImageTexture. Chaque appel crée son propre Rng local, seedé
// par l'appelant via DeriveTileSeed/DeriveAgentSeed -- ne touche JAMAIS
// aux flux RNG de World (_rngAgents/_rngVegetation/etc.), le rendu
// tourne hors de Tick() et ne doit pas décaler le déterminisme de la
// simulation.
public static class SpriteGenerator
{
    private const uint TrunkColor = 0x5A3D24;
    private const uint BerryColor = 0xC23B3B;

    // Dérive un seed stable à partir d'une position de tuile (végétation :
    // stable tant que la plante existe au même endroit) sans consommer
    // aucun flux RNG de la simulation.
    public static ulong DeriveTileSeed(int worldSeed, int x, int y)
    {
        ulong h = (ulong)(uint)worldSeed * 0x9E3779B97F4A7C15UL;
        h ^= (ulong)(uint)x * 0xC2B2AE3D27D4EB4FUL;
        h ^= (ulong)(uint)y * 0x165667B19E3779F9UL;
        h ^= h >> 33;
        return h;
    }

    // Dérive un seed stable à partir de l'Id stable d'un agent (survit à
    // la compaction du tableau, cf. CLAUDE.md).
    public static ulong DeriveAgentSeed(int worldSeed, uint agentId)
    {
        ulong h = (ulong)(uint)worldSeed * 0x9E3779B97F4A7C15UL;
        h ^= (ulong)agentId * 0xC2B2AE3D27D4EB4FUL;
        h ^= h >> 33;
        return h;
    }

    // Silhouette humanoïde 6x8. `facing` dérive le buffer miroir depuis
    // le buffer canonique (0=droite) plutôt que de régénérer
    // indépendamment -- garantit la relation miroir par construction.
    public static SpriteBitmap GenerateAgentSprite(ulong seed, byte facing, uint hueColor)
    {
        var canonical = GenerateAgentSpriteCanonical(seed, hueColor);
        return facing == 0 ? canonical : canonical.MirroredHorizontally();
    }

    private static SpriteBitmap GenerateAgentSpriteCanonical(ulong seed, uint hueColor)
    {
        var rng = new Rng(seed);
        var bmp = new SpriteBitmap(6, 8);
        uint headColor = Darken(hueColor, 0.7);

        DrawRect(bmp, 2, 0, 2, 2, headColor);
        DrawRect(bmp, 1, 2, 4, 4, hueColor);
        DrawRect(bmp, 1, 6, 2, 2, hueColor);
        DrawRect(bmp, 3, 6, 2, 2, hueColor);

        // Un bras asymétrique (côté tiré par RNG) casse la symétrie
        // gauche/droite -- sans ça, le miroir de Facing n'aurait aucun
        // effet visuel sur une silhouette parfaitement symétrique.
        bool armOnRight = rng.NextDouble() < 0.5;
        int armX = armOnRight ? 5 : 0;
        bmp.SetPixel(armX, 3, hueColor);

        return bmp;
    }

    // Disque irrégulier 4x4 à 6x6. `mature` change la FORME (plus
    // grand) ET la couleur (distincte), plus 1-2 pixels d'accent --
    // doit se voir "d'un coup d'oeil", pas seulement par la teinte.
    public static SpriteBitmap GenerateBushSprite(ulong seed, bool mature, uint youngColor, uint matureColor)
    {
        var rng = new Rng(seed);
        int diameter = mature ? 6 : 4;
        var bmp = new SpriteBitmap(diameter, diameter);
        int radius = diameter / 2;
        uint color = mature ? matureColor : youngColor;

        DrawDiscNoisy(bmp, radius, radius, radius, color, rng);

        if (mature)
        {
            int berries = 1 + (rng.NextDouble() < 0.5 ? 1 : 0);
            for (int i = 0; i < berries; i++)
            {
                int bx = (int)(rng.NextDouble() * diameter);
                int by = (int)(rng.NextDouble() * diameter);
                if (!bmp.IsTransparentAt(bx, by))
                {
                    bmp.SetPixel(bx, by, BerryColor);
                }
            }
        }

        return bmp;
    }

    // Tronc + couronne (1-3 disques superposés), taille croissante avec
    // growthRatio (Stage/MatureStage, déjà public sur Vegetation) --
    // grandit progressivement plutôt qu'un pop binaire jeune/mûr.
    public static SpriteBitmap GenerateTreeSprite(ulong seed, double growthRatio, uint crownColor)
    {
        var rng = new Rng(seed);
        double clamped = Math.Clamp(growthRatio, 0.0, 1.0);
        int size = 8 + (int)(6 * clamped); // 8..14

        var bmp = new SpriteBitmap(size, size);

        int trunkWidth = 1 + (rng.NextDouble() < 0.4 ? 1 : 0);
        int trunkHeight = Math.Max(2, size / 3);
        int trunkX = (size - trunkWidth) / 2;
        int trunkY = size - trunkHeight;
        DrawRect(bmp, trunkX, trunkY, trunkWidth, trunkHeight, TrunkColor);

        int crownLayers = 1 + (int)(rng.NextDouble() * 3); // 1..3
        int crownCenterY = trunkY - 1;
        for (int i = 0; i < crownLayers; i++)
        {
            int layerRadius = Math.Max(2, size / 2 - i);
            int jitterX = (int)(rng.NextDouble() * 3) - 1;
            int jitterY = (int)(rng.NextDouble() * 3) - 1;
            DrawDiscNoisy(bmp, size / 2 + jitterX, crownCenterY - i * 2 + jitterY, layerRadius, crownColor, rng);
        }

        return bmp;
    }

    private static void DrawRect(SpriteBitmap bmp, int x0, int y0, int w, int h, uint color)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bmp.SetPixel(x0 + x, y0 + y, color);
            }
        }
    }

    // Cercle rempli (test de distance, Sqrt autorisé) avec une bande de
    // bord "bruitée" -- un pixel proche du rayon a une chance (tirée par
    // rng, ordre de balayage fixe donc déterministe) d'être inclus/exclu
    // à l'inverse de la règle stricte, ce qui casse la symétrie parfaite
    // sans aucune trigonométrie.
    private static void DrawDiscNoisy(SpriteBitmap bmp, int cx, int cy, int radius, uint color, Rng rng)
    {
        int r = radius + 1;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                double dist = Math.Sqrt((double)(dx * dx + dy * dy));
                bool baseInside = dist <= radius;
                bool nearEdge = Math.Abs(dist - radius) <= 1.0;
                bool inside = baseInside;

                if (nearEdge)
                {
                    double roll = rng.NextDouble();
                    inside = baseInside ? roll < 0.7 : roll < 0.3;
                }

                if (inside)
                {
                    bmp.SetPixel(cx + dx, cy + dy, color);
                }
            }
        }
    }

    private static uint Darken(uint color, double factor)
    {
        int r = (int)(((color >> 16) & 0xFF) * factor);
        int g = (int)(((color >> 8) & 0xFF) * factor);
        int b = (int)((color & 0xFF) * factor);
        return ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }
}
