using System;
using System.Collections.Generic;

namespace Orchard
{
    public class Enemy : Mover  //george manu zian
    {
        public string type;
        public double hp = 6;
        public bool dead = false;
        public int dir = 1;
        public double walk = 0;
        public double lastHit = -99;

        public Enemy(double x, double y, string type)
        {
            this.x = x;
            this.y = y;
            this.type = type;
            r = 14;
            vx = 0;
            vy = 0;
        }

        //  farmer hits the enemy...
        public void hit(double d)
        {
            hp = hp - d;
            if (hp <= 0)
            {
                dead = true;
            }
        }

        //  closest living tree...
        public TreeEntity nearTree()
        {
            TreeEntity best = null;
            double bd = 1e9;
            for (int i = 0; i < U.G.trees.Count; i++)
            {
                TreeEntity t = U.G.trees[i];
                if (!t.alive) continue;
                double d = U.dist(this, t);
                if (d < bd)
                {
                    bd = d;
                    best = t;
                }
            }
            return best;
        }

        public virtual void update(double dt)
        {
        }

        //  look the way we moved...
        protected void FaceFrom(double px)
        {
            if (x - px < 0)
            {
                dir = -1;
            }
            else if (x - px > 0)
            {
                dir = 1;
            }
        }
    }

    // stalker artitbues... perception
    //  sees the farmer only inside a view cone with no hill in the way...
    // and remembers the last seen spot for 3 seconds (simple memory)
    public class Stalker : Enemy //zian
    {
        public double speed = 108;
        public bool sees = false;
        public double sight = C.CELL * 5;
        public double face = 0;
        Pos lastSeen;
        double memory = 0;
        bool hasFp = false;
        double fpx = 0;
        double fpy = 0;
        double fvx = 0;
        double fvy = 0;
        Navigator nav = new Navigator();
        double biteCd = 0;
        const double HALF_FOV = 70 * Math.PI / 180;

        public Stalker(double x, double y) : base(x, y, "stalker")
        {
        }

        public bool Searching
        {
            get { return !sees && memory > 0; }
        }

        //  is the point inside the view cone... 
        bool InView(Pos p) //zian
        {
            double a = Math.Atan2(p.y - y, p.x - x) - face;
            while (a > Math.PI) a = a - 2 * Math.PI;
            while (a < -Math.PI) a = a + 2 * Math.PI;
            return Math.Abs(a) <= HALF_FOV;
        }

        public override void update(double dt)
        {
            double px = x;
            double py0 = y;
            OrchardGame G = U.G;
            double spd = speed;
            if (U.terr(x, y) == C.MUD) spd = speed * 0.5;

            //  guess farmer speed so the chase aims ahead...
            if (hasFp && dt > 0)
            {
                double k = Math.Min(1, 6 * dt);
                fvx = fvx + ((G.farmer.x - fpx) / dt - fvx) * k;
                fvy = fvy + ((G.farmer.y - fpy) / dt - fvy) * k;
            }
            fpx = G.farmer.x;
            fpy = G.farmer.y;
            hasFp = true;

            double d = U.dist(this, G.farmer);

            //  bite the farmer when close...
            if (biteCd > 0) biteCd = biteCd - dt;
            if (d < 26 && biteCd <= 0 && G.running)
            {
                biteCd = C.STALKER_HIT_CD;
                if (G.farmer.Hurt(C.STALKER_DMG))
                {
                    double hpLeft = Math.Ceiling(G.farmer.hp);
                    G.LogEvent("Stalker bit the farmer (hp " + hpLeft + "/" + C.FARMER_HP + ")");
                    if (G.farmer.hp <= 0)
                    {
                        G.FarmerDown();
                    }
                }
            }

            //  can i see him...
            bool can;
            if (sees)
            {
                can = d < sight * 1.2 && Perception.LineOfSight(this, G.farmer);
            }
            else
            {
                bool inRange = d < sight;
                bool closeUp = d < C.CELL * 1.5;
                bool clearView = Perception.LineOfSight(this, G.farmer);
                if (inRange && clearView && (closeUp || InView(G.farmer))) can = true;
                else can = false;
            }
            if (can && !sees)
            {
                G.LogEvent("Stalker detected target");
            }
            if (!can && sees)
            {
                lastSeen = new Pos(G.farmer.x, G.farmer.y);
                memory = 3;
                G.LogEvent("Stalker lost sight, searching");
            }
            sees = can;

            if (can)
            {
                //  chase...
                memory = 0;
                double lead = Math.Min(0.6, d / (spd * 1.1));
                double ax = U.clamp(G.farmer.x + fvx * lead, r, C.WORLD_W - r);
                double ay = U.clamp(G.farmer.y + fvy * lead, r, C.WORLD_H - r);
                Pos aim = new Pos(ax, ay);
                if (Perception.WalkableLine(this, aim))
                {
                    nav.timer = nav.timer - dt;
                    Steering.steer(this, aim, spd * 1.1, dt, true);
                }
                else
                {
                    Go(U.w2c(G.farmer.x, G.farmer.y), 1000, spd * 1.1, dt, 0.3);
                }
            }
            else if (memory > 0)
            {
                //  go to the last place he was seen...
                memory = memory - dt;
                if (U.dist(this, lastSeen) < 20)
                {
                    memory = 0;
                    face = face + 2.5;
                }
                else
                {
                    Go(U.w2c(lastSeen.x, lastSeen.y), 2000, spd, dt, 0.5);
                }
            }
            else
            {
                //  nothing seen: go chew the nearest tree...
                TreeEntity t = nearTree();
                if (t != null)
                {
                    double td = U.dist(this, t);
                    if (td < 26)
                    {
                        vx = vx * 0.5;
                        vy = vy * 0.5;
                        t.gnaw(9, dt);
                    }
                    else if (td < 60 && Perception.WalkableLine(this, t))
                    {
                        Steering.arrive(this, Steering.slot(this, t, 20), spd, 10, dt, true);
                    }
                    else
                    {
                        Go(new Cell(t.cx, t.cy), t.cx * C.GH + t.cy, spd, dt, 0.6);
                    }
                }
            }

            Steering.separate(this, dt);

            //  face where it walks...
            double mvx = x - px;
            double mvy = y - py0;
            if (mvx * mvx + mvy * mvy > 0.04)
            {
                face = Math.Atan2(mvy, mvx);
            }
            if (Math.Abs(vx) + Math.Abs(vy) > 8)
            {
                walk = walk + dt * 8;
            }
            FaceFrom(px);
        }

        //  walk to a cell with a*...
        void Go(Cell target, int key, double spd, double dt, double every)
        {
            List<Cell> goals = new List<Cell>();
            goals.Add(target);
            nav.Plan(this, goals, key, every);
            if (nav.HasRoute)
            {
                nav.Walk(this, spd, dt);
            }
            else
            {
                nav.timer = nav.timer - dt;
                Steering.steer(this, U.cc(target.cx, target.cy), spd, dt, true);
            }
        }
    }

    //  bird... flocking .. the steering behavior
    //  flocking is reynolds' boids model: 3 group steering rules added together...
    //  separation (dont crowd), alignment (match heading), cohesion (stay together)...
    //  yellow bird (scatterling) eats fruit = saved...
    //  black bird (bad) eats fruit = lost and hunts yellow birds...
    public class Bird : Enemy //zian
    {
        public double speed;
        public bool bad;
        public double bob;
        public double flyH = 26;
        public Pos roam;
        public double killCd = 0;
        public Bait tt;
        public double confT = 0;
        public double noEatT = 0;
        public Pos spinC;
        double spinA = 0;

        //  pick the type name for the base class...
        static string BirdType(bool bad)
        {
            if (bad) return "bad";
            else return "scatterling";
        }

        public Bird(double x, double y, bool bad) : base(x, y, BirdType(bad))
        {
            if (bad)
            {
                speed = 118;
            }
            else
            {
                speed = 132;
            }
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

        //  caught in a tornado: dizzy 5s, no eating 10s...
        public void Confuse(Pos centre)
        {
            confT = C.CONFUSE_T;
            noEatT = C.NO_EAT_T;
            spinC = new Pos(centre.x, centre.y);
            spinA = Math.Atan2(y - centre.y, x - centre.x);
            tt = null;
        }

        // keeps a value between -1 and 1 (full at 55)
        static double nrm(double v)
        {
            if (v == 0) return 0;
            double s = 1;
            if (v < 0) s = -1;
            return s * Math.Min(1, Math.Abs(v) / 55);
        }

        public override void update(double dt) //zian
        {
            OrchardGame G = U.G;
            bob = bob + dt * 6;
            if (killCd > 0) killCd = killCd - dt;
            if (confT > 0) confT = confT - dt;
            if (noEatT > 0) noEatT = noEatT - dt;

            //  flocking... look at birds of my own kind (my neighbourhood)...
            double sx = 0;
            double sy = 0;
            double ax = 0;
            double ay = 0;
            double cx = 0;
            double cy = 0;
            int n = 0;
            double hl = U.hypot(vx, vy);
            double hx = 0;
            double hy = 0;
            if (hl > 0)
            {
                hx = vx / hl;
                hy = vy / hl;
            }
            for (int i = 0; i < G.enemies.Count; i++)
            {
                Enemy o = G.enemies[i];
                if (o.type != type) continue;
                if (o == this) continue;
                double d = U.dist(this, o);
                if (d > C.CELL * 3) continue;
                //  ignore birds right behind me...
                if (d > 0.01 && ((o.x - x) * hx + (o.y - y) * hy) / d < -0.6 && d > C.CELL * 1.1) continue;
                n++;
                ax = ax + o.vx;
                ay = ay + o.vy;
                cx = cx + o.x;
                cy = cy + o.y;
                //  separation: closer birds push harder...
                if (d < C.CELL * 1.1 && d > 0.01)
                {
                    double w = 1 - d / (C.CELL * 1.1);
                    sx = sx + (x - o.x) / d * (0.5 + w);
                    sy = sy + (y - o.y) / d * (0.5 + w);
                }
            }

            double fx = 0;
            double fy = 0;
            if (n > 0)
            {
                ax = ax / n;
                ay = ay / n;
                double al0 = U.hypot(ax, ay);
                if (al0 > 1)
                {
                    ax = ax / al0;
                    ay = ay / al0;
                }
                cx = cx / n - x;
                cy = cy / n - y;
                fx = fx + sx * 1.6 + ax * 0.9 + nrm(cx) * 0.7;
                fy = fy + sy * 1.6 + ay * 0.9 + nrm(cy) * 0.7;
            }

            //  go for fallen fruit, if none fly around...
            Bait goal = null;
            if (bad || noEatT <= 0)
            {
                goal = nearBait();
            }
            if (goal != null)
            {
                double dx = goal.x - x;
                double dy = goal.y - y;
                double l = U.hypot(dx, dy);
                if (l == 0) l = 1;
                fx = fx + dx / l * 1.6;
                fy = fy + dy / l * 1.6;
                tt = goal;
                if (U.dist(this, goal) < 20)
                {
                    int bi = G.baits.IndexOf(goal);
                    if (bi >= 0)
                    {
                        G.baits.RemoveAt(bi);
                        if (bad)
                        {
                            G.fruitLost++;
                            G.LogEvent("Bad bird stole a fruit");
                        }
                        else
                        {
                            G.fruitSaved++;
                            G.LogEvent("Scatterling saved a fruit");
                        }
                    }
                }
            }
            else
            {
                tt = null;
                if (U.dist(this, roam) < C.CELL)
                {
                    roam = U.randSky();
                }
                double dx = roam.x - x;
                double dy = roam.y - y;
                double l = U.hypot(dx, dy);
                if (l == 0) l = 1;
                fx = fx + dx / l * 1.0;
                fy = fy + dy / l * 1.0;
            }

            //  black bird: chase the nearest yellow bird...
            if (bad && killCd <= 0 && goal == null)
            {
                Bird prey = null;
                double pd = C.CELL * 4;
                for (int i = 0; i < G.enemies.Count; i++)
                {
                    Enemy o = G.enemies[i];
                    if (o.type != "scatterling") continue;
                    double d = U.dist(this, o);
                    if (d < pd)
                    {
                        pd = d;
                        prey = (Bird)o;
                    }
                }
                if (prey != null)
                {
                    //  aim a little ahead of the prey...
                    double lead = pd / speed;
                    double tx = prey.x + prey.vx * prey.speed * lead;
                    double ty = prey.y + prey.vy * prey.speed * lead;
                    double dx = tx - x;
                    double dy = ty - y;
                    double l = U.hypot(dx, dy);
                    if (l == 0) l = 1;
                    fx = fx + dx / l * 1.5;
                    fy = fy + dy / l * 1.5;
                }
            }

            //  black bird catches a yellow bird...
            if (bad && killCd <= 0)
            {
                for (int i = 0; i < G.enemies.Count; i++)
                {
                    Enemy o = G.enemies[i];
                    if (o.type == "scatterling" && !o.dead && U.dist(this, o) < C.CELL * 0.55)
                    {
                        o.dead = true;
                        killCd = C.KILL_COOLDOWN;
                        G.LogEvent("Bad bird caught a scatterling");
                        break;
                    }
                }
            }

            //  dizzy: spin round the tornado and forget everything else...
            if (confT > 0 && spinC != null)
            {
                spinA = spinA + dt * 5.5;
                double rr = C.CELL * (0.8 + 0.35 * Math.Sin(bob * 0.9));
                double tx = spinC.x + Math.Cos(spinA) * rr;
                double ty = spinC.y + Math.Sin(spinA) * rr * 0.75;
                double dx = tx - x;
                double dy = ty - y;
                double l = U.hypot(dx, dy);
                if (l == 0) l = 1;
                fx = dx / l * 2.5;
                fy = dy / l * 2.5;
            }

            //  yellow bird runs away from black birds...
            if (!bad)
            {
                for (int i = 0; i < G.enemies.Count; i++)
                {
                    Enemy o = G.enemies[i];
                    if (o.type != "bad") continue;
                    if (o.dead) continue;
                    double d = U.dist(this, o);
                    if (d < C.CELL * 2.5 && d > 0.01)
                    {
                        double mul = 3.2;
                        if (confT > 0) mul = 1.0;
                        double w = (1 - d / (C.CELL * 2.5)) * mul;
                        fx = fx + (x - o.x) / d * w;
                        fy = fy + (y - o.y) / d * w;
                    }
                }
            }

            //  turn away from the edge of the board...
            double m = C.CELL;
            if (x < m) fx = fx + (m - x) / m * 2.5;
            if (x > C.WORLD_W - m) fx = fx - (x - (C.WORLD_W - m)) / m * 2.5;
            if (y < m) fy = fy + (m - y) / m * 2.5;
            if (y > C.WORLD_H - m) fy = fy - (y - (C.WORLD_H - m)) / m * 2.5;

            //  new speed and move...
            double al = U.hypot(fx, fy);
            if (al == 0) al = 1;
            vx = vx + (fx / al - vx) * Math.Min(1, 6 * dt);
            vy = vy + (fy / al - vy) * Math.Min(1, 6 * dt);
            double vl = U.hypot(vx, vy);
            if (vl == 0) vl = 1;
            Steering.moveFree(this, vx / vl * speed * dt, vy / vl * speed * dt);
            if (vx < 0)
            {
                dir = -1;
            }
            else
            {
                dir = 1;
            }
        }

        //  nearest fruit that no closer bird of my kind is going for...
        Bait nearBait() //zian
        {
            Bait best = null;
            Bait any = null;
            double bd = 1e9;
            double ad = 1e9;
            List<Bait> baits = U.G.baits;
            for (int i = 0; i < baits.Count; i++)
            {
                Bait p = baits[i];
                double d = U.dist(this, p);
                if (d < ad)
                {
                    ad = d;
                    any = p;
                }
                bool claimed = false;
                for (int j = 0; j < U.G.enemies.Count; j++)
                {
                    Enemy o = U.G.enemies[j];
                    if (!(o is Bird)) continue;
                    Bird ob = (Bird)o;
                    if (ob == this) continue;
                    if (ob.type != type) continue;
                    if (ob.tt != p) continue;
                    if (U.dist(ob, p) < d)
                    {
                        claimed = true;
                        break;
                    }
                }
                if (!claimed && d < bd)
                {
                    bd = d;
                    best = p;
                }
            }
            if (best != null) return best;
            return any;
        }
    }
}
