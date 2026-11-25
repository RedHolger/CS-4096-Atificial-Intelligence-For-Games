using System.Collections.Generic;

namespace Orchard
{
    // ======================================================================
    //  ai technique: utility ai (desirability-based decision making)
    //
    //  every 0.25 s each shambler asks "how much do i WANT each thing right now?"
    //  each option gets a desirability score from 0 to 1, built by multiplying
    //  a few considerations (each also 0..1). the best score wins.
    //
    //   option        what it does                         main considerations
    //   ----------    ---------------------------------    --------------------------------------
    //   RaidTree      walk to a tree and gnaw it            distance, how weak the tree is,
    //                                                        is it guarded (farmer/thistle near), my health
    //   EatFruit      eat a fallen fruit (heals +3 hp)      distance, how hurt i am
    //   PackDog       gang up on pip or thistle             can i see the dog, distance,
    //                 (a dog only gets hurt when 2+          are other shamblers already on it or close
    //                 shamblers bite it at once)            enough to join, is the dog in our way
    //                                                        (pip repairing my tree, thistle biting us),
    //                                                        dog weakness, my health
    //   HitFarmer     hit back at the farmer                he hit me recently, he is close, my health
    //   Flee          run to the safest reachable spot,     how hurt i am, how close danger is
    //                 heal slowly, eat fruit on the way
    //
    //  extra rules that make it behave well:
    //   - commitment: the option it is already doing gets +0.12, so it does not flip every frame
    //   - compensation: multiplying many numbers < 1 punishes options with more considerations,
    //     so each score is corrected for how many considerations it has (dave mark's method)
    //   - perception: it only knows about dogs, the farmer and other shamblers it can SEE
    //     (6 cells, no hill in the way). trees and fallen fruit it can smell from further away
    //   - teamwork: shamblers read each other's current choice and target, so one that sees
    //     another already biting a dog decides to come and help
    // ======================================================================

    public enum ShamAct { RaidTree, EatFruit, PackDog, HitFarmer, Flee }

    public static class Utility
    {
        // 0..1 closeness: 1 when right there, 0 at maxDist or beyond
        public static double Near(double d, double maxDist)
        {
            double v = 1 - d / maxDist;
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        public static double Clamp01(double v)
        {
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        // multiply considerations, then correct for how many there are
        public static double Score(double weight, params double[] cons)
        {
            double s = 1;
            foreach (double c in cons) s *= Clamp01(c);
            int n = cons.Length;
            if (n > 1 && s > 0)
            {
                double mod = 1.0 - 1.0 / n;
                double makeUp = (1 - s) * mod;
                s = s + makeUp * s;
            }
            return weight * s;
        }
    }

    public class Shambler : Enemy
    {
        public const double MAX_HP = 6;
        public double speed = 86;

        // the decision and what it is aimed at (read by other shamblers, the renderer and the ui)
        public ShamAct act = ShamAct.RaidTree;
        public TreeEntity treeT;
        public Bait fruitT;
        public Mover dogT;
        public double[] scores = new double[5];

        double thinkT;
        double hitCd;
        Pos safeSpot;
        Navigator nav = new Navigator();
        bool la;

        public Shambler(double x, double y) : base(x, y, "shambler")
        {
            hp = MAX_HP;
            thinkT = U.R() * 0.25;          // spread the thinking of different shamblers over time
        }

        public double HpFrac { get { return hp / MAX_HP; } }

        // ---------------- perception ----------------
        bool CanSee(Pos p)
        {
            return U.dist(this, p) < C.CELL * 7 && Perception.LineOfSight(this, p);
        }

        double DangerAt(Pos p)
        {
            // how threatening is it to stand at p: farmer and thistle nearby
            OrchardGame G = U.G;
            double d = Utility.Near(U.dist(p, G.farmer), C.CELL * 3.5);
            if (G.thistle.state != ThistleState.Flee) d = System.Math.Max(d, 0.8 * Utility.Near(U.dist(p, G.thistle), C.CELL * 3));
            return d;
        }

        // how many OTHER shamblers are already biting / heading for this dog
        public static int Committed(Mover dog, Shambler except)
        {
            int n = 0;
            foreach (Enemy e in U.G.enemies)
            {
                Shambler s = e as Shambler;
                if (s == null || s == except || s.dead) continue;
                if (s.act == ShamAct.PackDog && s.dogT == dog) n++;
            }
            return n;
        }

        // other shamblers i can see that are close enough and healthy enough to join me
        int Helpers(Mover dog)
        {
            int n = 0;
            foreach (Enemy e in U.G.enemies)
            {
                Shambler s = e as Shambler;
                if (s == null || s == this || s.dead || s.act == ShamAct.Flee) continue;
                if (U.dist(s, dog) < C.CELL * 7 && CanSee(s)) n++;
            }
            return n;
        }

        // ---------------- desirability of each option ----------------
        void Think()
        {
            OrchardGame G = U.G;
            double hpF = HpFrac;
            double healthy = Utility.Clamp01(hpF * 1.3);           // ~1 when healthy, drops when hurt
            double injury = Utility.Clamp01((0.7 - hpF) / 0.55);    // 0 above 70% hp, 1 near death

            // RaidTree: best tree = close, weak and unguarded
            double bestTree = 0; TreeEntity bt = null;
            foreach (TreeEntity t in G.trees)
            {
                if (!t.alive) continue;
                double near = 0.25 + 0.75 * Utility.Near(U.dist(this, t), C.CELL * 16);
                double weak = 0.6 + 0.4 * (1 - t.hp / t.maxhp);
                double unguarded = 1 - 0.7 * DangerAt(t);
                double s = Utility.Score(0.62, near, weak, unguarded, healthy);
                if (s > bestTree) { bestTree = s; bt = t; }
            }

            // EatFruit: fallen fruit within smelling range; worth more when hurt
            double bestFruit = 0; Bait bf = null;
            foreach (Bait b in G.baits)
            {
                double d = U.dist(this, b);
                if (d > C.CELL * 8) continue;
                double want = 0.45 + 0.55 * injury;
                double safe = 1 - 0.6 * DangerAt(b);
                double s = Utility.Score(0.85, want, 0.3 + 0.7 * Utility.Near(d, C.CELL * 8), safe);
                if (s > bestFruit) { bestFruit = s; bf = b; }
            }

            // PackDog: only worth it with company
            double bestDog = 0; Mover bd = null;
            Mover[] dogs = { G.pip, G.thistle };
            foreach (Mover dog in dogs)
            {
                if (dog == null || DogHp(dog) <= 0 || !CanSee(dog)) continue;
                int on = Committed(dog, this);
                double pack;
                if (on >= 2) pack = 0.7;                 // already enough: helps, less needed
                else if (on == 1) pack = 1.0;            // one is on it, i make it two: very wanted
                else pack = Helpers(dog) > 0 ? 0.6 : 0.12;   // start a pack only if someone can join
                // grudge: this dog is getting in our way (pip repairing the tree i am chewing,
                // or thistle biting me or a shambler near me) -> much more worth ganging up on
                if (Interferes(dog)) pack = System.Math.Min(1, pack + 0.35);
                double weak = 0.6 + 0.4 * (1 - DogHp(dog) / 5.0);
                double s = Utility.Score(0.82, 0.35 + 0.65 * Utility.Near(U.dist(this, dog), C.CELL * 7), pack, weak, healthy);
                if (s > bestDog) { bestDog = s; bd = dog; }
            }

            // HitFarmer: retaliation when he hurt me recently and is close
            double recent = Utility.Clamp01(1 - (G.nightTime - lastHitByFarmer) / 2.0);
            double hitF = hitCd > 0 ? 0 : Utility.Score(0.85, recent, Utility.Near(U.dist(this, G.farmer), C.CELL * 2.5), healthy);

            // Flee: hurt and in danger
            double threat = DangerAt(this);
            double flee = Utility.Score(1.0, injury, 0.45 + 0.55 * threat);

            scores[(int)ShamAct.RaidTree] = bestTree;
            scores[(int)ShamAct.EatFruit] = bestFruit;
            scores[(int)ShamAct.PackDog] = bestDog;
            scores[(int)ShamAct.HitFarmer] = hitF;
            scores[(int)ShamAct.Flee] = flee;

            // commitment bonus for what it is already doing
            double[] s2 = (double[])scores.Clone();
            s2[(int)act] += 0.12;
            int best = 0;
            for (int i = 1; i < 5; i++) if (s2[i] > s2[best]) best = i;
            ShamAct next = (ShamAct)best;

            if (next != act || (next == ShamAct.PackDog && bd != dogT))
            {
                if (next == ShamAct.PackDog && Committed(bd, this) >= 1) G.LogOnce("pack", "Shamblers pack up on " + (bd == G.pip ? "Pip" : "Thistle") + "!");
                else if (next == ShamAct.Flee) G.LogOnce("flee", "Shambler hurt, fleeing to heal");
                else if (next == ShamAct.HitFarmer) G.LogOnce("hitback", "Shambler turns on the farmer");
                else if (next == ShamAct.EatFruit && injury > 0.3) G.LogOnce("eatheal", "Shambler goes for fruit to heal");
                nav.Reset();
                safeSpot = null;
            }
            act = next;
            treeT = bt; fruitT = bf; dogT = bd;
        }

        bool Interferes(Mover dog)
        {
            OrchardGame G = U.G;
            if (dog == G.pip && G.pip.target != null && G.pip.target == treeT && U.dist(this, treeT) < 60) return true;
            if (dog == G.thistle && G.thistle.state == ThistleState.Feed && G.thistle.target != null
                && (G.thistle.target == this || G.thistle.target is Shambler && U.dist(this, G.thistle.target) < C.CELL * 3)) return true;
            return false;
        }

        public static double DogHp(Mover dog)
        {
            if (dog is Pip) return ((Pip)dog).hp;
            if (dog is Thistle) return ((Thistle)dog).hp;
            return 0;
        }

        // ---------------- acting on the decision ----------------
        public override void update(double dt)
        {
            OrchardGame G = U.G;
            double px = x;
            bool onMud = U.terr(x, y) == C.MUD;
            double spd = speed * (onMud ? 0.45 : 1);
            if (hitCd > 0) hitCd -= dt;

            thinkT -= dt;
            if (thinkT <= 0) { thinkT = 0.25; Think(); }

            // targets can disappear between thoughts
            if (act == ShamAct.EatFruit && (fruitT == null || !G.baits.Contains(fruitT))) { thinkT = 0; act = ShamAct.RaidTree; }
            if (act == ShamAct.PackDog && (dogT == null || DogHp(dogT) <= 0)) { thinkT = 0; act = ShamAct.RaidTree; }
            if (act == ShamAct.RaidTree && (treeT == null || !treeT.alive)) treeT = nearTree();

            if (act == ShamAct.RaidTree) DoRaid(spd, dt);
            else if (act == ShamAct.EatFruit) DoEat(spd, dt);
            else if (act == ShamAct.PackDog) DoPack(spd, dt);
            else if (act == ShamAct.HitFarmer) DoHitFarmer(spd, dt);
            else DoFlee(spd, dt);

            Steering.separate(this, dt);
            if (System.Math.Abs(vx) + System.Math.Abs(vy) > 8) walk += dt * 8;
            FaceFrom(px);
        }

        void DoRaid(double spd, double dt)
        {
            TreeEntity goal = treeT;
            if (goal == null) return;
            double gd = U.dist(this, goal);
            if (gd < 26) { vx *= 0.5; vy *= 0.5; goal.gnaw(10, dt); }
            else if (gd < 60 && Perception.WalkableLine(this, goal))
                Steering.arrive(this, Steering.slot(this, goal, 20), spd, 10, dt, Steering.moveGround);   // take a spot round the tree
            else GoTo(new Cell(goal.cx, goal.cy), 100 + goal.cx * C.GH + goal.cy, spd, dt);
        }

        void DoEat(double spd, double dt)
        {
            Bait b = fruitT;
            if (U.dist(this, b) < 18)
            {
                U.G.baits.Remove(b);
                hp = System.Math.Min(MAX_HP, hp + 3);
                U.G.ShamblerAte(this);
                thinkT = 0;
                return;
            }
            if (Perception.WalkableLine(this, b)) { nav.timer -= dt; Steering.arrive(this, b, spd, 10, dt, Steering.moveGround); }
            else GoTo(U.w2c(b.x, b.y), 3000 + U.G.baits.IndexOf(b), spd, dt);
        }

        void DoPack(double spd, double dt)
        {
            Mover dog = dogT;
            double d = U.dist(this, dog);
            if (d < 24)
            {
                vx *= 0.6; vy *= 0.6;
                biting = true;      // damage is applied by the game once per frame per dog (pack rule)
            }
            else if (Perception.WalkableLine(this, dog)) { nav.timer -= dt; Steering.steer(this, Steering.slot(this, dog, 16), spd * 1.05, dt, Steering.moveGround); }
            else GoTo(U.w2c(dog.x, dog.y), 4000 + (dog is Pip ? 0 : 1), spd, dt);
        }

        public bool biting;

        void DoHitFarmer(double spd, double dt)
        {
            Farmer f = U.G.farmer;
            double d = U.dist(this, f);
            if (d < 26 && hitCd <= 0)
            {
                f.KnockBack(this, 230, 0.55);
                hitCd = 4.0;
                lastHitByFarmer = -99;     // grudge settled
                U.G.LogOnce("knock", "Shambler knocked the farmer back!");
                thinkT = 0;
                return;
            }
            if (Perception.WalkableLine(this, f)) { nav.timer -= dt; Steering.steer(this, f, spd * 1.15, dt, Steering.moveGround); }
            else GoTo(U.w2c(f.x, f.y), 5000, spd, dt);
        }

        void DoFlee(double spd, double dt)
        {
            OrchardGame G = U.G;
            // heal slowly once nothing is close
            if (DangerAt(this) < 0.05) hp = System.Math.Min(MAX_HP, hp + 0.35 * dt);
            if (safeSpot == null || U.dist(this, safeSpot) < 14 || nav.timer <= 0 && DangerAt(safeSpot) > 0.2) safeSpot = SafestSpot();
            if (safeSpot == null) return;
            GoTo(U.w2c(safeSpot.x, safeSpot.y), 6000 + U.w2c(safeSpot.x, safeSpot.y).cx * C.GH + U.w2c(safeSpot.x, safeSpot.y).cy, spd * 1.1, dt);
        }

        // reachable cell within ~7 cells that is far from the farmer and thistle, not too far to walk
        Pos SafestSpot()
        {
            OrchardGame G = U.G;
            Cell me = U.w2c(x, y);
            Pos best = null;
            double bestV = -1e9;
            for (int dx = -7; dx <= 7; dx++)
                for (int dy = -7; dy <= 7; dy++)
                {
                    int cx = me.cx + dx, cy = me.cy + dy;
                    if (!U.inB(cx, cy) || U.groundBlocked(cx, cy)) continue;
                    if (G.reach != null && !G.reach[cx, cy]) continue;
                    Pos p = U.cc(cx, cy);
                    double away = System.Math.Min(U.dist(p, G.farmer), G.thistle.state == ThistleState.Flee ? 1e9 : U.dist(p, G.thistle));
                    double walkCost = U.hypot(dx, dy) * C.CELL * 0.35;
                    double mud = G.grid[cx, cy] == C.MUD ? 40 : 0;
                    double v = System.Math.Min(away, C.CELL * 8) - walkCost - mud;
                    if (v > bestV) { bestV = v; best = p; }
                }
            return best;
        }

        void GoTo(Cell target, int key, double spd, double dt)
        {
            List<Cell> goals = new List<Cell>();
            goals.Add(target);
            List<Cell> path = nav.Plan(this, goals, key, 0.6);
            if (path != null && nav.route.Count > 1 && !la)
            {
                la = true;
                U.G.LogEvent("Shambler routed around terrain");
            }
            if (nav.HasRoute) nav.Walk(this, spd, dt);
            else { nav.timer -= dt; Steering.steer(this, U.cc(target.cx, target.cy), spd, dt, Steering.moveGround); }
        }
    }
}
