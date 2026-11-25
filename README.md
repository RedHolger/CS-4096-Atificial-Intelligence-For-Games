# Orchard of the Last Season

**CS4096 – Artificial Intelligence for Games**  
University of Limerick | Team Project

An orchard-defence game developed in Unity, demonstrating multiple Game AI techniques working together in real time.

## Quick Start – Play the Game

### Option 1: Unity Editor (Recommended)

1. [Download the game project](https://github.com/RedHolger/CS-4096-Atificial-Intelligence-For-Games/archive/refs/heads/main.zip).
2. Extract the ZIP file.
3. Install [Unity Hub](https://unity.com/download).
4. Install Unity Editor **6000.3.25f1**.
5. In Unity Hub, select **Add → Add project from disk**.
6. Select the `OrchardOfTheLastSeason` folder.
7. Open the project and press **Play**.
8. Click **Begin Night 1**.

No additional coding or manual scene configuration is required.

### Option 2: Standalone Game (macOS/Linux)

Open a terminal inside the repository folder:

```bash
chmod +x play.sh build.sh
./play.sh
```

The script automatically builds and launches the game using the installed Unity Editor.

## Game Controls

| Key | Action |
|---|---|
| WASD / Arrow Keys | Move the farmer |
| Space | Swing the scythe |
| F | Catch falling fruit |
| Tab | Display Utility AI scores |
| Enter | Start game |

## Game Objective

- Protect the six fruit trees.
- Collect falling fruit.
- Defend the orchard against enemies.
- Survive successive nights.
- Observe autonomous AI agents through the live sidebar and event log.

## AI Techniques

| Character / System | AI Technique |
|---|---|
| Pip | Behaviour Tree |
| Thistle | Finite State Machine |
| Shambler | Utility AI and Cooperation |
| Stalker | Perception and Predictive Pursuit |
| Birds | Flocking / Boids |
| Navigation | A* Pathfinding and Steering |
| Environment | Procedural Map Generation |

Press **Tab** during gameplay to inspect Shambler decision scores.

## Documentation

- [Game Blueprint](Docs/Orchard-Game-Blueprint.html)
- [Code Walkthrough](Docs/Orchard-Code-Walkthrough.html)
- [AI Source Code](OrchardOfTheLastSeason/Assets/Orchard/Scripts)
- [Technical Documentation](OrchardOfTheLastSeason/README.md)

Download and open the HTML documentation in a browser for interactive viewing.

## Supported Platforms

Windows, macOS and Linux through Unity Editor.

Standalone builds require the corresponding Unity Build Support module.

---

Developed as a team project for CS4096 at the University of Limerick.
