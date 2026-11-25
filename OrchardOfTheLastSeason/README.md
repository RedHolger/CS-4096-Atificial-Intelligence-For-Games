# Orchard of the Last Season (Unity port)

Exact Unity replica of the HTML version: same map generation, same AI, same sprites,
same sidebar, HUD, event log, overlays and night transitions.

## How to open and play
1. Unzip this folder anywhere.
2. Open Unity Hub, click Add, then "Add project from disk", and select the
   `OrchardOfTheLastSeason` folder.
3. Open it with Unity 6000.3.25f1 (any Unity 6 version works).
4. Press Play. No scene setup is needed: the game builds itself in whatever scene is open.
   The first time the project opens it switches Color Space to Gamma so colours
   blend exactly like the browser.

## Sharp picture (no blur)
The board is rendered at exactly the pixels it covers on screen, and the pixel-art sprites use
hard pixel edges. In the Game view also make sure Unity itself is not scaling the picture:
- open the aspect dropdown (says "Free Aspect") and UNTICK "Low Resolution Aspect Ratios" (Mac Retina)
- pick "Full HD (1920x1080)" and drag the Scale slider all the way left (smallest value), or use Play Maximized

## Sharp picture (important on a Mac)
The board is drawn at your screen's real resolution, so it is crisp at any size. What makes it blurry
is Unity's Game view option "Low Resolution Aspect Ratios" (it shows as "Scale 2x" on Retina screens:
the game is drawn at half resolution and zoomed). The project turns it off for you on open and on
Play (menu Orchard > Make Game View Sharp does it again). If it is still on: Game view aspect menu
(the "Free Aspect" dropdown) > untick "Low Resolution Aspect Ratios", then drag Scale fully left (1x).

## Recording a 16:9 video
The game is laid out for exactly 16:9: a slim sidebar of 188 px (15%) plus the landscape board
(1064 x 704, 85%; 24 x 16 cells) make 1252 x 704, so it fills a 1920 x 1080 frame edge to edge.
1. In the Game view, open the resolution dropdown (top left, "Free Aspect") and pick
   "Full HD (1920x1080)" (or add it with + as a Fixed Resolution 1920 x 1080).
2. Set the Game view to "Play Maximized" (or "Play Focused" then maximise the tab with Shift+Space).
3. Press Play and record the screen (e.g. Cmd+Shift+5 on a Mac), or use Unity's free Recorder
   package (Window > Package Manager > Unity Registry > Recorder), Movie output, 1920 x 1080, 60 fps.

## Visual design
The look of the board was upgraded:
- moonlit night with a lantern glow around the farmer, soft vignette, fireflies, lightning flashes
- lawn-style checkered grass, rocky hills that merge into ridges, puddles with rain ripples and splashes
- falling leaves when a tree is bitten, green sparkles and a dotted line when Pip repairs a tree
- hit sparks, puffs when enemies die, feathers when birds die, glowing fallen fruit
- colour-coded tree health bars, enemy health bars, a red "!" when a Stalker spots you,
  a small state dot over Thistle (yellow = Feed, blue = Flee), red eyes on bad birds
- a moon on the "Night N" screen
- drifting ground fog and a soft swoosh trail on the scythe
- redesigned sidebar: moon badge, 13-night progress track, "time until dawn" countdown,
  stat cards, colour-coded event log
- AI AGENTS panel with a technique tag on each agent (Behaviour tree, FSM, A* path, Perception,
  Flocking) and a LIVE status chip that shows what each AI is doing right now
  (Pip: Repairing / Following, Thistle: Wander / Feed / Flee, Stalker: "Spotted you!", counts on field)
- start, win and lose screens as a centred card with the AI techniques listed

## Controls
- WASD or Arrow keys: move the farmer
- Space: swing the scythe
- F: catch a falling fruit
- Click "Begin Night 1" to start, and "Play again" / "Try again" to restart

## Where each AI technique lives (Assets/Orchard/Scripts)
| Technique | File |
|---|---|
| Behaviour Tree (Pip repairs the weakest tree, tie goes to the first attacked) | PipBehaviourTree.cs |
| Finite State Machine (Thistle: Wander / Feed / Flee) | ThistleFSM.cs |
| Utility AI / desirability (Shambler decides what it wants) + A* pathfinding to get there | ShamblerUtilityAI.cs, AStar.cs |
| Perception, sight range + line of sight (Stalkers) | AStar.cs (Perception), Enemies.cs |
| Flocking / boids (yellow scatterlings and black birds) | Enemies.cs (Bird) |
| Procedural generation + reachability validation (fixed hills, bushes, trees; nightly mud and branches) | MapGenerator.cs |
| Game loop, waves, nights, input | OrchardGame.cs |
| Drawing of the board | GameRenderer.cs |
| Sidebar, HUD, event log, overlays | OrchardUI.cs |

Sprites are in Assets/Resources/OrchardSprites (the same images as in the HTML).

## AI refinements (same techniques, done better)
Measured over 12 seeded games with the same scripted player, before -> after:
enemies stuck on terrain 10.2% -> 0.6% of walking time, shambler time to reach a tree 4.4s -> 3.2s,
Pip target switches 119 -> 67 per game, Thistle's Flee state 0 -> about 9 times per game.

- A* pathfinding (AStar.cs, Enemies.cs): 8 directions with diagonal cost sqrt(2) and no corner cutting,
  octile heuristic, multi-goal search so a shambler picks the tree that is cheapest to WALK to,
  string-pulling path smoothing that will not cut through mud the path avoided, and it no longer
  steps back to its own cell centre every re-plan.
- Perception (Enemies.cs Stalker, AStar.cs Perception): sight line sampled every 5px (no gaps at
  diagonals), hysteresis (keeps you until 20% past sight range), memory of your last seen position
  for 3 s ("Stalker lost sight, searching"), and it walks with A* so hills and bushes never trap it.
- Behaviour tree (PipBehaviourTree.cs): Go-to and Repair are separate nodes that return Running,
  Pip finishes a repair before leaving (switches only if another tree is 15% weaker),
  first-attacked tie-break kept, follow-the-farmer uses hysteresis and arrive steering (no jitter).
- Finite state machine (ThistleFSM.cs): enemies now bite back, so Flee actually happens;
  Feed has enter/leave hysteresis (6 / 7.5 cells) and sticks to its target; Wander patrols the trees.
- Flocking (Enemies.cs Bird): separation weighted by distance, alignment on heading, birds spread
  over different fruits instead of all diving at one, black birds pursue (lead) the nearest yellow.
- Procedural generation + validation (MapGenerator.cs): layouts must also have at least 3 open,
  connected edge cells on every side, and enemies only spawn where they can reach the orchard.

### Second refinement pass
Measured the same way, previous -> now: enemies stacked on each other 0.08 -> 0.00 pairs per frame,
stuck time 0.57% -> 0.44%, average night reached by the scripted player 10.8 -> 11.5.
- A*: binary-heap open list (O(log n)), tie-break towards the goal, re-plans staggered between walkers,
  and immediate re-plan when a walker has not moved for half a second.
- Steering: ground enemies keep apart (separation) and take their own spot around a tree (slots),
  so they ring the tree instead of piling up on one point.
- Perception: 140 degree view cone (plus close-range noticing within 1.5 cells), the stalker faces
  where it walks, and it leads its chase to where the farmer is running.
- FSM: Thistle picks the most urgent enemy (one chewing a tree counts 2.5 cells closer) and leads her chase.
- Flocking: neighbour field of view (ignores flock-mates right behind) and soft board edges.
- Map validation: every tree must have at least 4 open cells around it.

## Map: trees off the edges, fallen logs on the paths

- Trees are placed at least 2 cells from every edge, so the whole crown and health bar are always on screen.
- Each night 5 fallen logs (2 cells long) are dropped across the paths enemies use: the game finds an
  A* route from a random edge cell to a tree and lays a log across it, at right angles to the route.
- Logs block ground enemies (Shamblers, Stalkers) like hills and bushes, so they re-plan with A* and walk round.
  The farmer, Pip, Thistle and the birds can step or fly over them.
- A log is only kept if the map is still valid: every tree reachable (BFS), every side of the board
  open for spawns, and every tree with at least 4 free cells around it.
- Tested on 300 random maps: no tree within 2 cells of an edge, no tree cut off, 5 logs every night.

## Scatterlings, sunrise and tree revival
- When a bad bird catches a scatterling it tumbles out of the sky and lies on the grass (with little
  dizzy stars) until dawn. Scatterlings are never replaced: next night only the survivors fly.
  Protect them by hitting bad birds with the scythe; they also dodge bad birds by themselves now
  (predator avoidance), and a bad bird rests 10 s after a catch.
- Every 200 fruit the scatterlings save brings the longest-fallen tree back to life (golden bloom).
  Progress is the yellow card in the sidebar. The number is C.REVIVE_COST in Core.cs; there are at
  most 4 fruit per tree per night, so 200 is a long-game goal. Lower it (e.g. 50) for a faster reward.
- Between nights the board shows a little sky scene: the sun rises ("Dawn"), crosses the sky and
  sets ("Night N"), with clouds, stars and the orchard on the hills (3.4 s).

## Shambler: utility AI (desirability)
Before, the Shambler only walked to the nearest tree (A to B). Now every 0.25 s it scores how much it
WANTS each option (0 to 1) and does the best one. Each score multiplies a few considerations, with a
compensation factor so options with more considerations are not unfairly punished. The option it is
already doing gets +0.12 so it does not flip-flop.

| Option | What it does | Considerations |
|---|---|---|
| Raid tree | walk to a tree and gnaw it | distance, how weak the tree is, is it guarded (farmer or Thistle near), my health |
| Eat fruit | eat a fallen fruit, heals +3 hp | distance, how hurt I am, is it safe |
| Pack a dog | gang up on Pip or Thistle | can I see the dog, distance, how many shamblers are already on it (0, 1, 2+), can another join me, is the dog in our way (Pip repairing my tree, Thistle biting us), dog weakness, my health |
| Hit farmer | hit back, knocks him back and dazes him 0.55 s | did he hit me in the last 2 s, how close he is, my health |
| Flee | run to the safest reachable spot (A*), heal slowly | how hurt I am, how close danger is |

Rules that tie it to the other characters:
- A dog only takes damage while 2 or more Shamblers bite it at the same time (pack rule). One alone does nothing.
- Shamblers only know about dogs, the farmer and other Shamblers they can SEE (7 cells, no hill in the way).
  They read each other's current choice, so one that sees another already on a dog comes to help.
- Pip's behaviour tree has a new top branch: when a pack has hurt him he runs to the farmer to recover,
  so trees stop being repaired until you rescue him.
- Thistle is only hurt one-on-one by Stalkers; Shamblers must pack her. A pack can push her into Flee.

What it looks like in play: a bubble over each Shambler shows its choice (green tree = raid,
orange apple = eat, amber paw = pack, red ! = hit farmer, blue drop = flee). Amber dotted lines join a
pack to its dog and a red ring shows when 2+ are biting. Press Tab to show five bars over each Shambler:
how much it wants each option right now (the chosen one is underlined).

Measured over 8 seeded games with the same scripted player: Shamblers spend about 85% of their time
raiding, 9% hunting dogs in packs, 7% fleeing; about 13 packs form and 33 retreats happen per game, and
difficulty stayed level (scripted player reaches night 11.6, before 10.8). Numbers to tune are at the
top of each option in ShamblerUtilityAI.cs.
