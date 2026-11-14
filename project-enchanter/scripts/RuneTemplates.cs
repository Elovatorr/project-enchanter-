using Godot;

namespace ProjectEnchanter;

/// <summary>
/// Built-in rune templates. Coordinates are in an arbitrary unit space — the
/// recognizer normalizes position, scale and rotation, so only the shape matters.
/// </summary>
public static class RuneTemplates
{
    /// <summary>
    /// "Fire": a caret — one stroke that rises from the lower left to an apex,
    /// then falls to the lower right, with gently curved legs.
    /// </summary>
    public static RuneTemplate Fire { get; } = new(
        "fire",
        new Vector2[]
        {
            new(-0.75f,  0.85f),
            new(-0.60f,  0.45f),
            new(-0.40f, -0.05f),
            new(-0.15f, -0.60f),
            new( 0.02f, -0.98f),
            new( 0.25f, -0.45f),
            new( 0.45f,  0.10f),
            new( 0.62f,  0.50f),
            new( 0.80f,  0.90f),
        });

    /// <summary>
    /// "Water": a tall pointed arch whose two legs curl into a loop at each
    /// foot, crossed by a horizontal bar and with a short notch under the apex.
    /// Each loop is part of its leg's stroke, so it is never mistaken for a
    /// rune-enclosing circle.
    /// </summary>
    public static RuneTemplate Water { get; } = new(
        "water",
        new Vector2[]
        {
            new(-0.06f, -0.99f),
            new(-0.13f, -0.94f),
            new(-0.20f, -0.83f),
            new(-0.27f, -0.66f),
            new(-0.33f, -0.45f),
            new(-0.38f, -0.20f),
            new(-0.41f,  0.05f),
            new(-0.43f,  0.25f),
            new(-0.44f,  0.42f),
            new(-0.60f,  0.48f),
            new(-0.65f,  0.62f),
            new(-0.63f,  0.77f),
            new(-0.52f,  0.86f),
            new(-0.38f,  0.88f),
            new(-0.27f,  0.80f),
            new(-0.24f,  0.66f),
            new(-0.31f,  0.52f),
            new(-0.40f,  0.44f),
            new(-0.44f,  0.42f),
        },
        new Vector2[]
        {
            new( 0.04f, -0.98f),
            new( 0.11f, -0.91f),
            new( 0.17f, -0.78f),
            new( 0.22f, -0.60f),
            new( 0.26f, -0.38f),
            new( 0.28f, -0.15f),
            new( 0.28f,  0.08f),
            new( 0.27f,  0.26f),
            new( 0.28f,  0.36f),
            new( 0.18f,  0.46f),
            new( 0.13f,  0.60f),
            new( 0.17f,  0.74f),
            new( 0.28f,  0.83f),
            new( 0.42f,  0.85f),
            new( 0.55f,  0.80f),
            new( 0.63f,  0.67f),
            new( 0.63f,  0.53f),
            new( 0.53f,  0.41f),
            new( 0.40f,  0.34f),
            new( 0.30f,  0.34f),
            new( 0.28f,  0.36f),
        },
        new Vector2[]
        {
            new(-0.52f, -0.05f),
            new(-0.02f, -0.10f),
            new( 0.52f, -0.08f),
        },
        new Vector2[]
        {
            new( 0.00f, -0.88f),
            new(-0.02f, -0.70f),
            new(-0.03f, -0.52f),
        });

    /// <summary>
    /// "Density": a zigzag double-peak stroke on top, a horizontal bar beneath
    /// it, and a downward V below the bar.
    /// </summary>
    public static RuneTemplate Density { get; } = new(
        "density",
        new Vector2[]
        {
            new(-0.75f, -0.20f),
            new(-0.35f, -0.85f),
            new(-0.05f, -0.35f),
            new( 0.25f, -0.90f),
            new( 0.55f, -0.20f),
        },
        new Vector2[]
        {
            new(-0.85f,  0.06f),
            new( 0.00f,  0.02f),
            new( 0.85f,  0.06f),
        },
        new Vector2[]
        {
            new(-0.35f,  0.55f),
            new( 0.05f,  1.05f),
            new( 0.50f,  0.50f),
        });
}
