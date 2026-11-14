using Godot;
using System;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>
/// Owns the player's carried items (wands). Numpad 1 cycles which item is held;
/// Tab opens the enchanting overlay directly for the held item, prefilled with
/// its current rune so the enchantment can be edited in place.
/// </summary>
public partial class InventoryController : Node
{
    private readonly InventoryModel _model = new();
    private readonly List<Wand> _wands = [];
    private PlayerController? _player;
    private EnchantingUI? _enchantingUi;
    private bool _pendingDefaultEquip;

    public int ItemCount => _wands.Count;

    public Wand? SelectedWand =>
        _model.SelectedIndex >= 0 && _model.SelectedIndex < _wands.Count
            ? _wands[_model.SelectedIndex]
            : null;

    public override void _Ready()
    {
        AddToGroup("inventory");
        EnsureReady();

        // Start with an item in hand so Tab always has something to edit.
        // Deferred: reparenting during _Ready fails while the scene tree is
        // still busy adding children.
        _pendingDefaultEquip = true;
    }

    public override void _Process(double delta)
    {
        if (!_pendingDefaultEquip) return;
        _pendingDefaultEquip = false;

        if (SelectedWand is { } wand && _player is not null && !wand.Held) wand.Equip(_player);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key) return;
        if (IsEnchantingOpen) return;

        if (key.Keycode == Key.Tab)
        {
            BeginEnchanting();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Kp1)
        {
            CycleNext();
            GetViewport().SetInputAsHandled();
        }
    }

    private bool IsEnchantingOpen =>
        (_enchantingUi ??= GetTree().GetFirstNodeInGroup("enchanting_ui") as EnchantingUI)
        is { Visible: true };

    /// <summary>Equips the next carried item (Numpad 1).</summary>
    public void CycleNext()
    {
        EnsureReady();
        if (_wands.Count == 0 || _player is null || !_player.ControlsEnabled) return;

        Wand? current = SelectedWand;
        _model.SelectNext();
        Wand? next = SelectedWand;
        if (next is null) return;

        if (next == current)
        {
            // Single item (or wrapped to itself): make sure it is in hand.
            if (!next.Held) next.Equip(_player);
            return;
        }

        current?.PutAway();
        next.Equip(_player);
    }

    /// <summary>Opens the enchanting overlay for the held item (Tab).</summary>
    public void BeginEnchanting()
    {
        EnsureReady();
        if (_player is null || SelectedWand is not { } wand) return;

        _enchantingUi ??= GetTree().GetFirstNodeInGroup("enchanting_ui") as EnchantingUI;
        Camera3D? camera = _player.GetNodeOrNull<Camera3D>("Camera3D");
        if (_enchantingUi is null || camera is null) return;

        if (!wand.Held) wand.Equip(_player);
        wand.ShowCloseUp(camera);
        IReadOnlyList<IReadOnlyList<Vector2>> existing = wand.CurrentStrokes;

        _enchantingUi.Open(
            // No runes recognised: keep the existing enchantment untouched.
            onComplete: (runes, strokes) =>
            {
                if (runes.Length > 0) wand.Enchant(runes[0], strokes);
                wand.EndCloseUp();
            },
            onCancel: wand.EndCloseUp,
            initialStrokes: existing);
    }

    /// <summary>Lazily resolves the wands and player (node _Ready order is not guaranteed).</summary>
    private void EnsureReady()
    {
        if (_wands.Count == 0)
        {
            foreach (Node node in GetTree().GetNodesInGroup("wand"))
            {
                if (node is Wand wand) _wands.Add(wand);
            }
            _wands.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            foreach (Wand _ in _wands) _model.Add(new InventoryItem("Wand"));
            if (_wands.Count > 0 && !_model.HasSelection) _model.Select(0);
        }

        _player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
    }
}
