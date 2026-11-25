using UnityEngine;

namespace Orchard
{
    public class TreeEntity : Pos
    {
        public int cx, cy;
        public double hp, maxhp;
        public bool alive;
        public int fruit;
        public double flash, healGlow, firstHit;
        public double diedAt, revived;

        public TreeEntity(int cx, int cy)
        {
            Pos c = U.cc(cx, cy);
            x = c.x; y = c.y;
            this.cx = cx; this.cy = cy;
            hp = C.TREE_HP; maxhp = C.TREE_HP;
            alive = true; fruit = 4;
            flash = 0; healGlow = 0;
            firstHit = double.PositiveInfinity;
        }

        public void gnaw(double ps, double dt)
        {
            if (!alive) return;
            if (double.IsPositiveInfinity(firstHit)) firstHit = U.G.nightTime;
            hp -= ps * dt;
            flash = 0.1;
            if (hp <= 0)
            {
                alive = false;
                diedAt = U.G.night * 1000 + U.G.nightTime;
                U.G.LogEvent("A tree has fallen");
            }
        }

        public void heal(double ps, double dt)
        {
            if (!alive) return;
            hp = System.Math.Min(maxhp, hp + ps * dt);
            healGlow = 0.15;
        }

        public bool eatFruit()
        {
            if (fruit > 0) { fruit--; return true; }
            return false;
        }
    }

    public class Farmer : Mover
    {
        public double faceX = 1, faceY = 0;
        public int dir = 1;
        public double swing = 0, walk = 0;
        public bool moving = false;
        public double stun = 0, kbx = 0, kby = 0;   // knocked back by a shambler

        public Farmer(double x, double y)
        {
            this.x = x; this.y = y; r = 15;
        }

        // pushed away from the attacker and dazed for a moment (cannot move or swing)
        public void KnockBack(Pos from, double strength, double dazed)
        {
            double dx = x - from.x, dy = y - from.y;
            double l = U.hypot(dx, dy);
            if (l < 0.01) { dx = 1; dy = 0; l = 1; }
            kbx = dx / l * strength; kby = dy / l * strength;
            stun = dazed;
        }

        public void update(double dt)
        {
            if (stun > 0)
            {
                stun -= dt;
                Steering.moveFree(this, kbx * dt, kby * dt);
                kbx *= System.Math.Max(0, 1 - 6 * dt); kby *= System.Math.Max(0, 1 - 6 * dt);
                moving = false;
                if (swing > 0) swing -= dt;
                return;
            }
            System.Collections.Generic.HashSet<string> k = U.G.keys;
            double mx = 0, my = 0;
            if (k.Contains("w") || k.Contains("arrowup")) my -= 1;
            if (k.Contains("s") || k.Contains("arrowdown")) my += 1;
            if (k.Contains("a") || k.Contains("arrowleft")) mx -= 1;
            if (k.Contains("d") || k.Contains("arrowright")) mx += 1;
            moving = (mx != 0 || my != 0);
            if (mx != 0 || my != 0)
            {
                double l = U.hypot(mx, my);
                mx /= l; my /= l;
                faceX = mx; faceY = my;
                if (mx != 0) dir = mx < 0 ? -1 : 1;
                Steering.moveFree(this, mx * C.FARMER_SPEED * dt, my * C.FARMER_SPEED * dt);
                walk += dt * 10;
            }
            if (swing > 0) swing -= dt;
        }
    }
}
