using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ProjectEnchanter;
using Xunit;

namespace ProjectEnchanter.Tests;

/// <summary>
/// Tests for RuneFeatures — the drawing → feature-vector pipeline. The parity
/// test guards against the C# implementation drifting from the Python trainer.
/// </summary>
public class RuneFeaturesTests
{
    private static List<List<Vector2>> Transform(
        IReadOnlyList<IReadOnlyList<Vector2>> strokes,
        float rotationRadians = 0f,
        float scale = 1f,
        Vector2? offset = null)
    {
        Vector2 o = offset ?? Vector2.Zero;
        return strokes.Select(s => s.Select(p => p.Rotated(rotationRadians) * scale + o).ToList()).ToList();
    }

    private static float MaxDifference(float[] a, float[] b)
    {
        float max = 0f;
        for (int i = 0; i < a.Length; i++) max = MathF.Max(max, MathF.Abs(a[i] - b[i]));
        return max;
    }

    // ---- Shape / contract ----

    [Fact]
    public void Extract_ReturnsFeatureCountDimensions()
    {
        (float[] plus, float[] minus) = RuneFeatures.Extract(RuneTemplates.Fire.Strokes);

        Assert.Equal(RuneFeatures.FeatureCount, plus.Length);
        Assert.Equal(RuneFeatures.FeatureCount, minus.Length);
        Assert.Equal(RuneModelWeights.InputSize, plus.Length);
    }

    [Fact]
    public void Extract_EmptyStrokes_ReturnsZeros()
    {
        (float[] plus, _) = RuneFeatures.Extract([]);

        Assert.All(plus, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Extract_IsDeterministic()
    {
        (float[] a, _) = RuneFeatures.Extract(RuneTemplates.Fire.Strokes);
        (float[] b, _) = RuneFeatures.Extract(RuneTemplates.Fire.Strokes);

        Assert.Equal(a, b);
    }

    // ---- Invariance (the model relies on these properties) ----

    [Fact]
    public void Extract_TranslationInvariant()
    {
        (float[] baseline, _) = RuneFeatures.Extract(RuneTemplates.Fire.Strokes);
        (float[] moved, _) = RuneFeatures.Extract(
            Transform(RuneTemplates.Fire.Strokes, offset: new Vector2(512, 333)));

        Assert.True(MaxDifference(baseline, moved) < 0.02f, $"max diff {MaxDifference(baseline, moved)}");
    }

    [Fact]
    public void Extract_ScaleInvariant()
    {
        (float[] baseline, _) = RuneFeatures.Extract(RuneTemplates.Fire.Strokes);
        (float[] scaled, _) = RuneFeatures.Extract(
            Transform(RuneTemplates.Fire.Strokes, scale: 7.5f, offset: new Vector2(400, 300)));

        Assert.True(MaxDifference(baseline, scaled) < 0.02f, $"max diff {MaxDifference(baseline, scaled)}");
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(270f)]
    public void Extract_RotationInvariant(float degrees)
    {
        (float[] baselinePlus, float[] baselineMinus) = RuneFeatures.Extract(RuneTemplates.Fire.Strokes);
        (float[] rotatedPlus, float[] rotatedMinus) = RuneFeatures.Extract(
            Transform(RuneTemplates.Fire.Strokes, degrees * MathF.PI / 180f, 3f, new Vector2(250, 250)));

        // The PCA axis has no sign, so a rotation may map "plus" onto "minus".
        // Compare the best pairing of the two mirror variants.
        float direct = MaxDifference(baselinePlus, rotatedPlus);
        float swapped = MaxDifference(baselinePlus, rotatedMinus);
        float best = MathF.Min(direct, swapped);

        Assert.True(best < 0.05f, $"max diff {best} (direct {direct}, swapped {swapped})");
    }

    // ---- Parity with the Python trainer (do not break!) ----

    [Fact]
    public void Extract_MatchesPythonTrainerParitySample()
    {
        // Rebuild the reference drawing ("fire" template) from the generated data.
        List<List<Vector2>> strokes = [];
        int index = 0;
        foreach (int length in RuneModelWeights.ParityStrokeLengths)
        {
            List<Vector2> stroke = [];
            for (int i = 0; i < length; i++)
            {
                stroke.Add(new Vector2(
                    RuneModelWeights.ParityPoints[index],
                    RuneModelWeights.ParityPoints[index + 1]));
                index += 2;
            }
            strokes.Add(stroke);
        }

        (float[] plus, _) = RuneFeatures.Extract(strokes);

        float maxDiff = MaxDifference(plus, RuneModelWeights.ParityFeatures);
        Assert.True(maxDiff < 1e-3f,
            $"C# features drifted from the Python trainer (max diff {maxDiff}). " +
            "Update RuneFeatures and/or retrain so both implementations match.");
    }
}
