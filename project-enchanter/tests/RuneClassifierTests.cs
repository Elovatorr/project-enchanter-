using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ProjectEnchanter;
using Xunit;

namespace ProjectEnchanter.Tests;

/// <summary>
/// Tests for the trained rune classifier: the caret fire rune must classify
/// correctly at any position/scale/rotation, and non-runes must not cast.
/// </summary>
public class RuneClassifierTests
{
    private static RuneClassifier NewClassifier() => new();

    private static List<List<Vector2>> Transform(
        IReadOnlyList<IReadOnlyList<Vector2>> strokes,
        float rotationRadians = 0f,
        float scale = 1f,
        Vector2? offset = null)
    {
        Vector2 o = offset ?? Vector2.Zero;
        return strokes.Select(s => s.Select(p => p.Rotated(rotationRadians) * scale + o).ToList()).ToList();
    }

    private static List<List<Vector2>> Jitter(List<List<Vector2>> strokes, float amplitude, int seed)
    {
        var rnd = new Random(seed);
        return strokes.Select(s => s.Select(p =>
            p + new Vector2(
                (float)(rnd.NextDouble() - 0.5) * amplitude,
                (float)(rnd.NextDouble() - 0.5) * amplitude)).ToList()).ToList();
    }

    private static List<List<Vector2>> Circle()
    {
        List<Vector2> top = [], bottom = [];
        for (int i = 0; i <= 12; i++)
        {
            float a = MathF.PI + i * MathF.PI / 12f;
            top.Add(new Vector2(MathF.Cos(a), MathF.Sin(a)) * 120f + new Vector2(400, 300));
            a = i * MathF.PI / 12f;
            bottom.Add(new Vector2(MathF.Cos(a), MathF.Sin(a)) * 120f + new Vector2(400, 300));
        }
        return [top, bottom];
    }

    private static List<List<Vector2>> Wave()
    {
        List<Vector2> wave = [];
        for (int i = 0; i <= 24; i++)
        {
            wave.Add(new Vector2(100f + i * 20f, 300f + MathF.Sin(i * MathF.Tau / 12f) * 40f));
        }
        return [wave];
    }

    // ---- Recognition ----

    [Fact]
    public void Recognize_TemplateStrokes_CastsFire()
    {
        RuneMatch? match = NewClassifier().Recognize(RuneTemplates.Fire.Strokes);

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
        Assert.InRange(match.Confidence, RuneClassifier.MatchThreshold, 1f);
    }

    [Fact]
    public void Recognize_EmptyStrokes_ReturnsNull()
    {
        Assert.Null(NewClassifier().Recognize([]));
    }

    [Fact]
    public void Recognize_DegenerateStrokes_ReturnsNull()
    {
        // A single click (one point) has no shape — the model must not guess.
        Assert.Null(NewClassifier().Recognize([new List<Vector2> { new(500, 500) }]));
        Assert.Null(NewClassifier().Recognize([new List<Vector2> { new(1, 1), new(1, 1) }]));
    }

    // ---- Invariance ----

    [Fact]
    public void Recognize_TranslatedAndScaled_CastsFire()
    {
        var strokes = Transform(RuneTemplates.Fire.Strokes, scale: 260f, offset: new Vector2(600, 350));

        RuneMatch? match = NewClassifier().Recognize(strokes);

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    [Theory]
    [InlineData(20f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(300f)]
    public void Recognize_Rotated_CastsFire(float degrees)
    {
        var strokes = Transform(RuneTemplates.Fire.Strokes, degrees * MathF.PI / 180f, 180f, new Vector2(400, 300));

        RuneMatch? match = NewClassifier().Recognize(strokes);

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    [Fact]
    public void Recognize_JitteredHandDrawn_CastsFire()
    {
        // Template scaled to gameplay size, densified and wobbled like a real hand.
        var dense = Transform(RuneTemplates.Fire.Strokes, scale: 180f, offset: new Vector2(450, 300))
            .Select(s => Densify(s, 8f)).ToList();
        var jittered = Jitter(dense, 10f, seed: 1234);

        RuneMatch? match = NewClassifier().Recognize(jittered);

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    [Fact]
    public void Recognize_IsDeterministic()
    {
        var strokes = Transform(RuneTemplates.Fire.Strokes, 0.3f, 150f, new Vector2(300, 200));
        var classifier = NewClassifier();

        RuneMatch? a = classifier.Recognize(strokes);
        RuneMatch? b = classifier.Recognize(strokes);

        Assert.NotNull(a);
        Assert.Equal(a!.Name, b!.Name);
        Assert.Equal(a.Confidence, b.Confidence);
    }

    // ---- Rejection ----

    [Fact]
    public void Recognize_Circle_DoesNotCast()
    {
        Assert.Null(NewClassifier().Recognize(Circle()));
    }

    [Fact]
    public void Recognize_Wave_DoesNotCast()
    {
        Assert.Null(NewClassifier().Recognize(Wave()));
    }

    [Fact]
    public void Recognize_SingleStraightLine_DoesNotCast()
    {
        Assert.Null(NewClassifier().Recognize([new List<Vector2> { new(100, 100), new(500, 500) }]));
    }

    // ---- Water rune ----

    [Fact]
    public void Recognize_WaterTemplate_CastsWater()
    {
        RuneMatch? match = NewClassifier().Recognize(RuneTemplates.Water.Strokes);

        Assert.NotNull(match);
        Assert.Equal("water", match!.Name);
        Assert.InRange(match.Confidence, RuneClassifier.MatchThreshold, 1f);
    }

    [Fact]
    public void Recognize_WaterTranslatedAndScaled_CastsWater()
    {
        var strokes = Transform(RuneTemplates.Water.Strokes, scale: 240f, offset: new Vector2(500, 250));

        RuneMatch? match = NewClassifier().Recognize(strokes);

        Assert.NotNull(match);
        Assert.Equal("water", match!.Name);
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(180f)]
    [InlineData(320f)]
    public void Recognize_WaterRotated_CastsWater(float degrees)
    {
        var strokes = Transform(RuneTemplates.Water.Strokes, degrees * MathF.PI / 180f, 200f, new Vector2(400, 300));

        RuneMatch? match = NewClassifier().Recognize(strokes);

        Assert.NotNull(match);
        Assert.Equal("water", match!.Name);
    }

    [Fact]
    public void Recognize_Fire_IsNotClassifiedAsWater()
    {
        RuneMatch? match = NewClassifier().Recognize(RuneTemplates.Fire.Strokes);

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    [Fact]
    public void Recognize_SingleWave_DoesNotCastWater()
    {
        // A single wavy line is not the full water rune (arch + feet loops + bar).
        List<Vector2> wave = [];
        for (int i = 0; i <= 30; i++)
        {
            float x = i / 30f * 2f - 1f;
            wave.Add(new Vector2(x * 200f + 400f, 300f + MathF.Sin(x * MathF.Tau) * 40f));
        }

        Assert.Null(NewClassifier().Recognize([wave]));
    }

    // ---- Density rune ----

    [Fact]
    public void Recognize_DensityTemplate_CastsDensity()
    {
        RuneMatch? match = NewClassifier().Recognize(RuneTemplates.Density.Strokes);

        Assert.NotNull(match);
        Assert.Equal("density", match!.Name);
    }

    [Fact]
    public void Recognize_DensityTranslatedAndScaled_CastsDensity()
    {
        var strokes = Transform(RuneTemplates.Density.Strokes, scale: 260f, offset: new Vector2(500, 250));

        RuneMatch? match = NewClassifier().Recognize(strokes);

        Assert.NotNull(match);
        Assert.Equal("density", match!.Name);
    }

    [Theory]
    [InlineData(45f)]
    [InlineData(180f)]
    [InlineData(300f)]
    public void Recognize_DensityRotated_CastsDensity(float degrees)
    {
        var strokes = Transform(RuneTemplates.Density.Strokes, degrees * MathF.PI / 180f, 200f, new Vector2(400, 300));

        RuneMatch? match = NewClassifier().Recognize(strokes);

        Assert.NotNull(match);
        Assert.Equal("density", match!.Name);
    }

    [Fact]
    public void Recognize_DensityPartial_DoesNotCast()
    {
        // Only the zigzag stroke: not the full three-stroke rune.
        Assert.Null(NewClassifier().Recognize([RuneTemplates.Density.Strokes[0].ToList()]));
    }

    // ---- Diagnostics (HUD) ----

    [Fact]
    public void RecognizeDetailed_BelowThreshold_StillNamesCandidate()
    {
        RuneDiagnostics diagnostics = NewClassifier().RecognizeDetailed(Wave());

        Assert.False(diagnostics.Matched);
        Assert.Equal("fire", diagnostics.Name); // the only rune the model knows
        Assert.InRange(diagnostics.Confidence, 0f, 1f);
    }

    [Fact]
    public void RecognizeDetailed_EmptyStrokes_ReturnsNothing()
    {
        RuneDiagnostics diagnostics = NewClassifier().RecognizeDetailed([]);

        Assert.False(diagnostics.Matched);
        Assert.Null(diagnostics.Name);
        Assert.Equal(0f, diagnostics.Confidence);
    }

    [Fact]
    public void RecognizeDetailed_TemplateStrokes_MatchesAboveThreshold()
    {
        RuneDiagnostics diagnostics = NewClassifier().RecognizeDetailed(RuneTemplates.Fire.Strokes);

        Assert.True(diagnostics.Matched);
        Assert.Equal("fire", diagnostics.Name);
        Assert.True(diagnostics.Confidence >= RuneClassifier.MatchThreshold);
    }

    private static List<Vector2> Densify(IReadOnlyList<Vector2> stroke, float maxSegment)
    {
        List<Vector2> dense = [stroke[0]];
        for (int i = 1; i < stroke.Count; i++)
        {
            float length = stroke[i].DistanceTo(stroke[i - 1]);
            int steps = Math.Max(1, (int)(length / maxSegment));
            for (int t = 1; t <= steps; t++)
            {
                dense.Add(stroke[i - 1].Lerp(stroke[i], (float)t / steps));
            }
        }
        return dense;
    }
}
