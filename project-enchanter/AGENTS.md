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
| `main.tscn` | Drawing scene: root `Node2D` "Main" with `DrawingSurface` attached (run with F6). |
| `scenes/room.tscn` | **Main scene**: first-person 3D room — floor, four walls, sun light, player, wand, HUD, enchanting and inventory overlays. |
| `scripts/PlayerController.cs` | `CharacterBody3D` first-person controller: WASD/Shift/Space + mouse look, Esc toggles capture, E raycasts interactions. |
| `scripts/IInteractable.cs` | Interface for look-at interactions (`InteractionPrompt`, `Interact`). |
| `scripts/Hud.cs` | Reticle, interaction prompt, and control hint (CanvasLayer). |
| `scripts/Wand.cs` | Wand in the inventory: stored/equipped state, close-up enchanting pose, element colour, etched rune decal, `Shoot()`. |
| `scripts/InventoryModel.cs` | Pure C# — inventory item selection (add/select/wrap/remove), unit-testable. |
| `scripts/InventoryUI.cs` | Tab-toggled inventory overlay: Items tab (hold/put away) and Enchant tab (opens `EnchantingUI` for the selected wand). |
| `scripts/RuneTextureFactory.cs` | Rasterizes drawn strokes into a transparent texture for the wand's rune decal. |
| `scripts/WandShotFactory.cs` | Pure C# — maps an enchanted element to shot descriptors (fire fireball, water jets, density cones). |
| `scripts/Projectile.cs` | `Node3D` shot projectile: travels along its launch direction, frees itself after its lifetime; group `projectile`. |
| `scripts/EnchantingUI.cs` | Full-screen overlay hosting the drawing scene; reports cast runes + strokes and closes. |
| `scripts/RoomSelfTest.cs` | In-engine test for the room + inventory + enchanting + shooting flow (see "Testing in engine"). |
| `tests/room_self_test.tscn` | Scene that runs `RoomSelfTest`. |
| `scripts/DrawingSurface.cs` | Godot node. Input (event positions), rendering, HUD, Identify button, and per-rune flash overlay; delegates to `DrawingSurfaceModel` + `RuneClassifier`. |
| `scripts/DrawingSurfaceModel.cs` | Pure C# — drawing/undo logic. No Godot node dependencies; unit-testable. |
| `scripts/RuneFeatures.cs` | Pure C# — drawing → feature vector (resample, smooth, PCA-canonicalize, structural stats). Must mirror the Python trainer; guarded by a parity test. |
| `scripts/RuneModel.cs` | Pure C# — MLP forward pass + feature standardization. |
| `scripts/RuneClassifier.cs` | Public classification API (`Recognize` / `RecognizeDetailed`) with the cast threshold. |
| `scripts/RuneModelWeights.cs` | Loads the trained model from CSV embedded resources (see `resources/rune_model/`). |
| `resources/rune_model/` | Trained weights as plain CSV (w1, b1, w2, b2, feature_mean/std, meta, parity). Embedded into the assembly; `.gdignore`d for Godot. |
| `shaders/water_jet.gdshader` | Procedural water-jet material: flowing fbm streaks/foam, fresnel rim, tip fade, subtle vertex whip. Used by both water cylinder layers. |
| `scripts/EnclosedRuneDetector.cs` | Pure C# — finds circle strokes and groups the runes drawn inside them (multi-rune drawings). |
| `scripts/RuneTemplate.cs` | Pure C# record: a named rune and its reference strokes. |
| `scripts/RuneTemplates.cs` | Reference rune shapes used for training data and the in-engine self-test: `Fire` (caret), `Water` (two stacked waves), `Density` (zigzag + bar + V). |
| `scripts/RecognitionSelfTest.cs` | In-engine automated test driver, run as a scene (see "Testing in engine"). |
| `tests/project-enchanter.tests.csproj` | xUnit test project (net8.0). |
| `tests/DrawingSurfaceModelTests.cs` | Unit/regression tests for the drawing model. |
| `tests/RuneFeaturesTests.cs` | Feature pipeline tests incl. invariance + Python-trainer parity. |
| `tests/RuneClassifierTests.cs` | Classifier tests: invariance, hand-drawn tolerance, rejection. |
| `tests/WandShotFactoryTests.cs` | Shot tests: per-element projectile count/shape/colour, forward direction, rejection of unknown elements. |
| `tests/InventoryModelTests.cs` | Inventory selection tests: add/select, wrap-around, removal keeps the selection valid. |
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
4. **Structural features** — eigenvalue ratio (thinness), path/RMS, corner fraction, sharpest turn, stroke count (normalized), mean inter-stroke gap/RMS.
5. **Standardize + MLP** — z-score using trainer-provided mean/std, then 102 → 160 (ReLU) → classes (softmax).
6. **Classify** — argmax over the non-"unknown" classes; cast when probability ≥ `RuneClassifier.MatchThreshold` (0.6). All-zero (degenerate) feature vectors are rejected outright.
7. **Multi-rune drawings** — if the drawing contains circles, `EnclosedRuneDetector.Find` identifies circle-like strokes: closed (start/end gap < 65% of radius), round/oval (radius std < 26%, range < 38%), covering ≥ 70% of directions, path length 0.5–1.8× circumference, and with **at most 2 sharp turns** (rejects polygons while allowing hand-drawn wobble/ellipses). Non-circle strokes lying ≥ 85% inside a circle belong to it (smallest enclosing circle wins; nested circles cast once). Each group is classified independently and all matches cast together. Flashes run in succession in **left-to-right** order (sorted by circle centre x), one colour per rune. The HUD shows the detected `Circles:` count to help diagnose circle-detection problems.
8. **Identification is manual** — strokes are not classified automatically. The player presses the on-screen **Identify rune** button, which calls `DrawingSurface.Identify()`: circled runes are classified individually; with no circles, the whole drawing is classified as a single rune. A **Draw demo (3 runes)** button (`DrawDemoRunes()`) fills the canvas with circled fire, water and density side by side.
9. **Feedback** — a cast flashes a full-screen overlay for `FlashDuration` (0.5 s) when confidence exceeds `FlashThreshold` (0.7). The colour is per-rune (`FireFlashColor` red, `WaterFlashColor` blue, `DensityFlashColor` green, `DefaultFlashColor` fallback); multiple runes flash their colours in sequence. All are `[Export]` tunables on `DrawingSurface`; add new runes' colours in `FlashColorFor()`.

Runes: **fire** (one-stroke caret), **water** (two stacked waves; each rises to a sharp peak, dips through a trough, then climbs high to the right), **density** (zigzag + horizontal bar + lower V). Current model: held-out accuracy ≈ 99.85%, with fire 99.6%, water 100%, density 100%, unknown 99.8%.

### Retraining and changing runes

```sh
pip install numpy
python tools/train_rune_model.py     # rewrites resources/rune_model/*.csv
```

- Model parameters live in `resources/rune_model/` as **plain CSV** (`w1.csv` 160×102, `b1.csv`, `w2.csv`, `b2.csv`, `feature_mean.csv`, `feature_std.csv`, `meta.csv`, plus parity files). They are compiled in as embedded resources by the csproj, so the game and tests read them without file IO. Edit them by hand and rebuild if needed — but keep dimensions consistent with `meta.csv` (the loader validates shapes and fails loudly).
- Training data is synthesized in the script: the rune template(s) from `RuneTemplates.cs` (mirrored manually in Python as `FIRE`) plus augmentation (rotation, scale, translation, jitter, densification) and an "unknown" class of scribbles/letters/shapes.
- **When adding a rune**: add its points to `tools/train_rune_model.py` (add to `RUNES` and `OUTPUT_NAMES`), mirror it in `scripts/RuneTemplates.cs`, add a flash colour in `DrawingSurface.FlashColorFor()`, retrain, then add classifier tests (classifies at transforms, isn't confused with existing runes, and near-misses are rejected).
- **Parity guard**: the trainer embeds a reference feature vector (`parity_features.csv`) computed for the fire template; `RuneFeaturesTests.Extract_MatchesPythonTrainerParitySample` fails if the C# feature code drifts. If you change `RuneFeatures.cs`, change `extract_features` in the trainer identically and retrain.

Gotchas learned the hard way (do not regress these):

- PCA axes have no sign — features were not rotation-invariant until the skew-based flip was added.
- Degenerate drawings (single point / zero size) produce all-zero features, which are out of distribution (the old model scored them "fire"); always reject them before inference.
- Absolute jitter on a small shape is disproportionately large: generate test jitter relative to the glyph size.
- Features must be standardized before the MLP; unstandardized structural features dominate training.
- Synthetic "scribbles" must be realistic (continuous wandering strokes, spirals, waves) — spiky random jumps are not representative and poison the unknown class.
- **UI visibility**: use a plain `Control` (not a nested `CanvasLayer`) for the drawing surface's buttons/flash — a nested CanvasLayer does not hide with its parent, so its buttons stay active outside enchanting.
- **Circular rune parts**: if a rune has closed loops, keep each loop in the *same stroke* as a longer line. A standalone closed stroke is treated by `EnclosedRuneDetector` as a rune-enclosing circle — the bare rune would report "no rune inside N circle(s)", and when circled the loops would be dropped from the region's strokes. `EnclosedRuneDetectorTests.Find_WaterTemplate_IsNotDetectedAsCircles` guards that the current templates contain no such strokes.
- **Input layering**: full-screen overlay backgrounds must use `MouseFilter = Ignore`; `Stop` swallows mouse events before `_UnhandledInput`, which makes drawing impossible. Position UI with `SetAnchorsAndOffsetsPreset`. A `Control` whose parent is a `Node2D` gets **no** viewport-sized rect (`SetAnchorsPreset(FullRect)` alone leaves it 0×0, putting children at negative coordinates and breaking buttons) — give it an explicit `Size = GetViewportRect().Size` and update it on `Viewport.SizeChanged`.

## Testing in engine

The in-engine self-test drives the real `DrawingSurface` (input → model → features → classifier) and prints `SELFTEST` lines:

```sh
dotnet build    # REQUIRED: Godot loads the prebuilt DLL; it does not compile
# then run res://tests/recognition_self_test.tscn in the Godot editor,
# or via the godot MCP: run_project(scene: "res://tests/recognition_self_test.tscn")
# and read the log with get_debug_output / the app_userdata log file.
```

It draws fire, water and density at various transforms (expect the right rune cast + its flash colour: red / blue / green), circle/line/wave (expect no cast and no flash), multi-rune cases (two circled runes → both cast; three circled runes → all three in left-to-right order; **imperfect hand-drawn circles** with gap/ovalness/wobble; circled unknown shape → no cast; circled fire alone → fire), and the three-rune demo-button case. It calls `DrawingSurface.Identify()` after drawing, matching the button flow. **Always `dotnet build` before running** — otherwise the game silently runs the previous DLL (this has caused false debugging detours).

## 3D room scene

`scenes/room.tscn` is the main scene (a 20 × 14 room, walls 3 m high, floor at y = 0):

- **Player** (`CharacterBody3D` + `PlayerController.cs`): spawns at (0, 0.1, 4); capsule 1.8 m, eye height 1.6 m. WASD move, Shift sprint, Space jump, mouse look, Esc toggles mouse capture, **E interacts**. Physical key checks — no input-map actions required.
- **Interaction**: `PlayerController` raycasts 3 m along the camera forward each physics frame; if the hit node implements `IInteractable`, its `InteractionPrompt` shows in the HUD and E calls `Interact`. Note group lookups (hud/player/wand) must be lazy — node `_Ready` order is not guaranteed.
- **Inventory** (`InventoryUI.cs` + `InventoryModel.cs`): Tab toggles a CanvasLayer overlay (layer 105) with two tabs. **Items** lists the carried wands (from group `wand`) and a Hold/Put away button (`Wand.Equip`/`Wand.PutAway`). **Enchant** opens `EnchantingUI` for the selected wand, passing `Wand.CurrentStrokes` so the canvas is prefilled; on cast/cancel the wand returns to the hand (or storage) and the inventory reopens. Tab is handled in `_Input` (before GUI focus traversal) and ignored while `EnchantingUI` is visible. Opening pauses player controls and shows the mouse; closing restores both. The old `EnchantingTable` node/script was removed.
- **Wand**: starts **stored** (hidden, collision off) in the inventory. `Equip(player)` reparents it to the camera at the hand pose; `PutAway()` returns it to its home transform and hides it. While enchanting it is shown close-up at ~1.4× scale with its top face toward the viewer (`ShowCloseUp`), and restored afterwards (`EndCloseUp`) to the hand or storage. A cast call `Enchant(element, strokes)` tints it, rasterizes the strokes (`RuneTextureFactory`) onto the `RuneDecal` plane and stores them in `CurrentStrokes` (used to prefill the editor).
- **HUD** (`Hud.cs`): centre reticle (two bars), bottom interaction prompt, control hint.
- **Shooting**: while the wand is `Held`, a left click (`InputEventMouseButton`, `ControlsEnabled` only) calls `Wand.Shoot(player)`, which spawns `Projectile` nodes straight ahead of the camera from `WandShotFactory.For(element)`: fire → one red sphere (fireball), water → a laser-like jet of two concentric long cylinders (a bright ~opaque core with strong emission inside a wider translucent sheath; both taper toward the tip and travel at 26 m/s), density → three green cones (`CylinderMesh` with `TopRadius = 0`). Water layers use `ShaderMaterial` with `shaders/water_jet.gdshader` (flowing foam streaks, fresnel rim, muzzle/tip fades, gentle vertex whip); `Projectile.CreateMaterial` falls back to the plain emissive material if the shader fails to load. Projectiles travel along the camera's forward direction (plus the descriptor's camera-space `Boost`) and `QueueFree()` after `Lifetime`; they live in group `projectile`.
- **Enchanting overlay** (`EnchantingUI.cs`): lightly dimmed background (so the wand preview stays visible) + title/hint/Cancel, hosting `res://main.tscn`. `DrawingSurface` raises `CastCompleted(runes, strokes)`; the overlay closes and invokes `Open`'s callback with both. Cancel invokes the optional `onCancel` (used to restore the wand).
- **Geometry**: floor plane (20 × 14) with a thin box collision; four wall boxes with collisions.
- **Lighting**: one `DirectionalLight3D` with shadows; materials are plain `StandardMaterial3D` colours.

Add new 3D content as nodes in this scene (or instanced sub-scenes), keeping collisions alongside meshes (`StaticBody3D` + `CollisionShape3D`). Verify by running the project and checking the log for errors; the player prints a "Player ready" line on startup.

The room/enchanting flow has an automated in-engine test:

```sh
dotnet build
# run res://tests/room_self_test.tscn (or via the godot MCP run_project)
```

It instantiates the room and checks the inventory starts closed and the wand stored. It then opens the inventory with a real Tab press, equips the wand via the Hold button, switches to the Enchant tab via a real click and opens the drawing overlay; it draws the demo runes, clicks Identify, and checks the wand becomes fire-enchanted, the overlay closes, the inventory reopens and the wand returns to the hand. Editing is covered too (canvas prefilled, Cancel keeps the element) and so is closing with Tab. The shooting checks fire through the real left-click pipeline: fire spawns one red sphere whose direction and displacement align with the camera forward (dot > 0.99); water spawns two blue cylinder layers (core + sheath) that fly dead ahead and use the water-jet `ShaderMaterial`; density spawns three green cones flying forward; all despawn after their lifetime (`GetNodesInGroup("projectile")` is the counter — use deltas since earlier shots stay alive). Finally it re-enchants from the inventory (loaded water rune) and checks the wand returns to the hand.

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

**Training terminal preference:** launch training in a **visible console window** so the user can watch epoch progress — on Windows use
`Start-Process python -ArgumentList "tools\train_rune_model.py" -WorkingDirectory <project>`
rather than a hidden/background shell. Training takes a few minutes.

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
| Changed `EnclosedRuneDetector` (circle/enclosure geometry) | Update `EnclosedRuneDetectorTests` (circle fit, enclosure, nesting, multi-circle, invariance) and add a self-test case if the on-screen flow changes. |
| Changed shooting (`WandShotFactory` descriptors, `Projectile`, `Wand.Shoot`, left-click wiring) | Update `WandShotFactoryTests` (per-element count/shape/colour/shader) and the ROOMSELFTEST shot checks; count projectiles as **deltas** — earlier shots stay alive until their lifetime elapses. |
| Changed `shaders/water_jet.gdshader` | Keep the uniform names (`color`, `opacity`, `emission_strength`) in sync with `Projectile.CreateMaterial`; run the room self-test and check the Godot log for shader compile errors. |
| Changed the enchant-edit flow (`InventoryUI.BeginEnchanting`, `Wand.ShowCloseUp`/`EndCloseUp`, `EnchantingUI.Open`, `DrawingSurface.LoadStrokes`) | Update `DrawingSurfaceModelTests` for `Load` and the ROOMSELFTEST edit checks (canvas prefill, cancel keeps the enchantment, re-enchant while held returns to the hand). |
| Changed the inventory (`InventoryModel`, `InventoryUI`, `Wand.Equip`/`PutAway`, Tab handling) | Update `InventoryModelTests` and the ROOMSELFTEST inventory checks (Tab toggle, hold/put away, Enchant tab, overlay/inventory hand-off). |
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
