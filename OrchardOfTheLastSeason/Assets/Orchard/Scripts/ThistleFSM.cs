namespace Orchard
{
    public enum ThistleState { Wander, Feed, Flee }

    // ai technique: finite state machine
    //   Wander -> Feed : a ground enemy comes within 6 cells (7 if it is chewing a tree);
    //                    enemies chewing trees are picked first, and she leads her chase
    //   Feed   -> Wander: the target is dead or gets further than 7.5 cells (hysteresis, no flicker)
    //   any    -> Flee : stalkers bite back, and 2+ shamblers together can maul her; at 2 hp she runs to the farmer
    //   Flee   -> Wander: healed back to 4 hp next to the farmer
    // while wandering she patrols around the living trees and slowly heals.
    public class Thistle : Mover
    {
        public ThistleState state = ThistleState.Wander;
        public double hp = 5;
        public Enemy target;
        public int dir = 1;
        public Pos patrol;
        public double hurtFlash;

        public void Hurt(double d) { hp = System.Math.Max(0, hp - d); hurtFlash = 0.15; }

        const double ENTER = C.CELL * 6, LEAVE = C.CELL * 7.5;

        public Thistle(double x, double y)
        {
            this.x = x; this.y = y; r = 12;
            patrol = U.randSky();
        }

        void set(ThistleState s)
        {
            if (state != s)
            {
                state = s;
                U.G.LogEvent("Thistle switched to " + s);
            }
        }

        public void update(double dt)
        {
            double px = x;
            OrchardGame G = U.G;
            if (hurtFlash > 0) hurtFlash -= dt;

            // ---- transitions ----
            if (state == ThistleState.Flee)
            {
                if (hp >= 4) set(ThistleState.Wander);
            }
            else if (hp <= 2) { target = null; set(ThistleState.Flee); }
            else if (state == ThistleState.Feed)
            {
                Enemy e = near();
                // stick with the current target unless another one is clearly closer
                if (e != null && (target == null || target.dead || U.dist(this, e) < 0.7 * U.dist(this, target))) target = e;
                if (target == null || target.dead || U.dist(this, target) > LEAVE) { target = null; set(ThistleState.Wander); }
            }
            else
            {
                Enemy e = near();
                if (e != null && U.dist(this, e) < ENTER + (Threatens(e) ? C.CELL : 0)) { target = e; set(ThistleState.Feed); }
            }

            // ---- behaviour of the current state ----
            if (state == ThistleState.Wander)
            {
                if (patrol == null || U.dist(this, patrol) < C.CELL * 0.6) patrol = PatrolPoint();
                Steering.arrive(this, patrol, C.DOG_SPEED * 0.7, 20, dt, Steering.moveFree);
                if (hp < 5) hp = System.Math.Min(5, hp + dt * 0.4);
            }
            else if (state == ThistleState.Feed)
            {
                if (target != null && !target.dead)
                {
                    Steering.steer(this, U.dist(this, target) < 24 ? (Pos)target : Lead(target), C.DOG_SPEED, dt, Steering.moveFree);
                    if (U.dist(this, target) < 24)
                    {
                        target.hit(8.0 * dt);
                        // a stalker fights back one-on-one; shamblers can only hurt her as a pack (see OrchardGame.PackBites)
                        if (target.type == "stalker") hp -= dt * 0.9;
                    }
                }
            }
            else
            {
                Steering.arrive(this, G.farmer, C.DOG_SPEED, 30, dt, Steering.moveFree);
                if (U.dist(this, G.farmer) < C.CELL * 1.2) hp = System.Math.Min(5, hp + dt * 1.2);
            }

            if (x - px < -0.05) dir = -1;
            else if (x - px > 0.05) dir = 1;
        }

        // patrol close to a living tree, so she is near where the enemies will come
        Pos PatrolPoint()
        {
            System.Collections.Generic.List<TreeEntity> alive = new System.Collections.Generic.List<TreeEntity>();
            foreach (TreeEntity t in U.G.trees) if (t.alive) alive.Add(t);
            if (alive.Count == 0) return U.randSky();
            TreeEntity pick = alive[(int)(U.R() * alive.Count) % alive.Count];
            double a = U.R() * 6.283, rr = C.CELL * (0.9 + U.R() * 0.8);
            return new Pos(U.clamp(pick.x + System.Math.Cos(a) * rr, C.CELL * 0.5, C.WORLD_W - C.CELL * 0.5),
                           U.clamp(pick.y + System.Math.Sin(a) * rr, C.CELL * 0.5, C.WORLD_H - C.CELL * 0.5));
        }

        // most urgent ground enemy (birds are ignored): the nearest one, but an enemy that is
        // already chewing a tree counts as 2.5 cells closer, so she defends the orchard first
        Enemy near()
        {
            Enemy b = null;
            double bd = 1e9;
            foreach (Enemy e in U.G.enemies)
            {
                if (e.type == "scatterling" || e.type == "bad" || e.dead) continue;
                double d = U.dist(this, e);
                if (Threatens(e)) d -= C.CELL * 2.5;
                if (d < bd) { bd = d; b = e; }
            }
            return b;
        }

        static bool Threatens(Enemy e)
        {
            foreach (TreeEntity t in U.G.trees) if (t.alive && U.dist(e, t) < 30) return true;
            return false;
        }

        // pursuit: aim a little ahead of a moving target
        Pos Lead(Enemy e)
        {
            double d = U.dist(this, e);
            double lead = System.Math.Min(0.4, d / C.DOG_SPEED);
            return new Pos(e.x + e.vx * lead, e.y + e.vy * lead);
        }
    }
}
