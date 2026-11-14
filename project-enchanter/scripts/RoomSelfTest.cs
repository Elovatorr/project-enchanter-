using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProjectEnchanter;

/// <summary>
/// In-engine test for the room + inventory + enchanting flow:
///   Tab opens the inventory → Hold equips the wand → the Enchant tab opens the
///   drawing UI → drawing a rune enchants the wand → the inventory reopens and
///   controls resume → the held wand shoots.
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
        var wand = room.GetNode<Wand>("Wand");
        var ui = room.GetNode<EnchantingUI>("EnchantingUI");
        var inventory = room.GetNode<InventoryUI>("InventoryUI");
        Camera3D camera = player.GetNode<Camera3D>("Camera3D");

        Check("hud-exists", GetTree().GetFirstNodeInGroup("hud") is Hud);
        Check("inventory-hidden-at-start", !inventory.IsOpen);
        Check("wand-starts-stored", !wand.Held && !wand.Enchanted && !wand.Visible && wand.Stored);
        Check("drawing-ui-hidden-at-start", ui.Drawing is not null && !ui.Drawing.IsUiVisible);

        // Tab opens the inventory through the real input pipeline.
        PressTab();
        await WaitFrames(3);
        Check("tab-opens-inventory", inventory.IsOpen);
        Check("inventory-pauses-controls", !player.ControlsEnabled);
        Check("inventory-lists-wand", inventory.ItemCount == 1, $"items={inventory.ItemCount}");
        Check("inventory-selects-wand", inventory.SelectedWand == wand);
        Check("items-tab-default", inventory.ActiveTab == 0, $"tab={inventory.ActiveTab}");

        // "Hold in hand" equips the wand (real button click).
        Rect2 holdButton = inventory.HoldButtonRect;
        Check("hold-button-visible", holdButton.Size.X > 0f && holdButton.Size.Y > 0f, $"{holdButton}");
        ClickAt(holdButton.GetCenter());
        await WaitFrames(3);
        Check("hold-equips-wand", wand.Held && wand.GetParent() == camera);
        Check("held-wand-visible", wand.Visible);

        // Enchant tab → "Open enchanting" → drawing overlay.
        Rect2 enchantTab = inventory.TabButtonRect(1);
        Check("enchant-tab-visible", enchantTab.Size.X > 0f && enchantTab.Size.Y > 0f, $"{enchantTab}");
        ClickAt(enchantTab.GetCenter());
        await WaitFrames(2);
        Check("enchant-tab-selected", inventory.ActiveTab == 1);
        Rect2 enchantButton = inventory.EnchantButtonRect;
        Check("enchant-button-visible", enchantButton.Size.X > 0f && enchantButton.Size.Y > 0f, $"{enchantButton}");
        ClickAt(enchantButton.GetCenter());
        await WaitFrames(3);
        Check("enchanting-ui-opens", ui.Visible && !inventory.IsOpen);
        Check("enchanting-pauses-controls", !player.ControlsEnabled);
        Check("enchanting-closeup", wand.InCloseUp && wand.GetParent() == camera);
        Check("enchanting-wand-enlarged", wand.Scale.X > 1.2f, $"scale={wand.Scale.X:0.00}");
        Check("enchanting-canvas-empty", ui.Drawing!.StrokeCount == 0, $"strokes={ui.Drawing.StrokeCount}");

        // Tab must be ignored while the enchanting overlay is up.
        PressTab();
        await WaitFrames(2);
        Check("tab-ignored-while-enchanting", !inventory.IsOpen && ui.Visible);

        // Mouse input must reach the drawing surface through the overlay (the
        // dim background must not swallow events).
        Check("drawing-ui-visible-while-enchanting", ui.Drawing.IsUiVisible);
        int strokesBefore = ui.Drawing.StrokeCount;
        SendSyntheticStroke();
        await WaitFrames(3);
        Check("drawing-receives-mouse-input", ui.Drawing.StrokeCount > strokesBefore,
            $"strokes {strokesBefore} -> {ui.Drawing.StrokeCount}");

        // Draw demo runes in the embedded drawing scene and identify them.
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
        Check("enchanting-ui-closes", !ui.Visible);
        Check("inventory-reopens-after-enchant", inventory.IsOpen);
        Check("inventory-still-paused", !player.ControlsEnabled);
        Check("wand-returns-to-hand", wand.Held && !wand.InCloseUp && wand.GetParent() == camera);
        MeshInstance3D decal = wand.GetNode<MeshInstance3D>("RuneDecal");
        Check("rune-decal-visible", decal.Visible);
        Check("rune-decal-has-texture",
            (decal.MaterialOverride as StandardMaterial3D)?.AlbedoTexture is not null);

        // Editing: reopen the enchant tab and cancel keeps the rune.
        ClickAt(inventory.TabButtonRect(1).GetCenter());
        await WaitFrames(2);
        ClickAt(inventory.EnchantButtonRect.GetCenter());
        await WaitFrames(3);
        Check("edit-ui-opens", ui.Visible && wand.InCloseUp);
        Check("edit-canvas-prefilled",
            ui.Drawing is not null && ui.Drawing.StrokeCount == wand.CurrentStrokes.Count,
            $"canvas={ui.Drawing?.StrokeCount}, wand={wand.CurrentStrokes.Count}");
        ui.Cancel();
        await WaitFrames(3);
        Check("edit-cancel-keeps-enchantment", wand.Element == "fire" && wand.Held, wand.Element);
        Check("edit-cancel-reopens-inventory", inventory.IsOpen && !ui.Visible);

        // Close the inventory: controls resume.
        PressTab();
        await WaitFrames(2);
        Check("tab-closes-inventory", !inventory.IsOpen && player.ControlsEnabled && !ui.Visible);

        // ---- Shooting via the real left-click pipeline ----
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

        // Water: a straight two-layer cylinder jet (the fireball from the previous shot is still alive).
        int beforeWater = Projectiles().Count;
        wand.Enchant("water", new[] { new[] { new Vector2(0, 0), new Vector2(1, 1) } });
        Vector3 waterAim = -camera.GlobalTransform.Basis.Z;
        wand.Shoot(player);
        shots = Projectiles();
        List<Node> waterShots = shots.Skip(beforeWater).ToList();
        Check("water-shot-spawns-jet", waterShots.Count == 2, $"projectiles={shots.Count}");
        Check("water-shots-are-cylinders", waterShots.All(n =>
            ((MeshInstance3D)n.GetChild(0)).Mesh is CylinderMesh c && c.Height > 0f));
        Check("water-shots-are-blue", waterShots.All(n =>
            ((MeshInstance3D)n.GetChild(0)).MaterialOverride is StandardMaterial3D m
            && SameRgb(m.AlbedoColor, WandShotFactory.ColorFor("water"))));
        Check("water-jets-look-translucent", waterShots.Any(n =>
            ((MeshInstance3D)n.GetChild(0)).MaterialOverride is StandardMaterial3D m
            && m.Transparency == BaseMaterial3D.TransparencyEnum.Alpha));
        Check("water-jets-fly-forward", waterShots.All(n => ((Projectile)n).Direction.Dot(waterAim) > 0.8f),
            $"aim={waterAim}, dots=[{string.Join(", ", waterShots.Select(n => ((Projectile)n).Direction.Dot(waterAim).ToString("0.00")))}]");

        // Density: three green cones.
        int beforeDensity = Projectiles().Count;
        wand.Enchant("density", new[] { new[] { new Vector2(0, 0), new Vector2(1, 1) } });
        Vector3 densityAim = -camera.GlobalTransform.Basis.Z;
        wand.Shoot(player);
        shots = Projectiles();
        List<Node> densityShots = shots.Skip(beforeDensity).ToList();
        Check("density-shot-spawns-three-cones", densityShots.Count == 3, $"projectiles={shots.Count}");
        Check("density-shots-are-cones", densityShots.All(n =>
            ((MeshInstance3D)n.GetChild(0)).Mesh is CylinderMesh c && c.TopRadius == 0f));
        Check("density-shots-are-green", densityShots.All(n =>
            ((MeshInstance3D)n.GetChild(0)).MaterialOverride is StandardMaterial3D m
            && m.AlbedoColor == WandShotFactory.ColorFor("density")));
        Check("density-shots-fly-forward", densityShots.All(n => ((Projectile)n).Direction.Dot(densityAim) > 0.8f),
            $"aim={densityAim}, dots=[{string.Join(", ", densityShots.Select(n => ((Projectile)n).Direction.Dot(densityAim).ToString("0.00")))}]");

        // All projectiles free themselves once their lifetime elapses.
        await ToSignal(GetTree().CreateTimer(2.7f), SceneTreeTimer.SignalName.Timeout);
        Check("shots-despawn-after-lifetime", Projectiles().Count == 0, $"projectiles={Projectiles().Count}");

        // ---- Re-enchant from the inventory while still holding the wand ----
        PressTab();
        await WaitFrames(2);
        ClickAt(inventory.TabButtonRect(1).GetCenter());
        await WaitFrames(2);
        ClickAt(inventory.EnchantButtonRect.GetCenter());
        await WaitFrames(3);
        Check("edit-again-ui-opens", ui.Visible && wand.InCloseUp);
        ui.Drawing!.LoadStrokes(ScaledTemplate(RuneTemplates.Water, 90f, new Vector2(560f, 360f)));
        await WaitFrames(1);
        ui.Drawing.Identify();
        await WaitFrames(4);
        Check("edit-applies-new-element", wand.Element == "water", wand.Element);
        Check("edit-closes-and-returns-to-hand",
            !ui.Visible && !wand.InCloseUp && wand.Held && wand.GetParent() == camera);
        Check("edit-reopens-inventory", inventory.IsOpen);
        Check("edit-keeps-new-strokes", wand.CurrentStrokes.Count == RuneTemplates.Water.Strokes.Count,
            $"strokes={wand.CurrentStrokes.Count}");

        PressTab();
        await WaitFrames(2);
        Check("inventory-closes-cleanly", !inventory.IsOpen && player.ControlsEnabled && !ui.Visible);

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

    /// <summary>Compares RGB only (materials may have a different alpha).</summary>
    private static bool SameRgb(Color a, Color b)
        => Mathf.IsEqualApprox(a.R, b.R) && Mathf.IsEqualApprox(a.G, b.G) && Mathf.IsEqualApprox(a.B, b.B);

    /// <summary>Copies a rune template's strokes scaled and offset into canvas space.</summary>
    private static List<List<Vector2>> ScaledTemplate(RuneTemplate rune, float scale, Vector2 offset)
    {
        List<List<Vector2>> strokes = [];
        foreach (IReadOnlyList<Vector2> stroke in rune.Strokes)
        {
            List<Vector2> copy = [];
            foreach (Vector2 p in stroke) copy.Add(p * scale + offset);
            strokes.Add(copy);
        }
        return strokes;
    }

    /// <summary>Presses and releases the Tab key through the real input pipeline.</summary>
    private static void PressTab()
    {
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = Key.Tab,
            PhysicalKeycode = Key.Tab,
            Pressed = true,
        });
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = Key.Tab,
            PhysicalKeycode = Key.Tab,
            Pressed = false,
        });
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
