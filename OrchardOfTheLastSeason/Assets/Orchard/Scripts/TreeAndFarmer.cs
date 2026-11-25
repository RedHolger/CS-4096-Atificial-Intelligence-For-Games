using System;
using System.Collections.Generic;

namespace Orchard //george manu zian
{
    public class TreeEntity : Pos
    {
        public int cx;
        public int cy;
        public double hp;
        public double maxhp;
        public bool alive;
        public int fruit;
        public double flash;
        public double healGlow;
        public double firstHit;

        public TreeEntity(int cx, int cy)
        {
            Pos c = U.cc(cx, cy);
            x = c.x;
            y = c.y;
            this.cx = cx;
            this.cy = cy;
            hp = C.TREE_HP;
            maxhp = C.TREE_HP;
            alive = true;
            fruit = 4;
            flash = 0;
            healGlow = 0;
            firstHit = 1e18;
        }

        //  enemy chews the tree...
        public void gnaw(double ps, double dt)
        {
            if (!alive) return;
            if (firstHit == 1e18)
            {
                firstHit = U.G.nightTime;
            }
            hp = hp - ps * dt;
            flash = 0.1;
            if (hp <= 0)
            {
                alive = false;
                U.G.LogEvent("A tree has fallen");
            }
        }

        //  pip repairs the tree...
        public void heal(double ps, double dt)
        {
            if (!alive) return;
            hp = Math.Min(maxhp, hp + ps * dt);
            healGlow = 0.15;
        }

        //  bring a fallen tree back to life...
        public void revive()
        {
            alive = true;
            hp = maxhp;
            fruit = 4;
            firstHit = 1e18;
            healGlow = 0.15;
        }
    }

    public class Farmer : Mover //george manu zian
    {
        public double faceX = 1;
        public double faceY = 0;
        public int dir = 1;
        public double swing = 0;
        public double walk = 0;
        public bool moving = false;
        public double hp = C.FARMER_HP;
        public double invuln = 0;
        public double sinceHit = 99;

        public Farmer(double x, double y)
        {
            this.x = x;
            this.y = y;
            r = 15;
        }

        //  stalker bit the farmer. returns true if hp was lost...
        public bool Hurt(double dmg)
        {
            if (invuln > 0) return false;
            if (hp <= 0) return false;
            hp = Math.Max(0, hp - dmg);
            invuln = C.FARMER_INVULN;
            sinceHit = 0;
            return true;
        }

        public void update(double dt)
        {
            if (invuln > 0)
            {
                invuln = invuln - dt;
            }
            sinceHit = sinceHit + dt;

            //  heal slowly when nobody bit him for a while...
            if (sinceHit > C.FARMER_REGEN_DELAY && hp < C.FARMER_HP)
            {
                hp = Math.Min(C.FARMER_HP, hp + C.FARMER_REGEN * dt);
            }

            HashSet<string> k = U.G.keys;
            double mx = 0;
            double my = 0;
            if (k.Contains("w") || k.Contains("arrowup")) my = my - 1;
            if (k.Contains("s") || k.Contains("arrowdown")) my = my + 1;
            if (k.Contains("a") || k.Contains("arrowleft")) mx = mx - 1;
            if (k.Contains("d") || k.Contains("arrowright")) mx = mx + 1;

            if (mx != 0 || my != 0)
            {
                moving = true;
                double l = U.hypot(mx, my);
                mx = mx / l;
                my = my / l;
                faceX = mx;
                faceY = my;
                if (mx < 0) dir = -1;
                if (mx > 0) dir = 1;
                Steering.moveFree(this, mx * C.FARMER_SPEED * dt, my * C.FARMER_SPEED * dt);
                walk = walk + dt * 10;
            }
            else
            {
                moving = false;
            }

            if (swing > 0)
            {
                swing = swing - dt;
            }
        }
    }
}
