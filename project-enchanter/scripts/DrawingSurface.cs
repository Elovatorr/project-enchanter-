using Godot;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>
/// Captures mouse/touch strokes and draws them on screen.
/// Foundation for the rune-drawing gameplay.
/// </summary>
public partial class DrawingSurface : Node2D
{
    private readonly DrawingSurfaceModel _model = new();
    private readonly RuneRecognizer _recognizer = new();
    private string? _lastCast;
    private string? _candidate;

    [Export] public Color StrokeColor { get; set; } = Colors.White;
    [Export] public float StrokeWidth { get; set; } = 4.0f;
    [Export] public float MinPointDistance
    {
        get => _model.MinPointDistance;
        set => _model.MinPointDistance = value;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                _model.BeginStroke(GetPointerPosition());
                QueueRedraw();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _model.IsDrawing:
                _model.EndStroke();
                TryCast();
                break;

            // Right-click removes the most recent stroke (undo, one stroke at a time).
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                if (_model.UndoLastStroke()) TryCast();
                break;

            case InputEventMouseMotion when _model.IsDrawing:
                _model.AppendPoint(GetPointerPosition());
                QueueRedraw();
                break;
        }
    }

    private Vector2 GetPointerPosition()
    {
        // Invert the canvas transform so drawing stays correct if a camera is added later.
        return GetCanvasTransform().AffineInverse() * GetViewport().GetMousePosition();
    }

    /// <summary>
    /// Runs after each completed stroke: updates the on-screen recognition HUD,
    /// and if the strokes match a rune, casts it (name shown, canvas cleared).
    /// </summary>
    private void TryCast()
    {
        RuneDiagnostics diagnostics = _recognizer.RecognizeDetailed(_model.Strokes);

        _candidate = diagnostics.Name is null
            ? $"no candidate · {_model.Strokes.Count} stroke(s)"
            : $"{diagnostics.Name} {diagnostics.Confidence:P0}"
              + (diagnostics.Matched ? " ✓ cast!" : $" (below threshold · {_model.Strokes.Count} stroke(s))");

        if (diagnostics.Matched)
        {
            _lastCast = $"{diagnostics.Name} ({diagnostics.Confidence:P0})";
            _model.Clear();
            _candidate = null;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Recognition HUD (top-left): live candidate info + last successful cast.
        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(16, 30),
            $"Drawing: {_model.Strokes.Count} stroke(s)", HorizontalAlignment.Left, -1, 22, Colors.LightGray);
        DrawString(font, new Vector2(16, 58),
            $"Best match: {_candidate ?? "—"}", HorizontalAlignment.Left, -1, 22,
            _candidate is not null && _candidate.Contains("cast!") ? Colors.Gold : Colors.White);
        if (_lastCast is not null)
        {
            DrawString(font, new Vector2(16, 86), $"Last cast: {_lastCast}",
                HorizontalAlignment.Left, -1, 22, Colors.Orange);
        }

        foreach (IReadOnlyList<Vector2> stroke in _model.Strokes)
        {
            if (stroke.Count == 1)
            {
                DrawCircle(stroke[0], StrokeWidth * 0.5f, StrokeColor);
            }
            else
            {
                DrawPolyline([.. stroke], StrokeColor, StrokeWidth);
            }
        }
    }
}
