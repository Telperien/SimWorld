using System;
using Godot;
using Simulation;

// Un buisson/arbre en pixels multiples (4x4 à 14x14) peint dans l'image
// plein-écran à CHAQUE tick pour des dizaines de milliers d'entités
// serait des millions de SetPixel par frame -- même stratégie que
// AgentRenderer (rendu GPU-instancié) plutôt que du CPU pixel-par-pixel
// (session 17b). Un MultiMeshInstance2D par "bucket" (texture générée
// une seule fois au démarrage) : jeune/mûr pour le buisson, trois paliers
// de croissance discrétisés pour l'arbre (Stage/MatureStage continu,
// mais un MultiMesh partage UNE texture entre toutes ses instances).
public partial class VegetationRenderer : Node2D
{
    private const ulong SpriteSeed = 1234;

    private World _world = null!;
    private VegetationCatalog _vegetationCatalog = null!;
    private byte _bushTypeId;
    private byte _treeTypeId;
    private int _bushMatureStage;
    private int _treeMatureStage;

    private MultiMeshInstance2D _bushYoung = null!;
    private MultiMeshInstance2D _bushMature = null!;
    private MultiMeshInstance2D _treeSmall = null!;
    private MultiMeshInstance2D _treeMedium = null!;
    private MultiMeshInstance2D _treeLarge = null!;

    public override void _Ready()
    {
        var worldRenderer = GetNode<WorldRenderer>("../WorldSprite");
        _world = worldRenderer.World;

        string vegetationJson = FileAccess.GetFileAsString("res://data/vegetation.json");
        _vegetationCatalog = VegetationCatalog.Load(vegetationJson);
        _vegetationCatalog.TryGetId("bush", out _bushTypeId);
        _vegetationCatalog.TryGetId("tree", out _treeTypeId);
        _bushMatureStage = _vegetationCatalog.Get(_bushTypeId).MatureStage;
        _treeMatureStage = _vegetationCatalog.Get(_treeTypeId).MatureStage;

        VegetationType bush = _vegetationCatalog.Get(_bushTypeId);
        VegetationType tree = _vegetationCatalog.Get(_treeTypeId);

        _bushYoung = BuildBucket(SpriteGenerator.GenerateBushSprite(SpriteSeed, mature: false, bush.Color, bush.MatureColor));
        _bushMature = BuildBucket(SpriteGenerator.GenerateBushSprite(SpriteSeed, mature: true, bush.Color, bush.MatureColor));
        _treeSmall = BuildBucket(SpriteGenerator.GenerateTreeSprite(SpriteSeed, 0.15, tree.Color));
        _treeMedium = BuildBucket(SpriteGenerator.GenerateTreeSprite(SpriteSeed, 0.5, tree.Color));
        _treeLarge = BuildBucket(SpriteGenerator.GenerateTreeSprite(SpriteSeed, 0.9, tree.Color));
    }

    public override void _Process(double delta)
    {
        int bushYoungCount = 0, bushMatureCount = 0, treeSmallCount = 0, treeMediumCount = 0, treeLargeCount = 0;

        for (int i = 0; i < _world.VegetationCount; i++)
        {
            Vegetation vegetation = _world.GetVegetation(i);
            if (_world.IsBurning(vegetation.X, vegetation.Y))
            {
                continue;
            }

            var position = new Vector2(vegetation.X + 0.5f, vegetation.Y + 0.5f);

            if (vegetation.Type == _bushTypeId)
            {
                bool mature = vegetation.Stage >= _bushMatureStage;
                SetInstance(mature ? _bushMature : _bushYoung, mature ? bushMatureCount++ : bushYoungCount++, position);
            }
            else if (vegetation.Type == _treeTypeId)
            {
                double growthRatio = _treeMatureStage > 0 ? (double)vegetation.Stage / _treeMatureStage : 1.0;
                if (growthRatio < 0.34)
                {
                    SetInstance(_treeSmall, treeSmallCount++, position);
                }
                else if (growthRatio < 0.67)
                {
                    SetInstance(_treeMedium, treeMediumCount++, position);
                }
                else
                {
                    SetInstance(_treeLarge, treeLargeCount++, position);
                }
            }
        }

        _bushYoung.Multimesh.VisibleInstanceCount = bushYoungCount;
        _bushMature.Multimesh.VisibleInstanceCount = bushMatureCount;
        _treeSmall.Multimesh.VisibleInstanceCount = treeSmallCount;
        _treeMedium.Multimesh.VisibleInstanceCount = treeMediumCount;
        _treeLarge.Multimesh.VisibleInstanceCount = treeLargeCount;
    }

    private static void SetInstance(MultiMeshInstance2D bucket, int index, Vector2 position)
    {
        if (index >= bucket.Multimesh.InstanceCount)
        {
            return;
        }

        bucket.Multimesh.SetInstanceTransform2D(index, new Transform2D(0, position));
    }

    private MultiMeshInstance2D BuildBucket(SpriteBitmap sprite)
    {
        var node = new MultiMeshInstance2D();
        var mesh = new QuadMesh { Size = new Vector2(sprite.Width, sprite.Height) };
        node.Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            Mesh = mesh,
            // Capacité prudente : le pire cas (toute la végétation d'un
            // type dans ce seul bucket) reste petit face à
            // AgentCapacityMultiplier -- coût négligeable.
            InstanceCount = Math.Max(_world.BushCount, _world.TreeCount) + 1024,
        };
        node.Texture = BuildTexture(sprite);
        AddChild(node);
        return node;
    }

    private static ImageTexture BuildTexture(SpriteBitmap sprite)
    {
        var image = Image.CreateEmpty(sprite.Width, sprite.Height, false, Image.Format.Rgba8);
        for (int y = 0; y < sprite.Height; y++)
        {
            for (int x = 0; x < sprite.Width; x++)
            {
                int offset = (y * sprite.Width + x) * 4;
                var color = new Color(
                    sprite.Rgba[offset] / 255f,
                    sprite.Rgba[offset + 1] / 255f,
                    sprite.Rgba[offset + 2] / 255f,
                    sprite.Rgba[offset + 3] / 255f);
                image.SetPixel(x, y, color);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
