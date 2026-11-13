using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ProjectEnchanter;
using Xunit;

namespace ProjectEnchanter.Tests;

/// <summary>
/// Tests for EnclosedRuneDetector — finding runes enclosed in hand-drawn
/// circles, including multiple circles per drawing.
/// </summary>
public class EnclosedRuneDetectorTests
{
    private static List<Vector2> Circle(Vector2 center, float radius, int points = 40)
    {
        List<Vector2> circle = [];
        for (int i = 0; i <= points; i++)
        {
            float angle = i * MathF.Tau / points;
            circle.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
        return circle;
    }

    private static List<Vector2> ScaledShape(IReadOnlyList<Vector2> shape, float scale, Vector2 offset)
        => shape.Select(p => p * scale + offset).ToList();

    // ---- Circle detection ----

    [Fact]
    public void TryFitCircle_Circle_ReturnsTrue()
    {
        bool ok = EnclosedRuneDetector.TryFitCircle(Circle(new Vector2(300, 200), 100f), out Vector2 center, out float radius);

        Assert.True(ok);
        Assert.True(center.DistanceTo(new Vector2(300, 200)) < 5f);
        Assert.InRange(radius, 95f, 105f);
    }

    [Fact]
    public void TryFitCircle_StraightLine_ReturnsFalse()
    {
        Assert.False(EnclosedRuneDetector.TryFitCircle(
            [new Vector2(0, 0), new Vector2(50, 10), new Vector2(100, 20), new Vector2(150, 30),
             new Vector2(200, 40), new Vector2(250, 50), new Vector2(300, 60), new Vector2(350, 70),
             new Vector2(400, 80), new Vector2(450, 90), new Vector2(500, 100)], out _, out _));
    }

    [Fact]
    public void TryFitCircle_Square_ReturnsFalse()
    {
        List<Vector2> square = [];
        Vector2[] corners = [new(0, 0), new(100, 0), new(100, 100), new(0, 100), new(0, 0)];
        for (int i = 1; i < corners.Length; i++)
        {
            for (int t = 0; t < 10; t++) square.Add(corners[i - 1].Lerp(corners[i], t / 10f));
        }
        square.Add(corners[^1]);

        Assert.False(EnclosedRuneDetector.TryFitCircle(square, out _, out _));
    }

    [Fact]
    public void TryFitCircle_OpenArc_ReturnsFalse()
    {
        // Three-quarters of a circle is not closed.
        List<Vector2> arc = [];
        for (int i = 0; i <= 30; i++)
        {
            float angle = i * MathF.Tau * 0.75f / 30f;
            arc.Add(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 100f + new Vector2(300, 300));
        }

        Assert.False(EnclosedRuneDetector.TryFitCircle(arc, out _, out _));
    }

    [Fact]
    public void TryFitCircle_Scribble_ReturnsFalse()
    {
        var rnd = new Random(3);
        List<Vector2> scribble = [new(200, 200)];
        for (int i = 0; i < 40; i++)
        {
            Vector2 last = scribble[^1];
            scribble.Add(last + new Vector2((float)(rnd.NextDouble() - 0.5) * 80f, (float)(rnd.NextDouble() - 0.5) * 80f));
        }

        Assert.False(EnclosedRuneDetector.TryFitCircle(scribble, out _, out _));
    }

    // ---- Enclosure grouping ----

    [Fact]
    public void Find_CircleWithFire_ReturnsOneGroup()
    {
        List<List<Vector2>> strokes =
        [
            Circle(new Vector2(400, 300), 150f),
            ScaledShape(RuneTemplates.Fire.Strokes[0], 60f, new Vector2(400, 300)),
        ];

        List<EnclosedRune> regions = EnclosedRuneDetector.Find(strokes);

        EnclosedRune region = Assert.Single(regions);
        Assert.Single(region.Strokes);
        Assert.Equal(150f, region.Radius, 3f);
    }

    [Fact]
    public void Find_NoCircle_ReturnsEmpty()
    {
        List<List<Vector2>> strokes = [ScaledShape(RuneTemplates.Fire.Strokes[0], 60f, new Vector2(400, 300))];

        Assert.Empty(EnclosedRuneDetector.Find(strokes));
    }

    [Fact]
    public void Find_CircleWithoutRune_ReturnsEmpty()
    {
        Assert.Empty(EnclosedRuneDetector.Find([Circle(new Vector2(400, 300), 150f)]));
    }

    [Fact]
    public void Find_RuneOutsideCircle_IsNotGrouped()
    {
        List<List<Vector2>> strokes =
        [
            Circle(new Vector2(200, 200), 100f),
            ScaledShape(RuneTemplates.Fire.Strokes[0], 80f, new Vector2(900, 700)),
        ];

        Assert.Empty(EnclosedRuneDetector.Find(strokes));
    }

    [Fact]
    public void Find_TwoCircles_ReturnsTwoGroupsInStrokeOrder()
    {
        List<List<Vector2>> strokes =
        [
            Circle(new Vector2(200, 200), 120f),
            ScaledShape(RuneTemplates.Fire.Strokes[0], 50f, new Vector2(200, 200)),
            Circle(new Vector2(600, 200), 120f),
            ScaledShape(RuneTemplates.Water.Strokes[0], 50f, new Vector2(600, 200)),
            ScaledShape(RuneTemplates.Water.Strokes[1], 50f, new Vector2(600, 200)),
        ];

        List<EnclosedRune> regions = EnclosedRuneDetector.Find(strokes);

        Assert.Equal(2, regions.Count);
        Assert.Single(regions[0].Strokes);
        Assert.Equal(2, regions[1].Strokes.Count);
    }

    [Fact]
    public void Find_NestedCircles_AssignsRuneToSmallest()
    {
        List<List<Vector2>> strokes =
        [
            Circle(new Vector2(400, 300), 200f),
            Circle(new Vector2(400, 300), 100f),
            ScaledShape(RuneTemplates.Fire.Strokes[0], 30f, new Vector2(400, 300)),
        ];

        List<EnclosedRune> regions = EnclosedRuneDetector.Find(strokes);

        // Only the inner circle holds a rune; it must not also be assigned to the
        // outer circle (which would cast the same rune twice).
        EnclosedRune region = Assert.Single(regions);
        Assert.Equal(100f, region.Radius, 3f);
        Assert.Single(region.Strokes);
    }

    [Fact]
    public void Find_TranslationAndScaleInvariant()
    {
        List<List<Vector2>> strokes =
        [
            Circle(new Vector2(1000, 700), 60f),
            ScaledShape(RuneTemplates.Fire.Strokes[0], 25f, new Vector2(1000, 700)),
        ];

        List<EnclosedRune> regions = EnclosedRuneDetector.Find(strokes);

        Assert.Single(regions);
        Assert.Single(regions[0].Strokes);
    }

    // ---- Integration: circled runes are classified correctly ----

    [Fact]
    public void Classify_CircledFireAndWater_FindsBothRunes()
    {
        var classifier = new RuneClassifier();
        List<List<Vector2>> strokes =
        [
            // circle + fire
            Circle(new Vector2(300, 300), 140f),
            ScaledShape(RuneTemplates.Fire.Strokes[0], 55f, new Vector2(300, 300)),
            // circle + water
            Circle(new Vector2(800, 300), 140f),
            ScaledShape(RuneTemplates.Water.Strokes[0], 55f, new Vector2(800, 300)),
            ScaledShape(RuneTemplates.Water.Strokes[1], 55f, new Vector2(800, 300)),
        ];

        List<string> names = EnclosedRuneDetector.Find(strokes)
            .Select(region => classifier.Recognize(region.Strokes))
            .Where(match => match is not null)
            .Select(match => match!.Name)
            .ToList();

        Assert.Equal(["fire", "water"], names);
    }

    [Fact]
    public void Classify_CircleWithWrongShape_DoesNotCast()
    {
        var classifier = new RuneClassifier();
        List<Vector2> wave = [];
        for (int i = 0; i <= 30; i++)
        {
            float x = i / 30f * 2f - 1f;
            wave.Add(new Vector2(x * 60f + 400f, 300f + MathF.Sin(x * MathF.Tau) * 15f));
        }
        List<List<Vector2>> strokes =
        [
            Circle(new Vector2(400, 300), 140f),
            wave,
        ];

        EnclosedRune region = Assert.Single(EnclosedRuneDetector.Find(strokes));
        Assert.Null(classifier.Recognize(region.Strokes));
    }
}
