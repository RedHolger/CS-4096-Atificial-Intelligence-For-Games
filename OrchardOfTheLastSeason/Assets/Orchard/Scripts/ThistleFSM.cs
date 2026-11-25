using System;
using System.Collections.Generic;

namespace Orchard
{
    public enum ThistleState //manu
    {
        Wander,
        Feed,
        Flee
    }

    // thistle brain... finite state machine 
    //  three states, she can only be in one at a time, and simple rules flip her between them...
    //   wander -> feed: a ground enemy comes within 6 cells (7 if it chews a tree)
    //    feed -> wander: target dead or further than 7.5 cells...
    //    any -> Flee: hp is 2 or less, she runs to the farmer...
    //    flee -> wander: hp back to 4...
    public class Thistle : Mover
    {
        public ThistleState state = ThistleState.Wander;
        public double hp = 5;
        public Enemy target;
        public int dir = 1;
        public Pos patrol;
        public double hurtFlash = 0;

        const double ENTER = C.CELL * 6;
        const double LEAVE = C.CELL * 7.5;

        public Thistle(double x, double y)
        {
            this.x = x;
            this.y = y;
            r = 12;
            patrol = U.randSky();
        }

        public void Hurt(double d)
        {
            hp = Math.Max(0, hp - d);
            hurtFlash = 0.15;
        }

        void SetState(ThistleState s)
        {
            if (state != s)
            {
                state = s;
                U.G.LogEvent("Thistle switched to " + s);
            }
        }

        public void update(double dt) //manu
        {
            double px = x;
            OrchardGame G = U.G;
            if (hurtFlash > 0) hurtFlash = hurtFlash - dt;

            //  change state...
            if (state == ThistleState.Flee)
            {
                if (hp >= 4)
                {
                    SetState(ThistleState.Wander);
                }
            }
            else if (hp <= 2)
            {
                target = null;
                SetState(ThistleState.Flee);
            }
            else if (state == ThistleState.Feed)
            {
                Enemy e = near();
                //  keep the target unless another one is much closer...
                if (e != null)
                {
                    if (target == null || target.dead || U.dist(this, e) < 0.7 * U.dist(this, target))
                    {
                        target = e;
                    }
                }
                if (target == null || target.dead || U.dist(this, target) > LEAVE)
                {
                    target = null;
                    SetState(ThistleState.Wander);
                }
            }
            else
            {
                Enemy e = near();
                if (e != null)
                {
                    double enter = ENTER;
                    if (Threatens(e)) enter = ENTER + C.CELL;
                    if (U.dist(this, e) < enter)
                    {
                        target = e;
                        SetState(ThistleState.Feed);
                    }
                }
            }

            //  do what the state says...
            if (state == ThistleState.Wander)
            {
                if (patrol == null || U.dist(this, patrol) < C.CELL * 0.6)
                {
                    patrol = PatrolPoint();
                }
                Steering.arrive(this, patrol, C.DOG_SPEED * 0.7, 20, dt, false);
                if (hp < 5)
                {
                    hp = Math.Min(5, hp + dt * 0.4);
                }
            }
            else if (state == ThistleState.Feed)
            {
                if (target != null && !target.dead)
                {
                    Pos aim;
                    if (U.dist(this, target) < 24)
                    {
                        aim = target;
                    }
                    else
                    {
                        aim = Lead(target);
                    }
                    Steering.steer(this, aim, C.DOG_SPEED, dt, false);
                    if (U.dist(this, target) < 24)
                    {
                        target.hit(8.0 * dt);
                        //  a stalker bites back. shamblers only hurt her as a pack...
                        if (target.type == "stalker")
                        {
                            hp = hp - dt * 0.9;
                        }
                    }
                }
            }
            else
            {
                Steering.arrive(this, G.farmer, C.DOG_SPEED, 30, dt, false);
                if (U.dist(this, G.farmer) < C.CELL * 1.2)
                {
                    hp = Math.Min(5, hp + dt * 1.2);
                }
            }

            if (x - px < -0.05) dir = -1;
            else if (x - px > 0.05) dir = 1;
        }

        //  random spot near a living tree...
        Pos PatrolPoint()
        {
            List<TreeEntity> alive = new List<TreeEntity>();
            for (int i = 0; i < U.G.trees.Count; i++)
            {
                if (U.G.trees[i].alive) alive.Add(U.G.trees[i]);
            }
            if (alive.Count == 0) return U.randSky();
            TreeEntity pick = alive[(int)(U.R() * alive.Count) % alive.Count];
            double a = U.R() * 6.283;
            double rr = C.CELL * (0.9 + U.R() * 0.8);
            double px = U.clamp(pick.x + Math.Cos(a) * rr, C.CELL * 0.5, C.WORLD_W - C.CELL * 0.5);
            double py = U.clamp(pick.y + Math.Sin(a) * rr, C.CELL * 0.5, C.WORLD_H - C.CELL * 0.5);
            return new Pos(px, py);
        }

        //  most urgent ground enemy, one that chews a tree counts 2.5 cells closer...
        Enemy near()
        {
            Enemy best = null;
            double bd = 1e9;
            for (int i = 0; i < U.G.enemies.Count; i++)
            {
                Enemy e = U.G.enemies[i];
                if (e.type == "scatterling" || e.type == "bad" || e.dead) continue;
                double d = U.dist(this, e);
                if (Threatens(e)) d = d - C.CELL * 2.5;
                if (d < bd)
                {
                    bd = d;
                    best = e;
                }
            }
            return best;
        }

        //  is the enemy at a living tree...
        static bool Threatens(Enemy e)
        {
            for (int i = 0; i < U.G.trees.Count; i++)
            {
                TreeEntity t = U.G.trees[i];
                if (t.alive && U.dist(e, t) < 30) return true;
            }
            return false;
        }

        //  aim a little ahead of where the target is going... uses the target speed...
        //  it already read (perception), so she cuts it off, not chases its tail...
        Pos Lead(Enemy e)
        {
            double d = U.dist(this, e);
            double lead = Math.Min(0.4, d / C.DOG_SPEED);
            return new Pos(e.x + e.vx * lead, e.y + e.vy * lead);
        }
    }
}
