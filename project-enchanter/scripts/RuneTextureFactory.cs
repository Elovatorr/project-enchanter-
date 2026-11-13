using Godot;
using System;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>
/// Rasterizes drawn strokes into a transparent texture, scaled to fit, so the
/// rune can be etched onto the wand's top face.
/// </summary>
public static class RuneTextureFactory
{
    /// <summary>Creates a texture with the strokes drawn in white on transparency.</summary>
    public static ImageTexture Create(
        IReadOnlyList<IReadOnlyList<Vector2>> strokes,
        int width = 128,
        int height = 512,
        int padding = 16,
        int brush = 5)
    {
        Image image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));

        // Bounds of all points.
        bool any = false;
        Vector2 min = Vector2.Zero, max = Vector2.Zero;
        foreach (IReadOnlyList<Vector2> stroke in strokes)
        {
            foreach (Vector2 p in stroke)
            {
                if (!any)
                {
                    min = max = p;
                    any = true;
                }
                else
                {
                    min = min.Min(p);
                    max = max.Max(p);
                }
            }
        }
        if (!any) return ImageTexture.CreateFromImage(image);

        Vector2 size = max - min;
        float innerWidth = width - padding * 2;
        float innerHeight = height - padding * 2;
        float scale = MathF.Min(
            size.X > 0.001f ? innerWidth / size.X : float.MaxValue,
            size.Y > 0.001f ? innerHeight / size.Y : float.MaxValue);
        if (float.IsInfinity(scale) || scale <= 0f) scale = 1f;

        Vector2 drawn = size * scale;
        Vector2 origin = new((width - drawn.X) * 0.5f, (height - drawn.Y) * 0.5f);

        Vector2 ToImage(Vector2 p) => origin + (p - min) * scale;

        Color ink = Colors.White;
        float brushRadius = brush * 0.5f;
        foreach (IReadOnlyList<Vector2> stroke in strokes)
        {
            if (stroke.Count == 1)
            {
                Stamp(image, ToImage(stroke[0]), brushRadius, ink);
                continue;
            }
            for (int i = 1; i < stroke.Count; i++)
            {
                Vector2 a = ToImage(stroke[i - 1]);
                Vector2 b = ToImage(stroke[i]);
                float length = a.DistanceTo(b);
                int steps = Math.Max(1, (int)MathF.Ceiling(length * 1.5f));
                for (int s = 0; s <= steps; s++)
                {
                    Stamp(image, a.Lerp(b, (float)s / steps), brushRadius, ink);
                }
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static void Stamp(Image image, Vector2 center, float radius, Color color)
    {
        int r = Math.Max(1, (int)MathF.Ceiling(radius));
        int cx = Mathf.RoundToInt(center.X);
        int cy = Mathf.RoundToInt(center.Y);

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                int x = cx + dx;
                int y = cy + dy;
                if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight()) continue;
                image.SetPixel(x, y, color);
            }
        }
    }
}
