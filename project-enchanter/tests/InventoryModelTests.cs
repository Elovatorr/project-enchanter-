using ProjectEnchanter;
using Xunit;

namespace ProjectEnchanter.Tests;

/// <summary>
/// Unit tests for InventoryModel — item list and selection rules used by the
/// Tab inventory UI.
/// </summary>
public class InventoryModelTests
{
    private static InventoryModel NewModel() => new();

    private static InventoryItem Wand(string element = "") =>
        new("Wand") { Element = element };

    // ---- Items ----

    [Fact]
    public void Add_FirstItem_SelectsIt()
    {
        var model = NewModel();

        int index = model.Add(Wand());

        Assert.Equal(0, index);
        Assert.Equal(0, model.SelectedIndex);
        Assert.NotNull(model.Selected);
        Assert.True(model.HasSelection);
    }

    [Fact]
    public void Add_MoreItems_KeepsExistingSelection()
    {
        var model = NewModel();
        model.Add(Wand());

        model.Add(Wand("fire"));

        Assert.Equal(2, model.Count);
        Assert.Equal(0, model.SelectedIndex);
    }

    [Fact]
    public void Items_ExposeNameElementAndEnchantedState()
    {
        var model = NewModel();
        model.Add(Wand("water"));

        InventoryItem item = Assert.Single(model.Items);
        Assert.Equal("Wand", item.Name);
        Assert.Equal("water", item.Element);
        Assert.True(item.Enchanted);
    }

    // ---- Selection ----

    [Fact]
    public void Select_ValidIndex_ChangesSelection()
    {
        var model = NewModel();
        model.Add(Wand());
        model.Add(Wand());

        Assert.True(model.Select(1));
        Assert.Equal(1, model.SelectedIndex);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Select_OutOfRange_IsIgnored(int index)
    {
        var model = NewModel();
        model.Add(Wand());
        model.Add(Wand());

        Assert.False(model.Select(index));
        Assert.Equal(0, model.SelectedIndex);
    }

    [Fact]
    public void SelectNext_WrapsAround()
    {
        var model = NewModel();
        model.Add(Wand());
        model.Add(Wand());

        Assert.True(model.SelectNext());
        Assert.Equal(1, model.SelectedIndex);
        Assert.True(model.SelectNext());
        Assert.Equal(0, model.SelectedIndex);
    }

    [Fact]
    public void SelectPrevious_WrapsAround()
    {
        var model = NewModel();
        model.Add(Wand());
        model.Add(Wand());

        Assert.True(model.SelectPrevious());
        Assert.Equal(1, model.SelectedIndex);
    }

    [Fact]
    public void SelectNext_OnEmptyInventory_IsNoOp()
    {
        var model = NewModel();

        Assert.False(model.SelectNext());
        Assert.False(model.SelectPrevious());
        Assert.Equal(-1, model.SelectedIndex);
        Assert.False(model.HasSelection);
    }

    // ---- Removal ----

    [Fact]
    public void RemoveAt_BeforeSelection_ShiftsSelectionDown()
    {
        var model = NewModel();
        model.Add(Wand());
        model.Add(Wand());
        model.Select(1);

        Assert.True(model.RemoveAt(0));

        Assert.Equal(1, model.Count);
        Assert.Equal(0, model.SelectedIndex);
    }

    [Fact]
    public void RemoveAt_Selected_ClampsToLastItem()
    {
        var model = NewModel();
        model.Add(Wand());
        model.Add(Wand());
        model.Select(1);

        Assert.True(model.RemoveAt(1));

        Assert.Equal(0, model.SelectedIndex);
        Assert.Single(model.Items);
    }

    [Fact]
    public void RemoveAt_LastRemainingItem_ClearsSelection()
    {
        var model = NewModel();
        model.Add(Wand());

        Assert.True(model.RemoveAt(0));

        Assert.Empty(model.Items);
        Assert.Equal(-1, model.SelectedIndex);
        Assert.Null(model.Selected);
    }

    [Fact]
    public void RemoveAt_InvalidIndex_IsIgnored()
    {
        var model = NewModel();
        model.Add(Wand());

        Assert.False(model.RemoveAt(3));
        Assert.Single(model.Items);
    }

    [Fact]
    public void Clear_RemovesEverythingAndSelection()
    {
        var model = NewModel();
        model.Add(Wand());
        model.Add(Wand());

        model.Clear();

        Assert.Empty(model.Items);
        Assert.Equal(-1, model.SelectedIndex);
    }

    // ---- Regression: swapped-out wand keeps a valid selection ----

    [Fact]
    public void Regression_RemoveSelectedThenSelect_StillWorks()
    {
        var model = NewModel();
        model.Add(Wand("fire"));
        model.Add(Wand("water"));
        model.Select(0);

        model.RemoveAt(0);
        Assert.True(model.HasSelection);

        Assert.True(model.Select(0));
        Assert.Equal("water", model.Selected!.Element);
    }
}
