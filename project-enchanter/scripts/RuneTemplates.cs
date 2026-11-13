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
    /// "Water": two parallel wavy strokes — an upper wave rising to a crest,
    /// dipping, then rising again, and a similar wave below it.
    /// </summary>
    public static RuneTemplate Water { get; } = new(
        "water",
        new Vector2[]
        {
            new(-1.00f,  0.15f),
            new(-0.85f, -0.10f),
            new(-0.65f, -0.32f),
            new(-0.45f, -0.42f),
            new(-0.25f, -0.35f),
            new(-0.05f, -0.12f),
            new( 0.15f,  0.08f),
            new( 0.35f,  0.15f),
            new( 0.55f,  0.08f),
            new( 0.75f, -0.12f),
            new( 1.00f, -0.42f),
        },
        new Vector2[]
        {
            new(-0.95f,  0.85f),
            new(-0.80f,  0.60f),
            new(-0.60f,  0.40f),
            new(-0.42f,  0.32f),
            new(-0.22f,  0.38f),
            new( 0.00f,  0.60f),
            new( 0.20f,  0.78f),
            new( 0.42f,  0.82f),
            new( 0.62f,  0.72f),
            new( 0.82f,  0.50f),
            new( 1.02f,  0.22f),
        });
}
