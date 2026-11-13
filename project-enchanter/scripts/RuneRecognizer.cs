using Godot;
using System;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>A successful recognition: the rune name and a confidence in [0, 1].</summary>
public sealed record RuneMatch(string Name, float Confidence);

/// <summary>
/// Diagnostic result: the best candidate regardless of whether it passed the
/// threshold, so the UI can show what the recognizer "sees" while testing.
/// </summary>
public sealed record RuneDiagnostics(string? Name, float Confidence, bool Matched);

/// <summary>
/// Recognizes runes drawn as one or more strokes, invariant to position, scale
/// and rotation, using the $P point-cloud recognizer approach (Vatavu et al. 2012):
/// every drawing is normalized into a point cloud (translated to its centroid,
/// scaled to a unit bounding-box diagonal) and compared to each template cloud
/// via greedy nearest-neighbour matching, trying many rotations.
/// </summary>
public class RuneRecognizer
{
    // Calibrated on the RMS-normalized unit space (caret fire rune): realistic
    // hand jitter measures < 1.0 (avg 0.57 at ±3% wobble), while generic shapes
    // (circle ≈ 8.5, square ≈ 8.4, triangle ≈ 5.5, line ≈ 12) measure far above.
    private const float MatchThreshold = 2.0f;
    private const int TotalPoints = 48;   // points per normalized cloud
    private const int RotationSteps = 24; // full-circle search, every 15°

    private readonly List<(string Name, List<Vector2> Cloud, int StrokeCount)> _templates = [];

    public RuneRecognizer()
    {
        RegisterTemplate(RuneTemplates.Fire);
    }

    /// <summary>Adds a rune template to the recognizer.</summary>
    public void RegisterTemplate(RuneTemplate template)
    {
        _templates.Add((template.Name, Normalize(Combine(SmoothStrokes(ResampleStrokes(template.Strokes, TotalPoints)))), template.Strokes.Count));
    }

    /// <summary>
    /// Attempts to recognize the given strokes. Returns the best match whose
    /// distance is under the threshold, or null.
    /// </summary>
    public RuneMatch? Recognize(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        RuneDiagnostics diagnostics = RecognizeDetailed(strokes);
        return diagnostics.Matched ? new RuneMatch(diagnostics.Name!, diagnostics.Confidence) : null;
    }

    /// <summary>
    /// Like <see cref="Recognize"/>, but always returns the best candidate and
    /// its confidence (0 when nothing matches), even below the cast threshold.
    /// </summary>
    public RuneDiagnostics RecognizeDetailed(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        if (strokes.Count == 0)
        {
            return new RuneDiagnostics(null, 0f, false);
        }

        List<Vector2> cloud = Normalize(Combine(SmoothStrokes(ResampleStrokes(strokes, TotalPoints))));
        if (cloud.Count == 0)
        {
            return new RuneDiagnostics(null, 0f, false);
        }

        string? bestName = null;
        float bestDistance = float.MaxValue;

        foreach ((string name, List<Vector2> template, int strokeCount) in _templates)
        {
            // A rune requires all of its strokes: don't fire on a half-drawn rune.
            if (strokes.Count < strokeCount) continue;

            float distance = BestDistanceOverRotations(cloud, template);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestName = name;
            }
        }

        if (bestName is null || bestDistance >= MatchThreshold)
        {
            return new RuneDiagnostics(bestName, ConfidenceFor(bestName is null ? MatchThreshold : bestDistance), false);
        }
        return new RuneDiagnostics(bestName, 1.0f - bestDistance / MatchThreshold, true);
    }

    private static float ConfidenceFor(float distance)
        => Math.Clamp(1.0f - distance / MatchThreshold, 0f, 1f);

    /// <summary>Tries all rotations of the drawing and returns the smallest cloud distance.</summary>
    private static float BestDistanceOverRotations(List<Vector2> cloud, List<Vector2> template)
    {
        float best = float.MaxValue;
        for (int step = 0; step < RotationSteps; step++)
        {
            float angle = step * MathF.Tau / RotationSteps;
            List<Vector2> rotated = [.. cloud];
            for (int i = 0; i < rotated.Count; i++)
            {
                rotated[i] = rotated[i].Rotated(angle);
            }

            float distance = GreedyCloudMatch(rotated, template);
            if (distance < best) best = distance;
        }
        return best;
    }

    /// <summary>Greedy nearest-neighbour distance in both directions; the smaller one wins.</summary>
    private static float GreedyCloudMatch(List<Vector2> a, List<Vector2> b)
        => MathF.Min(CloudDistance(a, b), CloudDistance(b, a));

    /// <summary>
    /// Greedy matching: each point of <paramref name="a"/> takes its nearest
    /// unmatched neighbour in <paramref name="b"/>; earlier points get a higher
    /// weight (per the $P paper).
    /// </summary>
    private static float CloudDistance(List<Vector2> a, List<Vector2> b)
    {
        int n = a.Count, m = b.Count;
        if (n == 0 || m == 0) return float.MaxValue;

        bool[] matched = new bool[m];
        float sum = 0f;
        const float start = 0.5f;

        for (int i = 0; i < n; i++)
        {
            int index = -1;
            float min = float.MaxValue;
            for (int j = 0; j < m; j++)
            {
                if (matched[j]) continue;
                float d = a[i].DistanceSquaredTo(b[j]);
                if (d < min)
                {
                    min = d;
                    index = j;
                }
            }
            matched[index] = true;
            float weight = 1f - (i - start + 1f) / n;
            sum += MathF.Sqrt(min) * weight;
        }

        return sum;
    }

    // ---- Smoothing (makes matching tolerant to hand jitter) ----

    /// <summary>
    /// Applies a small moving-average filter to each stroke (endpoints are
    /// partially weighted to stay anchored). Independent per-point noise is
    /// reduced while the overall shape is preserved.
    /// </summary>
    public static List<List<Vector2>> SmoothStrokes(IReadOnlyList<List<Vector2>> strokes, int passes = 2)
    {
        List<List<Vector2>> current = [.. strokes];
        for (int pass = 0; pass < passes; pass++)
        {
            List<List<Vector2>> next = new(current.Count);
            foreach (List<Vector2> stroke in current)
            {
                if (stroke.Count < 3)
                {
                    next.Add(stroke);
                    continue;
                }
                List<Vector2> smoothed = new(stroke.Count)
                {
                    (stroke[0] * 2f + stroke[1]) / 3f,
                };
                for (int i = 1; i < stroke.Count - 1; i++)
                {
                    smoothed.Add((stroke[i - 1] + stroke[i] * 2f + stroke[i + 1]) * 0.25f);
                }
                smoothed.Add((stroke[^2] + stroke[^1] * 2f) / 3f);
                next.Add(smoothed);
            }
            current = next;
        }
        return current;
    }

    // ---- Normalization (makes recognition position/scale invariant) ----

    /// <summary>Merges all strokes into a single point cloud.</summary>
    private static List<Vector2> Combine(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        List<Vector2> combined = [];
        foreach (IReadOnlyList<Vector2> stroke in strokes)
        {
            combined.AddRange(stroke);
        }
        return combined;
    }

    /// <summary>
    /// Centers the cloud on its centroid and scales it so the root-mean-square
    /// distance from the centroid is 1 — removing position and size differences.
    /// RMS radius (not bounding box) is used because it is rotation-invariant.
    /// </summary>
    private static List<Vector2> Normalize(List<Vector2> points)
    {
        if (points.Count == 0) return [];

        Vector2 centroid = Vector2.Zero;
        foreach (Vector2 p in points) centroid += p;
        centroid /= points.Count;

        float sumSquares = 0f;
        foreach (Vector2 p in points)
        {
            float dx = p.X - centroid.X;
            float dy = p.Y - centroid.Y;
            sumSquares += dx * dx + dy * dy;
        }
        float rms = MathF.Sqrt(sumSquares / points.Count);

        List<Vector2> normalized = new(points.Count);
        if (rms < 0.0001f)
        {
            foreach (Vector2 p in points) normalized.Add(Vector2.Zero);
            return normalized;
        }

        float scale = 1f / rms;
        foreach (Vector2 p in points)
        {
            normalized.Add((p - centroid) * scale);
        }
        return normalized;
    }

    // ---- Resampling (makes clouds density-comparable) ----

    /// <summary>
    /// Redraws the strokes as a fixed total number of points, each stroke
    /// getting points proportional to its arc length.
    /// </summary>
    public static List<List<Vector2>> ResampleStrokes(IReadOnlyList<IReadOnlyList<Vector2>> strokes, int totalPoints)
    {
        List<float> lengths = [];
        float totalLength = 0f;
        foreach (IReadOnlyList<Vector2> stroke in strokes)
        {
            float length = 0f;
            for (int i = 1; i < stroke.Count; i++)
            {
                length += stroke[i].DistanceTo(stroke[i - 1]);
            }
            lengths.Add(length);
            totalLength += length;
        }

        List<List<Vector2>> resampled = [];
        if (strokes.Count == 0 || totalLength <= 0f)
        {
            if (strokes.Count == 0) return resampled;
            // All strokes are single points (or zero length): keep them as-is.
            foreach (IReadOnlyList<Vector2> stroke in strokes)
            {
                resampled.Add([.. stroke]);
            }
            return resampled;
        }

        int assigned = 0;
        for (int s = 0; s < strokes.Count; s++)
        {
            int count = s == strokes.Count - 1
                ? totalPoints - assigned // last stroke absorbs rounding
                : Math.Max(2, (int)MathF.Round(lengths[s] / totalLength * totalPoints));
            count = Math.Min(count, totalPoints - assigned);
            if (count < 1) break;
            assigned += count;
            resampled.Add(ResamplePolyline(strokes[s], count));
        }
        return resampled;
    }

    /// <summary>Resamples one polyline into exactly <paramref name="count"/> evenly spaced points (by arc length).</summary>
    private static List<Vector2> ResamplePolyline(IReadOnlyList<Vector2> stroke, int count)
    {
        if (count <= 1 || stroke.Count == 1)
        {
            return [stroke[0]];
        }

        float[] cumulative = new float[stroke.Count];
        for (int i = 1; i < stroke.Count; i++)
        {
            cumulative[i] = cumulative[i - 1] + stroke[i].DistanceTo(stroke[i - 1]);
        }
        float total = cumulative[^1];
        if (total <= 0f)
        {
            List<Vector2> padded = [stroke[0]];
            while (padded.Count < count) padded.Add(stroke[0]);
            return padded;
        }

        List<Vector2> result = new(count);
        int segment = 0;
        for (int k = 0; k < count; k++)
        {
            float target = total * k / (count - 1);
            while (segment < stroke.Count - 2 && cumulative[segment + 1] < target)
            {
                segment++;
            }
            float segLength = cumulative[segment + 1] - cumulative[segment];
            float t = segLength <= 0f ? 0f : (target - cumulative[segment]) / segLength;
            result.Add(stroke[segment].Lerp(stroke[segment + 1], Math.Clamp(t, 0f, 1f)));
        }
        result[^1] = stroke[^1];
        return result;
    }
}
