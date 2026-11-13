using Godot;
using System;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>
/// Turns drawn strokes into the feature vector the rune model consumes.
/// IMPORTANT: this mirrors tools/train_rune_model.py exactly — if you change
/// the math here, change it there and retrain (the generated weights contain
/// a parity sample that the tests check against).
///
/// Pipeline:
///   1. resample every stroke to evenly-spaced points (48 total, split by length)
///   2. smooth twice with a 1-2-1 moving average (kills hand jitter)
///   3. canonicalize: centroid at origin, RMS radius 1, then rotate onto the
///      principal (PCA) axis. The axis sign is ambiguous, so two mirror
///      variants are produced; the model averages their predictions.
///   4. append 4 structural features: eigenvalue ratio (thinness),
///      path length / RMS, corner fraction, sharpest turn / pi.
/// </summary>
public static class RuneFeatures
{
    public const int TotalPoints = 48;
    public const int FeatureCount = TotalPoints * 2 + 6;

    /// <summary>Extracts the two mirror variants of the feature vector.</summary>
    public static (float[] Plus, float[] Minus) Extract(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        List<List<Vector2>> resampled = ResampleStrokes(strokes, TotalPoints);
        List<List<Vector2>> smoothed = SmoothStrokes(resampled, 2);

        List<Vector2> points = [];
        foreach (List<Vector2> stroke in smoothed) points.AddRange(stroke);

        if (points.Count == 0)
        {
            return (new float[FeatureCount], new float[FeatureCount]);
        }

        (float[] plus, float[] minus) = Canonicalize(points);
        (float thinness, float pathOverRms, float cornerFraction, float maxTurn) = StructuralFeatures(points);
        (float strokeCount, float gapOverRms) = StrokeShapeFeatures(resampled, points);

        void AddStructural(float[] v)
        {
            int o = TotalPoints * 2;
            v[o] = thinness;
            v[o + 1] = pathOverRms;
            v[o + 2] = cornerFraction;
            v[o + 3] = maxTurn;
            v[o + 4] = strokeCount;
            v[o + 5] = gapOverRms;
        }

        AddStructural(plus);
        AddStructural(minus);
        return (plus, minus);
    }

    // ---- Canonicalization (position/scale/rotation invariance) ----

    private static (float[] Plus, float[] Minus) Canonicalize(List<Vector2> points)
    {
        int n = points.Count;
        Vector2 centroid = Vector2.Zero;
        foreach (Vector2 p in points) centroid += p;
        centroid /= n;

        float sumSquares = 0f;
        foreach (Vector2 p in points)
        {
            Vector2 d = p - centroid;
            sumSquares += d.X * d.X + d.Y * d.Y;
        }
        float rms = MathF.Sqrt(sumSquares / n);

        float[] plus = new float[FeatureCount];
        float[] minus = new float[FeatureCount];
        if (rms < 0.0001f) return (plus, minus);

        float covXx = 0f, covXy = 0f, covYy = 0f;
        foreach (Vector2 p in points)
        {
            float x = (p.X - centroid.X) / rms;
            float y = (p.Y - centroid.Y) / rms;
            covXx += x * x;
            covXy += x * y;
            covYy += y * y;
        }
        covXx /= n;
        covXy /= n;
        covYy /= n;

        float theta = 0.5f * MathF.Atan2(2f * covXy, covXx - covYy);
        float cos = MathF.Cos(-theta);
        float sin = MathF.Sin(-theta);

        // The principal axis has no sign: resolve the 180° ambiguity
        // deterministically (radius-weighted x skew) so features are stable
        // under rotation.
        float skew = 0f;
        foreach (Vector2 p in points)
        {
            float x = (p.X - centroid.X) / rms;
            float y = (p.Y - centroid.Y) / rms;
            float rx = x * cos - y * sin;
            float ry = x * sin + y * cos;
            skew += rx * MathF.Sqrt(rx * rx + ry * ry);
        }
        float flip = skew < 0f ? -1f : 1f;

        int i = 0;
        foreach (Vector2 p in points)
        {
            float x = (p.X - centroid.X) / rms;
            float y = (p.Y - centroid.Y) / rms;
            float rx = (x * cos - y * sin) * flip;
            float ry = (x * sin + y * cos) * flip;
            plus[i] = rx;
            plus[i + 1] = ry;
            minus[i] = -rx;
            minus[i + 1] = ry;
            i += 2;
        }
        return (plus, minus);
    }

    // ---- Stroke topology (rotation/scale invariant) ----

    /// <summary>Stroke count (normalized) and mean gap between consecutive strokes, over RMS.</summary>
    private static (float StrokeCount, float GapOverRms) StrokeShapeFeatures(
        List<List<Vector2>> resampledStrokes, List<Vector2> points)
    {
        if (points.Count == 0) return (0f, 0f);

        Vector2 centroid = Vector2.Zero;
        foreach (Vector2 p in points) centroid += p;
        centroid /= points.Count;

        float sq = 0f;
        foreach (Vector2 p in points)
        {
            Vector2 d = p - centroid;
            sq += d.X * d.X + d.Y * d.Y;
        }
        float rms = MathF.Sqrt(sq / points.Count);
        if (rms < 0.0001f) return (0f, 0f);

        float gapSum = 0f;
        int gaps = 0;
        for (int i = 1; i < resampledStrokes.Count; i++)
        {
            List<Vector2> previous = resampledStrokes[i - 1];
            List<Vector2> current = resampledStrokes[i];
            if (previous.Count == 0 || current.Count == 0) continue;
            gapSum += previous[^1].DistanceTo(current[0]);
            gaps++;
        }
        float meanGap = gaps > 0 ? gapSum / gaps : 0f;

        return (resampledStrokes.Count / 5f, meanGap / rms);
    }

    // ---- Structural features (rotation/scale invariant) ----

    private static (float Thinness, float PathOverRms, float CornerFraction, float MaxTurn) StructuralFeatures(List<Vector2> points)
    {
        int n = points.Count;
        Vector2 centroid = Vector2.Zero;
        foreach (Vector2 p in points) centroid += p;
        centroid /= n;

        float sq = 0f;
        float path = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 d = points[i] - centroid;
            sq += d.X * d.X + d.Y * d.Y;
            if (i > 0) path += points[i].DistanceTo(points[i - 1]);
        }
        float rms = MathF.Sqrt(sq / n);
        if (rms < 0.0001f) return (0f, 0f, 0f, 0f);

        float covXx = 0f, covXy = 0f, covYy = 0f;
        foreach (Vector2 p in points)
        {
            float x = p.X - centroid.X;
            float y = p.Y - centroid.Y;
            covXx += x * x;
            covXy += x * y;
            covYy += y * y;
        }
        covXx /= n;
        covXy /= n;
        covYy /= n;
        float trace = covXx + covYy;
        float det = covXx * covYy - covXy * covXy;
        float disc = MathF.Sqrt(MathF.Max(trace * trace / 4f - det, 0f));
        float major = trace / 2f + disc;
        float minor = MathF.Max(trace / 2f - disc, 0f);
        float thinness = major > 1e-9f ? minor / major : 0f;

        List<Vector2> segments = [];
        for (int i = 1; i < n; i++)
        {
            Vector2 d = points[i] - points[i - 1];
            if (d.Length() > 1e-6f) segments.Add(d);
        }

        int corners = 0;
        float maxTurn = 0f;
        for (int i = 1; i < segments.Count; i++)
        {
            Vector2 a = segments[i - 1];
            Vector2 b = segments[i];
            float turn = MathF.Abs(MathF.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y));
            maxTurn = MathF.Max(maxTurn, turn);
            if (turn > MathF.PI / 3f) corners++; // 60°
        }

        float cornerFraction = segments.Count > 0 ? (float)corners / segments.Count : 0f;
        return (thinness, path / rms, cornerFraction, maxTurn / MathF.PI);
    }

    // ---- Resampling / smoothing (shared with the trainer) ----

    /// <summary>Redraws strokes as a fixed number of arc-length-even points, split by stroke length.</summary>
    public static List<List<Vector2>> ResampleStrokes(IReadOnlyList<IReadOnlyList<Vector2>> strokes, int totalPoints)
    {
        List<float> lengths = [];
        float totalLength = 0f;
        foreach (IReadOnlyList<Vector2> stroke in strokes)
        {
            float length = 0f;
            for (int i = 1; i < stroke.Count; i++) length += stroke[i].DistanceTo(stroke[i - 1]);
            lengths.Add(length);
            totalLength += length;
        }

        List<List<Vector2>> resampled = [];
        if (strokes.Count == 0) return resampled;
        if (totalLength <= 0f)
        {
            foreach (IReadOnlyList<Vector2> stroke in strokes) resampled.Add([.. stroke]);
            return resampled;
        }

        int assigned = 0;
        for (int s = 0; s < strokes.Count; s++)
        {
            int count = s == strokes.Count - 1
                ? totalPoints - assigned
                : Math.Max(2, (int)MathF.Round(lengths[s] / totalLength * totalPoints));
            count = Math.Min(count, totalPoints - assigned);
            if (count < 1) break;
            assigned += count;
            resampled.Add(ResamplePolyline(strokes[s], count));
        }
        return resampled;
    }

    private static List<Vector2> ResamplePolyline(IReadOnlyList<Vector2> stroke, int count)
    {
        if (count <= 1 || stroke.Count == 1) return [stroke[0]];

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
            while (segment < stroke.Count - 2 && cumulative[segment + 1] < target) segment++;
            float segLength = cumulative[segment + 1] - cumulative[segment];
            float t = segLength <= 0f ? 0f : (target - cumulative[segment]) / segLength;
            result.Add(stroke[segment].Lerp(stroke[segment + 1], Math.Clamp(t, 0f, 1f)));
        }
        result[^1] = stroke[^1];
        return result;
    }

    /// <summary>1-2-1 moving average per stroke, 2 passes by default.</summary>
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
}
