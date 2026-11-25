using System.Collections.Generic;
using UnityEngine;

namespace Orchard
{
    // ai technique: a* pathfinding (used by shamblers, and by stalkers when they walk)
    //  - 8 directions, diagonal steps cost sqrt(2), no cutting past the corner of a hill or bush
    //  - mud costs 3x, so they go round puddles when it is cheaper
    //  - octile-distance heuristic (admissible, so the path found is the cheapest one)
    //  - many goals at once: finds the tree that is closest to walk to, not closest as the crow flies
    //  - string pulling: the cell path is smoothed into straight runs between the real turning points
    public static class AStar
    {
        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        const double SQRT2 = 1.4142135623730951;

        static double Terrain(int x, int y)
        {
            return OrchardGame.I.grid[x, y] == C.MUD ? 3 : 1;
        }

        static double Octile(int ax, int ay, int bx, int by)
        {
            int dx = System.Math.Abs(ax - bx), dy = System.Math.Abs(ay - by);
            return System.Math.Max(dx, dy) + (SQRT2 - 1) * System.Math.Min(dx, dy);
        }

        public static List<Cell> FindPath(Cell s, Cell goal)
        {
            List<Cell> goals = new List<Cell>();
            goals.Add(goal);
            return FindPathToAny(s, goals);
        }

        // cheapest path from s to whichever goal is cheapest to reach (null if none can be reached)
        public static List<Cell> FindPathToAny(Cell s, List<Cell> goals)
        {
            if (goals.Count == 0 || !U.inB(s.cx, s.cy)) return null;
            int N = C.GH;   // packed index = x * GH + y
            double[,] gS = new double[C.GW, C.GH];
            int[,] parent = new int[C.GW, C.GH];
            bool[,] closed = new bool[C.GW, C.GH];
            bool[,] isGoal = new bool[C.GW, C.GH];
            for (int x = 0; x < C.GW; x++)
                for (int y = 0; y < C.GH; y++) { gS[x, y] = double.PositiveInfinity; parent[x, y] = -1; }
            foreach (Cell gc in goals) if (U.inB(gc.cx, gc.cy)) isGoal[gc.cx, gc.cy] = true;

            Heap open = new Heap();                    // binary min-heap on f, packed x*N+y
            gS[s.cx, s.cy] = 0;
            open.Push(H(s.cx, s.cy, goals), s.cx * N + s.cy);

            while (open.Count > 0)
            {
                int cur = open.Pop();
                int cx = cur / N, cy = cur % N;
                if (closed[cx, cy]) continue;
                closed[cx, cy] = true;

                if (isGoal[cx, cy])
                {
                    List<Cell> path = new List<Cell>();
                    int n = cur;
                    while (n != -1) { path.Add(new Cell(n / N, n % N)); n = parent[n / N, n % N]; }
                    path.Reverse();
                    return path;
                }

                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (!U.inB(nx, ny) || closed[nx, ny]) continue;
                    // goal cells hold a tree; the tree cell itself is walkable grass
                    if (U.groundBlocked(nx, ny) && !isGoal[nx, ny]) continue;
                    bool diag = k >= 4;
                    if (diag && (U.groundBlocked(cx + DX[k], cy) || U.groundBlocked(cx, cy + DY[k]))) continue; // no corner cutting
                    double step = (diag ? SQRT2 : 1) * Terrain(nx, ny);
                    double ng = gS[cx, cy] + step;
                    if (ng >= gS[nx, ny]) continue;
                    gS[nx, ny] = ng;
                    parent[nx, ny] = cur;
                    // tiny tie-break towards the goal keeps the search narrow on open ground
                    double h = H(nx, ny, goals);
                    open.Push(ng + h * 1.0001, nx * N + ny);
                }
            }
            return null;
        }

        // binary min-heap (open list): O(log n) push/pop instead of scanning the whole list
        class Heap
        {
            List<double> f = new List<double>();
            List<int> v = new List<int>();
            public int Count { get { return v.Count; } }
            public void Push(double key, int val)
            {
                f.Add(key); v.Add(val);
                int i = v.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (f[p] <= f[i]) break;
                    Swap(i, p); i = p;
                }
            }
            public int Pop()
            {
                int top = v[0];
                int last = v.Count - 1;
                Swap(0, last);
                f.RemoveAt(last); v.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < v.Count && f[l] < f[m]) m = l;
                    if (r < v.Count && f[r] < f[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
                return top;
            }
            void Swap(int a, int b)
            {
                double tf = f[a]; f[a] = f[b]; f[b] = tf;
                int tv = v[a]; v[a] = v[b]; v[b] = tv;
            }
        }

        static double H(int x, int y, List<Cell> goals)
        {
            double best = double.PositiveInfinity;
            foreach (Cell gc in goals) best = System.Math.Min(best, Octile(x, y, gc.cx, gc.cy));
            return best;
        }

        // string pulling: keep only the waypoints where the walker really has to turn.
        // a shortcut is allowed when a body-wide corridor along it is free of hills/bushes
        // and does not wade into mud the a* path chose to avoid.
        public static List<Pos> Smooth(Pos from, List<Cell> path, double radius)
        {
            List<Pos> pts = new List<Pos>();
            if (path == null || path.Count == 0) return pts;
            HashSet<int> onPath = new HashSet<int>();
            foreach (Cell c in path) onPath.Add(c.cx * C.GH + c.cy);

            Pos anchor = from;
            int i = 1;                                   // path[0] is the cell we stand in, never walk back to it
            if (path.Count == 1) { pts.Add(U.cc(path[0].cx, path[0].cy)); return pts; }
            while (i < path.Count)
            {
                int far = i;
                for (int j = path.Count - 1; j > i; j--)
                {
                    if (Clear(anchor, U.cc(path[j].cx, path[j].cy), radius, onPath)) { far = j; break; }
                }
                Pos p = U.cc(path[far].cx, path[far].cy);
                pts.Add(p);
                anchor = p;
                i = far + 1;
            }
            return pts;
        }

        static bool Clear(Pos a, Pos b, double radius, HashSet<int> onPath)
        {
            double dx = b.x - a.x, dy = b.y - a.y;
            double len = U.hypot(dx, dy);
            if (len < 1) return true;
            double nx = -dy / len, ny = dx / len;
            int steps = (int)System.Math.Ceiling(len / 6);
            for (int s = 0; s <= steps; s++)
            {
                double t = (double)s / steps;
                for (int side = -1; side <= 1; side++)
                {
                    double px = a.x + dx * t + nx * radius * side, py = a.y + dy * t + ny * radius * side;
                    Cell c = U.w2c(px, py);
                    if (U.groundBlocked(c.cx, c.cy)) return false;
                    if (OrchardGame.I.grid[c.cx, c.cy] == C.MUD && !onPath.Contains(c.cx * C.GH + c.cy)) return false;
                }
            }
            return true;
        }
    }

    // follows a smoothed a* route and re-plans on a timer or when the goal changes
    public class Navigator
    {
        public List<Pos> route = new List<Pos>();
        public int wp;
        public double timer;
        public int goalKey = -1;
        public int lastLen;
        double stuckT, sx, sy;

        // spread re-plans of different walkers over time instead of all on the same frame
        public Navigator() { timer = U.R() * 0.3; }

        // returns the cell path (for logging); walks the mover along the route
        public List<Cell> Plan(Mover e, List<Cell> goals, int key, double every)
        {
            List<Cell> path = null;
            if (timer <= 0 || key != goalKey)
            {
                timer = every;
                goalKey = key;
                path = AStar.FindPathToAny(U.w2c(e.x, e.y), goals);
                route = AStar.Smooth(e, path, e.r * 0.7);
                wp = 0;
                lastLen = path == null ? 0 : path.Count;
            }
            return path;
        }

        public bool HasRoute { get { return route.Count > 0 && wp < route.Count; } }

        public void Walk(Mover e, double spd, double dt)
        {
            timer -= dt;
            if (!HasRoute) return;
            // stuck recovery: barely moved for half a second -> re-plan right away
            stuckT += dt;
            if (stuckT >= 0.5)
            {
                if (System.Math.Abs(e.x - sx) + System.Math.Abs(e.y - sy) < 4) timer = 0;
                stuckT = 0; sx = e.x; sy = e.y;
            }
            Pos c = route[wp];
            Steering.steer(e, c, spd, dt, Steering.moveGround);
            double reach = wp == route.Count - 1 ? 6 : 13;
            if (U.dist(e, c) < reach) wp++;
        }

        public void Reset() { route.Clear(); wp = 0; timer = 0; goalKey = -1; }
    }

    // ai technique: perception (used by stalkers)
    // the sight line is sampled every few pixels; any hill cell it crosses blocks the view
    public static class Perception
    {
        public static bool LineOfSight(Pos a, Pos b)
        {
            OrchardGame G = OrchardGame.I;
            double dx = b.x - a.x, dy = b.y - a.y;
            double len = U.hypot(dx, dy);
            int steps = System.Math.Max(1, (int)System.Math.Ceiling(len / 5));
            for (int s = 0; s <= steps; s++)
            {
                double t = (double)s / steps;
                Cell c = U.w2c(a.x + dx * t, a.y + dy * t);
                if (U.inB(c.cx, c.cy) && G.grid[c.cx, c.cy] == C.HILL) return false;
            }
            return true;
        }

        // can a ground walker go straight there (no hill or bush on the line)?
        public static bool WalkableLine(Pos a, Pos b)
        {
            double dx = b.x - a.x, dy = b.y - a.y;
            double len = U.hypot(dx, dy);
            int steps = System.Math.Max(1, (int)System.Math.Ceiling(len / 6));
            for (int s = 0; s <= steps; s++)
            {
                double t = (double)s / steps;
                Cell c = U.w2c(a.x + dx * t, a.y + dy * t);
                if (U.groundBlocked(c.cx, c.cy)) return false;
            }
            return true;
        }
    }

    // ai technique: steering (seek with smoothing) + movement rules
    public static class Steering
    {
        // farmer, dogs and birds: free movement, only clamped to the board
        public static void moveFree(Mover e, double dx, double dy)
        {
            e.x = U.clamp(e.x + dx, e.r, C.WORLD_W - e.r);
            e.y = U.clamp(e.y + dy, e.r, C.WORLD_H - e.r);
        }

        // shamblers and stalkers: hills and bushes stop them
        public static void moveGround(Mover e, double dx, double dy)
        {
            Cell c = U.w2c(e.x + dx, e.y);
            if (!U.groundBlocked(c.cx, c.cy)) e.x = U.clamp(e.x + dx, e.r, C.WORLD_W - e.r);
            c = U.w2c(e.x, e.y + dy);
            if (!U.groundBlocked(c.cx, c.cy)) e.y = U.clamp(e.y + dy, e.r, C.WORLD_H - e.r);
        }

        public static void steer(Mover e, Pos t, double sp, double dt, System.Action<Mover, double, double> mv)
        {
            double dx = t.x - e.x, dy = t.y - e.y;
            double l = U.hypot(dx, dy);
            if (l == 0) l = 1;
            double vx = dx / l * sp, vy = dy / l * sp;
            e.vx += (vx - e.vx) * System.Math.Min(1, 8 * dt);
            e.vy += (vy - e.vy) * System.Math.Min(1, 8 * dt);
            mv(e, e.vx * dt, e.vy * dt);
        }

        // separation between ground walkers so they do not stack on one spot
        public static void separate(Mover e, double dt)
        {
            double px = 0, py = 0;
            foreach (Enemy o in U.G.enemies)
            {
                if (o == e || o.dead || o is Bird) continue;
                double d = U.dist(e, o), min = e.r + o.r;
                if (d < min && d > 0.01)
                {
                    double k = (min - d) / min;
                    px += (e.x - o.x) / d * k;
                    py += (e.y - o.y) / d * k;
                }
            }
            if (px != 0 || py != 0) moveGround(e, px * 70 * dt, py * 70 * dt);
        }

        // where to stand when attacking a tree: on my own side of it, so attackers ring the tree
        public static Pos slot(Mover e, Pos tree, double r)
        {
            double dx = e.x - tree.x, dy = e.y - tree.y;
            double l = U.hypot(dx, dy);
            if (l < 0.01) return new Pos(tree.x, tree.y + r);
            return new Pos(tree.x + dx / l * r, tree.y + dy / l * r);
        }

        // arrive: like seek, but slows down inside the last few pixels so it does not overshoot and jitter
        public static void arrive(Mover e, Pos t, double sp, double slowR, double dt, System.Action<Mover, double, double> mv)
        {
            double d = U.dist(e, t);
            double s = d < slowR ? sp * d / slowR : sp;
            steer(e, t, s, dt, mv);
        }
    }
}
