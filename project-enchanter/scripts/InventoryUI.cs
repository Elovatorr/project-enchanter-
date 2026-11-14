using Godot;
using System;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>
/// The player's inventory: opened with Tab, it lists carried items (wands) and
/// has an Enchant tab that opens the drawing overlay for the selected wand.
/// Replaces the old enchanting-table interaction.
/// </summary>
public partial class InventoryUI : CanvasLayer
{
    private readonly InventoryModel _model = new();
    private readonly List<Wand> _wands = [];
    private readonly List<Button> _rowButtons = [];

    private PlayerController? _player;
    private Hud? _hud;
    private EnchantingUI? _enchantingUi;

    private Button _itemsTab = null!;
    private Button _enchantTab = null!;
    private Control _itemsPage = null!;
    private Control _enchantPage = null!;
    private VBoxContainer _itemList = null!;
    private Label _selectedLabel = null!;
    private Button _holdButton = null!;
    private Label _enchantLabel = null!;
    private Button _enchantButton = null!;
    private int _tabIndex;

    public bool IsOpen => Visible;
    public int ActiveTab => _tabIndex;
    public int ItemCount => _wands.Count;

    public Wand? SelectedWand =>
        _model.SelectedIndex >= 0 && _model.SelectedIndex < _wands.Count
            ? _wands[_model.SelectedIndex]
            : null;

    /// <summary>Global rects exposed for in-engine tests.</summary>
    public Rect2 HoldButtonRect => _holdButton?.GetGlobalRect() ?? new Rect2();
    public Rect2 EnchantButtonRect => _enchantButton?.GetGlobalRect() ?? new Rect2();
    public Rect2 TabButtonRect(int index) => (index == 1 ? _enchantTab : _itemsTab)?.GetGlobalRect() ?? new Rect2();

    public override void _Ready()
    {
        AddToGroup("inventory");
        Layer = 105;
        Visible = false;

        ColorRect dim = new()
        {
            Color = new Color(0.02f, 0.02f, 0.05f, 0.72f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        PanelContainer panel = new();
        AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -300f;
        panel.OffsetRight = 300f;
        panel.OffsetTop = -230f;
        panel.OffsetBottom = 230f;

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 18);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        panel.AddChild(margin);

        VBoxContainer root = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        Label title = new() { Text = "Inventory", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        root.AddChild(title);

        HBoxContainer tabs = new() { Alignment = BoxContainer.AlignmentMode.Center };
        tabs.AddThemeConstantOverride("separation", 12);
        root.AddChild(tabs);

        _itemsTab = new Button { Text = "Items", CustomMinimumSize = new Vector2(150f, 40f) };
        _itemsTab.Pressed += () => ShowTab(0);
        tabs.AddChild(_itemsTab);

        _enchantTab = new Button { Text = "Enchant", CustomMinimumSize = new Vector2(150f, 40f) };
        _enchantTab.Pressed += () => ShowTab(1);
        tabs.AddChild(_enchantTab);

        _itemsPage = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        ((VBoxContainer)_itemsPage).AddThemeConstantOverride("separation", 8);
        root.AddChild(_itemsPage);

        _itemList = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _itemList.AddThemeConstantOverride("separation", 6);
        _itemsPage.AddChild(_itemList);

        _selectedLabel = new Label { Text = "No wand in inventory" };
        _itemsPage.AddChild(_selectedLabel);

        _holdButton = new Button { Text = "Hold in hand", CustomMinimumSize = new Vector2(0f, 40f) };
        _holdButton.Pressed += ToggleHold;
        _itemsPage.AddChild(_holdButton);

        _enchantPage = new VBoxContainer { Visible = false };
        ((VBoxContainer)_enchantPage).AddThemeConstantOverride("separation", 10);
        root.AddChild(_enchantPage);

        _enchantLabel = new Label { Text = "Selected: none" };
        _enchantPage.AddChild(_enchantLabel);

        _enchantButton = new Button { Text = "Open enchanting", CustomMinimumSize = new Vector2(0f, 44f) };
        _enchantButton.Pressed += BeginEnchanting;
        _enchantPage.AddChild(_enchantButton);

        Label hint = new()
        {
            Text = "Draw a rune to set or edit the selected wand's enchantment.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1f, 1f, 1f, 0.7f),
        };
        _enchantPage.AddChild(hint);

        Button close = new() { Text = "Close (Tab)", CustomMinimumSize = new Vector2(0f, 36f) };
        close.Pressed += Close;
        root.AddChild(close);

        ShowTab(0);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Keycode: Key.Tab }) return;
        if (IsEnchantingOpen) return;
        Toggle();
        GetViewport().SetInputAsHandled();
    }

    private bool IsEnchantingOpen
    {
        get
        {
            _enchantingUi ??= GetTree().GetFirstNodeInGroup("enchanting_ui") as EnchantingUI;
            return _enchantingUi is { Visible: true };
        }
    }

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    public void Open()
    {
        EnsureWands();
        _player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
        _hud ??= GetTree().GetFirstNodeInGroup("hud") as Hud;

        _player?.SetControlsEnabled(false);
        if (_hud is not null) _hud.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Visible = true;

        RefreshRows();
        ShowTab(_tabIndex);
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        _player?.SetControlsEnabled(true);
        if (_hud is not null) _hud.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void ShowTab(int index)
    {
        _tabIndex = index == 1 ? 1 : 0;
        _itemsPage.Visible = _tabIndex == 0;
        _enchantPage.Visible = _tabIndex == 1;

        // Highlight the active tab and dim the other.
        _itemsTab.Modulate = _tabIndex == 0 ? Colors.White : new Color(0.7f, 0.7f, 0.75f);
        _enchantTab.Modulate = _tabIndex == 1 ? Colors.White : new Color(0.7f, 0.7f, 0.75f);

        RefreshSelection();
    }

    /// <summary>Equips or puts away the selected wand (the Items tab action).</summary>
    public void ToggleHold()
    {
        Wand? wand = SelectedWand;
        if (wand is null || _player is null) return;

        if (wand.Held) wand.PutAway();
        else wand.Equip(_player);

        RefreshRows();
    }

    /// <summary>Opens the enchanting overlay for the selected wand (Enchant tab).</summary>
    public void BeginEnchanting()
    {
        Wand? wand = SelectedWand;
        if (wand is null || _player is null) return;

        _enchantingUi ??= GetTree().GetFirstNodeInGroup("enchanting_ui") as EnchantingUI;
        Camera3D? camera = _player.GetNodeOrNull<Camera3D>("Camera3D");
        if (_enchantingUi is null || camera is null) return;

        Close(); // hand input over to the enchanting overlay
        wand.ShowCloseUp(camera);
        IReadOnlyList<IReadOnlyList<Vector2>> existing = wand.CurrentStrokes;

        _enchantingUi.Open(
            // No runes recognised: keep the existing enchantment untouched.
            onComplete: (runes, strokes) =>
            {
                if (runes.Length > 0) wand.Enchant(runes[0], strokes);
                wand.EndCloseUp();
                Open();
            },
            onCancel: () =>
            {
                wand.EndCloseUp();
                Open();
            },
            initialStrokes: existing);
    }

    // ---- Internals ----

    /// <summary>Lazily resolves the wands (node _Ready order is not guaranteed).</summary>
    private void EnsureWands()
    {
        if (_wands.Count > 0) return;

        foreach (Node node in GetTree().GetNodesInGroup("wand"))
        {
            if (node is Wand wand) _wands.Add(wand);
        }
        _wands.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        foreach (Wand _ in _wands) _model.Add(new InventoryItem("Wand"));
        if (_wands.Count > 0 && !_model.HasSelection) _model.Select(0);
    }

    private void RefreshRows()
    {
        foreach (Node child in _itemList.GetChildren())
        {
            _itemList.RemoveChild(child);
            child.QueueFree();
        }
        _rowButtons.Clear();

        for (int i = 0; i < _wands.Count; i++)
        {
            int index = i;
            Button row = new()
            {
                Text = RowLabel(_wands[i]),
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0f, 34f),
            };
            row.Pressed += () =>
            {
                _model.Select(index);
                RefreshSelection();
            };
            _itemList.AddChild(row);
            _rowButtons.Add(row);
        }

        if (_wands.Count > 0 && !_model.HasSelection) _model.Select(0);
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        for (int i = 0; i < _rowButtons.Count; i++)
        {
            _rowButtons[i].Modulate = i == _model.SelectedIndex
                ? Colors.White
                : new Color(0.72f, 0.72f, 0.78f);
        }

        Wand? wand = SelectedWand;
        _selectedLabel.Text = wand is null ? "No wand in inventory" : $"Selected: {RowLabel(wand)}";
        _holdButton.Disabled = wand is null;
        _holdButton.Text = wand is { Held: true } ? "Put away" : "Hold in hand";
        _enchantLabel.Text = wand is null ? "Selected: none" : $"Selected: {RowLabel(wand)}";
        _enchantButton.Disabled = wand is null;
    }

    private static string RowLabel(Wand wand)
    {
        string element = wand.Enchanted ? wand.Element : "unenchanted";
        return $"Wand — {element}{(wand.Held ? " (held)" : "")}";
    }
}
