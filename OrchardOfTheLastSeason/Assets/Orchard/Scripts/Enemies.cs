using System.Collections.Generic;
using UnityEngine;

namespace Orchard
{
    public class Enemy : Mover
    {
        public string type;
        public double hp = 6;
        public bool dead = false;
        public int dir = 1;
        public double walk = 0;
        public double lastHitByFarmer = -99;   // night time of the farmer's last scythe hit (for retaliation)

        public Enemy(double x, double y, string type)
        {
            this.x = x; this.y = y; r = 14; this.type = type; vx = 0; vy = 0;
        }

        public void hit(double d)
        {
            hp -= d;
            if (hp <= 0) dead = true;
        }

        public TreeEntity nearTree()
        {
            TreeEntity b = null;
            double bd = 1e9;
            foreach (TreeEntity t in U.G.trees)
            {
                if (!t.alive) continue;
                double d = U.dist(this, t);
                if (d < bd) { bd = d; b = t; }
            }
            return b;
        }

        public virtual void update(double dt) { }

        protected void FaceFrom(double px)
        {
            if (x - px < 0) dir = -1;
            else if (x - px > 0) dir = 1;
        }
    }

    // shared by the ground walkers: the alive trees as a* goals
    public static class Targets
    {
        public static List<Cell> AliveTreeCells(out int key)
        {
            List<Cell> cells = new List<Cell>();
            key = 0;
            int bit = 1;
            foreach (TreeEntity t in U.G.trees)
            {
                if (t.alive) { cells.Add(new Cell(t.cx, t.cy)); key |= bit; }
                bit <<= 1;
            }
            return cells;
        }

        public static TreeEntity TreeAt(Cell c)
        {
            foreach (TreeEntity t in U.G.trees) if (t.alive && t.cx == c.cx && t.cy == c.cy) return t;
            return null;
        }
    }

    // the shambler (utility ai) lives in ShamblerUtilityAI.cs

    // perception: chases the farmer when he is within sight range, inside its 140 degree view cone
    // (or very close, within 1.5 cells, where it notices him anyway) AND no hill blocks the view.
    // once locked on it keeps him until he is 20% further than sight range (no flicker at the edge),
    // and leads its chase towards where he is running, not where he was.
    // when sight breaks it remembers where he was last seen and searches there for a few seconds.
    // all walking uses a* so hills and bushes never trap it.
    public class Stalker : Enemy
    {
        public double speed = 108;
        public bool sees = false;
        public double sight = C.CELL * 5;
        public double face = 0;                 // heading in radians
        Pos lastSeen;
        double memory;
        double fpx = double.NaN, fpy, fvx, fvy;  // farmer velocity estimate
        Navigator nav = new Navigator();
        public bool Searching { get { return !sees && memory > 0; } }

        const double HALF_FOV = 70 * System.Math.PI / 180;

        public Stalker(double x, double y) : base(x, y, "stalker") { }

        bool InView(Pos p)
        {
            double a = System.Math.Atan2(p.y - y, p.x - x) - face;
            while (a > System.Math.PI) a -= 2 * System.Math.PI;
            while (a < -System.Math.PI) a += 2 * System.Math.PI;
            return System.Math.Abs(a) <= HALF_FOV;
        }

        public override void update(double dt)
        {
            double px = x, py0 = y;
            OrchardGame G = U.G;
            bool onMud = U.terr(x, y) == C.MUD;
            double spd = speed * (onMud ? 0.5 : 1);

            // estimate the farmer's velocity (smoothed) for leading the chase
            if (!double.IsNaN(fpx) && dt > 0)
            {
                double k = System.Math.Min(1, 6 * dt);
                fvx += ((G.farmer.x - fpx) / dt - fvx) * k;
                fvy += ((G.farmer.y - fpy) / dt - fvy) * k;
            }
            fpx = G.farmer.x; fpy = G.farmer.y;

            double d = U.dist(this, G.farmer);
            bool can;
            if (sees) can = d < sight * 1.2 && Perception.LineOfSight(this, G.farmer);
            else can = d < sight && (d < C.CELL * 1.5 || InView(G.farmer)) && Perception.LineOfSight(this, G.farmer);
            if (can && !sees) G.LogEvent("Stalker detected target");
            if (!can && sees)
            {
                lastSeen = new Pos(G.farmer.x, G.farmer.y);
                memory = 3;
                G.LogEvent("Stalker lost sight, searching");
            }
            sees = can;

            if (can)
            {
                memory = 0;
                double lead = System.Math.Min(0.6, d / (spd * 1.1));
                Pos aim = new Pos(U.clamp(G.farmer.x + fvx * lead, r, C.WORLD_W - r), U.clamp(G.farmer.y + fvy * lead, r, C.WORLD_H - r));
                if (Perception.WalkableLine(this, aim)) { nav.timer -= dt; Steering.steer(this, aim, spd * 1.1, dt, Steering.moveGround); }
                else Go(U.w2c(G.farmer.x, G.farmer.y), 1000, spd * 1.1, dt, 0.3);
            }
            else if (memory > 0)
            {
                memory -= dt;
                if (U.dist(this, lastSeen) < 20) { memory = 0; face += 2.5; }   // glance around at the spot
                else Go(U.w2c(lastSeen.x, lastSeen.y), 2000, spd, dt, 0.5);
            }
            else
            {
                TreeEntity t = nearTree();
                if (t != null)
                {
                    double td = U.dist(this, t);
                    if (td < 26) { vx *= 0.5; vy *= 0.5; t.gnaw(9, dt); }
                    else if (td < 60 && Perception.WalkableLine(this, t)) Steering.arrive(this, Steering.slot(this, t, 20), spd, 10, dt, Steering.moveGround);
                    else Go(new Cell(t.cx, t.cy), t.cx * C.GH + t.cy, spd, dt, 0.6);
                }
            }
            Steering.separate(this, dt);
            // look where it is going
            double mvx = x - px, mvy = y - py0;
            if (mvx * mvx + mvy * mvy > 0.04) face = System.Math.Atan2(mvy, mvx);
            if (System.Math.Abs(vx) + System.Math.Abs(vy) > 8) walk += dt * 8;
            FaceFrom(px);
        }

        void Go(Cell target, int key, double spd, double dt, double every)
        {
            List<Cell> goals = new List<Cell>();
            goals.Add(target);
            nav.Plan(this, goals, key, every);
            if (nav.HasRoute) nav.Walk(this, spd, dt);
            else { nav.timer -= dt; Steering.steer(this, U.cc(target.cx, target.cy), spd, dt, Steering.moveGround); }
        }
    }

    // ai technique: flocking (boids: separation, alignment, cohesion) + goal seeking
    // yellow = good: eats fallen fruit -> saved, farmer can't hurt it.
    // black  = bad:  eats fallen fruit -> lost, hunts yellow birds (one at a time, 1.8s cooldown).
    public class Bird : Enemy
    {
        public double speed;
        public bool bad;
        public double bob;
        public double flyH = 26;
        public Pos roam;
        public double killCd = 0;
        public Pos tt;

        public Bird(double x, double y, bool bad) : base(x, y, bad ? "bad" : "scatterling")
        {
            speed = bad ? 118 : 132;
            r = 10;
            hp = 3;
            this.bad = bad;
            vx = U.R() - .5;
            vy = U.R() - .5;
            bob = U.R() * 6;
            flyH = 26;
            roam = U.randSky();
            killCd = 0;
        }

        static double nrm(double v)
        {
            if (v == 0) return 0;
            return v / System.Math.Abs(v) * System.Math.Min(1, System.Math.Abs(v) / 55);
        }

        public override void update(double dt)
        {
            OrchardGame G = U.G;
            bob += dt * 6;
            if (killCd > 0) killCd -= dt;

            // boids over birds of my own kind
            double sx = 0, sy = 0, ax = 0, ay = 0, cx = 0, cy = 0;
            int n = 0;
            double hl = U.hypot(vx, vy);
            double hx = hl > 0 ? vx / hl : 0, hy = hl > 0 ? vy / hl : 0;
            foreach (Enemy o in G.enemies)
            {
                if (o.type != type) continue;
                if (o == this) continue;
                double d = U.dist(this, o);
                if (d > C.CELL * 3) continue;
                // field of view: a bird does not follow flock-mates directly behind it
                if (d > 0.01 && ((o.x - x) * hx + (o.y - y) * hy) / d < -0.6 && d > C.CELL * 1.1) continue;
                n++;
                ax += o.vx; ay += o.vy;
                cx += o.x; cy += o.y;
                if (d < C.CELL * 1.1 && d > 0.01)
                {
                    // closer neighbours push harder
                    double w = 1 - d / (C.CELL * 1.1);
                    sx += (x - o.x) / d * (0.5 + w);
                    sy += (y - o.y) / d * (0.5 + w);
                }
            }
            double fx = 0, fy = 0;
            if (n > 0)
            {
                ax /= n; ay /= n;
                double al0 = U.hypot(ax, ay);
                if (al0 > 1) { ax /= al0; ay /= al0; }     // align to the flock's heading, not its raw speed
                cx = cx / n - x; cy = cy / n - y;
                fx += sx * 1.6 + ax * 0.9 + nrm(cx) * 0.7;
                fy += sy * 1.6 + ay * 0.9 + nrm(cy) * 0.7;
            }

            // both birds chase fallen fruit; if none, roam the sky
            Bait goal = nearBait();
            if (goal != null)
            {
                double dx = goal.x - x, dy = goal.y - y;
                double l = U.hypot(dx, dy);
                if (l == 0) l = 1;
                fx += dx / l * 1.6; fy += dy / l * 1.6;
                tt = goal;
                if (U.dist(this, goal) < 20)
                {
                    int i = G.baits.IndexOf(goal);
                    if (i >= 0)
                    {
                        G.baits.RemoveAt(i);
                        if (bad) { G.fruitLost++; G.LogEvent("Bad bird stole a fruit"); }
                        else { G.fruitSaved++; G.birdSaved++; G.LogEvent("Scatterling saved a fruit"); G.CheckRevive(); }
                    }
                }
            }
            else
            {
                tt = null;
                if (U.dist(this, roam) < C.CELL) roam = U.randSky();
                double dx = roam.x - x, dy = roam.y - y;
                double l = U.hypot(dx, dy);
                if (l == 0) l = 1;
                fx += dx / l * 1.0; fy += dy / l * 1.0;
            }

            // black birds hunt nearby yellow birds (capped by a cooldown = how many at a time)
            if (bad && killCd <= 0 && goal == null)
            {
                // pursuit: steer at where the nearest yellow bird is about to be
                Enemy prey = null;
                double pd = C.CELL * 4;
                foreach (Enemy o in G.enemies)
                {
                    if (o.type != "scatterling") continue;
                    double d = U.dist(this, o);
                    if (d < pd) { pd = d; prey = o; }
                }
                if (prey != null)
                {
                    Bird pb = (Bird)prey;
                    double lead = pd / speed;
                    double tx = prey.x + prey.vx * pb.speed * lead, ty = prey.y + prey.vy * pb.speed * lead;
                    double dx = tx - x, dy = ty - y;
                    double l = U.hypot(dx, dy);
                    if (l == 0) l = 1;
                    fx += dx / l * 1.5; fy += dy / l * 1.5;
                }
            }
            if (bad && killCd <= 0)
            {
                foreach (Enemy o in G.enemies)
                {
                    if (o.type == "scatterling" && !o.dead && U.dist(this, o) < C.CELL * 0.55)
                    {
                        o.dead = true;
                        G.BirdCaught((Bird)o);
                        killCd = C.KILL_COOLDOWN;
                        G.LogEvent("Bad bird caught a scatterling");
                        break;
                    }
                }
            }

            // predator avoidance: a scatterling flees from bad birds closing in (strongest when very close),
            // and the flock still holds together through separation/alignment/cohesion above
            if (!bad)
            {
                foreach (Enemy o in G.enemies)
                {
                    if (o.type != "bad" || o.dead) continue;
                    double d = U.dist(this, o);
                    if (d < C.CELL * 2.5 && d > 0.01)
                    {
                        double w = (1 - d / (C.CELL * 2.5)) * 3.2;
                        fx += (x - o.x) / d * w;
                        fy += (y - o.y) / d * w;
                    }
                }
            }

            // soft walls: turn away before reaching the board edge instead of sliding along it
            double m = C.CELL;
            if (x < m) fx += (m - x) / m * 2.5;
            if (x > C.WORLD_W - m) fx -= (x - (C.WORLD_W - m)) / m * 2.5;
            if (y < m) fy += (m - y) / m * 2.5;
            if (y > C.WORLD_H - m) fy -= (y - (C.WORLD_H - m)) / m * 2.5;

            double al = U.hypot(fx, fy);
            if (al == 0) al = 1;
            vx += (fx / al - vx) * System.Math.Min(1, 6 * dt);
            vy += (fy / al - vy) * System.Math.Min(1, 6 * dt);
            double vl = U.hypot(vx, vy);
            if (vl == 0) vl = 1;
            Steering.moveFree(this, vx / vl * speed * dt, vy / vl * speed * dt);
            dir = vx < 0 ? -1 : 1;
        }

        // nearest fallen fruit that no closer bird of my kind is already going for,
        // so the flock spreads over several fruits instead of all diving at one
        Bait nearBait()
        {
            Bait b = null, any = null;
            double bd = 1e9, ad = 1e9;
            foreach (Bait p in U.G.baits)
            {
                double d = U.dist(this, p);
                if (d < ad) { ad = d; any = p; }
                bool claimed = false;
                foreach (Enemy o in U.G.enemies)
                {
                    Bird ob = o as Bird;
                    if (ob == null || ob == this || ob.type != type || ob.tt != p) continue;
                    if (U.dist(ob, p) < d) { claimed = true; break; }
                }
                if (!claimed && d < bd) { bd = d; b = p; }
            }
            return b ?? any;
        }
    }
}
