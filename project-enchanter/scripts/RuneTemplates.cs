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
    /// "Water": two stacked waves. Each stroke climbs to a sharp peak, dips
    /// through a trough and then rises to end high on the right; the second
    /// wave sits below and slightly to the right of the first.
    /// </summary>
    public static RuneTemplate Water { get; } = new(
        "water",
        new Vector2[]
        {
            new(-1.24f,  0.03f),
            new(-0.99f, -0.43f),
            new(-0.74f, -0.89f),
            new(-0.65f, -0.96f),
            new(-0.48f, -0.68f),
            new(-0.15f, -0.30f),
            new( 0.19f, -0.05f),
            new( 0.32f, -0.02f),
            new( 0.53f, -0.26f),
            new( 0.78f, -0.60f),
            new( 1.05f, -0.90f),
        },
        new Vector2[]
        {
            new(-1.09f,  0.96f),
            new(-0.95f,  0.62f),
            new(-0.74f,  0.16f),
            new(-0.63f, -0.05f),
            new(-0.44f,  0.24f),
            new(-0.11f,  0.58f),
            new( 0.21f,  0.85f),
            new( 0.38f,  0.96f),
            new( 0.61f,  0.75f),
            new( 0.91f,  0.45f),
            new( 1.24f,  0.14f),
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
