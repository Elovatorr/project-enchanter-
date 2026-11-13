using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ProjectEnchanter;
using Xunit;

namespace ProjectEnchanter.Tests;

/// <summary>
/// Unit/regression tests for RuneRecognizer — the fire rune must be recognized
/// regardless of position, scale and rotation, and non-runes must be rejected.
/// </summary>
public class RuneRecognizerTests
{
    private static RuneRecognizer NewRecognizer() => new();

    /// <summary>Helper: draws the template strokes with an optional transform applied.</summary>
    private static List<List<Vector2>> Transform(
        IReadOnlyList<IReadOnlyList<Vector2>> strokes,
        float rotationRadians = 0f,
        float scale = 1f,
        Vector2? offset = null)
    {
        Vector2 o = offset ?? Vector2.Zero;
        List<List<Vector2>> result = [];
        foreach (IReadOnlyList<Vector2> stroke in strokes)
        {
            var transformed = new List<Vector2>();
            foreach (Vector2 p in stroke)
            {
                transformed.Add(p.Rotated(rotationRadians) * scale + o);
            }
            result.Add(transformed);
        }
        return result;
    }

    /// <summary>A generic scribble that should not match any rune (two strokes, so stroke-count checks pass).</summary>
    private static List<List<Vector2>> Circle()
    {
        List<Vector2> topHalf = [], bottomHalf = [];
        for (int i = 0; i < 13; i++)
        {
            float angle = i * MathF.PI / 12 - MathF.PI;
            topHalf.Add(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 100f);
        }
        for (int i = 0; i < 13; i++)
        {
            float angle = i * MathF.PI / 12;
            bottomHalf.Add(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 100f);
        }
        topHalf.Add(topHalf[^1]);
        bottomHalf.Add(bottomHalf[^1]);
        return [topHalf, bottomHalf];
    }

    // ---- Recognition ----

    [Fact]
    public void Recognize_TemplateStrokes_MatchesFire()
    {
        var recognizer = NewRecognizer();

        RuneMatch? match = recognizer.Recognize(Transform(RuneTemplates.Fire.Strokes));

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
        Assert.InRange(match.Confidence, 0f, 1f);
    }

    [Fact]
    public void Recognize_EmptyStrokes_ReturnsNull()
    {
        var recognizer = NewRecognizer();

        Assert.Null(recognizer.Recognize([]));
    }

    // ---- Diagnostics (live HUD feedback) ----

    [Fact]
    public void RecognizeDetailed_TemplateStrokes_MatchesWithHighConfidence()
    {
        var recognizer = NewRecognizer();

        RuneDiagnostics diag = recognizer.RecognizeDetailed(Transform(RuneTemplates.Fire.Strokes));

        Assert.True(diag.Matched);
        Assert.Equal("fire", diag.Name);
        Assert.InRange(diag.Confidence, 0.9f, 1f);
    }

    [Fact]
    public void RecognizeDetailed_BelowThreshold_StillNamesBestCandidate()
    {
        // A circle is not a rune, but the HUD should still show the best candidate.
        var recognizer = NewRecognizer();

        RuneDiagnostics diag = recognizer.RecognizeDetailed(Circle());

        Assert.False(diag.Matched);
        Assert.NotNull(diag.Name);
        Assert.InRange(diag.Confidence, 0f, 1f);
    }

    [Fact]
    public void RecognizeDetailed_NoStrokes_ReturnsNoCandidate()
    {
        var recognizer = NewRecognizer();

        RuneDiagnostics diag = recognizer.RecognizeDetailed([]);

        Assert.False(diag.Matched);
        Assert.Null(diag.Name);
        Assert.Equal(0f, diag.Confidence);
    }

    [Fact]
    public void RecognizeDetailed_TooFewStrokes_NamesNothingOrDoesNotMatch()
    {
        // With only single-stroke templates registered, a plain line segment
        // cannot be a full rune match (distance far above threshold).
        var recognizer = NewRecognizer();
        List<List<Vector2>> strokes = [[new Vector2(0, 0), new Vector2(100, 100)]];

        RuneDiagnostics diag = recognizer.RecognizeDetailed(strokes);

        Assert.False(diag.Matched);
    }

    // ---- Invariance: position, scale, rotation ----

    [Fact]
    public void Recognize_Translated_MatchesFire()
    {
        var recognizer = NewRecognizer();

        RuneMatch? match = recognizer.Recognize(Transform(
            RuneTemplates.Fire.Strokes, offset: new Vector2(512, 300)));

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    [Fact]
    public void Recognize_ScaledUp_MatchesFire()
    {
        var recognizer = NewRecognizer();

        RuneMatch? match = recognizer.Recognize(Transform(
            RuneTemplates.Fire.Strokes, scale: 4f, offset: new Vector2(700, 400)));

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    [Fact]
    public void Recognize_ScaledDown_MatchesFire()
    {
        var recognizer = NewRecognizer();

        RuneMatch? match = recognizer.Recognize(Transform(
            RuneTemplates.Fire.Strokes, scale: 0.15f, offset: new Vector2(50, 50)));

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(270f)]
    public void Recognize_Rotated_MatchesFire(float degrees)
    {
        var recognizer = NewRecognizer();

        RuneMatch? match = recognizer.Recognize(Transform(
            RuneTemplates.Fire.Strokes,
            rotationRadians: degrees * MathF.PI / 180f,
            offset: new Vector2(400, 300)));

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    // ---- Robustness to hand-drawn wobble ----

    [Fact]
    public void Recognize_JitteredStrokes_MatchesFire()
    {
        var recognizer = NewRecognizer();
        var jittered = Transform(RuneTemplates.Fire.Strokes, offset: new Vector2(300, 300));
        var random = new Random(1234);
        foreach (List<Vector2> stroke in jittered)
        {
            for (int i = 0; i < stroke.Count; i++)
            {
                stroke[i] += new Vector2(
                    (float)(random.NextDouble() - 0.5) * 0.12f,
                    (float)(random.NextDouble() - 0.5) * 0.12f);
            }
        }

        RuneMatch? match = recognizer.Recognize(jittered);

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    [Fact]
    public void Recognize_InterpolatedStrokes_MatchesFire()
    {
        // Hand-drawn input is much denser than the template: resampling must equalize.
        var recognizer = NewRecognizer();
        var dense = Transform(RuneTemplates.Fire.Strokes, offset: new Vector2(200, 200));
        List<List<Vector2>> strokes = dense.Select(AppendMidpoints).ToList();

        RuneMatch? match = recognizer.Recognize(strokes);

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    private static List<Vector2> AppendMidpoints(IReadOnlyList<Vector2> stroke)
    {
        var dense = new List<Vector2>();
        for (int i = 0; i < stroke.Count; i++)
        {
            dense.Add(stroke[i]);
            if (i + 1 < stroke.Count)
            {
                dense.Add(stroke[i].Lerp(stroke[i + 1], 0.25f));
                dense.Add(stroke[i].Lerp(stroke[i + 1], 0.5f));
                dense.Add(stroke[i].Lerp(stroke[i + 1], 0.75f));
            }
        }
        return dense;
    }

    // ---- Rejection ----

    [Fact]
    public void Recognize_CircleScribble_ReturnsNull()
    {
        var recognizer = NewRecognizer();

        Assert.Null(recognizer.Recognize(Circle()));
    }

    [Fact]
    public void Recognize_SingleStraightLine_ReturnsNull()
    {
        var recognizer = NewRecognizer();
        List<List<Vector2>> strokes =
        [
            [new Vector2(0, 0), new Vector2(80, 60), new Vector2(160, 120)],
        ];

        Assert.Null(recognizer.Recognize(strokes));
    }

    // ---- Resampling (supporting logic) ----

    [Fact]
    public void ResampleStrokes_SingleStroke_ProducesExactCount()
    {
        List<Vector2> stroke =
        [
            new(0, 0), new(10, 0), new(20, 0), new(30, 0),
        ];

        List<List<Vector2>> result = RuneRecognizer.ResampleStrokes([stroke], 8);

        Assert.Single(result);
        Assert.Equal(8, result[0].Count);
        Assert.Equal(Vector2.Zero, result[0][0]);
        Assert.Equal(new Vector2(30, 0), result[0][^1]);
    }

    [Fact]
    public void ResampleStrokes_MultipleStrokes_PointsSplitByLength()
    {
        List<Vector2> shortStroke = [new Vector2(0, 0), new Vector2(10, 0)];   // length 10
        List<Vector2> longStroke =
        [
            new(0, 0), new(30, 0), new(60, 0), new(90, 0), new(120, 0),        // length 120
        ];

        List<List<Vector2>> result = RuneRecognizer.ResampleStrokes([shortStroke, longStroke], 14);

        Assert.Equal(2, result.Count);
        // 10/130 of 14 points ≈ 1 → minimum of 2; long stroke gets the rest.
        Assert.Equal(2, result[0].Count);
        Assert.Equal(12, result[1].Count);
        Assert.Equal(new Vector2(120, 0), result[1][^1]);
    }

    [Fact]
    public void ResampleStrokes_PointOnlyStroke_KeepsPoints()
    {
        List<List<Vector2>> result = RuneRecognizer.ResampleStrokes([[new Vector2(5, 5)]], 8);

        Assert.Single(result);
        Assert.Single(result[0]);
    }

    [Fact]
    public void ResampleStrokes_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(RuneRecognizer.ResampleStrokes([], 8));
    }

    // ---- Regression: full drawing session (model + recognizer) ----

    [Fact]
    public void FullSession_ModelStrokesFeedRecognizer()
    {
        // Simulates the player drawing the fire rune through the real model:
        // template geometry scaled up and offset, with small hand wobble.
        var model = new DrawingSurfaceModel();
        var recognizer = NewRecognizer();

        const float scale = 180f;
        Vector2 offset = new(400, 300);
        var rnd = new Random(42);
        Vector2 Wobble()
        {
            // ~3px of smooth-ish hand wobble on a ~360px glyph
            return new Vector2((float)(rnd.NextDouble() - 0.5) * 6f, (float)(rnd.NextDouble() - 0.5) * 6f);
        }

        foreach (IReadOnlyList<Vector2> templateStroke in RuneTemplates.Fire.Strokes)
        {
            model.BeginStroke(templateStroke[0] * scale + offset + Wobble());
            foreach (Vector2 point in templateStroke.Skip(1))
            {
                model.AppendPoint(point * scale + offset + Wobble());
            }
            model.EndStroke();
        }

        RuneMatch? match = recognizer.Recognize(model.Strokes);

        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }

    [Fact]
    public void FullSession_SingleStrokeCastsFire()
    {
        // Fire is a one-stroke rune: a single swoosh through the real model must cast.
        var model = new DrawingSurfaceModel();
        var recognizer = NewRecognizer();

        List<List<Vector2>> strokes = Transform(RuneTemplates.Fire.Strokes, scale: 180f, offset: new Vector2(400, 300));
        model.BeginStroke(strokes[0][0]);
        foreach (Vector2 point in strokes[0].Skip(1))
        {
            model.AppendPoint(point);
        }
        model.EndStroke();

        RuneMatch? match = recognizer.Recognize(model.Strokes);
        Assert.NotNull(match);
        Assert.Equal("fire", match!.Name);
    }
}
