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
| `scripts/DrawingSurface.cs` | Godot node. Input (event positions), rendering, HUD, Identify button, and per-rune flash overlay; delegates to `DrawingSurfaceModel` + `RuneClassifier`. |
| `scripts/DrawingSurfaceModel.cs` | Pure C# — drawing/undo logic. No Godot node dependencies; unit-testable. |
| `scripts/RuneFeatures.cs` | Pure C# — drawing → feature vector (resample, smooth, PCA-canonicalize, structural stats). Must mirror the Python trainer; guarded by a parity test. |
| `scripts/RuneModel.cs` | Pure C# — MLP forward pass + feature standardization. |
| `scripts/RuneClassifier.cs` | Public classification API (`Recognize` / `RecognizeDetailed`) with the cast threshold. |
| `scripts/RuneModelWeights.cs` | Loads the trained model from CSV embedded resources (see `resources/rune_model/`). |
| `resources/rune_model/` | Trained weights as plain CSV (w1, b1, w2, b2, feature_mean/std, meta, parity). Embedded into the assembly; `.gdignore`d for Godot. |
| `scripts/RuneTemplate.cs` | Pure C# record: a named rune and its reference strokes. |
| `scripts/RuneTemplates.cs` | Reference rune shapes used for training data and the in-engine self-test (currently `Fire`, a caret). |
| `scripts/RecognitionSelfTest.cs` | In-engine automated test driver, run as a scene (see "Testing in engine"). |
| `tests/project-enchanter.tests.csproj` | xUnit test project (net8.0). |
| `tests/DrawingSurfaceModelTests.cs` | Unit/regression tests for the drawing model. |
| `tests/RuneFeaturesTests.cs` | Feature pipeline tests incl. invariance + Python-trainer parity. |
| `tests/RuneClassifierTests.cs` | Classifier tests: invariance, hand-drawn tolerance, rejection. |
| `tests/recognition_self_test.tscn` | Scene that runs `RecognitionSelfTest` in-engine. |
| `tools/train_rune_model.py` | Trains the MLP (numpy) and writes `resources/rune_model/*.csv`. |

## Design principle: keep logic out of the node

Godot nodes (`Node2D`, etc.) cannot be instantiated in a test runner, so any
logic embedded in `DrawingSurface` is untestable. **The pattern is:**

- `DrawingSurface` (node) = input handling + rendering + delegation only.
- `DrawingSurfaceModel` (pure class) = all decision-making, state, and rules.

When adding new gameplay features:
1. Put the new logic in `DrawingSurfaceModel` (or a new pure class next to it).
2. Have the node delegate to it and just call `QueueRedraw()` on changes.
3. Write tests against the pure class.

## Classification pipeline (trained model)

The runes are recognized by a small **neural network** (MLP, numpy-trained,
weights compiled in). Pipeline:

1. **Resample** — strokes → points evenly spaced by arc length (48 total, split proportional to stroke length).
2. **Smooth** — 1-2-1 moving average, 2 passes (per-stroke).
3. **Canonicalize** — centroid at origin, RMS radius 1, rotate onto the PCA axis. The axis sign is ambiguous: resolve the 180° flip deterministically via radius-weighted x skew, then emit two mirror variants (the model averages their predictions).
4. **Structural features** — eigenvalue ratio (thinness), path/RMS, corner fraction, sharpest turn.
5. **Standardize + MLP** — z-score using trainer-provided mean/std, then 100 → 96 (ReLU) → classes (softmax).
6. **Classify** — argmax over the non-"unknown" classes; cast when probability ≥ `RuneClassifier.MatchThreshold` (0.6). All-zero (degenerate) feature vectors are rejected outright.
7. **Identification is manual** — strokes are not classified automatically. The player presses the on-screen **Identify rune** button, which calls `DrawingSurface.Identify()` → `TryCast()`.
8. **Feedback** — a cast flashes a full-screen overlay for `FlashDuration` (0.5 s) when confidence exceeds `FlashThreshold` (0.7). The colour is per-rune (`FireFlashColor` red, `WaterFlashColor` blue, `DefaultFlashColor` fallback). All are `[Export]` tunables on `DrawingSurface`; add new runes' colours in `FlashColorFor()`.

Runes: **fire** (one-stroke caret), **water** (two parallel waves). Current model: held-out accuracy ≈ 99.3%, fire recall ≈ 99.1%, water recall ≈ 99.4%.

### Retraining and changing runes

```sh
pip install numpy
python tools/train_rune_model.py     # rewrites resources/rune_model/*.csv
```

- Model parameters live in `resources/rune_model/` as **plain CSV** (`w1.csv` 96×100, `b1.csv`, `w2.csv`, `b2.csv`, `feature_mean.csv`, `feature_std.csv`, `meta.csv`, plus parity files). They are compiled in as embedded resources by the csproj, so the game and tests read them without file IO. Edit them by hand and rebuild if needed — but keep dimensions consistent with `meta.csv` (the loader validates shapes and fails loudly).
- Training data is synthesized in the script: the rune template(s) from `RuneTemplates.cs` (mirrored manually in Python as `FIRE`) plus augmentation (rotation, scale, translation, jitter, densification) and an "unknown" class of scribbles/letters/shapes.
- **When adding a rune**: add its points to `tools/train_rune_model.py` (add to `RUNES` and `OUTPUT_NAMES`), mirror it in `scripts/RuneTemplates.cs`, add a flash colour in `DrawingSurface.FlashColorFor()`, retrain, then add classifier tests (classifies at transforms, isn't confused with existing runes, and near-misses are rejected).
- **Parity guard**: the trainer embeds a reference feature vector (`parity_features.csv`) computed for the fire template; `RuneFeaturesTests.Extract_MatchesPythonTrainerParitySample` fails if the C# feature code drifts. If you change `RuneFeatures.cs`, change `extract_features` in the trainer identically and retrain.

Gotchas learned the hard way (do not regress these):

- PCA axes have no sign — features were not rotation-invariant until the skew-based flip was added.
- Degenerate drawings (single point / zero size) produce all-zero features, which are out of distribution (the old model scored them "fire"); always reject them before inference.
- Absolute jitter on a small shape is disproportionately large: generate test jitter relative to the glyph size.
- Features must be standardized before the MLP; unstandardized structural features dominate training.
- Synthetic "scribbles" must be realistic (continuous wandering strokes, spirals, waves) — spiky random jumps are not representative and poison the unknown class.

## Testing in engine

The in-engine self-test drives the real `DrawingSurface` (input → model → features → classifier) and prints `SELFTEST` lines:

```sh
dotnet build    # REQUIRED: Godot loads the prebuilt DLL; it does not compile
# then run res://tests/recognition_self_test.tscn in the Godot editor,
# or via the godot MCP: run_project(scene: "res://tests/recognition_self_test.tscn")
# and read the log with get_debug_output / the app_userdata log file.
```

It draws fire and water at various transforms (expect the right rune cast + its flash colour: red for fire, blue for water) and circle/line/wave (expect no cast and no flash). It calls `DrawingSurface.Identify()` after drawing, matching the button flow. **Always `dotnet build` before running** — otherwise the game silently runs the previous DLL (this has caused false debugging detours).

## Commands

```sh
# Build the game assembly (or click the hammer/Build button in the Godot editor)
dotnet build

# Run the tests
dotnet test tests\project-enchanter.tests.csproj

# Retrain the rune classifier (rewrites resources/rune_model/*.csv)
python tools/train_rune_model.py

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
| Changed `DrawingSurface` (node) only — input mapping, rendering | Pure engine behavior is not unit-testable. Verify with the in-engine self-test scene and/or manually in Godot (F5); do not fake engine tests with mocks. If the change altered *when* the classifier is called, check that the model tests still cover the new sequence. |
| Changed `RuneFeatures.cs` (feature math) | Mirror the change in `tools/train_rune_model.py`'s `extract_features`, retrain (this refreshes the parity sample), and re-run the invariance tests. The parity test will catch drift otherwise. |
| Changed the model/training (shapes, augmentation, architecture, epochs) | Retrain, check held-out accuracy in the trainer output, and re-run `RuneClassifierTests`. Keep held-out accuracy ≳ 99% and fire recall ≳ 98%. |
| Changed `RuneClassifier` threshold or API | Update the threshold-related tests and the HUD self-test expectations. |
| Added a rune | See "Retraining and changing runes": trainer `FIRE`/shapes + `OUTPUT_NAMES` + `RuneTemplates.cs` + retrain + classifier tests for the new rune and rejection of the others. |
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
