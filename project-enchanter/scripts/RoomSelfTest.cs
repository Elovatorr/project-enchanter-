using Godot;
using System;
using System.Threading.Tasks;

namespace ProjectEnchanter;

/// <summary>
/// In-engine test for the 3D room + enchanting flow:
///   table interaction opens the drawing UI → drawing a rune enchants the wand
///   → UI closes and controls resume → wand can be picked up into the hand.
/// Run with: godot --path . res://tests/room_self_test.tscn
/// </summary>
public partial class RoomSelfTest : Node
{
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
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
            GD.PrintErr($"ROOMSELFTEST DRIVER EXCEPTION: {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task RunTests()
    {
        PackedScene scene = GD.Load<PackedScene>("res://scenes/room.tscn");
        Node3D room = scene.Instantiate<Node3D>();
        AddChild(room);
        await WaitFrames(12);

        var player = room.GetNode<PlayerController>("Player");
        var table = room.GetNode<EnchantingTable>("EnchantingTable");
        var wand = room.GetNode<Wand>("Wand");
        var ui = room.GetNode<EnchantingUI>("EnchantingUI");

        Check("hud-exists", GetTree().GetFirstNodeInGroup("hud") is Hud);
        Check("table-prompts-enchant", table.InteractionPrompt.Contains("Enchant wand"), table.InteractionPrompt);
        Check("wand-not-enchanted", !wand.Enchanted);
        Check("drawing-ui-hidden-at-start", ui.Drawing is not null && !ui.Drawing.IsUiVisible);

        // Interacting opens the enchanting overlay and pauses controls.
        table.Interact(player);
        await WaitFrames(3);
        Check("ui-opens", ui.Visible);
        Check("controls-paused", !player.ControlsEnabled);

        // The wand is presented up close in front of the camera.
        Check("wand-closeup", wand.InCloseUp && wand.GetParent() == player.GetNode("Camera3D"));
        Check("wand-enlarged", wand.Scale.X > 1.2f, $"scale={wand.Scale.X:0.00}");

        // Mouse input must reach the drawing surface through the overlay (the
        // dim background must not swallow events).
        Check("drawing-ui-visible-while-enchanting", ui.Drawing!.IsUiVisible);
        int strokesBefore = ui.Drawing.StrokeCount;
        SendSyntheticStroke();
        await WaitFrames(3);
        Check("drawing-receives-mouse-input", ui.Drawing.StrokeCount > strokesBefore,
            $"strokes {strokesBefore} -> {ui.Drawing.StrokeCount}");

        // Draw demo runes in the embedded drawing scene and identify them.
        Check("drawing-embedded", ui.Drawing is not null);
        ui.Drawing.DrawDemoRunes();
        await WaitFrames(2);

        // Click the Identify button through the real input pipeline.
        Rect2 button = ui.Drawing.IdentifyButtonRect;
        Check("identify-button-visible", button.Size.X > 0f && button.Size.Y > 0f, $"{button}");
        int castsBeforeClick = ui.Drawing.CastCount;
        ClickAt(button.GetCenter());
        await WaitFrames(4);
        Check("identify-button-clicks", ui.Drawing.CastCount > castsBeforeClick,
            $"casts {castsBeforeClick} -> {ui.Drawing.CastCount}");
        await WaitFrames(4);

        Check("wand-enchanted", wand.Enchanted, $"element='{wand.Element}'");
        Check("wand-element-fire", wand.Element == "fire", wand.Element);
        Check("ui-closes", !ui.Visible);
        Check("controls-resume", player.ControlsEnabled);
        Check("drawing-ui-hidden-after-close", !ui.Drawing.IsUiVisible);

        // The wand returns to the table with the rune etched on its top face.
        Check("wand-restored", !wand.InCloseUp && wand.GetParent() == room);
        MeshInstance3D decal = wand.GetNode<MeshInstance3D>("RuneDecal");
        Check("rune-decal-visible", decal.Visible);
        Check("rune-decal-has-texture",
            (decal.MaterialOverride as StandardMaterial3D)?.AlbedoTexture is not null);

        // Pick the wand up: attaches to the camera.
        wand.Interact(player);
        await WaitFrames(2);
        Check("wand-held", wand.Held);
        Check("wand-attached-to-camera", wand.GetParent() == player.GetNode("Camera3D"));
        Check("wand-no-prompt", wand.InteractionPrompt.Length == 0, wand.InteractionPrompt);

        GD.Print($"ROOMSELFTEST SUMMARY: passed={_passed} failed={_failed}");
        await WaitFrames(2);
        GetTree().Quit(_failed == 0 ? 0 : 1);
    }

    private void Check(string name, bool ok, string? detail = null)
    {
        if (ok) _passed++; else _failed++;
        string extra = detail is null ? "" : $" detail='{detail}'";
        GD.Print($"ROOMSELFTEST {(ok ? "PASS" : "FAIL")} [{name}]{extra}");
    }

    /// <summary>Sends a short stroke via the real input pipeline.</summary>
    private static void SendSyntheticStroke()
    {
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = new Vector2(300f, 300f),
        });
        for (int i = 1; i <= 10; i++)
        {
            Input.ParseInputEvent(new InputEventMouseMotion
            {
                Position = new Vector2(300f + i * 12f, 300f + i * 5f),
            });
        }
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = new Vector2(420f, 350f),
        });
    }

    /// <summary>Sends a left click at a viewport position through the real input pipeline.</summary>
    private static void ClickAt(Vector2 position)
    {
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = position,
        });
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = position,
        });
    }

    private async Task WaitFrames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
