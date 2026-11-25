using System;
using System.Collections.Generic;

namespace Orchard
{
    //  a* pathfinding on the grid... this is navigation...
    //  8 directions, mud costs 3, can aim at many goals at once...
    // we added two small speed ups of our own on top of plain a*:
    //    - open list kept as a binary heap, so pulling the next cell is fast...
    //   - string pull after, to drop points on straight runs (smoother walk)
    public static class AStar  //george
    {
        static int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        const double SQRT2 = 1.4142135623730951;

        static double Terrain(int x, int y)
        {
            if (OrchardGame.I.grid[x, y] == C.MUD) return 3;
            return 1;
        }

        //  guess of distance still to go... heuristic for 8 way movement...
        // (cheaper than real distance but never over guesses, so a* stays correct)
        static double Octile(int ax, int ay, int bx, int by)
        {
            int dx = Math.Abs(ax - bx);
            int dy = Math.Abs(ay - by);
            return Math.Max(dx, dy) + (SQRT2 - 1) * Math.Min(dx, dy);
        }

        public static List<Cell> FindPath(Cell s, Cell goal)
        {
            List<Cell> goals = new List<Cell>();
            goals.Add(goal);
            return FindPathToAny(s, goals);
        }

        //  cheapest path to the nearest goal, null if no goal can be reached...
        public static List<Cell> FindPathToAny(Cell s, List<Cell> goals)
        {
            if (goals.Count == 0) return null;
            if (!U.inB(s.cx, s.cy)) return null;

            int N = C.GH;
            double[,] gS = new double[C.GW, C.GH];
            int[,] parent = new int[C.GW, C.GH];
            bool[,] closed = new bool[C.GW, C.GH];
            bool[,] isGoal = new bool[C.GW, C.GH];

            for (int x = 0; x < C.GW; x++)
            {
                for (int y = 0; y < C.GH; y++)
                {
                    gS[x, y] = 1e18;
                    parent[x, y] = -1;
                }
            }
            for (int i = 0; i < goals.Count; i++)
            {
                if (U.inB(goals[i].cx, goals[i].cy))
                {
                    isGoal[goals[i].cx, goals[i].cy] = true;
                }
            }

            Heap open = new Heap();
            gS[s.cx, s.cy] = 0;
            open.Push(H(s.cx, s.cy, goals), s.cx * N + s.cy);

            while (open.Count > 0)
            {
                int cur = open.Pop();
                int cx = cur / N;
                int cy = cur % N;
                if (closed[cx, cy]) continue;
                closed[cx, cy] = true;

                //  reached a goal: walk back through parents...
                if (isGoal[cx, cy])
                {
                    List<Cell> path = new List<Cell>();
                    int n = cur;
                    while (n != -1)
                    {
                        path.Add(new Cell(n / N, n % N));
                        n = parent[n / N, n % N];
                    }
                    path.Reverse();
                    return path;
                }

                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k];
                    int ny = cy + DY[k];
                    if (!U.inB(nx, ny)) continue;
                    if (closed[nx, ny]) continue;
                    //  the tree cell is a goal so it is allowed...
                    if (U.groundBlocked(nx, ny) && !isGoal[nx, ny]) continue;

                    bool diag = k >= 4;
                    if (diag)
                    {
                        //  no cutting corners...
                        if (U.groundBlocked(cx + DX[k], cy)) continue;
                        if (U.groundBlocked(cx, cy + DY[k])) continue;
                    }

                    double step = Terrain(nx, ny);
                    if (diag) step = step * SQRT2;
                    double ng = gS[cx, cy] + step;
                    if (ng >= gS[nx, ny]) continue;

                    gS[nx, ny] = ng;
                    parent[nx, ny] = cur;
                    double h = H(nx, ny, goals);
                    open.Push(ng + h * 1.0001, nx * N + ny);
                }
            }
            return null;
        }

        // our own speed up... open list kept as a binary heap (smallest f on top)
        //  a plain sorted list would work too but this pulls the next cell faster...
        class Heap  //manu (after discusing with team)
        {
            List<double> f = new List<double>();
            List<int> v = new List<int>();

            public int Count
            {
                get { return v.Count; }
            }

            public void Push(double key, int val)
            {
                f.Add(key);
                v.Add(val);
                int i = v.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (f[p] <= f[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public int Pop()
            {
                int top = v[0];
                int last = v.Count - 1;
                Swap(0, last);
                f.RemoveAt(last);
                v.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1;
                    int r = l + 1;
                    int m = i;
                    if (l < v.Count && f[l] < f[m]) m = l;
                    if (r < v.Count && f[r] < f[m]) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                double tf = f[a];
                f[a] = f[b];
                f[b] = tf;
                int tv = v[a];
                v[a] = v[b];
                v[b] = tv;
            }
        }

        static double H(int x, int y, List<Cell> goals)
        {
            double best = 1e18;
            for (int i = 0; i < goals.Count; i++)
            {
                double d = Octile(x, y, goals[i].cx, goals[i].cy);
                if (d < best) best = d;
            }
            return best;
        }

        //  our own tidy up after a*... "string pull" the path so it keeps only the...
        // corners where the walker must actually turn (drops the zig zag on open ground)
        public static List<Pos> Smooth(Pos from, List<Cell> path, double radius)
        {
            List<Pos> pts = new List<Pos>();
            if (path == null) return pts;
            if (path.Count == 0) return pts;

            //  remember which cells are on the path...
            bool[,] onPath = new bool[C.GW, C.GH];
            for (int i = 0; i < path.Count; i++)
            {
                onPath[path[i].cx, path[i].cy] = true;
            }

            if (path.Count == 1)
            {
                pts.Add(U.cc(path[0].cx, path[0].cy));
                return pts;
            }

            Pos anchor = from;
            int idx = 1;
            while (idx < path.Count)
            {
                int far = idx;
                for (int j = path.Count - 1; j > idx; j--)
                {
                    Pos target = U.cc(path[j].cx, path[j].cy);
                    if (Clear(anchor, target, radius, onPath))
                    {
                        far = j;
                        break;
                    }
                }
                Pos p = U.cc(path[far].cx, path[far].cy);
                pts.Add(p);
                anchor = p;
                idx = far + 1;
            }
            return pts;
        }

        //  is the straight line from a to b free for a body with this radius...
        static bool Clear(Pos a, Pos b, double radius, bool[,] onPath)
        {
            double dx = b.x - a.x;
            double dy = b.y - a.y;
            double len = U.hypot(dx, dy);
            if (len < 1) return true;
            double nx = -dy / len;
            double ny = dx / len;
            int steps = (int)Math.Ceiling(len / 6);
            for (int s = 0; s <= steps; s++)
            {
                double t = (double)s / steps;
                for (int side = -1; side <= 1; side++)
                {
                    double px = a.x + dx * t + nx * radius * side;
                    double py = a.y + dy * t + ny * radius * side;
                    Cell c = U.w2c(px, py);
                    if (U.groundBlocked(c.cx, c.cy)) return false;
                    //  do not cut through mud that the path avoided... 
                    if (OrchardGame.I.grid[c.cx, c.cy] == C.MUD && !onPath[c.cx, c.cy]) return false;
                }
            }
            return true;
        }
    }

    //  walks a route and makes a new one every few tenths of a second...
    public class Navigator
    {
        public List<Pos> route = new List<Pos>();
        public int wp = 0;
        public double timer;
        public int goalKey = -1;
        double stuckT = 0;
        double sx = 0;
        double sy = 0;

        public Navigator()
        {
            timer = U.R() * 0.3;
        }

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
            }
            return path;
        }

        public bool HasRoute
        {
            get { return route.Count > 0 && wp < route.Count; }
        }

        public void Walk(Mover e, double spd, double dt)
        {
            timer = timer - dt;
            if (!HasRoute) return;

            //  stuck for half a second: plan again...
            stuckT = stuckT + dt;
            if (stuckT >= 0.5)
            {
                double moved = Math.Abs(e.x - sx) + Math.Abs(e.y - sy);
                if (moved < 4)
                {
                    timer = 0;
                }
                stuckT = 0;
                sx = e.x;
                sy = e.y;
            }

            Pos c = route[wp];
            Steering.steer(e, c, spd, dt, true);
            double reach = 13;
            if (wp == route.Count - 1) reach = 6;
            if (U.dist(e, c) < reach)
            {
                wp++;
            }
        }

        public void Reset()
        {
            route.Clear();
            wp = 0;
            timer = 0;
            goalKey = -1;
        }
    }

    // perception... line of sight checks (can a see b, hill blocks view)
    public static class Perception  //manu... this class is called by zian stalker and george shambler
    {
        //  hills block the view...
        public static bool LineOfSight(Pos a, Pos b)
        {
            OrchardGame G = OrchardGame.I;
            double dx = b.x - a.x;
            double dy = b.y - a.y;
            double len = U.hypot(dx, dy);
            int steps = Math.Max(1, (int)Math.Ceiling(len / 5));
            for (int s = 0; s <= steps; s++)
            {
                double t = (double)s / steps;
                Cell c = U.w2c(a.x + dx * t, a.y + dy * t);
                if (U.inB(c.cx, c.cy) && G.grid[c.cx, c.cy] == C.HILL)
                {
                    return false;
                }
            }
            return true;
        }

        //  can a ground walker go straight from a to b...
        public static bool WalkableLine(Pos a, Pos b)
        {
            double dx = b.x - a.x;
            double dy = b.y - a.y;
            double len = U.hypot(dx, dy);
            int steps = Math.Max(1, (int)Math.Ceiling(len / 6));
            for (int s = 0; s <= steps; s++)
            {
                double t = (double)s / steps;
                Cell c = U.w2c(a.x + dx * t, a.y + dy * t);
                if (U.groundBlocked(c.cx, c.cy))
                {
                    return false;
                }
            }
            return true;
        }
    }

    // steeringmovement.... seek, arrive (slow down near goal), separate (no overlap)
    //  ground = true means hills and bushes stop the mover...
    public static class Steering //manu
    {
        //  free move, only stays on the board...
        public static void moveFree(Mover e, double dx, double dy)
        {
            e.x = U.clamp(e.x + dx, e.r, C.WORLD_W - e.r);
            e.y = U.clamp(e.y + dy, e.r, C.WORLD_H - e.r);
        }

        //  ground move, x and y checked one by one...
        public static void moveGround(Mover e, double dx, double dy)
        {
            Cell c = U.w2c(e.x + dx, e.y);
            if (!U.groundBlocked(c.cx, c.cy))
            {
                e.x = U.clamp(e.x + dx, e.r, C.WORLD_W - e.r);
            }
            c = U.w2c(e.x, e.y + dy);
            if (!U.groundBlocked(c.cx, c.cy))
            {
                e.y = U.clamp(e.y + dy, e.r, C.WORLD_H - e.r);
            }
        }

        static void Move(Mover e, double dx, double dy, bool ground)
        {
            if (ground)
            {
                moveGround(e, dx, dy);
            }
            else
            {
                moveFree(e, dx, dy);
            }
        }

        //  move towards t, speed changes smoothly...
        public static void steer(Mover e, Pos t, double sp, double dt, bool ground)
        {
            double dx = t.x - e.x;
            double dy = t.y - e.y;
            double l = U.hypot(dx, dy);
            if (l == 0) l = 1;
            double vx = dx / l * sp;
            double vy = dy / l * sp;
            e.vx = e.vx + (vx - e.vx) * Math.Min(1, 8 * dt);
            e.vy = e.vy + (vy - e.vy) * Math.Min(1, 8 * dt);
            Move(e, e.vx * dt, e.vy * dt, ground);
        }

        //  push away from other ground walkers so they do not stack...
        public static void separate(Mover e, double dt)
        {
            double px = 0;
            double py = 0;
            List<Enemy> list = U.G.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                Enemy o = list[i];
                if (o == e) continue;
                if (o.dead) continue;
                if (o.type == "scatterling" || o.type == "bad") continue;
                double d = U.dist(e, o);
                double min = e.r + o.r;
                if (d < min && d > 0.01)
                {
                    double k = (min - d) / min;
                    px = px + (e.x - o.x) / d * k;
                    py = py + (e.y - o.y) / d * k;
                }
            }
            if (px != 0 || py != 0)
            {
                moveGround(e, px * 70 * dt, py * 70 * dt);
            }
        }

        //  spot next to the tree on my own side, so attackers go round the tree...
        public static Pos slot(Mover e, Pos tree, double r)
        {
            double dx = e.x - tree.x;
            double dy = e.y - tree.y;
            double l = U.hypot(dx, dy);
            if (l < 0.01) return new Pos(tree.x, tree.y + r);
            return new Pos(tree.x + dx / l * r, tree.y + dy / l * r);
        }

        //  like steer but slows down near the target...
        public static void arrive(Mover e, Pos t, double sp, double slowR, double dt, bool ground)
        {
            double d = U.dist(e, t);
            double s = sp;
            if (d < slowR)
            {
                s = sp * d / slowR;
            }
            steer(e, t, s, dt, ground);
        }
    }
}
