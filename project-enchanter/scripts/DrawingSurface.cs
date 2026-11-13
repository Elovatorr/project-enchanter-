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
    private readonly RuneClassifier _classifier = new();
    private string? _lastCast;
    private string? _candidate;
    private ColorRect? _flashRect;
    private Tween? _flashTween;
    private Button? _identifyButton;

    [Export] public Color StrokeColor { get; set; } = Colors.White;
    [Export] public float StrokeWidth { get; set; } = 4.0f;
    [Export] public float FlashThreshold { get; set; } = 0.7f;
    [Export] public float FlashDuration { get; set; } = 0.5f;
    [Export] public Color FireFlashColor { get; set; } = new(1f, 0.1f, 0.05f, 0.55f);
    [Export] public Color WaterFlashColor { get; set; } = new(0.1f, 0.35f, 1f, 0.55f);
    [Export] public Color DefaultFlashColor { get; set; } = new(1f, 0.6f, 0.1f, 0.55f);
    [Export] public float MinPointDistance
    {
        get => _model.MinPointDistance;
        set => _model.MinPointDistance = value;
    }

    public string? LastCast => _lastCast;
    public string? Candidate => _candidate;
    public int CastCount { get; private set; }
    public int FlashCount { get; private set; }
    public Color LastFlashColor { get; private set; }
    public bool IsFlashing => _flashTween is { } tween && tween.IsRunning();

    public override void _Ready()
    {
        // Overlay layer for the "cast succeeded" flash and the Identify button.
        CanvasLayer layer = new() { Layer = 100 };
        AddChild(layer);

        _flashRect = new ColorRect
        {
            Color = new Color(DefaultFlashColor.R, DefaultFlashColor.G, DefaultFlashColor.B, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _flashRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_flashRect);

        _identifyButton = new Button
        {
            Text = "Identify rune",
            CustomMinimumSize = new Vector2(170, 44),
        };
        _identifyButton.Pressed += Identify;
        layer.AddChild(_identifyButton);
        _identifyButton.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _identifyButton.Position += new Vector2(-85f, -64f);
    }

    /// <summary>
    /// Classifies everything currently drawn. Wire this to UI (the Identify
    /// button calls it); it is not run automatically on stroke completion.
    /// </summary>
    public void Identify()
    {
        TryCast();
    }
    public int StrokeCount => _model.Strokes.Count;
    public int PointCount
    {
        get
        {
            int n = 0;
            foreach (IReadOnlyList<Vector2> stroke in _model.Strokes) n += stroke.Count;
            return n;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                _model.BeginStroke(CanvasPosition(press));
                QueueRedraw();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _model.IsDrawing:
                _model.EndStroke();
                QueueRedraw();
                break;

            // Right-click removes the most recent stroke (undo, one stroke at a time).
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                if (_model.UndoLastStroke())
                {
                    _candidate = null;
                    QueueRedraw();
                }
                break;

            case InputEventMouseMotion motion when _model.IsDrawing:
                _model.AppendPoint(CanvasPosition(motion));
                QueueRedraw();
                break;
        }
    }

    /// <summary>
    /// Converts a mouse event's viewport position into canvas coordinates, so
    /// drawing stays correct if a camera (zoom/pan) is added later. Using the
    /// event's own position (instead of polling the mouse) keeps this working
    /// with synthetic input and touch events too.
    /// </summary>
    private Vector2 CanvasPosition(InputEventMouse e)
        => GetCanvasTransform().AffineInverse() * e.Position;

    /// <summary>
    /// Runs after each completed stroke: updates the on-screen classification
    /// HUD, and if the strokes are recognized as a rune, casts it (name shown,
    /// canvas cleared for the next drawing).
    /// </summary>
    private void TryCast()
    {
        RuneDiagnostics diagnostics = _classifier.RecognizeDetailed(_model.Strokes);

        _candidate = diagnostics.Name is null
            ? $"no candidate · {_model.Strokes.Count} stroke(s)"
            : $"{diagnostics.Name} {diagnostics.Confidence:P0}"
              + (diagnostics.Matched ? " ✓ cast!" : $" (below threshold · {_model.Strokes.Count} stroke(s))");

        if (diagnostics.Matched)
        {
            _lastCast = $"{diagnostics.Name} ({diagnostics.Confidence:P0})";
            CastCount++;
            _model.Clear();
            _candidate = null;

            // Flash the screen when the model is confident the rune was drawn.
            if (diagnostics.Confidence > FlashThreshold)
            {
                FlashScreen(FlashColorFor(diagnostics.Name));
            }
        }
        QueueRedraw();
    }

    /// <summary>Per-rune flash colour: fire red, water blue, fallback orange.</summary>
    private Color FlashColorFor(string? runeName) => runeName switch
    {
        "fire" => FireFlashColor,
        "water" => WaterFlashColor,
        _ => DefaultFlashColor,
    };

    /// <summary>Flashes the full-screen overlay for <see cref="FlashDuration"/> seconds.</summary>
    private void FlashScreen(Color color)
    {
        if (_flashRect is null) return;

        _flashTween?.Kill();
        _flashRect.Color = color;
        LastFlashColor = color;
        FlashCount++;

        _flashTween = CreateTween();
        _flashTween.TweenProperty(_flashRect, "color:a", 0f, FlashDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
    }

    public override void _Draw()
    {
        // Classification HUD (top-left): live candidate info + last successful cast.
        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(16, 30),
            $"Drawing: {_model.Strokes.Count} stroke(s)", HorizontalAlignment.Left, -1, 22, Colors.LightGray);
        DrawString(font, new Vector2(16, 58),
            $"Best match: {_candidate ?? "press Identify"}", HorizontalAlignment.Left, -1, 22,
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
