# AGENTS.md — Guidance for AI Agents Working on This Project

Project: **project-enchanter** — a Godot 4.7 (.NET / C#) rune-drawing game.
This file tells coding agents how to work here: how the project is organized,
how to keep tests in sync with code changes, and the rules for keeping this
file itself up to date.

**Rule of thumb: any agent that changes code in this repository must update
this file in the same session if the change affects architecture, test
policy, build commands, or the sections below.** Stale guidance is worse
than no guidance.

---

## Project layout

| Path | Purpose |
|---|---|
| `project.godot` | Godot project config. `[dotnet] project/assembly_name` **must match** the csproj file name (`project-enchanter`, no spaces) or C# scripts fail to instantiate at runtime. |
| `project-enchanter.csproj` | Godot.NET.Sdk 4.7.2, net8.0. `tests\**` is excluded from this build. |
| `main.tscn` | Main (only) scene. Root `Node2D` "Main" with `DrawingSurface` attached. |
| `scripts/DrawingSurface.cs` | Godot node. Handles input and rendering only; delegates stroke logic to `DrawingSurfaceModel` and casting to `RuneRecognizer`. |
| `scripts/DrawingSurfaceModel.cs` | Pure C# class — drawing/undo logic. No Godot node dependencies; unit-testable. |
| `scripts/RuneRecognizer.cs` | Pure C# — $P point-cloud rune recognition (position/scale/rotation invariant). See "Recognition pipeline". |
| `scripts/RuneTemplate.cs` | Pure C# record: a named rune and its reference strokes. |
| `scripts/RuneTemplates.cs` | Built-in rune definitions (currently `Fire`). Coordinates are arbitrary units — the recognizer normalizes everything. |
| `tests/project-enchanter.tests.csproj` | xUnit test project (net8.0). |
| `tests/DrawingSurfaceModelTests.cs` | Unit/regression tests for the drawing model. |
| `tests/RuneRecognizerTests.cs` | Unit/regression tests for recognition, incl. invariance (translation/scale/rotation) and rejection cases. |

## Design principle: keep logic out of the node

Godot nodes (`Node2D`, etc.) cannot be instantiated in a test runner, so any
logic embedded in `DrawingSurface` is untestable. **The pattern is:**

- `DrawingSurface` (node) = input handling + rendering + delegation only.
- `DrawingSurfaceModel` (pure class) = all decision-making, state, and rules.

When adding new gameplay features:
1. Put the new logic in `DrawingSurfaceModel` (or a new pure class next to it).
2. Have the node delegate to it and just call `QueueRedraw()` on changes.
3. Write tests against the pure class.

## Recognition pipeline (RuneRecognizer)

How a drawing becomes a rune match:

1. **Resample** — each stroke is redrawn as points evenly spaced by arc length; points per stroke are allocated proportional to stroke length (48 total).
2. **Smooth** — a small moving-average filter (2 passes) reduces per-point hand jitter.
3. **Normalize** — the combined point cloud is translated to its centroid and scaled so the **RMS distance from the centroid** is 1. RMS radius (not bounding box) is mandatory: bounding boxes change under rotation and would break rotation invariance.
4. **Match** — greedy nearest-neighbour cloud distance ($P style), tried over 24 rotations (every 15°); the smaller of the two match directions wins.
5. **Guard** — a template is skipped if the drawing has fewer strokes than the template (a half-drawn rune must not fire).
6. **Threshold** — `MatchThreshold = 2.0` in the normalized unit space (caret fire rune). Calibration: realistic hand jitter measures < 1.0 (avg ≈ 0.57 at ±3% wobble); generic shapes (circle ≈ 8.5, square ≈ 8.4, triangle ≈ 5.5, line ≈ 12) measure far above. Retune by measuring, not guessing. Note: rotation invariance means an upside-down caret (a "V") also matches fire — acceptable for now, revisit if shapes conflict.

For testing, `RecognizeDetailed` returns the best candidate and confidence
**even when below the threshold** (and `Matched = false`); the HUD in
`DrawingSurface._Draw` shows it live: stroke count, best match + confidence
("cast!" in gold when over threshold), and the last successful cast. Note:
a drawing with too few strokes reports *no* candidate (the stroke guard runs
before scoring) — the HUD's stroke count is the feedback for that case.

Gotchas learned the hard way (do not regress these):

- `ResamplePolyline` must walk cumulative arc length — naive per-segment stepping drifts and scrambles point order for dense input.
- Do not "interpolate" points as `(a + b) * t` — that is not on the segment; use `a.Lerp(b, t)`.
- `new List<Vector2>(capacity) { [0] = x }` throws: capacity ≠ count. Initialize then assign or add.

### Adding a new rune

1. Define its stroke points in `RuneTemplates.cs` (any unit space; shape is all that matters — match the reference picture approximately).
2. Add tests mirroring `RuneRecognizerTests`: template match, translation/scale/rotation invariance, rejection of scribbles, and a full-session regression through `DrawingSurfaceModel`.
3. If recognition is too strict/lenient in playtests, adjust `MatchThreshold` and re-measure the calibration numbers above.

## Commands

```sh
# Build the game assembly (or click the hammer/Build button in the Godot editor)
dotnet build

# Run the tests
dotnet test tests\project-enchanter.tests.csproj

# Run the game: press F5 inside the Godot editor (Rider alone will not launch it)
```

The Godot editor's log is at
`%APPDATA%\Godot\app_userdata\project enchanter\logs\godot.log` — check it
when something "doesn't work" at runtime.

## How to update tests when code changes

**Every change to gameplay logic requires a matching change in
`tests/`. Use this decision table:**

| You changed... | Do this |
|---|---|
| An existing rule in `DrawingSurfaceModel` (thresholds, filtering, undo, etc.) | Update the existing test(s) that pin the old behavior, and note the behavior change in the test name or a comment. |
| Added a new method / state to `DrawingSurfaceModel` | Add tests for the happy path, the guard/edge cases (empty state, in-progress stroke, no-op cases), and one end-to-end "full session" test exercising it with the existing methods. |
| Added a new pure class | Create a new test class `Tests/<ClassName>Tests.cs` following the same structure (helper factory, sections grouped by behavior, regression tests). |
| Changed `DrawingSurface` (node) only — input mapping, rendering | Pure engine behavior is not unit-testable. Verify manually in Godot (F5) and note it; do not fake engine tests with mocks. If the change altered *when* the model is called, check that the model tests still cover the new sequence. |
| Renamed / deleted a class or method | Update all references in `tests/` in the same change — the build must stay green. |
| Changed exported tunables (e.g. `MinPointDistance` default) | Update any test that relied on the default and any test names mentioning the value. |

### Test conventions (follow the existing style)

- One test class per source class, named `<ClassName>Tests`, in namespace `ProjectEnchanter.Tests`.
- Group tests under `// ---- Section ----` headers (Stroke creation, Undo, Clear, Regression...).
- Use a small static factory (e.g. `NewModel(...)`) instead of repeating construction.
- Name tests `<Method/Behavior>_<Condition>_<ExpectedOutcome>`.
- Regression tests live at the bottom under `// ---- Regression: ... ----` and simulate complete user sessions (begin → append → end → undo...). When you fix a bug, add a regression test that would have caught it.
- After writing tests, run `dotnet test tests\project-enchanter.tests.csproj` and leave it green — no warnings from xUnit analyzers (e.g. prefer `Assert.Single`/`Assert.Empty` over `Assert.Equal(n, collection)` for sizes 0 and 1).
- Do not edit files under `.godot/` — it is generated.

## Keeping this file current

Update AGENTS.md in the same commit/session whenever you:

- Add, move, rename, or delete source or test files (update the layout table).
- Add a new build or test command, or change how the project runs.
- Introduce a new architectural pattern (e.g. another pure-logic class the node delegates to).
- Change test conventions, project structure, or anything else documented here.
- Fix a subtle configuration gotcha (like the assembly-name mismatch) — document it so the next agent doesn't rediscover it.

Keep the file concise: tables and rules, not prose. Remove advice that no
longer applies rather than letting it accumulate.
