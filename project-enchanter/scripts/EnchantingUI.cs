using Godot;
using System;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>
/// Full-screen enchanting overlay: hosts the drawing scene on top of the 3D
/// game (with a light dim so the wand close-up stays visible). A successful rune
/// cast closes the overlay and reports the runes and the drawn strokes.
/// </summary>
public partial class EnchantingUI : CanvasLayer
{
    private DrawingSurface? _drawing;
    private Action<string[], IReadOnlyList<IReadOnlyList<Vector2>>>? _onComplete;
    private Action? _onCancel;
    private PlayerController? _player;
    private Hud? _hud;
    private Label? _title;
    private Label? _hint;

    private const string EnchantTitle = "Enchant your wand";
    private const string EditTitle = "Edit your wand's enchantment";
    private const string EnchantHint = "Draw a rune (circle it to combine several), then press Identify.";
    private const string EditHint = "The current rune is loaded — undo strokes with right click, redraw, then press Identify.";

    /// <summary>The embedded drawing canvas (used by tests).</summary>
    public DrawingSurface? Drawing => _drawing;

    public override void _Ready()
    {
        AddToGroup("enchanting_ui");
        Layer = 110;
        Visible = false;

        // Light dim so the wand preview in front of the camera remains visible.
        // MouseFilter must be Ignore: a Stop control would swallow the mouse
        // events the drawing surface needs to capture strokes.
        ColorRect background = new()
        {
            Color = new Color(0.02f, 0.02f, 0.05f, 0.35f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Drawing scene first so labels/buttons draw above it.
        PackedScene scene = GD.Load<PackedScene>("res://main.tscn");
        _drawing = scene.Instantiate<DrawingSurface>();
        AddChild(_drawing);
        _drawing.CastCompleted += OnCastCompleted;

        Label title = new()
        {
            Text = EnchantTitle,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        title.OffsetLeft = -300f;
        title.OffsetRight = 300f;
        title.OffsetTop = 28f;
        title.OffsetBottom = 76f;
        AddChild(title);
        _title = title;

        Label hint = new()
        {
            Text = EnchantHint,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1f, 1f, 1f, 0.75f),
        };
        hint.AddThemeFontSizeOverride("font_size", 18);
        hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        hint.OffsetLeft = -420f;
        hint.OffsetRight = 420f;
        hint.OffsetTop = 84f;
        hint.OffsetBottom = 112f;
        AddChild(hint);
        _hint = hint;

        Button cancel = new() { Text = "Cancel" };
        cancel.Pressed += Cancel;
        cancel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        cancel.OffsetLeft = -136f;
        cancel.OffsetRight = -24f;
        cancel.OffsetTop = 24f;
        cancel.OffsetBottom = 68f;
        AddChild(cancel);
    }

    /// <summary>
    /// Opens the overlay; the callbacks run on cast / cancel. When
    /// <paramref name="initialStrokes"/> is given (editing an existing
    /// enchantment) the canvas starts with those strokes loaded.
    /// </summary>
    public void Open(
        Action<string[], IReadOnlyList<IReadOnlyList<Vector2>>> onComplete,
        Action? onCancel = null,
        IReadOnlyList<IReadOnlyList<Vector2>>? initialStrokes = null)
    {
        _onComplete = onComplete;
        _onCancel = onCancel;
        _player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
        _hud = GetTree().GetFirstNodeInGroup("hud") as Hud;

        bool editing = initialStrokes is { Count: > 0 };
        if (_title is not null) _title.Text = editing ? EditTitle : EnchantTitle;
        if (_hint is not null) _hint.Text = editing ? EditHint : EnchantHint;

        _drawing?.ClearDrawing();
        if (editing) _drawing?.LoadStrokes(initialStrokes!);
        _player?.SetControlsEnabled(false);
        if (_hud is not null) _hud.Visible = false;

        Input.MouseMode = Input.MouseModeEnum.Visible;
        Visible = true;
    }

    /// <summary>Closes the overlay without casting (Cancel button).</summary>
    public void Cancel()
    {
        Action? onCancel = _onCancel;
        CloseCore();
        onCancel?.Invoke();
    }

    /// <summary>Closes the overlay without invoking any callback.</summary>
    public void Close()
    {
        CloseCore();
    }

    private void OnCastCompleted(string[] runes, IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        if (!Visible) return;

        Action<string[], IReadOnlyList<IReadOnlyList<Vector2>>>? callback = _onComplete;
        CloseCore();
        callback?.Invoke(runes, strokes);
    }

    private void CloseCore()
    {
        Visible = false;
        _onComplete = null;
        _onCancel = null;
        _drawing?.ClearDrawing();
        _player?.SetControlsEnabled(true);
        if (_hud is not null) _hud.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }
}
