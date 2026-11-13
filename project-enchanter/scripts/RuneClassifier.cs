using Godot;
using System;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>A successful recognition: the rune name and a confidence in [0, 1].</summary>
public sealed record RuneMatch(string Name, float Confidence);

/// <summary>
/// Diagnostic result: the best rune candidate regardless of whether it passed
/// the threshold, so the UI can show what the classifier "sees" while testing.
/// </summary>
public sealed record RuneDiagnostics(string? Name, float Confidence, bool Matched);

/// <summary>
/// Classifies drawn strokes as a rune using the trained neural network
/// (see tools/train_rune_model.py). Invariant to position, scale and rotation
/// by construction of the features (see RuneFeatures).
/// </summary>
public class RuneClassifier
{
    /// <summary>
    /// Minimum probability before a rune "casts". Trained rejection of scribbles
    /// is already strong (~0.3% false positives at argmax); 0.6 adds margin.
    /// </summary>
    public const float MatchThreshold = 0.6f;

    /// <summary>Index of the "unknown" output class.</summary>
    private static readonly int UnknownIndex = Array.IndexOf(RuneModel.OutputNames, "unknown");

    private readonly string[] _runeNames;
    private readonly int[] _runeIndices;

    public RuneClassifier()
    {
        List<string> names = [];
        List<int> indices = [];
        for (int i = 0; i < RuneModel.OutputNames.Length; i++)
        {
            if (i == UnknownIndex) continue;
            names.Add(RuneModel.OutputNames[i]);
            indices.Add(i);
        }
        _runeNames = [.. names];
        _runeIndices = [.. indices];
    }

    /// <summary>True when the model knows at least one rune.</summary>
    public bool HasRunes => _runeNames.Length > 0;

    /// <summary>
    /// Classifies the strokes. Returns the most likely rune if its probability
    /// is at or above the threshold, or null.
    /// </summary>
    public RuneMatch? Recognize(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        RuneDiagnostics diagnostics = RecognizeDetailed(strokes);
        return diagnostics.Matched ? new RuneMatch(diagnostics.Name!, diagnostics.Confidence) : null;
    }

    /// <summary>
    /// Like <see cref="Recognize"/>, but always returns the best rune candidate
    /// and its probability, even below the cast threshold.
    /// </summary>
    public RuneDiagnostics RecognizeDetailed(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        if (strokes.Count == 0 || _runeNames.Length == 0)
        {
            return new RuneDiagnostics(null, 0f, false);
        }

        (float[] plus, float[] minus) = RuneFeatures.Extract(strokes);

        // Degenerate drawings (single point, zero-size) produce an all-zero
        // feature vector, which is outside the training distribution.
        bool degenerate = true;
        foreach (float v in plus)
        {
            if (v != 0f)
            {
                degenerate = false;
                break;
            }
        }
        if (degenerate) return new RuneDiagnostics(null, 0f, false);

        float[] probabilities = RuneModel.PredictAveraged(plus, minus);

        int best = -1;
        float bestProbability = -1f;
        for (int i = 0; i < _runeIndices.Length; i++)
        {
            float p = probabilities[_runeIndices[i]];
            if (p > bestProbability)
            {
                bestProbability = p;
                best = i;
            }
        }

        if (best < 0) return new RuneDiagnostics(null, 0f, false);

        bool matched = bestProbability >= MatchThreshold;
        return new RuneDiagnostics(_runeNames[best], bestProbability, matched);
    }
}
