using Godot;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>
/// Pure logic for capturing strokes — no Godot node dependencies, so it can be unit-tested.
/// DrawingSurface delegates its input handling to this model.
/// </summary>
public class DrawingSurfaceModel
{
    public float MinPointDistance { get; set; } = 3.0f;

    private readonly List<List<Vector2>> _strokes = [];

    /// <summary>Completed and in-progress strokes, in drawing order.</summary>
    public IReadOnlyList<IReadOnlyList<Vector2>> Strokes => _strokes;

    private List<Vector2>? _currentStroke;

    /// <summary>True while the pen is down (between BeginStroke and EndStroke).</summary>
    public bool IsDrawing => _currentStroke is not null;

    /// <summary>Starts a new stroke at the given point.</summary>
    public void BeginStroke(Vector2 point)
    {
        _currentStroke = [point];
        _strokes.Add(_currentStroke);
    }

    /// <summary>Finishes the current stroke.</summary>
    public void EndStroke()
    {
        _currentStroke = null;
    }

    /// <summary>
    /// Appends a point to the in-progress stroke. Points closer than
    /// MinPointDistance to the previous point are ignored.
    /// Does nothing when no stroke is in progress.
    /// </summary>
    public void AppendPoint(Vector2 point)
    {
        if (_currentStroke is not { Count: > 0 } stroke) return;

        if (stroke[^1].DistanceTo(point) < MinPointDistance)
        {
            return;
        }

        stroke.Add(point);
    }

    /// <summary>
    /// Removes the most recent stroke (an in-progress stroke counts as most recent).
    /// Returns false when there is nothing to undo.
    /// </summary>
    public bool UndoLastStroke()
    {
        if (_strokes.Count == 0)
        {
            _currentStroke = null;
            return false;
        }

        _strokes.RemoveAt(_strokes.Count - 1);
        _currentStroke = null;
        return true;
    }

    /// <summary>Removes all strokes.</summary>
    public void Clear()
    {
        _strokes.Clear();
        _currentStroke = null;
    }
}
