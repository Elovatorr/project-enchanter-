using Godot;
using ProjectEnchanter;
using Xunit;

namespace ProjectEnchanter.Tests;

/// <summary>
/// Unit tests for WandShotFactory — per-element shot composition, colours,
/// shapes, and rejection of unknown elements.
/// </summary>
public class WandShotFactoryTests
{
    private static ShotDescriptor[] Shots(string element) =>
        (ShotDescriptor[])WandShotFactory.For(element);

    // ---- Per element ----

    [Fact]
    public void For_Fire_SingleRedFireball()
    {
        ShotDescriptor[] shots = Shots("fire");

        ShotDescriptor shot = Assert.Single(shots);
        Assert.Equal(ShotShape.Sphere, shot.Shape);
        Assert.Equal(new Color(1f, 0.25f, 0.15f), shot.Color);
        Assert.True(shot.Size > 0.1f, $"fireball should be bulky: size={shot.Size}");
        Assert.Equal(Vector3.Zero, shot.Boost); // straight ahead
    }

    [Fact]
    public void For_Water_ThreeBlueJetsArcUpward()
    {
        ShotDescriptor[] shots = Shots("water");

        Assert.Equal(3, shots.Length);
        foreach (ShotDescriptor shot in shots)
        {
            Assert.Equal(ShotShape.Sphere, shot.Shape);
            Assert.Equal(new Color(0.25f, 0.5f, 1f), shot.Color);
            Assert.True(shot.Boost.Y > 0f, $"jets should arc upward: boost={shot.Boost}");
        }
        // Fan left/centre/right: lateral boosts must span both sides.
        Assert.Contains(shots, s => s.Boost.X < 0f);
        Assert.Contains(shots, s => s.Boost.X > 0f);
    }

    [Fact]
    public void For_Density_ThreeGreenCones()
    {
        ShotDescriptor[] shots = Shots("density");

        Assert.Equal(3, shots.Length);
        foreach (ShotDescriptor shot in shots)
        {
            Assert.Equal(ShotShape.Cone, shot.Shape);
            Assert.Equal(new Color(0.25f, 0.85f, 0.35f), shot.Color);
        }
    }

    [Fact]
    public void For_UnknownElement_NoProjectiles()
    {
        Assert.Empty(WandShotFactory.For("unknown"));
        Assert.Empty(WandShotFactory.For(""));
        Assert.Empty(WandShotFactory.For("fire2"));
    }

    // ---- Shot parameters are usable ----

    [Fact]
    public void For_EveryElement_ShotsHavePositiveSpeedSizeLifetime()
    {
        foreach (string element in new[] { "fire", "water", "density" })
        {
            foreach (ShotDescriptor shot in WandShotFactory.For(element))
            {
                Assert.True(shot.Speed > 0f, $"{element}: speed={shot.Speed}");
                Assert.True(shot.Size > 0f, $"{element}: size={shot.Size}");
                Assert.True(shot.Lifetime > 0f, $"{element}: lifetime={shot.Lifetime}");
            }
        }
    }

    [Fact]
    public void For_EveryElement_ShotsFanInCameraSpaceForward()
    {
        // -Z is forward; no shot may push the direction backwards.
        foreach (string element in new[] { "fire", "water", "density" })
        {
            foreach (ShotDescriptor shot in WandShotFactory.For(element))
            {
                Vector3 direction = (shot.Boost + new Vector3(0, 0, -1)).Normalized();
                Assert.True(direction.Z < 0f, $"{element}: shot must travel forward: {direction}");
            }
        }
    }

    // ---- Colour helper shared with the wand ----

    [Fact]
    public void ColorFor_KnownElements_ReturnsElementColour()
    {
        Assert.Equal(new Color(1f, 0.25f, 0.15f), WandShotFactory.ColorFor("fire"));
        Assert.Equal(new Color(0.25f, 0.5f, 1f), WandShotFactory.ColorFor("water"));
        Assert.Equal(new Color(0.25f, 0.85f, 0.35f), WandShotFactory.ColorFor("density"));
    }

    // ---- Regression: unknown elements must not fire anything ----

    [Fact]
    public void For_EnchantedButUnknownElement_RegressionNoProjectiles()
    {
        // A wand enchanted with an unrecognised element used to spawn gold shots;
        // shooting must be a no-op instead.
        Assert.Empty(WandShotFactory.For("mystery"));
    }
}
