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
    public void For_Water_TwoCylinderJetLayers()
    {
        ShotDescriptor[] shots = Shots("water");

        // A laser-like jet: a bright core inside a wider translucent sheath.
        Assert.Equal(2, shots.Length);
        foreach (ShotDescriptor shot in shots)
        {
            Assert.Equal(ShotShape.Cylinder, shot.Shape);
            Assert.Equal(new Color(0.25f, 0.5f, 1f), shot.Color);
            Assert.Equal(Vector3.Zero, shot.Boost); // dead ahead: no fan
            Assert.True(shot.Length > 1f, $"jet should be long: length={shot.Length}");
            Assert.InRange(shot.Alpha, 0.05f, 1f);
            Assert.True(shot.Speed > 10f, $"jet should be fast/laser-like: speed={shot.Speed}");
        }
        // One opaque-ish core and one more transparent sheath, both using the jet shader.
        Assert.Contains(shots, s => s.Alpha >= 0.9f);
        Assert.Contains(shots, s => s.Alpha < 0.5f);
        Assert.Contains(shots, s => s.EmissionStrength > 1f); // bright core
        Assert.All(shots, s => Assert.Equal(WandShotFactory.WaterShaderPath, s.ShaderPath));
    }

    [Fact]
    public void For_NonWaterElements_DoNotUseTheWaterShader()
    {
        Assert.All(WandShotFactory.For("fire"), s => Assert.Equal("", s.ShaderPath));
        Assert.All(WandShotFactory.For("density"), s => Assert.Equal("", s.ShaderPath));
    }

    [Fact]
    public void FiresContinuously_OnlyWater()
    {
        Assert.True(WandShotFactory.FiresContinuously("water"));
        Assert.False(WandShotFactory.FiresContinuously("fire"));
        Assert.False(WandShotFactory.FiresContinuously("density"));
        Assert.False(WandShotFactory.FiresContinuously(""));
        Assert.False(WandShotFactory.FiresContinuously("unknown"));
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
                if (shot.Shape == ShotShape.Cylinder)
                {
                    Assert.True(shot.Length > 0f, $"{element}: cylinder length={shot.Length}");
                }
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
