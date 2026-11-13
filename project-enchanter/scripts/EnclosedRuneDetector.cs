using Godot;
using System;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>A rune drawing found inside a hand-drawn circle.</summary>
public sealed record EnclosedRune(
    IReadOnlyList<IReadOnlyList<Vector2>> Strokes,
    Vector2 Center,
    float Radius);

/// <summary>
/// Finds runes that the player enclosed in circles, so several runes can be
/// identified in one drawing. Invariant to position and scale.
///
/// A stroke counts as a "circle" when it is closed, roughly round (low radius
/// variation), covers most directions, and has a path length close to a
/// circumference. Strokes lying (almost entirely) inside a circle belong to it;
/// nested strokes are assigned to the smallest enclosing circle.
/// </summary>
public static class EnclosedRuneDetector
{
    private const float ClosureGap = 0.65f;      // max start/end gap, × radius (hand circles end unevenly)
    private const float MaxRadiusVariation = 0.26f; // std/mean radius
    private const float MaxRadiusRange = 0.38f;     // (max-min)/mean, allows ovals
    private const int DirectionBins = 36;
    private const float MinDirectionCoverage = 0.70f;
    private const float PathRatioMin = 0.5f;     // path / circumference
    private const float PathRatioMax = 1.8f;
    private const int MaxSharpTurns = 2;         // polygons have corners; circles don't
    private const float SharpTurnRadians = MathF.PI / 3f; // 60°
    private const float InsideFraction = 0.85f;  // points inside to consider enclosed
    private const float InsideMargin = 1.10f;    // × radius

    /// <summary>Counts stroke(s) that look like hand-drawn circles.</summary>
    public static int CountCircles(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        int count = 0;
        foreach (IReadOnlyList<Vector2> stroke in strokes)
        {
            if (TryFitCircle(stroke, out _, out _)) count++;
        }
        return count;
    }

    /// <summary>
    /// Returns one group per circle that encloses at least one non-circle
    /// stroke. An empty list means no enclosed runes were found.
    /// </summary>
    public static List<EnclosedRune> Find(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        int n = strokes.Count;
        if (n < 2) return [];

        // 1. Identify circle-like strokes.
        List<(int Index, Vector2 Center, float Radius)> circles = [];
        for (int i = 0; i < n; i++)
        {
            if (TryFitCircle(strokes[i], out Vector2 center, out float radius))
            {
                circles.Add((i, center, radius));
            }
        }
        if (circles.Count == 0) return [];

        // 2. Assign each non-circle stroke to the smallest circle containing it.
        HashSet<int> circleIndices = [.. circles.ConvertAll(c => c.Index)];
        Dictionary<int, List<IReadOnlyList<Vector2>>> grouped = [];
        for (int i = 0; i < n; i++)
        {
            if (circleIndices.Contains(i)) continue;

            int bestCircle = -1;
            float bestRadius = float.MaxValue;
            for (int c = 0; c < circles.Count; c++)
            {
                if (circles[c].Radius >= bestRadius) continue;
                if (!IsMostlyInside(strokes[i], circles[c].Center, circles[c].Radius)) continue;
                bestCircle = c;
                bestRadius = circles[c].Radius;
            }
            if (bestCircle < 0) continue;

            if (!grouped.TryGetValue(bestCircle, out List<IReadOnlyList<Vector2>>? list))
            {
                list = [];
                grouped[bestCircle] = list;
            }
            list.Add(strokes[i]);
        }

        // 3. Emit regions in stroke order for stable results.
        List<EnclosedRune> result = [];
        for (int c = 0; c < circles.Count; c++)
        {
            if (grouped.TryGetValue(c, out List<IReadOnlyList<Vector2>>? runeStrokes))
            {
                result.Add(new EnclosedRune(runeStrokes, circles[c].Center, circles[c].Radius));
            }
        }
        return result;
    }

    /// <summary>Fits a circle and checks the stroke actually looks like one.</summary>
    public static bool TryFitCircle(IReadOnlyList<Vector2> stroke, out Vector2 center, out float radius)
    {
        center = Vector2.Zero;
        radius = 0f;
        if (stroke.Count < 10) return false;

        Vector2 centroid = Vector2.Zero;
        foreach (Vector2 p in stroke) centroid += p;
        centroid /= stroke.Count;

        float sum = 0f;
        foreach (Vector2 p in stroke) sum += p.DistanceTo(centroid);
        float mean = sum / stroke.Count;
        if (mean < 4f) return false; // too small to be a usable circle

        float variance = 0f;
        int[] bins = new int[DirectionBins];
        float path = 0f;
        for (int i = 0; i < stroke.Count; i++)
        {
            Vector2 d = stroke[i] - centroid;
            float dist = d.Length();
            variance += (dist - mean) * (dist - mean);

            float angle = MathF.Atan2(d.Y, d.X);
            if (angle < 0f) angle += MathF.Tau;
            int bin = Math.Min((int)(angle / MathF.Tau * DirectionBins), DirectionBins - 1);
            bins[bin]++;

            if (i > 0) path += stroke[i].DistanceTo(stroke[i - 1]);
        }

        float std = MathF.Sqrt(variance / stroke.Count);
        if (std / mean > MaxRadiusVariation) return false; // not round

        float minRadius = float.MaxValue, maxRadius = 0f;
        foreach (Vector2 p in stroke)
        {
            float dist = p.DistanceTo(centroid);
            if (dist < minRadius) minRadius = dist;
            if (dist > maxRadius) maxRadius = dist;
        }
        if ((maxRadius - minRadius) / mean > MaxRadiusRange) return false; // square/oval

        int covered = 0;
        foreach (int count in bins) if (count > 0) covered++;
        if (covered < DirectionBins * MinDirectionCoverage) return false; // not a full loop

        float pathRatio = path / (MathF.Tau * mean);
        if (pathRatio < PathRatioMin || pathRatio > PathRatioMax) return false; // spiral/scribble

        // Reject polygons: a circle's direction changes smoothly, while square
        // corners produce several sharp turns.
        int sharpTurns = 0;
        Vector2 previousDir = Vector2.Zero;
        for (int i = 1; i < stroke.Count; i++)
        {
            Vector2 d = stroke[i] - stroke[i - 1];
            if (d.LengthSquared() < 1e-8f) continue;

            if (previousDir != Vector2.Zero)
            {
                float turn = MathF.Abs(MathF.Atan2(d.X * previousDir.Y - d.Y * previousDir.X,
                                                   d.X * previousDir.X + d.Y * previousDir.Y));
                if (turn > SharpTurnRadians) sharpTurns++;
                if (sharpTurns > MaxSharpTurns) return false;
            }
            previousDir = d;
        }

        float gap = stroke[0].DistanceTo(stroke[^1]);
        if (gap > ClosureGap * mean) return false; // not closed

        center = centroid;
        radius = mean;
        return true;
    }

    /// <summary>True when nearly all of the stroke lies inside the circle.</summary>
    public static bool IsMostlyInside(IReadOnlyList<Vector2> stroke, Vector2 center, float radius)
    {
        if (stroke.Count == 0) return false;

        int inside = 0;
        float limit = radius * InsideMargin;
        foreach (Vector2 p in stroke)
        {
            if (p.DistanceTo(center) <= limit) inside++;
        }
        return (float)inside / stroke.Count >= InsideFraction;
    }
}
