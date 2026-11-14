using Godot;
using System;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>
/// Captures mouse/touch strokes and draws them on screen.
/// Foundation for the rune-drawing gameplay.
/// </summary>
public partial class DrawingSurface : Node2D
{
    /// <summary>Emitted when identification produces at least one rune cast.
    /// The argument is a comma-separated list of rune names.</summary>
    [Signal]
    public delegate void RunesCastEventHandler(string runes);

    /// <summary>
    /// C# event with the full detail of a cast: rune names and a snapshot of the
    /// strokes that were drawn (used to etch the rune onto the wand).
    /// </summary>
    public event Action<string[], IReadOnlyList<IReadOnlyList<Vector2>>>? CastCompleted;

    private readonly DrawingSurfaceModel _model = new();
    private readonly RuneClassifier _classifier = new();
    private string? _lastCast;
    private string? _candidate;
    private readonly List<Color> _flashHistory = [];
    private ColorRect? _flashRect;
    private Tween? _flashTween;
    private Control? _uiLayer;
    private Button? _identifyButton;

    [Export] public Color StrokeColor { get; set; } = Colors.White;
    [Export] public float StrokeWidth { get; set; } = 4.0f;
    [Export] public float FlashThreshold { get; set; } = 0.7f;
    [Export] public float FlashDuration { get; set; } = 0.5f;
    [Export] public Color FireFlashColor { get; set; } = new(1f, 0.1f, 0.05f, 0.55f);
    [Export] public Color WaterFlashColor { get; set; } = new(0.1f, 0.35f, 1f, 0.55f);
    [Export] public Color DensityFlashColor { get; set; } = new(0.15f, 0.85f, 0.25f, 0.55f);
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
    public int CircleCount { get; private set; }

    /// <summary>Number of strokes currently drawn (used by in-engine tests).</summary>
    public int StrokeCount => _model.Strokes.Count;

    /// <summary>Whether the drawing UI (buttons/flash) is actually visible.</summary>
    public bool IsUiVisible => _uiLayer?.IsVisibleInTree() ?? false;

    /// <summary>Global rect of the Identify button (used by in-engine tests).</summary>
    public Rect2 IdentifyButtonRect => _identifyButton?.GetGlobalRect() ?? new Rect2();
    public Color LastFlashColor { get; private set; }
    public IReadOnlyList<Color> FlashHistory => _flashHistory;

    public override void _Ready()
    {
        // UI (flash + buttons) lives in a plain Control so it inherits visibility
        // from any parent CanvasLayer (e.g. the enchanting overlay). A nested
        // CanvasLayer would NOT hide with its parent and its buttons would stay
        // active outside enchanting.
        _uiLayer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_uiLayer);
        // A Control under a Node2D does NOT inherit the viewport rect, so give it
        // an explicit viewport-sized rect (and keep it in sync on resize).
        _uiLayer.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _uiLayer.Size = GetViewportRect().Size;
        GetViewport().SizeChanged += OnViewportResized;

        _flashRect = new ColorRect
        {
            Color = new Color(DefaultFlashColor.R, DefaultFlashColor.G, DefaultFlashColor.B, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _flashRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _uiLayer.AddChild(_flashRect);

        Button identifyButton = new()
        {
            Text = "Identify rune",
            CustomMinimumSize = new Vector2(170, 44),
        };
        identifyButton.Pressed += Identify;
        _uiLayer.AddChild(identifyButton);
        AnchorBottomCenter(identifyButton, -95f, 95f);
        _identifyButton = identifyButton;

        Button demoButton = new()
        {
            Text = "Draw demo (3 runes)",
            CustomMinimumSize = new Vector2(220, 44),
        };
        demoButton.Pressed += DrawDemoRunes;
        _uiLayer.AddChild(demoButton);
        AnchorBottomCenter(demoButton, -325f, -105f);
    }

    private void OnViewportResized()
    {
        if (_uiLayer is not null) _uiLayer.Size = GetViewportRect().Size;
    }

    /// <summary>Anchors a button to the bottom centre with the given x offsets.</summary>
    private static void AnchorBottomCenter(Control control, float left, float right)
    {
        control.AnchorLeft = 0.5f;
        control.AnchorRight = 0.5f;
        control.AnchorTop = 1f;
        control.AnchorBottom = 1f;
        control.OffsetLeft = left;
        control.OffsetRight = right;
        control.OffsetTop = -64f;
        control.OffsetBottom = -20f;
    }

    /// <summary>
    /// Fills the canvas with a ready-made multi-rune test case: circled fire,
    /// water and density runes, side by side.
    /// </summary>
    public void DrawDemoRunes()
    {
        _model.Clear();
        _candidate = null;
        _lastCast = null;

        Rect2 view = GetViewportRect();
        Vector2 center = view.Size / 2f;
        float radius = MathF.Min(view.Size.X, view.Size.Y) * 0.13f;
        float gap = radius * 2.7f;

        AddCircledRune(RuneTemplates.Fire, center - new Vector2(gap, 0f), radius);
        AddCircledRune(RuneTemplates.Water, center, radius);
        AddCircledRune(RuneTemplates.Density, center + new Vector2(gap, 0f), radius);

        QueueRedraw();
    }

    private void AddCircledRune(RuneTemplate rune, Vector2 center, float radius)
    {
        // Circle enclosure.
        const int circlePoints = 48;
        _model.BeginStroke(center + new Vector2(radius, 0f));
        for (int i = 1; i <= circlePoints; i++)
        {
            float angle = i * MathF.Tau / circlePoints;
            _model.AppendPoint(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
        _model.EndStroke();

        // Rune inside, scaled to fit comfortably.
        float scale = radius * 0.5f;
        foreach (IReadOnlyList<Vector2> stroke in rune.Strokes)
        {
            _model.BeginStroke(stroke[0] * scale + center);
            for (int i = 1; i < stroke.Count; i++)
            {
                _model.AppendPoint(stroke[i] * scale + center);
            }
            _model.EndStroke();
        }
    }

    /// <summary>Clears all strokes and any pending result (used when closing the UI).</summary>
    public void ClearDrawing()
    {
        _model.Clear();
        _candidate = null;
        QueueRedraw();
    }

    /// <summary>
    /// Loads existing strokes into the canvas so the player can edit a rune that
    /// is already enchanted (e.g. the wand's current rune).
    /// </summary>
    public void LoadStrokes(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        _model.Load(strokes);
        _candidate = null;
        QueueRedraw();
    }

    private static List<List<Vector2>> SnapshotStrokes(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        List<List<Vector2>> copy = [];
        foreach (IReadOnlyList<Vector2> stroke in strokes) copy.Add([.. stroke]);
        return copy;
    }

    /// <summary>
    /// Classifies what is drawn. Runes enclosed in circles are identified
    /// individually (several at once); without circles the whole drawing is
    /// classified as a single rune.
    /// </summary>
    public void Identify()
    {
        CircleCount = EnclosedRuneDetector.CountCircles(_model.Strokes);
        List<EnclosedRune> regions = EnclosedRuneDetector.Find(_model.Strokes);
        if (regions.Count > 0)
        {
            CastEnclosedRunes(regions);
        }
        else
        {
            TryCast();
        }
    }

    /// <summary>Classifies each circled rune, casts the matches, flashes their colours.</summary>
    private void CastEnclosedRunes(List<EnclosedRune> regions)
    {
        List<string> names = [];
        List<string> runeNames = [];
        List<Color> flashColors = [];
        float bestConfidence = 0f;

        // Left-to-right so multi-rune flashes read in a predictable order.
        regions.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));

        foreach (EnclosedRune region in regions)
        {
            RuneDiagnostics diagnostics = _classifier.RecognizeDetailed(region.Strokes);
            if (!diagnostics.Matched || diagnostics.Name is null) continue;

            runeNames.Add(diagnostics.Name);
            names.Add($"{diagnostics.Name} ({diagnostics.Confidence:P0})");
            flashColors.Add(FlashColorFor(diagnostics.Name));
            bestConfidence = MathF.Max(bestConfidence, diagnostics.Confidence);
        }

        if (names.Count == 0)
        {
            _candidate = $"no rune inside {regions.Count} circle(s)";
            QueueRedraw();
            return;
        }

        _lastCast = string.Join(", ", names);
        CastCount += names.Count;
        IReadOnlyList<IReadOnlyList<Vector2>> strokes = SnapshotStrokes(
            runeNames.Count > 0 ? regions[0].Strokes : []);
        _model.Clear();
        _candidate = null;
        EmitSignal(SignalName.RunesCast, string.Join(",", runeNames));
        CastCompleted?.Invoke([.. runeNames], strokes);

        if (bestConfidence > FlashThreshold)
        {
            FlashScreen(flashColors);
        }
        QueueRedraw();
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
            IReadOnlyList<IReadOnlyList<Vector2>> strokes = SnapshotStrokes(_model.Strokes);
            _model.Clear();
            _candidate = null;
            EmitSignal(SignalName.RunesCast, diagnostics.Name ?? "");
            CastCompleted?.Invoke([diagnostics.Name ?? ""], strokes);

            // Flash the screen when the model is confident the rune was drawn.
            if (diagnostics.Confidence > FlashThreshold)
            {
                FlashScreen([FlashColorFor(diagnostics.Name)]);
            }
        }
        QueueRedraw();
    }

    /// <summary>Per-rune flash colour: fire red, water blue, density green, fallback orange.</summary>
    private Color FlashColorFor(string? runeName) => runeName switch
    {
        "fire" => FireFlashColor,
        "water" => WaterFlashColor,
        "density" => DensityFlashColor,
        _ => DefaultFlashColor,
    };

    /// <summary>
    /// Flashes the full-screen overlay once per colour (so multiple runes each
    /// get their own flash), each for <see cref="FlashDuration"/> seconds.
    /// </summary>
    private void FlashScreen(IReadOnlyList<Color> colors)
    {
        if (_flashRect is null || colors.Count == 0) return;

        _flashTween?.Kill();
        FlashCount += colors.Count;
        LastFlashColor = colors[^1];
        _flashHistory.AddRange(colors);

        _flashTween = CreateTween();
        foreach (Color color in colors)
        {
            Color flash = color;
            _flashTween.TweenCallback(Callable.From(() => _flashRect.Color = flash));
            _flashTween.TweenProperty(_flashRect, "color:a", 0f, FlashDuration)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
        }
    }

    public override void _Draw()
    {
        // Classification HUD (top-left): live candidate info + last successful cast.
        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(16, 30),
            $"Drawing: {_model.Strokes.Count} stroke(s)   Circles: {CircleCount}", HorizontalAlignment.Left, -1, 22, Colors.LightGray);
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
