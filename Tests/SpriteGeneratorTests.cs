using Simulation;

namespace Tests;

public class SpriteGeneratorTests
{
    [Fact]
    public void SpriteGenerator_IsDeterministic_ForSameSeedAndParams()
    {
        ulong seed = SpriteGenerator.DeriveTileSeed(42, 10, 20);

        var bushA = SpriteGenerator.GenerateBushSprite(seed, mature: true, 0x3f7d32, 0x2f6b1f);
        var bushB = SpriteGenerator.GenerateBushSprite(seed, mature: true, 0x3f7d32, 0x2f6b1f);
        Assert.True(bushA.Equals(bushB));

        var treeA = SpriteGenerator.GenerateTreeSprite(seed, 0.8, 0x2d5a27);
        var treeB = SpriteGenerator.GenerateTreeSprite(seed, 0.8, 0x2d5a27);
        Assert.True(treeA.Equals(treeB));

        var agentA = SpriteGenerator.GenerateAgentSprite(seed, 0, 0xc47a4f);
        var agentB = SpriteGenerator.GenerateAgentSprite(seed, 0, 0xc47a4f);
        Assert.True(agentA.Equals(agentB));
    }

    [Fact]
    public void AgentSprite_RespondsToFacing()
    {
        ulong seed = SpriteGenerator.DeriveAgentSeed(7, 123);
        var facingRight = SpriteGenerator.GenerateAgentSprite(seed, 0, 0xc47a4f);
        var facingLeft = SpriteGenerator.GenerateAgentSprite(seed, 1, 0xc47a4f);

        Assert.True(facingLeft.Equals(facingRight.MirroredHorizontally()));

        // Les deux orientations doivent réellement différer -- sinon le
        // bras asymétrique (cf. GenerateAgentSpriteCanonical) n'a pas
        // fait son travail et Facing serait un no-op visuel.
        Assert.False(facingLeft.Equals(facingRight));
    }

    [Fact]
    public void TreeSprites_VaryInAppearance_AcrossSeeds()
    {
        var reference = SpriteGenerator.GenerateTreeSprite(SpriteGenerator.DeriveTileSeed(1, 0, 0), 1.0, 0x2d5a27);

        bool foundDifferent = false;
        for (int i = 1; i <= 10; i++)
        {
            var other = SpriteGenerator.GenerateTreeSprite(SpriteGenerator.DeriveTileSeed(1, i, i * 3), 1.0, 0x2d5a27);
            if (!other.Equals(reference))
            {
                foundDifferent = true;
                break;
            }
        }

        Assert.True(foundDifferent, "tous les arbres generes sont identiques malgre des seeds differents");
    }
}
