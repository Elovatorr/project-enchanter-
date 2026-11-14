using Godot;

namespace ProjectEnchanter;

/// <summary>
/// First-person HUD: a centre reticle, an interaction prompt, and a control hint.
/// Lives on its own CanvasLayer above the 3D scene.
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label _prompt = null!;

    public override void _Ready()
    {
        AddToGroup("hud");
        Layer = 100;

        Control root = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Reticle: two thin bars crossing at screen centre.
        AddBar(root, new Vector2(2f, 16f));
        AddBar(root, new Vector2(16f, 2f));

        _prompt = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _prompt.AddThemeFontSizeOverride("font_size", 24);
        _prompt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _prompt.OffsetLeft = -320f;
        _prompt.OffsetRight = 320f;
        _prompt.OffsetTop = -120f;
        _prompt.OffsetBottom = -84f;
        root.AddChild(_prompt);

        Label hint = new()
        {
            Text = "WASD move · Shift sprint · Space jump · Tab inventory · Esc mouse",
            Modulate = new Color(1f, 1f, 1f, 0.65f),
        };
        hint.AddThemeFontSizeOverride("font_size", 16);
        hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        hint.OffsetLeft = 16f;
        hint.OffsetTop = -34f;
        hint.OffsetBottom = -10f;
        hint.OffsetRight = 620f;
        root.AddChild(hint);
    }

    /// <summary>Shows or clears the interaction prompt under the reticle.</summary>
    public void SetPrompt(string? text)
    {
        string value = text ?? "";
        if (_prompt.Text != value) _prompt.Text = value;
    }

    private static void AddBar(Control root, Vector2 size)
    {
        ColorRect bar = new()
        {
            Color = new Color(1f, 1f, 1f, 0.85f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = size,
        };
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        bar.OffsetLeft = -size.X * 0.5f;
        bar.OffsetRight = size.X * 0.5f;
        bar.OffsetTop = -size.Y * 0.5f;
        bar.OffsetBottom = size.Y * 0.5f;
        root.AddChild(bar);
    }
}
