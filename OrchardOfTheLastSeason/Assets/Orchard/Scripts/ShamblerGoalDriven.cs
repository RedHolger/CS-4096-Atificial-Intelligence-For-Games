using System;
using System.Collections.Generic;

namespace Orchard
{
    // shambler brain... goal driven behaviour
    //
    //  pick a goal, chase it...
    // we took that same idea and gave each goal a number instead of a yes/no...
    //  so every 0.25s each goal gets a want score from 0 to 1, highest want wins...
    //  team calls this our desired way... just with numbers...
    //
    //  goals: raid tree, eat fruit, pack a dog, hit farmer, flee, whirl...
    //
    // our own small rules on top...
    //  - the goal it does now gets a tiny extra want, so it does not flip every tick...
    // - a goal with more checks is judged a bit softer (our own fairness rule)
    //  - it only acts on what it can see (7 cells, no hill in way)... perception...
    //  - shamblers read what other shamblers do, so a pack can form...

    public enum ShamAct
    {
        RaidTree,
        EatFruit,
        PackDog,
        HitFarmer,
        Flee,
        Whirl
    }

    public static class GoalScore //george
    {
        //  near... 1 when close, 0 when far away...
        public static double Near(double d, double max)
        {
            double v = 1 - d / max;
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }

        // keep a number inside 0 to 1...
        public static double Clamp01(double v)
        {
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }

        // our own want score...
        // test was all-or-nothing, so i multiply the checks (all must be ok)
        //  but many small checks make the number tiny, so a goal with lots of checks...
        // would always lose... team made our own "lift" rule to fix that:
        //  the more checks a goal has, the more we let it go one one low check...
        
        public static double Score(double w, double[] c)
        {
            double p = 1;                       // p... product of all checks
            for (int i = 0; i < c.Length; i++)
            {
                p = p * Clamp01(c[i]);
            }
            int n = c.Length;                   // n... how many checks
            if (n > 1 && p > 0)
            {
                double lift = 1.0 - 1.0 / n;    // more checks, more forgiving
                p = p + (1 - p) * lift * p;     // nudge the score back up
            }
            return w * p;
        }
    }

    public class Shambler : Enemy
    {
        public const double MAX_HP = 6;
        public const int N_ACT = 6;
        public const double WHIRL_R = 18;

        public double speed = 86;

        //  what it decided and what it aims at...
        public ShamAct act = ShamAct.RaidTree;
        public TreeEntity treeT;
        public Bait fruitT;
        public Mover dogT;
        public double[] scores = new double[6];
        public bool biting = false;

        //  whirl state...
        public Pos whirlC;
        public double whirlCd = 0;
        double whirlA = 0;
        double whirlT = 0;
        double whirlWait = 0;
        Pos wcTmp;

        double thinkT;
        double hitCd = 0;
        Pos safeSpot;
        Navigator nav = new Navigator();
        bool loggedReroute = false;

        public Shambler(double x, double y) : base(x, y, "shambler")
        {
            hp = MAX_HP;
            thinkT = U.R() * 0.25;
        }

        // what it can see (perception)

        //  can see... within 7 cells and no hill blocking the straight line...
        bool CanSee(Pos p)
        {
            if (U.dist(this, p) >= C.CELL * 7) return false;
            return Perception.LineOfSight(this, p);
        }

        // how dangerous is it to stand at p (farmer and thistle near)
        double DangerAt(Pos p)
        {
            OrchardGame G = U.G;
            double d = GoalScore.Near(U.dist(p, G.farmer), C.CELL * 3.5);
            if (G.thistle.state != ThistleState.Flee)
            {
                double d2 = 0.8 * GoalScore.Near(U.dist(p, G.thistle), C.CELL * 3);
                if (d2 > d) d = d2;
            }
            return d;
        }

        //  other shamblers already biting or going for this dog...
        public static int Committed(Mover dog, Shambler except)
        {
            int n = 0;
            List<Enemy> list = U.G.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].type != "shambler") continue;
                Shambler s = (Shambler)list[i];
                if (s == except) continue;
                if (s.dead) continue;
                if (s.act == ShamAct.PackDog && s.dogT == dog)
                {
                    n++;
                }
            }
            return n;
        }

        //  shamblers i can see near the dog that could join me...
        int Helpers(Mover dog)
        {
            int n = 0;
            List<Enemy> list = U.G.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].type != "shambler") continue;
                Shambler s = (Shambler)list[i];
                if (s == this) continue;
                if (s.dead) continue;
                if (s.act == ShamAct.Flee) continue;
                if (U.dist(s, dog) < C.CELL * 7 && CanSee(s))
                {
                    n++;
                }
            }
            return n;
        }

        // work out the want of every goal, then pick the biggest

        void Think() //george
        {
            OrchardGame G = U.G;
            double hpF = hp / MAX_HP;
            double healthy = GoalScore.Clamp01(hpF * 1.3);
            double injury = GoalScore.Clamp01((0.7 - hpF) / 0.55);

            //  raid tree: close, weak and not guarded...
            double bestTree = 0;
            TreeEntity bt = null;
            for (int i = 0; i < G.trees.Count; i++)
            {
                TreeEntity t = G.trees[i];
                if (!t.alive) continue;
                double near = 0.25 + 0.75 * GoalScore.Near(U.dist(this, t), C.CELL * 16);
                double weak = 0.6 + 0.4 * (1 - t.hp / t.maxhp);
                double unguarded = 1 - 0.7 * DangerAt(t);
                //  cheap wall peek... if a hill, bush or log sits in the straight line the...
                //  tree wants a bit less. no A* yet, A* only runs later for the tree that wins...
                double clear = 1.0;
                if (!Perception.WalkableLine(this, t)) clear = 0.6;
                double s = GoalScore.Score(0.62, new double[] { near, weak, unguarded, clear, healthy });
                if (s > bestTree)
                {
                    bestTree = s;
                    bt = t;
                }
            }

            //  eat fruit: fruit in smelling range, more wanted when hurt...
            double bestFruit = 0;
            Bait bf = null;
            for (int i = 0; i < G.baits.Count; i++)
            {
                Bait b = G.baits[i];
                double d = U.dist(this, b);
                if (d > C.CELL * 8) continue;
                double want = 0.45 + 0.55 * injury;
                double safe = 1 - 0.6 * DangerAt(b);
                double close = 0.3 + 0.7 * GoalScore.Near(d, C.CELL * 8);
                double s = GoalScore.Score(0.85, new double[] { want, close, safe });
                if (s > bestFruit)
                {
                    bestFruit = s;
                    bf = b;
                }
            }

            //  pack dog: only good with company...
            double bestDog = 0;
            Mover bd = null;
            Mover[] dogs = new Mover[] { G.pip, G.thistle };
            for (int i = 0; i < 2; i++)
            {
                Mover dog = dogs[i];
                if (dog == null) continue;
                if (DogHp(dog) <= 0) continue;
                if (!CanSee(dog)) continue;
                int on = Committed(dog, this);
                double pack;
                if (on >= 2)
                {
                    pack = 0.7;
                }
                else if (on == 1)
                {
                    pack = 1.0;
                }
                else
                {
                    if (Helpers(dog) > 0)
                    {
                        pack = 0.6;
                    }
                    else
                    {
                        pack = 0.12;
                    }
                }
                //  grudge: the dog is in our way...
                if (Interferes(dog))
                {
                    pack = Math.Min(1, pack + 0.35);
                }
                double weak = 0.6 + 0.4 * (1 - DogHp(dog) / 5.0);
                double close = 0.35 + 0.65 * GoalScore.Near(U.dist(this, dog), C.CELL * 7);
                double s = GoalScore.Score(0.82, new double[] { close, pack, weak, healthy });
                if (s > bestDog)
                {
                    bestDog = s;
                    bd = dog;
                }
            }

            //  hit farmer: pay back when he hit me a moment ago...
            double recent = GoalScore.Clamp01(1 - (G.nightTime - lastHit) / 2.0);
            double hitF = 0;
            if (hitCd <= 0)
            {
                double fclose = GoalScore.Near(U.dist(this, G.farmer), C.CELL * 2.5);
                hitF = GoalScore.Score(0.85, new double[] { recent, fclose, healthy });
            }

            //  whirl: spin a tornado with a friend when birds are close...
            double whirl = WhirlScore(healthy);
            Pos wc = wcTmp;

            //  flee: hurt and in danger...
            double threat = DangerAt(this);
            double flee = GoalScore.Score(1.0, new double[] { injury, 0.45 + 0.55 * threat });

            scores[(int)ShamAct.RaidTree] = bestTree;
            scores[(int)ShamAct.EatFruit] = bestFruit;
            scores[(int)ShamAct.PackDog] = bestDog;
            scores[(int)ShamAct.HitFarmer] = hitF;
            scores[(int)ShamAct.Flee] = flee;
            scores[(int)ShamAct.Whirl] = whirl;

            //  our own stickiness rule... goal it already does gets a tiny extra want...
            // so it does not flip goal every tick (looked twitchy without this)
            double[] s2 = new double[N_ACT];
            for (int i = 0; i < N_ACT; i++)
            {
                s2[i] = scores[i];
            }
            if (act == ShamAct.Whirl)
            {
                s2[(int)act] = s2[(int)act] + 0.2;
            }
            else
            {
                s2[(int)act] = s2[(int)act] + 0.12;
            }
            int best = 0;
            for (int i = 1; i < N_ACT; i++)
            {
                if (s2[i] > s2[best]) best = i;
            }
            ShamAct next = (ShamAct)best;

            if (next != act || (next == ShamAct.PackDog && bd != dogT))
            {
                if (next == ShamAct.PackDog && Committed(bd, this) >= 1)
                {
                    string who = "Thistle";
                    if (bd == G.pip) who = "Pip";
                    G.LogOnce("pack", "Shamblers pack up on " + who + "!");
                }
                else if (next == ShamAct.Flee)
                {
                    G.LogOnce("flee", "Shambler hurt, fleeing to heal");
                }
                else if (next == ShamAct.HitFarmer)
                {
                    G.LogOnce("hitback", "Shambler turns on the farmer");
                }
                else if (next == ShamAct.EatFruit && injury > 0.3)
                {
                    G.LogOnce("eatheal", "Shambler goes for fruit to heal");
                }
                else if (next == ShamAct.Whirl)
                {
                    G.LogOnce("whirl", "Shamblers start circling: a tornado is coming!");
                }

                nav.Reset();
                safeSpot = null;
                if (next == ShamAct.Whirl)
                {
                    whirlC = wc;
                    whirlT = 0;
                    whirlWait = 0;
                    whirlA = Math.Atan2(y - wc.y, x - wc.x);
                }
                else
                {
                    whirlC = null;
                }
            }
            else if (next == ShamAct.Whirl && whirlC == null)
            {
                whirlC = wc;
            }

            act = next;
            treeT = bt;
            fruitT = bf;
            dogT = bd;
        }

        //whirl

        //  how much do yellow birds pull me to spin a tornado at this spot... george
        public static double BirdSignal(Pos me)
        {
            double best = 0;
            List<Enemy> list = U.G.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].type != "scatterling") continue;
                Bird b = (Bird)list[i];
                if (b.dead) continue;
                if (b.noEatT > 0) continue;

                double d = U.dist(me, b);
                double overhead = GoalScore.Near(d, C.CELL * 6);

                //  is the bird flying towards me...
                double approach = 0;
                double vl = U.hypot(b.vx, b.vy);
                if (vl > 0.01)
                {
                    double ux = b.vx / vl;
                    double uy = b.vy / vl;
                    double rx = me.x - b.x;
                    double ry = me.y - b.y;
                    double along = rx * ux + ry * uy;
                    if (along > 0 && along < C.CELL * 9)
                    {
                        double perp = Math.Abs(rx * uy - ry * ux);
                        if (perp < C.CELL * 2.5)
                        {
                            double sideFit = 1 - perp / (C.CELL * 2.5);
                            double alongFit = 0.4 + 0.6 * GoalScore.Near(along, C.CELL * 9);
                            approach = 0.9 * sideFit * alongFit;
                        }
                    }
                }

                double far = 0.4 * GoalScore.Near(d, C.CELL * 14);
                double sig = Math.Max(Math.Max(overhead, approach), far);
                if (sig > best) best = sig;
            }
            return best;
        }

        //  score of whirl. the circle centre is put in wcTmp... george
        double WhirlScore(double healthy)
        {
            OrchardGame G = U.G;
            wcTmp = null;
            if (G.night < C.WHIRL_NIGHT) return 0;
            if (whirlCd > 0) return 0;
            //  wait a bit after the last tornado...
            if (act != ShamAct.Whirl && G.nightTime - G.lastTornadoAt < 6) return 0;

            //  find a friend already whirling, or a friend that can start...
            Shambler joinS = null;
            Shambler mate = null;
            double jd = C.CELL * 6;
            double md = C.CELL * 6;
            for (int i = 0; i < G.enemies.Count; i++)
            {
                if (G.enemies[i].type != "shambler") continue;
                Shambler s = (Shambler)G.enemies[i];
                if (s == this) continue;
                if (s.dead) continue;
                double d = U.dist(this, s);
                if (s.act == ShamAct.Whirl && s.whirlC != null && U.dist(this, s.whirlC) < jd)
                {
                    jd = U.dist(this, s.whirlC);
                    joinS = s;
                }
                else if (s.act != ShamAct.Flee && s.whirlCd <= 0 && d < md && CanSee(s))
                {
                    md = d;
                    mate = s;
                }
            }

            double partner;
            Pos centre;
            if (joinS != null)
            {
                partner = 1.0;
                centre = joinS.whirlC;
            }
            else if (mate != null)
            {
                partner = 0.8;
                centre = new Pos((x + mate.x) / 2, (y + mate.y) / 2);
            }
            else
            {
                return 0;
            }
            //  keep my own circle once started...
            if (act == ShamAct.Whirl && whirlC != null)
            {
                centre = whirlC;
            }
            wcTmp = centre;

            //  only one tornado at a time...
            if (G.tornados.Count > 0) return 0;

            double birds = BirdSignal(centre);
            if (joinS != null || act == ShamAct.Whirl)
            {
                birds = Math.Max(birds, 0.7);
            }
            if (birds <= 0) return 0;
            return GoalScore.Score(1.15, new double[] { 0.3 + 0.7 * birds, partner, healthy });
        }

        //  is the dog in our way...
        bool Interferes(Mover dog)
        {
            OrchardGame G = U.G;
            //  pip repairs the tree i chew...
            if (dog == G.pip && G.pip.target != null && G.pip.target == treeT && U.dist(this, treeT) < 60)
            {
                return true;
            }
            //  thistle bites me or a shambler near me...
            if (dog == G.thistle && G.thistle.state == ThistleState.Feed && G.thistle.target != null)
            {
                Enemy tg = G.thistle.target;
                if (tg == this) return true;
                if (tg.type == "shambler" && U.dist(this, tg) < C.CELL * 3) return true;
            }
            return false;
        }

        public static double DogHp(Mover dog)
        {
            if (dog is Pip) return ((Pip)dog).hp;
            if (dog is Thistle) return ((Thistle)dog).hp;
            return 0;
        }

        //acting

        public override void update(double dt)
        {
            OrchardGame G = U.G;
            double px = x;
            double spd = speed;
            if (U.terr(x, y) == C.MUD) spd = speed * 0.45;
            if (hitCd > 0) hitCd = hitCd - dt;
            if (whirlCd > 0) whirlCd = whirlCd - dt;

            thinkT = thinkT - dt;
            if (thinkT <= 0)
            {
                thinkT = 0.25;
                Think();
            }

            //  target can be gone since the last think...
            if (act == ShamAct.EatFruit && (fruitT == null || !G.baits.Contains(fruitT)))
            {
                thinkT = 0;
                act = ShamAct.RaidTree;
            }
            if (act == ShamAct.PackDog && (dogT == null || DogHp(dogT) <= 0))
            {
                thinkT = 0;
                act = ShamAct.RaidTree;
            }
            if (act == ShamAct.RaidTree && (treeT == null || !treeT.alive))
            {
                treeT = nearTree();
            }
            if (act == ShamAct.Whirl && whirlC == null)
            {
                thinkT = 0;
                act = ShamAct.RaidTree;
            }

            if (act == ShamAct.RaidTree) DoRaid(spd, dt);
            else if (act == ShamAct.EatFruit) DoEat(spd, dt);
            else if (act == ShamAct.PackDog) DoPack(spd, dt);
            else if (act == ShamAct.HitFarmer) DoHitFarmer(spd, dt);
            else if (act == ShamAct.Whirl) DoWhirl(spd, dt);
            else DoFlee(spd, dt);

            Steering.separate(this, dt);
            if (Math.Abs(vx) + Math.Abs(vy) > 8)
            {
                walk = walk + dt * 8;
            }
            FaceFrom(px);
        }

        void DoRaid(double spd, double dt)
        {
            TreeEntity goal = treeT;
            if (goal == null) return;
            double gd = U.dist(this, goal);
            if (gd < 26)
            {
                vx = vx * 0.5;
                vy = vy * 0.5;
                goal.gnaw(10, dt);
            }
            else if (gd < 60 && Perception.WalkableLine(this, goal))
            {
                Steering.arrive(this, Steering.slot(this, goal, 20), spd, 10, dt, true);
            }
            else
            {
                GoTo(new Cell(goal.cx, goal.cy), 100 + goal.cx * C.GH + goal.cy, spd, dt);
            }
        }

        void DoEat(double spd, double dt)
        {
            Bait b = fruitT;
            if (U.dist(this, b) < 18)
            {
                U.G.baits.Remove(b);
                hp = Math.Min(MAX_HP, hp + 3);
                U.G.ShamblerAte(this);
                thinkT = 0;
                return;
            }
            if (Perception.WalkableLine(this, b))
            {
                nav.timer = nav.timer - dt;
                Steering.arrive(this, b, spd, 10, dt, true);
            }
            else
            {
                GoTo(U.w2c(b.x, b.y), 3000 + U.G.baits.IndexOf(b), spd, dt);
            }
        }

        void DoPack(double spd, double dt)
        {
            Mover dog = dogT;
            double d = U.dist(this, dog);
            if (d < 24)
            {
                vx = vx * 0.6;
                vy = vy * 0.6;
                // damage is done in OrchardGame (pack rule)
                biting = true;
            }
            else if (Perception.WalkableLine(this, dog))
            {
                nav.timer = nav.timer - dt;
                Steering.steer(this, Steering.slot(this, dog, 16), spd * 1.05, dt, true);
            }
            else
            {
                int k = 1;
                if (dog is Pip) k = 0;
                GoTo(U.w2c(dog.x, dog.y), 4000 + k, spd, dt);
            }
        }

        void DoHitFarmer(double spd, double dt)
        {
            Farmer f = U.G.farmer;
            double d = U.dist(this, f);
            if (d < 26 && hitCd <= 0)
            {
                hitCd = 4.0;
                lastHit = -99;
                U.G.LogOnce("knock", "Shambler swiped at the farmer (grudge)");
                thinkT = 0;
                return;
            }
            if (Perception.WalkableLine(this, f))
            {
                nav.timer = nav.timer - dt;
                Steering.steer(this, f, spd * 1.15, dt, true);
            }
            else
            {
                GoTo(U.w2c(f.x, f.y), 5000, spd, dt);
            }
        }

        //  walk round the centre. when 2 or more shamblers circled for 1.4 s a tornado starts...
        void DoWhirl(double spd, double dt)
        {
            OrchardGame G = U.G;
            Pos c = whirlC;
            whirlWait = whirlWait + dt;
            double d = U.dist(this, c);

            if (d > WHIRL_R + 22)
            {
                //  go to the centre first...
                if (Perception.WalkableLine(this, c))
                {
                    nav.timer = nav.timer - dt;
                    Steering.steer(this, c, spd, dt, true);
                }
                else
                {
                    GoTo(U.w2c(c.x, c.y), 7000, spd, dt);
                }
            }
            else
            {
                //  all turn the same way...
                whirlA = whirlA + 4.2 * dt;
                Pos p = new Pos(c.x + Math.Cos(whirlA) * WHIRL_R, c.y + Math.Sin(whirlA) * WHIRL_R * 0.7);
                Steering.steer(this, p, spd * 1.1, dt, true);
            }

            //  count shamblers circling this centre...
            int on = 0;
            for (int i = 0; i < G.enemies.Count; i++)
            {
                if (G.enemies[i].type != "shambler") continue;
                Shambler s = (Shambler)G.enemies[i];
                if (s.dead) continue;
                if (s.act != ShamAct.Whirl) continue;
                if (s.whirlC == null) continue;
                if (U.dist(s.whirlC, c) < 24 && U.dist(s, c) < WHIRL_R + 22)
                {
                    on++;
                }
            }
            if (on >= 2 && d < WHIRL_R + 22)
            {
                whirlT = whirlT + dt;
            }

            if (whirlT >= C.WHIRL_SPIN)
            {
                G.MakeTornado(c);
                for (int i = 0; i < G.enemies.Count; i++)
                {
                    if (G.enemies[i].type != "shambler") continue;
                    Shambler s = (Shambler)G.enemies[i];
                    if (s.act != ShamAct.Whirl) continue;
                    if (s.whirlC == null) continue;
                    if (U.dist(s.whirlC, c) > 24) continue;
                    s.EndWhirl(C.WHIRL_COOLDOWN);
                }
                return;
            }

            //  nobody came: give up for a while...
            if (whirlWait > 6)
            {
                EndWhirl(6);
            }
        }

        public void EndWhirl(double cd)
        {
            whirlCd = cd;
            whirlT = 0;
            whirlWait = 0;
            whirlC = null;
            act = ShamAct.RaidTree;
            thinkT = 0;
            nav.Reset();
        }

        void DoFlee(double spd, double dt)
        {
            //  heal slowly when nothing is near...
            if (DangerAt(this) < 0.05)
            {
                hp = Math.Min(MAX_HP, hp + 0.35 * dt);
            }
            if (safeSpot == null || U.dist(this, safeSpot) < 14 || (nav.timer <= 0 && DangerAt(safeSpot) > 0.2))
            {
                safeSpot = SafestSpot();
            }
            if (safeSpot == null) return;
            Cell sc = U.w2c(safeSpot.x, safeSpot.y);
            GoTo(sc, 6000 + sc.cx * C.GH + sc.cy, spd * 1.1, dt);
        }

        //  best cell within 7 cells: far from farmer and thistle, not far to walk...
        Pos SafestSpot()
        {
            OrchardGame G = U.G;
            Cell me = U.w2c(x, y);
            Pos best = null;
            double bestV = -1e9;
            for (int dx = -7; dx <= 7; dx++)
            {
                for (int dy = -7; dy <= 7; dy++)
                {
                    int cx = me.cx + dx;
                    int cy = me.cy + dy;
                    if (!U.inB(cx, cy)) continue;
                    if (U.groundBlocked(cx, cy)) continue;
                    if (G.reach != null && !G.reach[cx, cy]) continue;
                    Pos p = U.cc(cx, cy);
                    double away = U.dist(p, G.farmer);
                    if (G.thistle.state != ThistleState.Flee)
                    {
                        double dt2 = U.dist(p, G.thistle);
                        if (dt2 < away) away = dt2;
                    }
                    double walkCost = U.hypot(dx, dy) * C.CELL * 0.35;
                    double mud = 0;
                    if (G.grid[cx, cy] == C.MUD) mud = 40;
                    double v = Math.Min(away, C.CELL * 8) - walkCost - mud;
                    if (v > bestV)
                    {
                        bestV = v;
                        best = p;
                    }
                }
            }
            return best;
        }

        //  walk to a cell with a* ... // george (uses manus a* pathfinding)
        void GoTo(Cell target, int key, double spd, double dt)
        {
            List<Cell> goals = new List<Cell>();
            goals.Add(target);
            List<Cell> path = nav.Plan(this, goals, key, 0.6);
            if (path != null && nav.route.Count > 1 && !loggedReroute)
            {
                loggedReroute = true;
                U.G.LogEvent("Shambler routed around terrain");
            }
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
}
