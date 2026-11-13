using Godot;
using System;
using System.Collections.Generic;
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

        // Water (two waves): expect cast + blue flash.
        await DrawRune("water-identity", RuneTemplates.Water,
            s => TransformStroke(s, 0f, 200f, new Vector2(450, 300)), "water");
        await DrawRune("water-rotated-25", RuneTemplates.Water,
            s => TransformStroke(s, 25f, 220f, new Vector2(500, 350)), "water");
        await DrawRune("water-scaled-down", RuneTemplates.Water,
            s => TransformStroke(s, 0f, 70f, new Vector2(250, 200)), "water");

        // Negatives: shapes that must never cast.
        await DrawStrokes("circle", CircleStrokes(), null);
        await DrawStrokes("straight-line", [new List<Vector2> { new(100, 100), new(500, 500) }], null);
        await DrawStrokes("horizontal-wave", WaveStrokes(), null);

        GD.Print($"SELFTEST SUMMARY: passed={_passed} failed={_failed}");
        await WaitFrames(2);
        GetTree().Quit(_failed == 0 ? 0 : 1);
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
        await ClearCanvas();
        int previousCasts = _surface.CastCount;
        int previousFlashes = _surface.FlashCount;

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
        bool ok = casted == (expectedRune is not null)
                  && flashed == (expectedRune is not null);

        if (expectedRune is not null)
        {
            bool rightRune = cast.StartsWith(expectedRune, StringComparison.OrdinalIgnoreCase);
            Color expectedColor = expectedRune == "water" ? _surface.WaterFlashColor : _surface.FireFlashColor;
            bool rightColor = _surface.LastFlashColor.IsEqualApprox(expectedColor);
            ok = ok && rightRune && rightColor;
        }

        if (ok) _passed++; else _failed++;
        GD.Print($"SELFTEST {(ok ? "PASS" : "FAIL")} [{name}] casted={casted} flashed={flashed} " +
                 $"color={_surface.LastFlashColor.ToHtml()} lastCast='{cast}' candidate='{_surface.Candidate}'");
    }

    private async Task ClearCanvas()
    {
        // Right-clicks undo strokes one at a time; enough presses for any test case.
        for (int i = 0; i < 3; i++)
        {
            SendMouseButton(new Vector2(10, 10), true, MouseButton.Right);
            SendMouseButton(new Vector2(10, 10), false, MouseButton.Right);
            await WaitFrames(1);
        }
        await WaitFrames(1);
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
