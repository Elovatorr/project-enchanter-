# Project Enchanter

A first-person magic game prototype built with **Godot 4.7 (.NET / C#)**. Explore a small
3D room, walk up to the enchanting table, and **draw runes** to enchant a wand. Runes are
recognised by a small neural network trained on synthetic, hand-drawn-style samples — no
templates or hard-coded shapes at runtime.

## Features

- **First-person 3D room** — floor, walls, lighting, and an enchanting-table prism, with
  WASD movement, sprint, jump, gravity, and mouse look.
- **Look-at interaction** — a centre reticle and prompt appear when you aim at an
  interactable; press **E** to use it.
- **Enchanting table** — interacting presents the wand up close, large in view, and opens
  the drawing overlay.
- **Rune drawing** — freehand drawing on the overlay (the original 2D canvas scene is still
  runnable on its own).
- **ML rune recognition** — a small MLP classifies `fire`, `water`, `density`, or `unknown`.
  Recognition is invariant to **position, scale, and rotation**, tolerates hand-drawn wobble,
  and rejects scribbles.
- **Multiple runes at once** — draw each rune inside a hand-drawn circle; each circled rune
  is segmented and classified independently.
- **Wand enchanting** — a successful cast tints the wand with the element colour and
  **etches the drawn rune onto its top face**, where it stays when you pick the wand up.
- **Wand shooting** — left click while holding an enchanted wand fires its element:
  fire → a fireball, water → three jets of water, density → three green cones.

## Controls

| Input | Action |
|---|---|
| Mouse | Look |
| W A S D | Move |
| Shift | Sprint |
| Space | Jump |
| E | Interact (table, wand) |
| Left click | Shoot the wand's element effect (fireball / water jets / green cones) while holding an enchanted wand |
| Esc | Release / capture the mouse |
| Left drag | Draw (during enchanting) |
| Right click | Undo last stroke |
| Identify rune button | Classify the drawing |
| Draw demo (3 runes) button | Fill the canvas with a test case |

## Requirements

- [Godot 4.7 (.NET edition)](https://godotengine.org/download)
- [.NET SDK 8+](https://dotnet.microsoft.com/download)
- Python 3 + `numpy` (only needed to retrain the model)

## Getting started

```sh
# from the repository root
cd project-enchanter

# build the C# assembly (Godot loads the prebuilt DLL; it does not compile)
dotnet build
```

Open `project-enchanter/project.godot` in the Godot editor and press **F5** — the 3D room is
the main scene. Press **F6** with `main.tscn` open to run the standalone drawing canvas.

> **Important:** always `dotnet build` after changing C# before running the game, otherwise
> Godot silently runs the previous DLL.

## Project layout

```
project-enchanter/
├── project.godot               Godot project (main scene = scenes/room.tscn)
├── scenes/room.tscn            3D room: player, table, wand, HUD, enchanting overlay
├── main.tscn                   Standalone drawing-canvas scene
├── scripts/
│   ├── PlayerController.cs     FPS movement, mouse look, interaction raycast, left-click fire
│   ├── Hud.cs                  Reticle, interaction prompt, control hints
│   ├── IInteractable.cs        Look-at interaction interface
│   ├── EnchantingTable.cs      Opens the enchanting overlay for the wand
│   ├── Wand.cs                 Close-up pose, element colour, rune decal, pick-up, Shoot()
│   ├── WandShotFactory.cs      Element → shot descriptors (fireball / jets / cones)
│   ├── Projectile.cs           Moving projectile node; frees itself after its lifetime
│   ├── EnchantingUI.cs         Overlay hosting the drawing scene
│   ├── DrawingSurface.cs       Drawing input/render, HUD, cast signal, flash
│   ├── DrawingSurfaceModel.cs  Pure drawing/undo logic
│   ├── RuneFeatures.cs         Drawing → invariant feature vector
│   ├── RuneModel.cs            MLP forward pass
│   ├── RuneClassifier.cs       Classification API + cast threshold
│   ├── RuneModelWeights.cs     Loads weights from embedded CSV resources
│   ├── RuneTextureFactory.cs   Strokes → texture for the wand decal
│   ├── EnclosedRuneDetector.cs Circle detection and rune grouping
│   ├── RuneTemplates.cs        Reference rune shapes (fire, water, density)
│   └── RoomSelfTest.cs         In-engine room/enchanting test
├── resources/rune_model/       Trained weights as plain CSV (embedded at build)
├── tests/                      xUnit tests + in-engine test scenes
└── tools/train_rune_model.py   Trains the model (numpy) and writes the CSVs
```

## How recognition works

1. **Segment** — `EnclosedRuneDetector` finds circle-like strokes (closed, round, smooth);
   strokes inside each circle are classified as one rune.
2. **Features** — strokes are resampled (48 points by arc length), smoothed, normalised
   (centroid + RMS radius), rotated onto their principal axis (sign resolved
   deterministically), mirrored, and extended with structural stats: thinness, path length,
   corner fraction, sharpest turn, stroke count, and inter-stroke gap.
3. **Model** — features are standardised and passed through an MLP
   (102 → 160 ReLU → 4 softmax classes). Held-out accuracy is ≈ 99.8%.
4. **Cast** — a rune casts when its probability is ≥ 0.6; the screen flashes the rune's
   colour (fire red, water blue, density green).

## Retraining the model

```sh
cd project-enchanter
pip install numpy
python tools/train_rune_model.py     # rewrites resources/rune_model/*.csv
dotnet build
```

Weights live in `resources/rune_model/` as readable CSV, embedded into the assembly at build
time. Add a new rune by adding its shape to `tools/train_rune_model.py` (`RUNES` and
`OUTPUT_NAMES`), mirroring it in `scripts/RuneTemplates.cs`, and giving it a flash colour in
`DrawingSurface.FlashColorFor`. To keep the C# feature code and the trainer in sync, there is
a parity sample in the generated CSVs checked by the test suite.

## Testing

```sh
# unit tests
dotnet test tests/project-enchanter.tests.csproj

# in-engine suites (build first!)
#   res://tests/recognition_self_test.tscn  - rune recognition and drawing HUD
#   res://tests/room_self_test.tscn         - room, interaction, enchanting, wand, shooting
```

Run the scenes from the Godot editor (F6) or via an MCP/automation tool, and read the
`SELFTEST` / `ROOMSELFTEST` lines in the Godot output log.

## License

Prototype project — no license specified.
