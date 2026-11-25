using System.Collections.Generic;
using UnityEngine;

namespace Orchard
{
    // ai technique: procedural generation + validation
    // hills (~10%), 8 bushes and the 6 trees are placed once and stay the whole game.
    // a breadth-first flood fill from the farmer's start checks that
    //   - every tree can be reached, and
    //   - every side of the board has at least 3 open edge cells joined to the orchard,
    //     so enemies can always come in from all four sides and never spawn in a sealed pocket, and
    //   - every tree has at least 4 open cells around it (never boxed in by hills);
    // otherwise the layout is thrown away. the reachable area is kept and used to pick spawn points.
    // each night only the mud puddles and fallen branches are regenerated (from the rain).
    public static class MapGenerator
    {
        static bool Clear(int cx, int cy, int scx, int scy)
        {
            return System.Math.Abs(cx - scx) <= 1 && System.Math.Abs(cy - scy) <= 1;
        }

        public static void BuildStatic(OrchardGame g)
        {
            int N = C.GW * C.GH;
            int tries = 0;
            while (true)
            {
                tries++;
                g.grid = new int[C.GW, C.GH]; // all GRASS (0)
                int scx = C.GW >> 1, scy = C.GH >> 1;

                int hillCount = (int)System.Math.Floor(N * 0.10);
                if (tries > 40) hillCount = (int)System.Math.Floor(hillCount * 0.8);
                int placed = 0, gg = 0;
                while (placed < hillCount && gg < 4000)
                {
                    gg++;
                    int cx = 1 + (int)(U.R() * (C.GW - 2));
                    int cy = 1 + (int)(U.R() * (C.GH - 2));
                    if (Clear(cx, cy, scx, scy) || g.grid[cx, cy] != C.GRASS) continue;
                    g.grid[cx, cy] = C.HILL;
                    placed++;
                }

                for (int i = 0; i < 8; i++)
                {
                    int cx = 1 + (int)(U.R() * (C.GW - 2));
                    int cy = 1 + (int)(U.R() * (C.GH - 2));
                    if (!Clear(cx, cy, scx, scy) && g.grid[cx, cy] == C.GRASS) g.grid[cx, cy] = C.BUSH;
                }

                List<Cell> tc = new List<Cell>();
                gg = 0;
                while (tc.Count < 6 && gg < 1500)
                {
                    gg++;
                    // trees keep 2 cells from every edge, so the whole crown is always on screen
                    int cx = 2 + (int)(U.R() * (C.GW - 4));
                    int cy = 2 + (int)(U.R() * (C.GH - 4));
                    if (g.grid[cx, cy] != C.GRASS) continue;
                    if (System.Math.Abs(cx - scx) <= 1 && System.Math.Abs(cy - scy) <= 1) continue;
                    bool cl = false;
                    for (int i = 0; i < tc.Count; i++)
                    {
                        if (System.Math.Abs(tc[i].cx - cx) + System.Math.Abs(tc[i].cy - cy) < 3) { cl = true; break; }
                    }
                    if (cl) continue;
                    tc.Add(new Cell(cx, cy));
                }
                if (tc.Count < 6)
                {
                    if (tries > 200) break;
                    continue;
                }
                for (int i = 0; i < tc.Count; i++) g.grid[tc[i].cx, tc[i].cy] = C.GRASS;

                if (!ReachableStatic(g, new Cell(scx, scy), tc) || !EdgesOpen(g) || !TreesOpen(g, tc))
                {
                    if (tries > 200) break;
                    continue;
                }

                Finish(g, tc, tries);
                return;
            }

            // safety net (practically never reached): keep the last layout so the game can run
            List<Cell> fallback = new List<Cell>();
            for (int x = 3; x < C.GW - 2 && fallback.Count < 6; x += 3)
                fallback.Add(new Cell(x, 3));
            for (int i = 0; i < fallback.Count; i++) g.grid[fallback[i].cx, fallback[i].cy] = C.GRASS;
            Finish(g, fallback, tries);
        }

        static void Finish(OrchardGame g, List<Cell> tc, int tries)
        {
            g.baseGrid = (int[,])g.grid.Clone(); // snapshot hills+bushes+grass (no mud)
            g.treeCells = tc;
            g.trees = new List<TreeEntity>();
            for (int i = 0; i < tc.Count; i++) g.trees.Add(new TreeEntity(tc[i].cx, tc[i].cy));
            g.staticBuilt = true;
            g.LogEvent("Map built: hills & trees set (" + tries + " tries)");
        }

        // every tree needs at least 4 open cells around it (of 8), so it is never boxed in by hills:
        // enemies can surround it and pip can always get next to it
        static bool TreesOpen(OrchardGame g, List<Cell> tc)
        {
            foreach (Cell t in tc)
            {
                int open = 0;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = t.cx + dx, y = t.cy + dy;
                        if (U.inB(x, y) && g.grid[x, y] == C.GRASS && g.reach[x, y]) open++;
                    }
                if (open < 4) return false;
            }
            return true;
        }

        // every side needs at least 3 edge cells inside the reachable area
        static bool EdgesOpen(OrchardGame g)
        {
            int left = 0, right = 0, top = 0, bottom = 0;
            for (int i = 0; i < C.GH; i++)
            {
                if (g.reach[0, i]) left++;
                if (g.reach[C.GW - 1, i]) right++;
            }
            for (int i = 0; i < C.GW; i++)
            {
                if (g.reach[i, 0]) top++;
                if (g.reach[i, C.GH - 1]) bottom++;
            }
            return left >= 3 && right >= 3 && top >= 3 && bottom >= 3;
        }

        // validation: flood fill over grass only (hills/bushes block); mud is added later and only helps
        public static bool ReachableStatic(OrchardGame g, Cell start, List<Cell> tc)
        {
            bool[,] seen = new bool[C.GW, C.GH];
            Queue<Cell> q = new Queue<Cell>();
            q.Enqueue(start);
            seen[start.cx, start.cy] = true;
            int[] ndx = { 1, -1, 0, 0 };
            int[] ndy = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                Cell c = q.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.cx + ndx[k], ny = c.cy + ndy[k];
                    if (!(U.inB(nx, ny) && g.grid[nx, ny] == C.GRASS) || seen[nx, ny]) continue;
                    seen[nx, ny] = true;
                    q.Enqueue(new Cell(nx, ny));
                }
            }
            g.reach = seen;
            for (int i = 0; i < tc.Count; i++)
                if (!seen[tc[i].cx, tc[i].cy]) return false;
            return true;
        }

        // ---- fallen logs: blocks on the paths enemies walk ----
        // a log is dropped across the a* route from a random edge cell to a tree, at right angles to it,
        // so walkers must find a new way round. each log is only kept if the map stays valid:
        // every tree reachable, every side still open, every tree with 4+ free cells around it.
        public const int LOGS_PER_NIGHT = 5;

        static void PlaceLogs(OrchardGame g)
        {
            g.logs = new List<FallenLog>();
            NightValid(g);   // reach map for picking edge cells
            int scx = C.GW >> 1, scy = C.GH >> 1;
            int tries = 0;
            while (g.logs.Count < LOGS_PER_NIGHT && tries < 60)
            {
                tries++;
                Cell start = RandomEdgeCell(g);
                if (start.cx < 0) break;
                Cell tc = g.treeCells[(int)(U.R() * g.treeCells.Count) % g.treeCells.Count];
                List<Cell> path = AStar.FindPath(start, tc);
                if (path == null || path.Count < 6) continue;
                int i = (int)(path.Count * (0.3 + U.R() * 0.4));
                if (i < 1 || i > path.Count - 2) continue;
                Cell a = path[i];
                int dx = path[i + 1].cx - path[i - 1].cx, dy = path[i + 1].cy - path[i - 1].cy;
                bool horiz = System.Math.Abs(dx) < System.Math.Abs(dy);   // lie across the direction of travel
                Cell b = horiz ? new Cell(a.cx + 1, a.cy) : new Cell(a.cx, a.cy + 1);
                if (!LogCellOk(g, a, scx, scy) || !LogCellOk(g, b, scx, scy)) continue;
                g.grid[a.cx, a.cy] = C.LOG;
                g.grid[b.cx, b.cy] = C.LOG;
                if (!NightValid(g))
                {
                    g.grid[a.cx, a.cy] = C.GRASS;
                    g.grid[b.cx, b.cy] = C.GRASS;
                    continue;
                }
                FallenLog lg = new FallenLog();
                lg.cx = a.cx; lg.cy = a.cy; lg.horiz = horiz; lg.seed = U.R();
                g.logs.Add(lg);
            }
            NightValid(g);   // leaves g.reach matching tonight's map (used for spawn points)
        }

        // a log cell must be open grass, inside the board, away from the farmer's start,
        // not touching a tree and not next to another log
        static bool LogCellOk(OrchardGame g, Cell c, int scx, int scy)
        {
            if (c.cx < 1 || c.cy < 1 || c.cx > C.GW - 2 || c.cy > C.GH - 2) return false;
            if (g.grid[c.cx, c.cy] != C.GRASS) return false;
            if (System.Math.Abs(c.cx - scx) <= 2 && System.Math.Abs(c.cy - scy) <= 2) return false;
            foreach (Cell t in g.treeCells)
                if (System.Math.Abs(t.cx - c.cx) <= 1 && System.Math.Abs(t.cy - c.cy) <= 1) return false;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = c.cx + dx, y = c.cy + dy;
                    if (U.inB(x, y) && g.grid[x, y] == C.LOG) return false;
                }
            return true;
        }

        static Cell RandomEdgeCell(OrchardGame g)
        {
            for (int k = 0; k < 200; k++)
            {
                int e = (int)(U.R() * 4), cx, cy;
                if (e == 0) { cx = 0; cy = (int)(U.R() * C.GH); }
                else if (e == 1) { cx = C.GW - 1; cy = (int)(U.R() * C.GH); }
                else if (e == 2) { cy = 0; cx = (int)(U.R() * C.GW); }
                else { cy = C.GH - 1; cx = (int)(U.R() * C.GW); }
                if (!U.groundBlocked(cx, cy) && (g.reach == null || g.reach[cx, cy])) return new Cell(cx, cy);
            }
            return new Cell(-1, -1);
        }

        // same three checks as the static map, but over tonight's grid (mud is walkable, logs are not)
        static bool NightValid(OrchardGame g)
        {
            bool[,] seen = new bool[C.GW, C.GH];
            Queue<Cell> q = new Queue<Cell>();
            Cell s = new Cell(C.GW >> 1, C.GH >> 1);
            q.Enqueue(s); seen[s.cx, s.cy] = true;
            int[] ndx = { 1, -1, 0, 0 };
            int[] ndy = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                Cell c = q.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.cx + ndx[k], ny = c.cy + ndy[k];
                    if (!U.inB(nx, ny) || seen[nx, ny] || U.groundBlocked(nx, ny)) continue;
                    seen[nx, ny] = true;
                    q.Enqueue(new Cell(nx, ny));
                }
            }
            g.reach = seen;
            foreach (Cell t in g.treeCells) if (!seen[t.cx, t.cy]) return false;
            if (!EdgesOpen(g)) return false;
            foreach (Cell t in g.treeCells)
            {
                int open = 0;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = t.cx + dx, y = t.cy + dy;
                        if (U.inB(x, y) && !U.groundBlocked(x, y) && seen[x, y]) open++;
                    }
                if (open < 4) return false;
            }
            return true;
        }

        // only the mud puddles + fallen branches change each night (from the rain)
        public static void NewNightTerrain(OrchardGame g)
        {
            g.grid = (int[,])g.baseGrid.Clone();
            int scx = C.GW >> 1, scy = C.GH >> 1;
            int mudTarget = (int)System.Math.Floor(C.GW * C.GH * 0.10);
            int mud = 0, gg = 0;
            while (mud < mudTarget && gg < 5000)
            {
                gg++;
                int bx = 1 + (int)(U.R() * (C.GW - 2));
                int by = 1 + (int)(U.R() * (C.GH - 2));
                for (int k = 0; k < 4; k++)
                {
                    int cx = bx + (int)(U.R() * 3 - 1);
                    int cy = by + (int)(U.R() * 3 - 1);
                    if (U.inB(cx, cy) && g.grid[cx, cy] == C.GRASS && !Clear(cx, cy, scx, scy))
                    {
                        g.grid[cx, cy] = C.MUD;
                        mud++;
                        if (mud >= mudTarget) break;
                    }
                }
            }
            for (int i = 0; i < g.treeCells.Count; i++) g.grid[g.treeCells[i].cx, g.treeCells[i].cy] = C.GRASS; // trees never sit on mud

            PlaceLogs(g);

            g.branches = new List<Branch>();
            for (int i = 0; i < 18; i++)
            {
                int cx = (int)(U.R() * C.GW), cy = (int)(U.R() * C.GH);
                if (g.grid[cx, cy] == C.GRASS)
                {
                    Pos c = U.cc(cx, cy);
                    Branch b = new Branch();
                    b.x = c.x + (U.R() - .5) * 30;
                    b.y = c.y + (U.R() - .5) * 30;
                    b.a = U.R() * 6.28;
                    g.branches.Add(b);
                }
            }

            g.tufts = new List<Tuft>();
            for (int i = 0; i < 105; i++)
            {
                int cx = (int)(U.R() * C.GW), cy = (int)(U.R() * C.GH);
                if (g.grid[cx, cy] == C.GRASS)
                {
                    Pos c = U.cc(cx, cy);
                    Tuft t = new Tuft();
                    t.x = c.x + (U.R() - .5) * 26;
                    t.y = c.y + (U.R() - .5) * 26;
                    g.tufts.Add(t);
                }
            }
        }
    }
}
