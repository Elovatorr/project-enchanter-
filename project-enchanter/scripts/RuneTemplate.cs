using Godot;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>A named rune template: the reference strokes the player should draw.</summary>
public sealed class RuneTemplate
{
    public string Name { get; }
    public IReadOnlyList<IReadOnlyList<Vector2>> Strokes { get; }

    public RuneTemplate(string name, params Vector2[][] strokes)
    {
        Name = name;
        Strokes = strokes;
    }
}
