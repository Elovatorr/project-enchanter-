using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>An item the player carries (currently always a wand).</summary>
public sealed class InventoryItem
{
    public InventoryItem(string name) => Name = name;

    public string Name { get; set; }

    /// <summary>Enchanted element ("fire"/"water"/"density"), empty when none.</summary>
    public string Element { get; set; } = "";

    public bool Enchanted => Element.Length > 0;
}

/// <summary>
/// Selection state for the player's inventory. Pure C# (no Godot nodes), so it
/// is unit-testable; the UI maps item indices onto the wands in the scene.
/// </summary>
public class InventoryModel
{
    private readonly List<InventoryItem> _items = [];

    public IReadOnlyList<InventoryItem> Items => _items;
    public int Count => _items.Count;

    /// <summary>Index of the selected item, or -1 when the inventory is empty.</summary>
    public int SelectedIndex { get; private set; } = -1;

    public InventoryItem? Selected =>
        SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex] : null;

    public bool HasSelection => Selected is not null;

    /// <summary>Adds an item; the first one becomes selected. Returns its index.</summary>
    public int Add(InventoryItem item)
    {
        _items.Add(item);
        if (SelectedIndex < 0) SelectedIndex = _items.Count - 1;
        return _items.Count - 1;
    }

    /// <summary>Selects by index; out-of-range indices are ignored.</summary>
    public bool Select(int index)
    {
        if (index < 0 || index >= _items.Count) return false;
        SelectedIndex = index;
        return true;
    }

    /// <summary>Selects the next item, wrapping around; no-op when empty.</summary>
    public bool SelectNext()
    {
        if (_items.Count == 0) return false;
        SelectedIndex = (SelectedIndex + 1) % _items.Count;
        return true;
    }

    /// <summary>Selects the previous item, wrapping around; no-op when empty.</summary>
    public bool SelectPrevious()
    {
        if (_items.Count == 0) return false;
        SelectedIndex = (SelectedIndex - 1 + _items.Count) % _items.Count;
        return true;
    }

    /// <summary>
    /// Removes an item, keeping the selection in range (or -1 when empty).
    /// Returns false for an invalid index.
    /// </summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return false;

        _items.RemoveAt(index);
        if (_items.Count == 0)
        {
            SelectedIndex = -1;
        }
        else if (index < SelectedIndex)
        {
            SelectedIndex--;
        }
        else if (SelectedIndex >= _items.Count)
        {
            SelectedIndex = _items.Count - 1;
        }
        return true;
    }

    /// <summary>Removes every item and clears the selection.</summary>
    public void Clear()
    {
        _items.Clear();
        SelectedIndex = -1;
    }
}
