using System;
using System.Collections.Generic;

namespace Orchard
{
    // procedural generation random layout checked by flood fill
    // discard and retry until every tree is reachable and every side is open
    public static class MapGenerator //zian
    {
        public const int LOGS_PER_NIGHT = 5;

        static bool NearStart(int cx, int cy, int scx, int scy)
        {
            if (Math.Abs(cx - scx) <= 1 && Math.Abs(cy - scy) <= 1) return true;
            return false;
        }

        public static void BuildStatic(OrchardGame g) //zian    
        {
            int N = C.GW * C.GH;
            int tries = 0;
            while (true)
            {
                tries++;
                g.grid = new int[C.GW, C.GH];
                int scx = C.GW / 2;
                int scy = C.GH / 2;

                // hills ~10% of cells
                int hillCount = (int)Math.Floor(N * 0.10);
                if (tries > 40)
                {
                    hillCount = (int)Math.Floor(hillCount * 0.8);
                }
                int placed = 0;
                int guard = 0;
                while (placed < hillCount && guard < 4000)
                {
                    guard++;
                    int cx = 1 + (int)(U.R() * (C.GW - 2));
                    int cy = 1 + (int)(U.R() * (C.GH - 2));
                    if (NearStart(cx, cy, scx, scy)) continue;
                    if (g.grid[cx, cy] != C.GRASS) continue;
                    g.grid[cx, cy] = C.HILL;
                    placed++;
                }

                // scatter 8 bushes
                for (int i = 0; i < 8; i++)
                {
                    int cx = 1 + (int)(U.R() * (C.GW - 2));
                    int cy = 1 + (int)(U.R() * (C.GH - 2));
                    if (!NearStart(cx, cy, scx, scy) && g.grid[cx, cy] == C.GRASS)
                    {
                        g.grid[cx, cy] = C.BUSH;
                    }
                }

                // 6 trees, kept at least 2 cells from every edge
                List<Cell> tc = new List<Cell>();
                guard = 0;
                while (tc.Count < 6 && guard < 1500)
                {
                    guard++;
                    int cx = 2 + (int)(U.R() * (C.GW - 4));
                    int cy = 2 + (int)(U.R() * (C.GH - 4));
                    if (g.grid[cx, cy] != C.GRASS) continue;
                    if (NearStart(cx, cy, scx, scy)) continue;
                    bool tooClose = false;
                    for (int i = 0; i < tc.Count; i++)
                    {
                        int md = Math.Abs(tc[i].cx - cx) + Math.Abs(tc[i].cy - cy);
                        if (md < 3)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose) continue;
                    tc.Add(new Cell(cx, cy));
                }
                if (tc.Count < 6)
                {
                    if (tries > 200) break;
                    continue;
                }
                for (int i = 0; i < tc.Count; i++)
                {
                    g.grid[tc[i].cx, tc[i].cy] = C.GRASS;
                }

                // fairness checks - retry if any fail
                if (!ReachableStatic(g, new Cell(scx, scy), tc)) 
                {
                    if (tries > 200) break;
                    continue;
                }
                if (!EdgesOpen(g))
                {
                    if (tries > 200) break;
                    continue;
                }
                if (!TreesOpen(g, tc))
                {
                    if (tries > 200) break;
                    continue;
                }

                Finish(g, tc, tries);
                return;
            }

            // fallback: simple row of trees (almost never reached)
            List<Cell> fallback = new List<Cell>();
            for (int x = 3; x < C.GW - 2 && fallback.Count < 6; x = x + 3)
            {
                fallback.Add(new Cell(x, 3));
            }
            for (int i = 0; i < fallback.Count; i++)
            {
                g.grid[fallback[i].cx, fallback[i].cy] = C.GRASS;
            }
            Finish(g, fallback, tries);
        }

        static void Finish(OrchardGame g, List<Cell> tc, int tries)
        {
            g.baseGrid = (int[,])g.grid.Clone();
            g.treeCells = tc;
            g.trees = new List<TreeEntity>();
            for (int i = 0; i < tc.Count; i++)
            {
                g.trees.Add(new TreeEntity(tc[i].cx, tc[i].cy));
            }
            g.staticBuilt = true;
            g.LogEvent("Map built: hills & trees set (" + tries + " tries)");
        }

        // each tree needs 4 open neighbours
        static bool TreesOpen(OrchardGame g, List<Cell> tc)
        {
            for (int i = 0; i < tc.Count; i++)
            {
                int open = 0;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = tc[i].cx + dx;
                        int y = tc[i].cy + dy;
                        if (U.inB(x, y) && g.grid[x, y] == C.GRASS && g.reach[x, y])
                        {
                            open++;
                        }
                    }
                }
                if (open < 4) return false;
            }
            return true;
        }

        // 3 reachable edge cells per side keeps entry routes open
        static bool EdgesOpen(OrchardGame g)
        {
            int left = 0;
            int right = 0;
            int top = 0;
            int bottom = 0;
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
            if (left >= 3 && right >= 3 && top >= 3 && bottom >= 3) return true;
            return false;
        }

        // BFS from start - stores walkable cells in g.reach
        static bool ReachableStatic(OrchardGame g, Cell start, List<Cell> tc) //zian
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
                    int nx = c.cx + ndx[k];
                    int ny = c.cy + ndy[k];
                    if (!U.inB(nx, ny)) continue;
                    if (g.grid[nx, ny] != C.GRASS) continue;
                    if (seen[nx, ny]) continue;
                    seen[nx, ny] = true;
                    q.Enqueue(new Cell(nx, ny));
                }
            }
            g.reach = seen;
            for (int i = 0; i < tc.Count; i++)
            {
                if (!seen[tc[i].cx, tc[i].cy]) return false;
            }
            return true;
        }

        // place logs across enemy approach paths each night

        static void PlaceLogs(OrchardGame g) //zian
        {
            g.logs = new List<FallenLog>();
            NightValid(g);
            int scx = C.GW / 2;
            int scy = C.GH / 2;
            int tries = 0;
            while (g.logs.Count < LOGS_PER_NIGHT && tries < 60)
            {
                tries++;
                Cell start = RandomEdgeCell(g);
                if (start.cx < 0) break;
                Cell tc = g.treeCells[(int)(U.R() * g.treeCells.Count) % g.treeCells.Count];
                List<Cell> path = AStar.FindPath(start, tc);
                if (path == null) continue;
                if (path.Count < 6) continue;

                int i = (int)(path.Count * (0.3 + U.R() * 0.4));
                if (i < 1 || i > path.Count - 2) continue;
                Cell a = path[i];
                int dx = path[i + 1].cx - path[i - 1].cx;
                int dy = path[i + 1].cy - path[i - 1].cy;

                // orient the log perpendicular to the path direction
                bool horiz = Math.Abs(dx) < Math.Abs(dy);
                Cell b;
                if (horiz)
                {
                    b = new Cell(a.cx + 1, a.cy);
                }
                else
                {
                    b = new Cell(a.cx, a.cy + 1);
                }
                if (!LogCellOk(g, a, scx, scy)) continue;
                if (!LogCellOk(g, b, scx, scy)) continue;

                g.grid[a.cx, a.cy] = C.LOG;
                g.grid[b.cx, b.cy] = C.LOG;
                if (!NightValid(g))
                {
                    // undo - this log would block a tree
                    g.grid[a.cx, a.cy] = C.GRASS;
                    g.grid[b.cx, b.cy] = C.GRASS;
                    continue;
                }
                FallenLog lg = new FallenLog();
                lg.cx = a.cx;
                lg.cy = a.cy;
                lg.horiz = horiz;
                lg.seed = U.R();
                g.logs.Add(lg);
            }
            NightValid(g);
        }

        // log cell must be clear grass, away from start and other logs
        static bool LogCellOk(OrchardGame g, Cell c, int scx, int scy)
        {
            if (c.cx < 1 || c.cy < 1 || c.cx > C.GW - 2 || c.cy > C.GH - 2) return false;
            if (g.grid[c.cx, c.cy] != C.GRASS) return false;
            if (Math.Abs(c.cx - scx) <= 2 && Math.Abs(c.cy - scy) <= 2) return false;
            for (int i = 0; i < g.treeCells.Count; i++)
            {
                Cell t = g.treeCells[i];
                if (Math.Abs(t.cx - c.cx) <= 1 && Math.Abs(t.cy - c.cy) <= 1) return false;
            }
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = c.cx + dx;
                    int y = c.cy + dy;
                    if (U.inB(x, y) && g.grid[x, y] == C.LOG) return false;
                }
            }
            return true;
        }

        static Cell RandomEdgeCell(OrchardGame g)
        {
            for (int k = 0; k < 200; k++)
            {
                int e = (int)(U.R() * 4);
                int cx = 0;
                int cy = 0;
                if (e == 0)
                {
                    cx = 0;
                    cy = (int)(U.R() * C.GH);
                }
                else if (e == 1)
                {
                    cx = C.GW - 1;
                    cy = (int)(U.R() * C.GH);
                }
                else if (e == 2)
                {
                    cy = 0;
                    cx = (int)(U.R() * C.GW);
                }
                else
                {
                    cy = C.GH - 1;
                    cx = (int)(U.R() * C.GW);
                }
                if (!U.groundBlocked(cx, cy) && (g.reach == null || g.reach[cx, cy]))
                {
                    return new Cell(cx, cy);
                }
            }
            return new Cell(-1, -1);
        }

        // same fairness check as BuildStatic but includes mud and tonight's logs
        static bool NightValid(OrchardGame g)
        {
            bool[,] seen = new bool[C.GW, C.GH];
            Queue<Cell> q = new Queue<Cell>();
            Cell s = new Cell(C.GW / 2, C.GH / 2);
            q.Enqueue(s);
            seen[s.cx, s.cy] = true;
            int[] ndx = { 1, -1, 0, 0 };
            int[] ndy = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                Cell c = q.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.cx + ndx[k];
                    int ny = c.cy + ndy[k];
                    if (!U.inB(nx, ny)) continue;
                    if (seen[nx, ny]) continue;
                    if (U.groundBlocked(nx, ny)) continue;
                    seen[nx, ny] = true;
                    q.Enqueue(new Cell(nx, ny));
                }
            }
            g.reach = seen;

            for (int i = 0; i < g.treeCells.Count; i++)
            {
                if (!seen[g.treeCells[i].cx, g.treeCells[i].cy]) return false;
            }
            if (!EdgesOpen(g)) return false;

            for (int i = 0; i < g.treeCells.Count; i++)
            {
                Cell t = g.treeCells[i];
                int open = 0;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = t.cx + dx;
                        int y = t.cy + dy;
                        if (U.inB(x, y) && !U.groundBlocked(x, y) && seen[x, y])
                        {
                            open++;
                        }
                    }
                }
                if (open < 4) return false;
            }
            return true;
        }

        //  new terrain for each night: mud, logs, branches, grass tufts...
        public static void NewNightTerrain(OrchardGame g) //zian
        {
            g.grid = (int[,])g.baseGrid.Clone();
            int scx = C.GW / 2;
            int scy = C.GH / 2;

            // mud patches ~10% of cells
            int mudTarget = (int)Math.Floor(C.GW * C.GH * 0.10);
            int mud = 0;
            int guard = 0;
            while (mud < mudTarget && guard < 5000)
            {
                guard++;
                int bx = 1 + (int)(U.R() * (C.GW - 2));
                int by = 1 + (int)(U.R() * (C.GH - 2));
                for (int k = 0; k < 4; k++)
                {
                    int cx = bx + (int)(U.R() * 3 - 1);
                    int cy = by + (int)(U.R() * 3 - 1);
                    if (U.inB(cx, cy) && g.grid[cx, cy] == C.GRASS && !NearStart(cx, cy, scx, scy))
                    {
                        g.grid[cx, cy] = C.MUD;
                        mud++;
                        if (mud >= mudTarget) break;
                    }
                }
            }

            // trees stay on grass
            for (int i = 0; i < g.treeCells.Count; i++)
            {
                g.grid[g.treeCells[i].cx, g.treeCells[i].cy] = C.GRASS;
            }

            PlaceLogs(g);

            // decorative branches
            g.branches = new List<Branch>();
            for (int i = 0; i < 18; i++)
            {
                int cx = (int)(U.R() * C.GW);
                int cy = (int)(U.R() * C.GH);
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

            // grass tufts
            g.tufts = new List<Tuft>();
            for (int i = 0; i < 105; i++)
            {
                int cx = (int)(U.R() * C.GW);
                int cy = (int)(U.R() * C.GH);
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
