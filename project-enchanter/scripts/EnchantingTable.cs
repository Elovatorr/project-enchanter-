using Godot;
using System;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>
/// The enchanting table. Looking at it and pressing E opens the drawing UI; a
/// successful identification enchants the wand resting on the table.
/// </summary>
public partial class EnchantingTable : StaticBody3D, IInteractable
{
    private Wand? _wand;

    /// <summary>
    /// Resolved lazily: the table may be ready before the wand node (node order
    /// determines _Ready order), so the group lookup can't happen in _Ready.
    /// </summary>
    private Wand? WandRef => _wand ??= GetTree().GetFirstNodeInGroup("wand") as Wand;

    public override void _Ready()
    {
        AddToGroup("enchanting_table");
    }

    public string InteractionPrompt
    {
        get
        {
            Wand? wand = WandRef;
            if (wand is null) return "Enchanting table";
            // Holding the wand does not lock the enchantment: the table can edit it.
            if (wand.Held) return "[E] Edit enchantment";
            if (wand.Enchanted) return "[E] Re-enchant wand";
            return "[E] Enchant wand";
        }
    }

    public void Interact(PlayerController player)
    {
        Wand? wand = WandRef;
        if (wand is null || wand.InCloseUp) return;

        EnchantingUI? ui = GetTree().GetFirstNodeInGroup("enchanting_ui") as EnchantingUI;
        if (ui is null) return;

        // Present the wand up close while the player draws/edits. This works while
        // the wand is held; EndCloseUp puts it back in the hand afterwards.
        Camera3D camera = player.GetNode<Camera3D>("Camera3D");
        IReadOnlyList<IReadOnlyList<Vector2>> existing = wand.CurrentStrokes;
        wand.ShowCloseUp(camera);

        ui.Open(
            onComplete: (runes, strokes) =>
            {
                // No runes recognised: keep the existing enchantment untouched.
                if (runes.Length > 0) wand.Enchant(runes[0], strokes);
                wand.EndCloseUp();
            },
            onCancel: wand.EndCloseUp,
            initialStrokes: existing);
    }
}
