# Orchard of the Last Season — CS4096 AI for Games

Unity 6 (`6000.3.25f1`) implementation of **Orchard of the Last Season**, an orchard-defense game showcasing multiple classical and modern Game AI techniques working together in real time.

## Quick Start (Executable Environment)

### 1. Run the Standalone Executable (macOS)
```bash
./play.sh
# or:
make run
```
If `Builds/macOS/OrchardOfTheLastSeason.app` is not built yet, `./play.sh` automatically builds it via Unity batchmode and launches it.

Useful options:
- `./play.sh --build` — force a fresh rebuild and launch the standalone app
- `./play.sh --console` — run the executable attached to your terminal
- `./play.sh --unity` — open `OrchardOfTheLastSeason` in Unity Editor (`6000.3.25f1`)
- `./play.sh --docs` — open the HTML blueprint and code walkthrough in your browser

### 2. Build Manually from CLI
```bash
./build.sh                    # builds Builds/macOS/OrchardOfTheLastSeason.app
./build.sh --target linux64   # builds Builds/Linux/OrchardOfTheLastSeason.x86_64
./build.sh --target win64     # builds Builds/Windows/OrchardOfTheLastSeason.exe
./build.sh --target webgl     # builds Builds/WebGL
./build.sh --prepare-only     # creates/updates Main.unity and ProjectSettings
```
Set `UNITY_BIN=/path/to/Unity` if your Unity 6 installation is in a custom location.
> Note: Cross-platform builds require the corresponding Unity Build Support module to be installed in Unity Hub (e.g. Mac Build Support, Linux Build Support, Windows Build Support, or WebGL Build Support).

### 3. Open Directly in Unity Hub
1. Open **Unity Hub** -> **Add** -> **Add project from disk** -> select `OrchardOfTheLastSeason`.
2. Open with Unity `6000.3.25f1` (or any Unity 6 release).
3. Press **Play** (or use menu **Orchard > Build macOS App** to generate a standalone `.app`).

## Controls
- `W A S D` / `Arrow Keys` — Move the farmer
- `Space` — Swing scythe (attack enemies / pluck fruit from trees)
- `F` — Catch falling fruit
- `Tab` — Toggle Shambler Utility AI desirability bars
- `Enter` — Start game on title screen

## AI Techniques & Architecture
| Technique | Implementation |
|---|---|
| **Behaviour Tree** (Pip repairs the weakest tree / retreats to farmer when hurt) | `OrchardOfTheLastSeason/Assets/Orchard/Scripts/PipBehaviourTree.cs` |
| **Finite State Machine** (Thistle: `Wander` / `Feed` / `Flee`) | `OrchardOfTheLastSeason/Assets/Orchard/Scripts/ThistleFSM.cs` |
| **Utility AI** (Shambler desirability scoring + pack coordination) | `OrchardOfTheLastSeason/Assets/Orchard/Scripts/ShamblerUtilityAI.cs` |
| **A\* Pathfinding & Perception** (8-dir octile A\*, path smoothing, LoS sight cone) | `OrchardOfTheLastSeason/Assets/Orchard/Scripts/AStar.cs` |
| **Steering & Flocking (Boids)** (Stalkers, Scatterlings, Black Birds) | `OrchardOfTheLastSeason/Assets/Orchard/Scripts/Enemies.cs` |
| **Procedural Map Generation & Validation** (trees, hills, mud, nightly fallen logs) | `OrchardOfTheLastSeason/Assets/Orchard/Scripts/MapGenerator.cs` |

For full design notes, test measurements, and step-by-step documentation:
- [START-HERE.txt](START-HERE.txt)
- [OrchardOfTheLastSeason/README.md](OrchardOfTheLastSeason/README.md)
- `Docs/Orchard-Game-Blueprint.html`
- `Docs/Orchard-Code-Walkthrough.html`

## Repository Layout
```text
.
├── Docs/                        # HTML Blueprint & Code Walkthrough infographics
├── OrchardOfTheLastSeason/      # Unity 6 (6000.3.25f1) project
│   ├── Assets/Orchard/Editor/   # Editor setup, sharp GameView, and CLI/Menu builder
│   ├── Assets/Orchard/Scenes/   # Main.unity bootstrap scene for standalone builds
│   ├── Assets/Orchard/Scripts/  # Core game loop, renderer, UI, mapgen, and AI agents
│   ├── Assets/Resources/        # Pixel-art sprites (.bytes)
│   ├── Packages/                # Unity package manifest & lockfile
│   └── ProjectSettings/         # Unity project & Gamma color space configuration
├── START-HERE.txt               # Quick-start guide from original package
├── build.sh                     # CLI Unity standalone builder
├── play.sh                      # Build-if-needed & launch executable wrapper
└── Makefile                     # Convenience targets (run, build, unity, docs, clean)
```
