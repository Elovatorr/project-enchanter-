using Godot;
using ProjectEnchanter;
using System.Collections.Generic;
using Xunit;

namespace ProjectEnchanter.Tests;

/// <summary>
/// Unit tests for DrawingSurfaceModel — stroke capture, distance filtering, undo, and clear.
/// </summary>
public class DrawingSurfaceModelTests
{
    private static DrawingSurfaceModel NewModel(float minPointDistance = 3.0f)
    {
        return new DrawingSurfaceModel { MinPointDistance = minPointDistance };
    }

    // ---- Stroke creation ----

    [Fact]
    public void BeginStroke_CreatesSinglePointStroke()
    {
        var model = NewModel();

        model.BeginStroke(new Vector2(10, 10));

        Assert.Single(model.Strokes);
        var stroke = Assert.Single(model.Strokes[^1]);
        Assert.Equal(new Vector2(10, 10), stroke);
    }

    [Fact]
    public void BeginStroke_IsDrawing_True()
    {
        var model = NewModel();

        Assert.False(model.IsDrawing);
        model.BeginStroke(Vector2.Zero);
        Assert.True(model.IsDrawing);
    }

    [Fact]
    public void EndStroke_StopsDrawing()
    {
        var model = NewModel();
        model.BeginStroke(Vector2.Zero);

        model.EndStroke();

        Assert.False(model.IsDrawing);
    }

    // ---- Point appending / distance filter ----

    [Fact]
    public void AppendPoint_WithinMinDistance_IsIgnored()
    {
        var model = NewModel(minPointDistance: 3.0f);
        model.BeginStroke(new Vector2(0, 0));

        model.AppendPoint(new Vector2(2, 0)); // 2 < 3 → too close

        Assert.Single(model.Strokes[^1]);
    }

    [Fact]
    public void AppendPoint_BeyondMinDistance_IsAdded()
    {
        var model = NewModel(minPointDistance: 3.0f);
        model.BeginStroke(new Vector2(0, 0));

        model.AppendPoint(new Vector2(4, 0)); // 4 >= 3

        Assert.Equal(2, model.Strokes[^1].Count);
        Assert.Equal(new Vector2(4, 0), model.Strokes[^1][^1]);
    }

    [Fact]
    public void AppendPoint_FiltersDistanceFromLastPointNotFirst()
    {
        var model = NewModel(minPointDistance: 3.0f);
        model.BeginStroke(new Vector2(0, 0));
        model.AppendPoint(new Vector2(10, 0)); // accepted
        model.AppendPoint(new Vector2(11, 0)); // 1 from (10,0) → ignored
        model.AppendPoint(new Vector2(13, 0)); // 3 from (10,0) → accepted

        Assert.Equal(3, model.Strokes[^1].Count);
        Assert.Equal(new Vector2(13, 0), model.Strokes[^1][^1]);
    }

    [Fact]
    public void AppendPoint_BeforeBeginStroke_DoesNothing()
    {
        var model = NewModel();

        model.AppendPoint(new Vector2(5, 5));

        Assert.Empty(model.Strokes);
        Assert.False(model.IsDrawing);
    }

    [Fact]
    public void AppendPoint_AfterEndStroke_DoesNothing()
    {
        var model = NewModel();
        model.BeginStroke(Vector2.Zero);
        model.EndStroke();

        model.AppendPoint(new Vector2(5, 5));

        Assert.Single(model.Strokes[^1]);
    }

    // ---- Undo ----

    [Fact]
    public void UndoLastStroke_RemovesMostRecentStroke()
    {
        var model = NewModel();
        model.BeginStroke(new Vector2(0, 0));
        model.AppendPoint(new Vector2(10, 0));
        model.EndStroke();
        model.BeginStroke(new Vector2(50, 50));
        model.EndStroke();

        bool undone = model.UndoLastStroke();

        Assert.True(undone);
        Assert.Single(model.Strokes);
        Assert.Equal(new Vector2(0, 0), model.Strokes[0][0]);
    }

    [Fact]
    public void UndoLastStroke_OnEmptyCanvas_ReturnsFalse()
    {
        var model = NewModel();

        bool undone = model.UndoLastStroke();

        Assert.False(undone);
        Assert.Empty(model.Strokes);
    }

    [Fact]
    public void UndoLastStroke_CancelsInProgressStroke()
    {
        var model = NewModel();
        model.BeginStroke(new Vector2(0, 0));
        model.AppendPoint(new Vector2(10, 0));

        bool undone = model.UndoLastStroke();

        Assert.True(undone);
        Assert.Empty(model.Strokes);
        Assert.False(model.IsDrawing);
    }

    [Fact]
    public void UndoLastStroke_AfterEndStroke_StopsDrawing()
    {
        var model = NewModel();
        model.BeginStroke(Vector2.Zero);
        model.EndStroke();

        model.UndoLastStroke();

        Assert.False(model.IsDrawing);
    }

    [Fact]
    public void UndoLastStroke_RemovesStrokesOneAtATTime()
    {
        var model = NewModel();
        for (int i = 0; i < 3; i++)
        {
            model.BeginStroke(new Vector2(i, i));
            model.EndStroke();
        }

        Assert.True(model.UndoLastStroke());
        Assert.Equal(2, model.Strokes.Count);
        Assert.True(model.UndoLastStroke());
        Assert.Single(model.Strokes);
        Assert.True(model.UndoLastStroke());
        Assert.Empty(model.Strokes);
        Assert.False(model.UndoLastStroke()); // nothing left
    }

    // ---- Clear ----

    [Fact]
    public void Clear_RemovesAllStrokes()
    {
        var model = NewModel();
        model.BeginStroke(Vector2.Zero);
        model.EndStroke();
        model.BeginStroke(new Vector2(1, 1));

        model.Clear();

        Assert.Empty(model.Strokes);
        Assert.False(model.IsDrawing);
    }

    [Fact]
    public void Clear_OnEmptyCanvas_IsSafe()
    {
        var model = NewModel();

        model.Clear();

        Assert.Empty(model.Strokes);
    }

    // ---- Load (edit an existing rune) ----

    [Fact]
    public void Load_ReplacesExistingStrokes()
    {
        var model = NewModel();
        model.BeginStroke(new Vector2(0, 0));
        model.EndStroke();

        model.Load([new List<Vector2> { new(10, 10), new(20, 20) }]);

        Assert.Single(model.Strokes);
        Assert.Equal(2, model.Strokes[0].Count);
        Assert.Equal(new Vector2(10, 10), model.Strokes[0][0]);
    }

    [Fact]
    public void Load_CopiesPoints_SoTheSourceIsNotAliased()
    {
        var model = NewModel();
        List<Vector2> source = [new(0, 0), new(5, 5)];

        model.Load([source]);
        source.Add(new Vector2(99, 99));

        Assert.Equal(2, model.Strokes[0].Count);
    }

    [Fact]
    public void Load_EmptyList_ClearsStrokes()
    {
        var model = NewModel();
        model.BeginStroke(Vector2.Zero);
        model.EndStroke();

        model.Load([]);

        Assert.Empty(model.Strokes);
    }

    [Fact]
    public void Load_SkipsEmptyStrokes()
    {
        var model = NewModel();

        model.Load([[], new List<Vector2> { new(1, 1) }, []]);

        Assert.Single(model.Strokes);
    }

    [Fact]
    public void Load_WhileDrawing_StopsTheInProgressStroke()
    {
        var model = NewModel();
        model.BeginStroke(new Vector2(3, 3));

        model.Load([new List<Vector2> { new(0, 0), new(4, 4) }]);

        Assert.False(model.IsDrawing);
        Assert.Single(model.Strokes);
    }

    // ---- Regression: full drawing session ----

    [Fact]
    public void FullSession_MultipleStrokesAreKeptInOrder()
    {
        var model = NewModel();
        model.BeginStroke(new Vector2(0, 0));
        model.AppendPoint(new Vector2(5, 0));
        model.AppendPoint(new Vector2(10, 0));
        model.EndStroke();
        model.BeginStroke(new Vector2(20, 20));
        model.EndStroke();

        Assert.Equal(2, model.Strokes.Count);
        Assert.Equal(3, model.Strokes[0].Count);
        Assert.Single(model.Strokes[1]);
    }

    [Fact]
    public void FullSession_NewStrokeAfterUndoStartsFresh()
    {
        var model = NewModel();
        model.BeginStroke(new Vector2(0, 0));
        model.EndStroke();
        model.UndoLastStroke();

        model.BeginStroke(new Vector2(7, 7));
        model.AppendPoint(new Vector2(20, 20));

        Assert.Single(model.Strokes);
        Assert.Equal(2, model.Strokes[^1].Count);
        Assert.Equal(new Vector2(7, 7), model.Strokes[^1][0]);
    }

    [Fact]
    public void FullSession_UndoesNewStrokesAfterClear()
    {
        var model = NewModel();
        model.BeginStroke(Vector2.Zero);
        model.UndoLastStroke();
        model.Clear();

        model.BeginStroke(new Vector2(1, 1));

        Assert.Single(model.Strokes);
    }

    [Fact]
    public void Regression_EditSession_UndoesLoadedStrokesAndDrawsNewOnes()
    {
        // Editing a wand's rune: the previous strokes are loaded, some are
        // undone, and new ones are drawn before identifying again.
        var model = NewModel();
        model.Load(
        [
            new List<Vector2> { new(0, 0), new(4, 4) },
            new List<Vector2> { new(10, 10), new(14, 14) },
        ]);

        Assert.True(model.UndoLastStroke());
        model.BeginStroke(new Vector2(50, 50));
        model.AppendPoint(new Vector2(60, 60));
        model.EndStroke();

        Assert.Equal(2, model.Strokes.Count);
        Assert.Equal(new Vector2(50, 50), model.Strokes[^1][0]);
    }
}
