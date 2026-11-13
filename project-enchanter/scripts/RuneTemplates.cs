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
}
