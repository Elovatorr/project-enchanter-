using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProjectEnchanter;

/// <summary>
/// In-engine recognition self-test. Run with:
///   godot --path . res://tests/recognition_self_test.tscn
/// It instances the real DrawingSurface, synthesizes mouse strokes through the
/// actual input pipeline (Input.ParseInputEvent), and prints PASS/FAIL lines
/// prefixed with "SELFTEST" so MCP debug output can be parsed.
/// </summary>
public partial class RecognitionSelfTest : Node
{
    private DrawingSurface _surface = null!;
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        _surface = new DrawingSurface();
        AddChild(_surface);
        _ = Run();
    }

    private async Task Run()
    {
        try
        {
            await RunTests();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"SELFTEST DRIVER EXCEPTION: {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task RunTests()
    {
        await WaitFrames(15); // let the surface initialize

        // Fire (caret) through various transforms: expect cast + red flash.
        await DrawRune("fire-identity", RuneTemplates.Fire,
            s => TransformStroke(s, 0f, 200f, new Vector2(450, 300)), "fire");
        await DrawRune("fire-translated+small", RuneTemplates.Fire,
            s => TransformStroke(s, 15f, 110f, new Vector2(200, 200)), "fire");
        await DrawRune("fire-rotated-minus-35", RuneTemplates.Fire,
            s => TransformStroke(s, -35f, 260f, new Vector2(600, 350)), "fire");
        await DrawRune("fire-rotated-90", RuneTemplates.Fire,
            s => TransformStroke(s, 90f, 180f, new Vector2(400, 300)), "fire");
        await DrawRune("fire-scaled-down", RuneTemplates.Fire,
            s => TransformStroke(s, 0f, 60f, new Vector2(150, 150)), "fire");

        // Water (arch + feet loops + bar + apex notch): expect cast + blue flash.
        await DrawRune("water-identity", RuneTemplates.Water,
            s => TransformStroke(s, 0f, 200f, new Vector2(450, 300)), "water");
        await DrawRune("water-rotated-25", RuneTemplates.Water,
            s => TransformStroke(s, 25f, 220f, new Vector2(500, 350)), "water");
        await DrawRune("water-scaled-down", RuneTemplates.Water,
            s => TransformStroke(s, 0f, 70f, new Vector2(250, 200)), "water");

        // Density (zigzag + bar + V): expect cast + green flash.
        await DrawRune("density-identity", RuneTemplates.Density,
            s => TransformStroke(s, 0f, 200f, new Vector2(450, 300)), "density");
        await DrawRune("density-rotated-40", RuneTemplates.Density,
            s => TransformStroke(s, 40f, 230f, new Vector2(550, 320)), "density");
        await DrawRune("density-scaled-down", RuneTemplates.Density,
            s => TransformStroke(s, 0f, 70f, new Vector2(250, 200)), "density");

        // Negatives: shapes that must never cast.
        await DrawStrokes("circle", CircleStrokes(), null);
        await DrawStrokes("straight-line", [new List<Vector2> { new(100, 100), new(500, 500) }], null);
        await DrawStrokes("horizontal-wave", WaveStrokes(), null);

        // Multiple runes, each enclosed in a circle.
        await DrawMultiRune("two-circled-runes", TwoCircledRunes(), ["fire", "water"]);
        await DrawMultiRune("circled-unknown-shape", CircledUnknown(), []);
        await DrawMultiRune("circled-fire-only", CircledFireOnly(), ["fire"]);
        await DrawMultiRune("three-circled-runes", ThreeCircledRunes(), ["fire", "water", "density"]);
        await DrawMultiRune("two-handdrawn-circles", TwoHandDrawnCircles(), ["fire", "water"]);

        // The "Draw demo" button's ready-made two-rune case.
        await DemoRunesCase();

        GD.Print($"SELFTEST SUMMARY: passed={_passed} failed={_failed}");
        await WaitFrames(2);
        GetTree().Quit(_failed == 0 ? 0 : 1);
    }

    private static List<List<Vector2>> TwoCircledRunes()
    {
        List<List<Vector2>> strokes = [CircleStroke(new Vector2(300, 300), 140f)];
        strokes.Add(TransformStroke(RuneTemplates.Fire.Strokes[0], 0f, 55f, new Vector2(300, 300)));

        strokes.Add(CircleStroke(new Vector2(800, 300), 140f));
        foreach (IReadOnlyList<Vector2> wave in RuneTemplates.Water.Strokes)
        {
            strokes.Add(TransformStroke(wave, 0f, 55f, new Vector2(800, 300)));
        }
        return strokes;
    }

    private static List<List<Vector2>> CircledUnknown()
    {
        List<Vector2> wave = [];
        for (int i = 0; i <= 30; i++)
        {
            float x = i / 30f * 2f - 1f;
            wave.Add(new Vector2(x * 60f + 400f, 300f + MathF.Sin(x * MathF.Tau) * 15f));
        }
        return [CircleStroke(new Vector2(400, 300), 140f), wave];
    }

    private static List<List<Vector2>> CircledFireOnly()
    {
        return
        [
            CircleStroke(new Vector2(500, 350), 150f),
            TransformStroke(RuneTemplates.Fire.Strokes[0], 0f, 60f, new Vector2(500, 350)),
        ];
    }

    private static List<List<Vector2>> TwoHandDrawnCircles()
    {
        List<List<Vector2>> strokes = [HandDrawnCircle(new Vector2(350, 320), 140f, seed: 7)];
        strokes.Add(TransformStroke(RuneTemplates.Fire.Strokes[0], 0f, 55f, new Vector2(350, 320)));

        strokes.Add(HandDrawnCircle(new Vector2(800, 320), 140f, seed: 8));
        foreach (IReadOnlyList<Vector2> wave in RuneTemplates.Water.Strokes)
        {
            strokes.Add(TransformStroke(wave, 0f, 55f, new Vector2(800, 320)));
        }
        return strokes;
    }

    /// <summary>Simulates a human circle: oval, wobbly, uneven, with an open gap.</summary>
    private static List<Vector2> HandDrawnCircle(Vector2 center, float radius, int seed)
    {
        var rnd = new Random(seed);
        float sweep = MathF.Tau - 22f * MathF.PI / 180f; // ~22° gap
        float phase = (float)rnd.NextDouble() * MathF.Tau;
        List<Vector2> circle = [];
        for (int i = 0; i <= 34; i++)
        {
            float t = (float)i / 34f;
            float angle = t * sweep;
            float r = radius * (1f + 0.04f * MathF.Sin(phase + t * MathF.Tau * 1.5f)
                                + (float)(rnd.NextDouble() - 0.5) * 0.06f);
            circle.Add(center + new Vector2(MathF.Cos(angle) * r * 1.15f, MathF.Sin(angle) * r));
        }
        return circle;
    }

    private static List<List<Vector2>> ThreeCircledRunes()
    {
        List<List<Vector2>> strokes = [];
        (RuneTemplate Rune, Vector2 Center)[] cases =
        [
            (RuneTemplates.Fire, new Vector2(250, 300)),
            (RuneTemplates.Water, new Vector2(700, 300)),
            (RuneTemplates.Density, new Vector2(1150, 300)),
        ];
        foreach ((RuneTemplate rune, Vector2 center) in cases)
        {
            strokes.Add(CircleStroke(center, 150f));
            foreach (IReadOnlyList<Vector2> stroke in rune.Strokes)
            {
                strokes.Add(TransformStroke(stroke, 0f, 55f, center));
            }
        }
        return strokes;
    }

    // ---- Drawing helpers (through the real input pipeline) ----

    private async Task DrawRune(string name, RuneTemplate template, Func<IReadOnlyList<Vector2>, List<Vector2>> transform, string expectedRune)
    {
        List<List<Vector2>> strokes = [];
        foreach (IReadOnlyList<Vector2> templateStroke in template.Strokes)
        {
            strokes.Add(transform(templateStroke));
        }
        await DrawStrokes(name, strokes, expectedRune);
    }

    private async Task DrawStrokes(string name, List<List<Vector2>> strokes, string? expectedRune)
    {
        await DrawRaw(name, strokes, expectedRune is null ? [] : [expectedRune]);
    }

    /// <summary>Draws and identifies, expecting zero or more cast runes.</summary>
    private async Task DrawMultiRune(string name, List<List<Vector2>> strokes, string[] expectedRunes)
    {
        await DrawRaw(name, strokes, expectedRunes);
    }

    private async Task DrawRaw(string name, List<List<Vector2>> strokes, string[] expectedRunes)
    {
        await ClearCanvas();
        int previousCasts = _surface.CastCount;
        int previousFlashes = _surface.FlashCount;
        int previousFlashHistory = _surface.FlashHistory.Count;

        foreach (List<Vector2> stroke in strokes)
        {
            SendMouseButton(stroke[0], true);
            for (int i = 1; i < stroke.Count; i++)
            {
                // Interpolate a few motion events per segment, like a real hand.
                for (int t = 1; t <= 4; t++)
                {
                    SendMouseMotion(stroke[i - 1].Lerp(stroke[i], t / 4f));
                }
            }
            SendMouseButton(stroke[^1], false);
            await WaitFrames(1);
        }

        // Identification is manual now: draw, then press the button.
        _surface.Identify();
        await WaitFrames(3);

        string cast = _surface.LastCast ?? "";
        bool casted = _surface.CastCount > previousCasts;
        bool flashed = _surface.FlashCount > previousFlashes;
        bool expectCast = expectedRunes.Length > 0;

        bool ok = casted == expectCast && flashed == expectCast;
        foreach (string rune in expectedRunes)
        {
            ok = ok && cast.Contains(rune, StringComparison.OrdinalIgnoreCase);
        }
        if (expectCast)
        {
            ok = ok && _surface.FlashCount - previousFlashes == expectedRunes.Length;

            // Each rune flashes in detection order: verify the whole sequence.
            List<Color> expectedColors = expectedRunes.Select(FlashColorForRune).ToList();
            List<Color> actualColors = _surface.FlashHistory.Skip(previousFlashHistory).ToList();
            ok = ok && actualColors.Count == expectedColors.Count
                    && actualColors.Zip(expectedColors).All(pair => pair.First.IsEqualApprox(pair.Second));
        }

        if (ok) _passed++; else _failed++;
        GD.Print($"SELFTEST {(ok ? "PASS" : "FAIL")} [{name}] casted={casted} flashes={_surface.FlashCount - previousFlashes} " +
                 $"colors=[{string.Join(", ", _surface.FlashHistory.Skip(previousFlashHistory).Select(c => c.ToHtml()))}] " +
                 $"lastCast='{cast}' candidate='{_surface.Candidate}'");
    }

    private Color FlashColorForRune(string rune) => rune switch
    {
        "water" => _surface.WaterFlashColor,
        "density" => _surface.DensityFlashColor,
        _ => _surface.FireFlashColor,
    };

    private async Task DemoRunesCase()
    {
        await ClearCanvas();
        int previousCasts = _surface.CastCount;
        int previousFlashes = _surface.FlashCount;

        _surface.DrawDemoRunes();
        await WaitFrames(2);
        _surface.Identify();
        await WaitFrames(3);

        string cast = _surface.LastCast ?? "";
        bool ok = _surface.CastCount - previousCasts == 3
                  && _surface.FlashCount - previousFlashes == 3
                  && cast.Contains("fire", StringComparison.OrdinalIgnoreCase)
                  && cast.Contains("water", StringComparison.OrdinalIgnoreCase)
                  && cast.Contains("density", StringComparison.OrdinalIgnoreCase);

        if (ok) _passed++; else _failed++;
        GD.Print($"SELFTEST {(ok ? "PASS" : "FAIL")} [demo-runes] casted={_surface.CastCount - previousCasts} " +
                 $"flashes={_surface.FlashCount - previousFlashes} lastCast='{cast}'");
    }

    private async Task ClearCanvas()
    {
        // Right-clicks undo strokes one at a time; enough presses for the
        // largest multi-rune case.
        for (int i = 0; i < 8; i++)
        {
            SendMouseButton(new Vector2(10, 10), true, MouseButton.Right);
            SendMouseButton(new Vector2(10, 10), false, MouseButton.Right);
            await WaitFrames(1);
        }
        await WaitFrames(1);
    }

    private static List<Vector2> CircleStroke(Vector2 center, float radius, int points = 48)
    {
        List<Vector2> circle = [];
        for (int i = 0; i <= points; i++)
        {
            float angle = i * MathF.Tau / points;
            circle.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
        return circle;
    }

    private void SendMouseButton(Vector2 position, bool pressed, MouseButton button = MouseButton.Left)
    {
        _surface._UnhandledInput(new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = pressed,
            Position = position,
        });
    }

    private void SendMouseMotion(Vector2 position)
    {
        _surface._UnhandledInput(new InputEventMouseMotion
        {
            Position = position,
        });
    }

    // ---- Test shape builders ----

    private static List<Vector2> TransformStroke(IReadOnlyList<Vector2> stroke, float degrees, float scale, Vector2 offset)
    {
        List<Vector2> result = [];
        foreach (Vector2 p in stroke)
        {
            result.Add(p.Rotated(degrees * MathF.PI / 180f) * scale + offset);
        }
        return result;
    }

    private static List<List<Vector2>> CircleStrokes()
    {
        List<Vector2> top = [], bottom = [];
        for (int i = 0; i <= 12; i++)
        {
            float angle = MathF.PI + i * MathF.PI / 12f;
            top.Add(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 120f + new Vector2(400, 300));
            angle = i * MathF.PI / 12f;
            bottom.Add(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 120f + new Vector2(400, 300));
        }
        return [top, bottom];
    }

    private static List<List<Vector2>> WaveStrokes()
    {
        List<Vector2> wave = [];
        for (int i = 0; i <= 24; i++)
        {
            float x = 100f + i * 20f;
            wave.Add(new Vector2(x, 300f + MathF.Sin(i * MathF.Tau / 24f * 2f) * 40f));
        }
        return [wave];
    }

    private async Task WaitFrames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
