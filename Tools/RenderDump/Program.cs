using System.Drawing;
using System.Drawing.Imaging;
using Simulation;

// Reproduit EXACTEMENT la logique de pixel de scripts/WorldRenderer.cs
// (Redraw()) pour verifier -- sans lancer Godot -- si les seams
// rectilignes signales (session 17b, partie 2) existent deja dans les
// DONNEES de simulation ou apparaissent uniquement dans le pipeline de
// rendu Godot. Seed/size en dur : memes valeurs que WorldRenderer.cs
// (Seed=42, Size=512), pour comparer au meme monde que celui vu au F5.
const int seed = 42;
const int size = 512;

string basePath = AppContext.BaseDirectory;
var terrainCatalog = TerrainCatalog.Load(ReadJsonOrThrow(Path.Combine(basePath, "data", "terrain.json")));
var vegetationCatalog = VegetationCatalog.Load(ReadJsonOrThrow(Path.Combine(basePath, "data", "vegetation.json")));
var speciesCatalog = SpeciesCatalog.Load(ReadJsonOrThrow(Path.Combine(basePath, "data", "species.json")));
var config = SimulationConfig.Load(ReadJsonOrThrow(Path.Combine(basePath, "data", "simulation.json")));

// Session filet : cf. Tools/SimReport/Program.cs, meme raisonnement.
static string ReadJsonOrThrow(string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException(
            $"fichier de configuration introuvable : '{Path.GetFileName(path)}' attendu a '{path}'", path);
    }
    return File.ReadAllText(path);
}

var world = new World(seed, size, terrainCatalog, vegetationCatalog, speciesCatalog, config);

string outputPath = args.Length > 0 ? args[0] : Path.Combine(basePath, "render-dump.png");

using (var bitmap = new Bitmap(size, size, PixelFormat.Format24bppRgb))
{
    // Meme ordre exact que WorldRenderer.Redraw() : terrain d'abord
    // (couleur du catalogue, ou couleur feu si en train de bruler),
    // puis un pixel par entite de vegetation par-dessus.
    for (int y = 0; y < size; y++)
    {
        for (int x = 0; x < size; x++)
        {
            Color color = world.IsBurning(x, y)
                ? Color.FromArgb(255, 102, 0)
                : ColorFromHex(terrainCatalog.Get(world.GetTerrainId(x, y)).Color);
            bitmap.SetPixel(x, y, color);
        }
    }

    for (int i = 0; i < world.VegetationCount; i++)
    {
        Vegetation vegetation = world.GetVegetation(i);
        if (world.IsBurning(vegetation.X, vegetation.Y))
        {
            continue;
        }

        Color color = ColorFromHex(vegetationCatalog.Get(vegetation.Type).Color);
        bitmap.SetPixel(vegetation.X, vegetation.Y, color);
    }

    bitmap.Save(outputPath, ImageFormat.Png);

    // Recadrages agrandis (plus proche voisin, sans lissage) des zones
    // suspectes vues sur le PNG plein format -- a l'echelle 1:1 du PNG
    // complet, un artefact fin (bande de quelques pixels de haut) est
    // difficile a distinguer d'un vrai motif Perlin organique.
    SaveZoomedCrop(bitmap, 200, 0, 280, 180, 4, InsertSuffix(outputPath, "-crop-checker"));
    SaveZoomedCrop(bitmap, 0, 380, size, 60, 4, InsertSuffix(outputPath, "-crop-band"));
}

Console.WriteLine($"PNG ecrit : {outputPath}");

// Complement bon marche (partie 2) : dump RLE des ids de terrain BRUTS
// le long des deux lignes suspectes -- verticale a 3/4 largeur,
// horizontale au milieu. Si les donnees sont propres a ces coordonnees,
// le seam signale au F5 est forcement cote pipeline de rendu Godot, pas
// dans Simulation.
int verticalX = size * 3 / 4;
int horizontalY = size / 2;

Console.WriteLine();
Console.WriteLine($"Scanline verticale x={verticalX} (y de 0 a {size - 1}), ids de terrain en RLE :");
PrintRunLengthEncoded(x => world.GetTerrainId(verticalX, x), size, terrainCatalog);

Console.WriteLine();
Console.WriteLine($"Scanline horizontale y={horizontalY} (x de 0 a {size - 1}), ids de terrain en RLE :");
PrintRunLengthEncoded(x => world.GetTerrainId(x, horizontalY), size, terrainCatalog);

// Verification de l'hypothese "bande d'arbres" (session 17b) : le PNG
// montre une bande horizontale plus sombre vers y=400-430, non explicable
// par le TERRAIN (RLE ci-dessus propre a ces coordonnees) -- teste si
// c'est en fait une bande dense d'ARBRES (plus sombres que les buissons/
// l'herbe), consequence du balayage raster de SeedInitialVegetation qui
// remplit d'abord tous les buissons puis, une fois leur capacite
// atteinte, continue le MEME balayage en ne plantant plus que des
// arbres jusqu'a la capacite arbre -- une bande etroite si la capacite
// arbre (bien plus petite que la capacite buisson) s'epuise vite.
Console.WriteLine();
Console.WriteLine("Densite bush/tree par bande de 10 lignes (y), pour reperer la bande sombre :");
vegetationCatalog.TryGetId("bush", out byte bushTypeId);
int[] bushPerBand = new int[size / 10];
int[] treePerBand = new int[size / 10];
for (int i = 0; i < world.VegetationCount; i++)
{
    Vegetation v = world.GetVegetation(i);
    int band = v.Y / 10;
    if (band >= bushPerBand.Length)
    {
        continue;
    }
    if (v.Type == bushTypeId)
    {
        bushPerBand[band]++;
    }
    else
    {
        treePerBand[band]++;
    }
}
for (int b = 0; b < bushPerBand.Length; b++)
{
    if (treePerBand[b] > bushPerBand[b] || treePerBand[b] > 20)
    {
        Console.WriteLine($"  y=[{b * 10,4},{b * 10 + 9,4}] : bush={bushPerBand[b],4} tree={treePerBand[b],4}  <-- bande a forte densite d'arbres");
    }
}

// Meme verification par colonne (x), pour la ligne verticale signalee.
Console.WriteLine();
Console.WriteLine("Densite bush/tree par bande de 10 colonnes (x), pour reperer une eventuelle ligne verticale :");
int[] bushPerColBand = new int[size / 10];
int[] treePerColBand = new int[size / 10];
for (int i = 0; i < world.VegetationCount; i++)
{
    Vegetation v = world.GetVegetation(i);
    int band = v.X / 10;
    if (band >= bushPerColBand.Length)
    {
        continue;
    }
    if (v.Type == bushTypeId)
    {
        bushPerColBand[band]++;
    }
    else
    {
        treePerColBand[band]++;
    }
}
for (int b = 0; b < bushPerColBand.Length; b++)
{
    if (treePerColBand[b] > bushPerColBand[b] || treePerColBand[b] > 20)
    {
        Console.WriteLine($"  x=[{b * 10,4},{b * 10 + 9,4}] : bush={bushPerColBand[b],4} tree={treePerColBand[b],4}  <-- bande a forte densite d'arbres");
    }
}

static void PrintRunLengthEncoded(Func<int, byte> terrainAt, int length, TerrainCatalog catalog)
{
    int runStart = 0;
    byte runId = terrainAt(0);
    for (int i = 1; i <= length; i++)
    {
        byte current = i < length ? terrainAt(i) : byte.MaxValue;
        if (i == length || current != runId)
        {
            string name = TerrainName(catalog, runId);
            Console.WriteLine($"  [{runStart,4}, {i - 1,4}] : {name}");
            runStart = i;
            if (i < length)
            {
                runId = current;
            }
        }
    }
}

static string TerrainName(TerrainCatalog catalog, byte id)
{
    try
    {
        return catalog.Get(id).Name;
    }
    catch (ArgumentException)
    {
        return $"id={id}";
    }
}

static void SaveZoomedCrop(Bitmap source, int cropX, int cropY, int cropW, int cropH, int scale, string outputPath)
{
    using var zoomed = new Bitmap(cropW * scale, cropH * scale, PixelFormat.Format24bppRgb);
    for (int y = 0; y < cropH; y++)
    {
        for (int x = 0; x < cropW; x++)
        {
            Color color = source.GetPixel(cropX + x, cropY + y);
            for (int dy = 0; dy < scale; dy++)
            {
                for (int dx = 0; dx < scale; dx++)
                {
                    zoomed.SetPixel(x * scale + dx, y * scale + dy, color);
                }
            }
        }
    }

    zoomed.Save(outputPath, ImageFormat.Png);
    Console.WriteLine($"Recadrage ecrit : {outputPath}");
}

static string InsertSuffix(string path, string suffix)
{
    string dir = Path.GetDirectoryName(path) ?? "";
    string name = Path.GetFileNameWithoutExtension(path);
    string ext = Path.GetExtension(path);
    return Path.Combine(dir, $"{name}{suffix}{ext}");
}

static Color ColorFromHex(uint hex)
{
    int r = (int)((hex >> 16) & 0xFF);
    int g = (int)((hex >> 8) & 0xFF);
    int b = (int)(hex & 0xFF);
    return Color.FromArgb(r, g, b);
}
