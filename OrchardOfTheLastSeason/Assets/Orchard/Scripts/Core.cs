using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orchard
{
    //  all game numbers in one place... 
    public static class C //george manu zian
    {
        public const int GW = 24;
        public const int GH = 16;
        public const int CELL = 44;
        public const int WORLD_W = GW * CELL;
        public const int WORLD_H = GH * CELL;
        public const int VW = WORLD_W;
        public const int VH = WORLD_H;

        //  terrain codes...
        public const int GRASS = 0;
        public const int BUSH = 1;
        public const int HILL = 2;
        public const int MUD = 3;
        public const int LOG = 4;

        public const int YELLOW_TARGET = 5;
        public const int TOTAL_NIGHTS = 13;

        public const double TREE_HP = 70;
        public const double FARMER_SPEED = 165;
        public const double DOG_SPEED = 175;
        public const double SCYTHE_RANGE = 72;
        public const double SCYTHE_DMG = 3.2;
        public const double CATCH_RANGE = 78;
        public const double NIGHT_LEN = 60;
        public const double TRANS_LEN = 3.4;

        //  whirl and tornado...
        public const double WHIRL_NIGHT = 2;
        public const double WHIRL_SPIN = 1.4;
        public const double TORNADO_LIFE = 5.0;
        public const double CONFUSE_T = 5.0;
        public const double NO_EAT_T = 10.0;
        public const double WHIRL_COOLDOWN = 16.0;

        //  farmer health...
        public const double FARMER_HP = 10;
        public const double STALKER_DMG = 1;
        public const double STALKER_HIT_CD = 1.6;
        public const double FARMER_INVULN = 1.2;
        public const double FARMER_REGEN_DELAY = 5;
        public const double FARMER_REGEN = 0.5;

        public const double KILL_COOLDOWN = 10.0;
    }

    public class Pos //george manu zian
    {
        public double x;
        public double y;

        public Pos()
        {
        }

        public Pos(double x, double y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Cell
    {
        public int cx;
        public int cy;

        public Cell(int cx, int cy)
        {
            this.cx = cx;
            this.cy = cy;
        }
    }

    //  anything that moves... 
    public class Mover : Pos
    {
        public double r;
        public double vx;
        public double vy;
    }

    public class Bait : Pos //george manu zian
    {
        public Bait(double x, double y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public class FlyingFruit : Pos
    {
        public double vy;
        public double life;
    }

    public class Branch : Pos
    {
        public double a;
    }

    public class Tuft : Pos
    {
    }

    //  log lies on cell cx,cy and the cell to the right (horiz) or below...
    public class FallenLog
    {
        public int cx;
        public int cy;
        public bool horiz;
        public double seed;
    }

    public class Tornado : Pos
    {
        public double life = C.TORNADO_LIFE;
        public double r = C.CELL * 3;
        public double spin = 0;
        public List<Bird> hit = new List<Bird>();

        public Tornado(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        public bool HasHit(Bird b)
        {
            for (int i = 0; i < hit.Count; i++)
            {
                if (hit[i] == b)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public class SpawnEvent
    {
        public string type;
        public int n;
        public double t;
    }

    
    public static class U //george manu zian
    {
        static System.Random rng = new System.Random();

        public static double R()
        {
            return rng.NextDouble();
        }

        public static OrchardGame G
        {
            get { return OrchardGame.I; }
        }

        //  centre of a cell...
        public static Pos cc(int cx, int cy)
        {
            return new Pos(cx * C.CELL + C.CELL / 2.0, cy * C.CELL + C.CELL / 2.0);
        }

        //  pixel position to cell...
        public static Cell w2c(double x, double y)
        {
            int cx = (int)Math.Floor(x / C.CELL);
            int cy = (int)Math.Floor(y / C.CELL);
            return new Cell(cx, cy);
        }

        public static bool inB(int cx, int cy)
        {
            if (cx < 0 || cy < 0) return false;
            if (cx >= C.GW || cy >= C.GH) return false;
            return true;
        }

        public static int terr(double x, double y)
        {
            Cell c = w2c(x, y);
            if (inB(c.cx, c.cy))
            {
                return G.grid[c.cx, c.cy];
            }
            return C.HILL;
        }

        //  hills, bushes and logs stop ground walkers...
        public static bool groundBlocked(int cx, int cy)
        {
            if (!inB(cx, cy)) return true;
            int v = G.grid[cx, cy];
            if (v == C.BUSH || v == C.HILL || v == C.LOG) return true;
            return false;
        }

        public static double hypot(double a, double b)
        {
            return Math.Sqrt(a * a + b * b);
        }

        public static double dist(Pos a, Pos b)
        {
            return hypot(a.x - b.x, a.y - b.y);
        }

        public static double clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static string season(int n)
        {
            if (n <= 3) return "Early Autumn";
            if (n <= 6) return "Mid Autumn";
            if (n <= 9) return "Late Autumn";
            if (n <= 11) return "First Frost";
            return "Deep Winter";
        }

        //  random spot in the sky, away from the edge...
        public static Pos randSky()
        {
            double x = C.CELL + R() * (C.WORLD_W - C.CELL * 2);
            double y = C.CELL + R() * (C.WORLD_H - C.CELL * 2);
            return new Pos(x, y);
        }

        public static double round(double v)
        {
            return Math.Floor(v + 0.5);
        }

        public static string two(int n)
        {
            return n.ToString("00");
        }

        public static Color hex(int rgb)
        {
            return hex(rgb, 1);
        }

        public static Color hex(int rgb, double a)
        {
            float r = ((rgb >> 16) & 255) / 255f;
            float g = ((rgb >> 8) & 255) / 255f;
            float b = (rgb & 255) / 255f;
            return new Color(r, g, b, (float)a);
        }

        public static Color rgba(int r, int g, int b, double a)
        {
            return new Color(r / 255f, g / 255f, b / 255f, (float)a);
        }

        // insertion sort by spawn time (keeps order of equal times)
        public static void SortEvents(List<SpawnEvent> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                SpawnEvent item = list[i];
                int j = i - 1;
                while (j >= 0 && list[j].t.CompareTo(item.t) > 0)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = item;
            }
        }
    }
}
