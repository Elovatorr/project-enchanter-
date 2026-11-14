using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
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
        ui.Drawing!.DrawDemoRunes();
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

        // ---- Shooting via the real left-click pipeline ----
        Camera3D camera = player.GetNode<Camera3D>("Camera3D");
        Vector3 aim = -camera.GlobalTransform.Basis.Z;
        int projectilesBefore = Projectiles().Count;
        ClickAt(new Vector2(600f, 400f)); // a click anywhere fires the held wand
        await WaitFrames(2);
        Godot.Collections.Array<Node> shots = Projectiles();
        Check("fire-shot-spawns-one", shots.Count == 1, $"projectiles {projectilesBefore} -> {shots.Count}");
        if (shots.Count > 0)
        {
            Projectile fireball = (Projectile)shots[0];
            MeshInstance3D shotMesh = (MeshInstance3D)fireball.GetChild(0);
            Check("fire-shot-is-sphere", shotMesh.Mesh is SphereMesh, shotMesh.Mesh?.GetType().Name ?? "null");
            Check("fire-shot-is-red",
                (shotMesh.MaterialOverride as StandardMaterial3D)?.AlbedoColor == WandShotFactory.ColorFor("fire"),
                (shotMesh.MaterialOverride as StandardMaterial3D)?.AlbedoColor.ToString() ?? "null");
            Check("fire-shot-travels-forward", fireball.Direction.Dot(aim) > 0.99f,
                $"dot={fireball.Direction.Dot(aim):0.000}");
            Vector3 spawnedAt = fireball.GlobalPosition;
            await WaitFrames(30);
            Vector3 moved = fireball.GlobalPosition - spawnedAt;
            Check("fire-shot-moves-forward", moved.Length() > 0.5f && moved.Normalized().Dot(aim) > 0.99f,
                $"moved {moved.Length():0.0} m, dot={moved.Normalized().Dot(aim):0.000}");
        }

        // Water: three blue jets (the fireball from the previous shot is still alive).
        int beforeWater = Projectiles().Count;
        wand.Enchant("water", new[] { new[] { new Vector2(0, 0), new Vector2(1, 1) } });
        wand.Shoot(player);
        shots = Projectiles();
        List<Node> waterShots = shots.Skip(beforeWater).ToList();
        Check("water-shot-spawns-three", waterShots.Count == 3, $"projectiles={shots.Count}");
        Check("water-shots-are-blue", waterShots.All(n =>
            ((MeshInstance3D)n.GetChild(0)).MaterialOverride is StandardMaterial3D m
            && m.AlbedoColor == WandShotFactory.ColorFor("water")));
        Check("water-jets-arc-upward", waterShots.All(n => ((Projectile)n).Direction.Y > 0f));
        Check("water-jets-fly-forward", waterShots.All(n => ((Projectile)n).Direction.Dot(aim) > 0.8f));

        // Density: three green cones.
        int beforeDensity = Projectiles().Count;
        wand.Enchant("density", new[] { new[] { new Vector2(0, 0), new Vector2(1, 1) } });
        wand.Shoot(player);
        shots = Projectiles();
        List<Node> densityShots = shots.Skip(beforeDensity).ToList();
        Check("density-shot-spawns-three-cones", densityShots.Count == 3, $"projectiles={shots.Count}");
        Check("density-shots-are-cones", densityShots.All(n =>
            ((MeshInstance3D)n.GetChild(0)).Mesh is CylinderMesh c && c.TopRadius == 0f));
        Check("density-shots-are-green", densityShots.All(n =>
            ((MeshInstance3D)n.GetChild(0)).MaterialOverride is StandardMaterial3D m
            && m.AlbedoColor == WandShotFactory.ColorFor("density")));
        Check("density-shots-fly-forward", densityShots.All(n => ((Projectile)n).Direction.Dot(aim) > 0.8f));

        // All projectiles free themselves once their lifetime elapses.
        await ToSignal(GetTree().CreateTimer(2.7f), SceneTreeTimer.SignalName.Timeout);
        Check("shots-despawn-after-lifetime", Projectiles().Count == 0, $"projectiles={Projectiles().Count}");

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

    /// <summary>Live shot projectiles (spawned by Wand.Shoot).</summary>
    private Godot.Collections.Array<Node> Projectiles() => GetTree().GetNodesInGroup("projectile");

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
