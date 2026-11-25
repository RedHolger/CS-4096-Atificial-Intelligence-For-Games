using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orchard //george manu zian
{
    //  draws the board every frame into a render texture...
    //  each draw call gets the next sorting order, so what is drawn later is on top...
    // canvas coordinates: x to the right, y down. unity world: (x, -y)
    public class GameRenderer
    {
        public const int LAYER = 31;
        public RenderTexture rt;

        Camera cam;
        Transform root;
        Material mat;
        Dictionary<string, Sprite> img;
        Sprite circle;
        Sprite pixel;

        //  pools so we do not make new objects every frame...
        List<SpriteRenderer> pool = new List<SpriteRenderer>();
        int used = 0;
        List<LineRenderer> lpool = new List<LineRenderer>();
        int lused = 0;
        int order = 0;
        Vector3[] arcPts = new Vector3[21];

        double vt = 0;

        //  kinds of things that are sorted by y...
        const int K_BUSH = 1;
        const int K_APPLE = 2;
        const int K_TREE = 3;
        const int K_ENEMY = 4;
        const int K_DOG = 5;
        const int K_FARMER = 6;

        class Item
        {
            public double y;
            public int k;
            public double x;
            public double h;
            public TreeEntity tree;
            public Enemy enemy;
            public Mover dog;
            public Farmer farmer;
            public string imgName;
        }
        List<Item> items = new List<Item>();

        // one blob = a hill or mud cell (k 1 = the cell, 2 to 4 = bridges to neighbours)
        class Blob
        {
            public double x;
            public double y;
            public int k;
        }

        public GameRenderer(Dictionary<string, Sprite> img)
        {
            this.img = img;
            rt = MakeRT(C.VW, C.VH);

            GameObject camGo = new GameObject("Orchard Board Camera");
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = C.VH / 2f;
            cam.transform.position = new Vector3(C.VW / 2f, -C.VH / 2f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = U.hex(0x3f6b3a);
            cam.cullingMask = 1 << LAYER;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.allowMSAA = true;
            cam.allowHDR = false;
            cam.depth = -50;
            cam.targetTexture = rt;

            root = new GameObject("Orchard Board").transform;

            Shader sh = null;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            }
            if (sh == null)
            {
                sh = Shader.Find("Sprites/Default");
            }
            mat = new Material(sh);

            circle = Shapes.Circle();
            pixel = Shapes.Pixel();
        }

        static RenderTexture MakeRT(int w, int h)
        {
            RenderTexture t = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            t.antiAliasing = 4;
            t.filterMode = FilterMode.Bilinear;
            t.Create();
            return t;
        }

        //  draw the board with the same number of pixels as it has on screen, so it stays sharp...
        public bool Resize(int w, int h)
        {
            if (rt != null && rt.width == w && rt.height == h) return false;
            RenderTexture old = rt;
            rt = MakeRT(w, h);
            cam.targetTexture = rt;
            if (old != null)
            {
                old.Release();
                UnityEngine.Object.Destroy(old);
            }
            return true;
        }

        // very basic drawing

        SpriteRenderer Next()
        {
            SpriteRenderer r;
            if (used < pool.Count)
            {
                r = pool[used];
            }
            else
            {
                GameObject go = new GameObject("d");
                go.layer = LAYER;
                go.transform.SetParent(root, false);
                r = go.AddComponent<SpriteRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                pool.Add(r);
            }
            used++;
            if (!r.enabled) r.enabled = true;
            r.sortingOrder = order;
            order++;
            return r;
        }

        LineRenderer NextLine()
        {
            LineRenderer l;
            if (lused < lpool.Count)
            {
                l = lpool[lused];
            }
            else
            {
                GameObject go = new GameObject("a");
                go.layer = LAYER;
                go.transform.SetParent(root, false);
                l = go.AddComponent<LineRenderer>();
                l.sharedMaterial = mat;
                l.useWorldSpace = true;
                l.numCapVertices = 0;
                l.numCornerVertices = 0;
                l.alignment = LineAlignment.View;
                l.textureMode = LineTextureMode.Stretch;
                l.shadowCastingMode = ShadowCastingMode.Off;
                l.receiveShadows = false;
                lpool.Add(l);
            }
            lused++;
            if (!l.enabled) l.enabled = true;
            l.sortingOrder = order;
            order++;
            return l;
        }

        //  picture with its top left corner at sx, sy...
        void DrawImage(Sprite s, double sx, double sy, double w, double h, bool flip, double alpha)
        {
            SpriteRenderer r = Next();
            r.sprite = s;
            r.flipX = flip;
            r.color = new Color(1, 1, 1, (float)alpha);
            Transform t = r.transform;
            t.localPosition = new Vector3((float)(sx + w / 2.0), (float)(-(sy + h / 2.0)), 0);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3((float)(w / s.rect.width), (float)(h / s.rect.height), 1);
        }

        //  sprite by name, x is the middle, y is the bottom (anchorBottom) or the middle...
        void Spr(string name, double x, double y, double h, bool flip, bool anchorBottom)
        {
            Spr(name, x, y, h, flip, anchorBottom, 1);
        }

        void Spr(string name, double x, double y, double h, bool flip, bool anchorBottom, double alpha)
        {
            Sprite s;
            if (!img.TryGetValue(name, out s)) return;
            if (s == null) return;
            double w = h * s.rect.width / s.rect.height;
            double sx = U.round(x - w / 2);
            double sy;
            if (anchorBottom)
            {
                sy = U.round(y - h);
            }
            else
            {
                sy = U.round(y - h / 2);
            }
            DrawImage(s, sx, sy, w, h, flip, alpha);
        }

        void E(double cx, double cy, double rx, double ry, Color c)
        {
            SpriteRenderer r = Next();
            r.sprite = circle;
            r.flipX = false;
            r.color = c;
            Transform t = r.transform;
            t.localPosition = new Vector3((float)cx, (float)(-cy), 0);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3((float)(rx * 2), (float)(ry * 2), 1);
        }

        void FillRect(double x, double y, double w, double h, Color c)
        {
            SpriteRenderer r = Next();
            r.sprite = pixel;
            r.flipX = false;
            r.color = c;
            Transform t = r.transform;
            t.localPosition = new Vector3((float)(x + w / 2.0), (float)(-(y + h / 2.0)), 0);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3((float)w, (float)h, 1);
        }

        //  straight line with a width...
        void Line(double x1, double y1, double x2, double y2, double width, Color c)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= 0) return;
            SpriteRenderer r = Next();
            r.sprite = pixel;
            r.flipX = false;
            r.color = c;
            Transform t = r.transform;
            t.localPosition = new Vector3((float)((x1 + x2) / 2.0), (float)(-(y1 + y2) / 2.0), 0);
            t.localRotation = Quaternion.Euler(0, 0, (float)(Math.Atan2(-dy, dx) * 180.0 / Math.PI));
            t.localScale = new Vector3((float)len, (float)width, 1);
        }

        //  part of a circle line from angle a0 to a1...
        void Arc(double cx, double cy, double R, double a0, double a1, double width, Color c)
        {
            LineRenderer l = NextLine();
            int n = arcPts.Length - 1;
            for (int i = 0; i <= n; i++)
            {
                double a = a0 + (a1 - a0) * i / n;
                double px = cx + Math.Cos(a) * R;
                double py = cy + Math.Sin(a) * R;
                arcPts[i] = new Vector3((float)px, (float)(-py), 0);
            }
            l.positionCount = arcPts.Length;
            l.SetPositions(arcPts);
            l.startWidth = (float)width;
            l.endWidth = (float)width;
            l.startColor = c;
            l.endColor = c;
        }

        void StrokeEllipse(double cx, double cy, double rx, double ry, double width, Color c)
        {
            LineRenderer l = NextLine();
            int n = arcPts.Length - 1;
            for (int i = 0; i <= n; i++)
            {
                double a = Math.PI * 2 * i / n;
                double px = cx + Math.Cos(a) * rx;
                double py = cy + Math.Sin(a) * ry;
                arcPts[i] = new Vector3((float)px, (float)(-py), 0);
            }
            l.positionCount = arcPts.Length;
            l.SetPositions(arcPts);
            l.startWidth = (float)width;
            l.endWidth = (float)width;
            l.startColor = c;
            l.endColor = c;
        }

        //  bar with round ends...
        void Pill(double x, double y, double w, double h, Color c)
        {
            double r = h / 2;
            if (w < h)
            {
                E(x + w / 2, y + r, w / 2, r, c);
                return;
            }
            FillRect(x + r, y, w - h, h, c);
            E(x + r, y + r, r, r, c);
            E(x + w - r, y + r, r, r, c);
        }

        void Begin()
        {
            used = 0;
            lused = 0;
            order = -32000;
        }

        void End()
        {
            for (int i = used; i < pool.Count; i++)
            {
                if (pool[i].enabled) pool[i].enabled = false;
            }
            for (int i = lused; i < lpool.Count; i++)
            {
                if (lpool[i].enabled) lpool[i].enabled = false;
            }
        }

        // rendering helpers 

        // same number for the same a, b, c (so hill stones do not jump around)
        static double vhash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791);
                h = (h ^ (h >> 13)) * 1274126177u;
                h = h ^ (h >> 16);
                return h / 4294967296.0;
            }
        }

        static Color Col(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        static Color Col(int r, int g, int b, double a)
        {
            return new Color(r / 255f, g / 255f, b / 255f, (float)a);
        }

        //  green, yellow or red for a health fraction...
        static Color HpCol(double f)
        {
            if (f > 0.6) return U.hex(0x8fd36a);
            if (f > 0.3) return U.hex(0xe3b341);
            return U.hex(0xe0573e);
        }

        static bool CellIs(OrchardGame g, int x, int y, int v)
        {
            if (x < 0 || y < 0 || x >= C.GW || y >= C.GH) return false;
            return g.grid[x, y] == v;
        }

        // list of blobs for a terrain type. a cell plus bridges to its right,
        //  lower and diagonal neighbour, so next cells melt into one shape...
        List<Blob> MakeBlobs(OrchardGame g, int v)
        {
            List<Blob> list = new List<Blob>();
            for (int x = 0; x < C.GW; x++)
            {
                for (int y = 0; y < C.GH; y++)
                {
                    if (!CellIs(g, x, y, v)) continue;
                    Pos c = U.cc(x, y);
                    list.Add(NewBlob(c.x, c.y, 1));
                    if (CellIs(g, x + 1, y, v))
                    {
                        list.Add(NewBlob(c.x + C.CELL / 2, c.y, 2));
                    }
                    if (CellIs(g, x, y + 1, v))
                    {
                        list.Add(NewBlob(c.x, c.y + C.CELL / 2, 3));
                    }
                    if (CellIs(g, x + 1, y + 1, v) && CellIs(g, x + 1, y, v) && CellIs(g, x, y + 1, v))
                    {
                        list.Add(NewBlob(c.x + C.CELL / 2, c.y + C.CELL / 2, 4));
                    }
                }
            }
            return list;
        }

        static Blob NewBlob(double x, double y, int k)
        {
            Blob b = new Blob();
            b.x = x;
            b.y = y;
            b.k = k;
            return b;
        }

        // ground

        void DrawGround()
        {
            FillRect(0, 0, C.VW, C.VH, U.hex(0x5f9552));
        }

        void DrawMud(OrchardGame g)
        {
            Color rim = U.hex(0x6a4a34);
            Color body = U.hex(0x8d6746);
            Color inner = U.hex(0x7a5a3e);
            List<Blob> blobs = MakeBlobs(g, C.MUD);
            for (int i = 0; i < blobs.Count; i++) E(blobs[i].x, blobs[i].y + 2, 25, 18, rim);
            for (int i = 0; i < blobs.Count; i++) E(blobs[i].x, blobs[i].y, 23, 16, body);
            for (int i = 0; i < blobs.Count; i++) E(blobs[i].x, blobs[i].y + 1, 17, 11, inner);
        }

        //  grass tuft: 3 thin strokes...
        void DrawTuft(double x, double y)
        {
            Color c = Col(52, 112, 56, 0.6);
            Line(x, y, x - 3, y - 7, 2, c);
            Line(x, y, x, y - 8, 2, c);
            Line(x, y, x + 3, y - 7, 2, c);
        }

        void DrawBranches(OrchardGame g)
        {
            Color bc = U.hex(0x7a5636);
            Color bc2 = U.hex(0x93694a);
            for (int i = 0; i < g.branches.Count; i++)
            {
                Branch br = g.branches[i];
                double dx = Math.Cos(br.a) * 10;
                double dy = Math.Sin(br.a) * 10;
                Line(br.x - dx, br.y - dy, br.x + dx, br.y + dy, 3, bc);
                Line(br.x, br.y, br.x + Math.Cos(br.a + 0.6) * 6, br.y + Math.Sin(br.a + 0.6) * 6, 2, bc2);
            }
        }

        //  fallen log: brown trunk over 2 cells, cut ends, small twig with leaves...
        void DrawLogs(OrchardGame g)
        {
            if (g.logs == null) return;
            Color dark = U.hex(0x63402a);
            Color bark = U.hex(0x8a5e3c);
            Color hi = U.hex(0xb2845a);
            Color ring = U.hex(0xeccb94);
            Color ringIn = U.hex(0xcfa468);
            Color leaf = U.hex(0x6fae4c);
            Color leaf2 = U.hex(0x93cc62);

            for (int i = 0; i < g.logs.Count; i++)
            {
                FallenLog lg = g.logs[i];
                Pos a = U.cc(lg.cx, lg.cy);
                Pos b;
                if (lg.horiz)
                {
                    b = U.cc(lg.cx + 1, lg.cy);
                }
                else
                {
                    b = U.cc(lg.cx, lg.cy + 1);
                }

                //  a little crooked...
                double tilt = (lg.seed - 0.5) * 8;
                double ax;
                double ay;
                double bx;
                double by;
                if (lg.horiz)
                {
                    ax = a.x - 17;
                    ay = a.y + tilt;
                    bx = b.x + 17;
                    by = b.y - tilt;
                }
                else
                {
                    ax = a.x + tilt;
                    ay = a.y - 17;
                    bx = b.x - tilt;
                    by = b.y + 17;
                }

                Line(ax, ay + 2, bx, by + 2, 24, dark);
                Line(ax, ay, bx, by, 20, bark);

                //  light line on top...
                double offx = 0;
                double offy = 0;
                if (lg.horiz)
                {
                    offy = 5;
                }
                else
                {
                    offx = 5;
                }
                Line(ax - offx, ay - offy, bx - offx, by - offy, 5, hi);

                double len = U.hypot(bx - ax, by - ay);
                double ux = (bx - ax) / len;
                double uy = (by - ay) / len;

                //  bark lines...
                for (int k = 1; k <= 3; k++)
                {
                    double t = k / 4.0;
                    double px = ax + (bx - ax) * t;
                    double py = ay + (by - ay) * t;
                    Line(px - uy * 8 - ux * 4, py + ux * 8 - uy * 4, px + uy * 6 + ux * 3, py - ux * 6 + uy * 3, 2, dark);
                }

                //  ends...
                E(bx, by, 10.5, 10.5, ring);
                E(bx, by, 6.5, 6.5, ringIn);
                E(bx, by, 2.2, 2.2, dark);
                E(ax, ay, 10, 10, dark);

                //  twig with leaves...
                double mx = (ax + bx) / 2 + uy * 9;
                double my = (ay + by) / 2 - ux * 9;
                Line((ax + bx) / 2, (ay + by) / 2, mx - ux * 6 + uy * 6, my - uy * 6 - ux * 6, 2.4, bark);
                E(mx - ux * 6 + uy * 8, my - uy * 6 - ux * 8, 6.5, 4.5, leaf);
                E(mx - ux * 1 + uy * 11, my - uy * 1 - ux * 11, 4.5, 3.2, leaf2);
            }
        }

        void DrawHills(OrchardGame g)
        {
            Color rim = U.hex(0x7e7667);
            Color body = U.hex(0xaba28d);
            Color top = U.hex(0xc5bca6);
            Color hi = U.hex(0xe0d8c3);
            Color moss = Col(104, 158, 76, .8);

            List<Blob> blobs = MakeBlobs(g, C.HILL);
            for (int i = 0; i < blobs.Count; i++) E(blobs[i].x, blobs[i].y + 3, 26, 20, rim);
            for (int i = 0; i < blobs.Count; i++) E(blobs[i].x, blobs[i].y, 24, 18, body);
            for (int i = 0; i < blobs.Count; i++) E(blobs[i].x - 3, blobs[i].y - 4, 18, 12, top);
            for (int i = 0; i < blobs.Count; i++)
            {
                if (blobs[i].k != 1) continue;
                double x = blobs[i].x;
                double y = blobs[i].y;
                E(x - 8, y - 8, 8, 4, hi);
                E(x + 10, y + 6, 6, 3, moss);
                E(x - 12, y + 9, 5, 2.5, moss);
            }

            //  small stones on every hill cell...
            Color s1 = U.hex(0x6f6757);
            Color s2 = U.hex(0xc2baa5);
            Color s3 = U.hex(0xe2dac6);
            for (int x = 0; x < C.GW; x++)
            {
                for (int y = 0; y < C.GH; y++)
                {
                    if (g.grid[x, y] != C.HILL) continue;
                    Pos c = U.cc(x, y);
                    int n = 1 + (int)(vhash(x, y, 3) * 2);
                    for (int k = 0; k < n; k++)
                    {
                        double rx = c.x + (vhash(x, y, k * 3 + 4) - .5) * 26;
                        double ry = c.y + (vhash(x, y, k * 3 + 5) - .5) * 18;
                        double s = 4 + vhash(x, y, k * 3 + 6) * 3;
                        E(rx + 1, ry + 2, s, s * 0.7, s1);
                        E(rx, ry, s, s * 0.7, s2);
                        E(rx - 1, ry - 1, s * 0.5, s * 0.3, s3);
                    }
                }
            }
        }

        void DrawBush(double x, double y)
        {
            Color d = U.hex(0x2f6c36);
            Color m = U.hex(0x3a8441);
            Color l1 = U.hex(0x58a84d);
            Color l2 = U.hex(0x4d9c46);
            Color l3 = U.hex(0x7cc062);
            Color berry = U.hex(0xe0575a);
            E(x - 9, y - 6, 12, 11, d);
            E(x + 9, y - 6, 12, 11, d);
            E(x, y - 14, 14, 13, m);
            E(x - 5, y - 12, 8, 7, l1);
            E(x + 6, y - 9, 6, 5, l2);
            E(x - 2, y - 19, 5, 4, l3);
            E(x - 8, y - 4, 2, 2, berry);
            E(x + 5, y - 15, 2, 2, berry);
            E(x + 10, y - 3, 2, 2, berry);
        }

        // rendering and aiindicators 

        Item MakeItem(double y, int k, double x, double h)
        {
            Item it = new Item();
            it.y = y;
            it.k = k;
            it.x = x;
            it.h = h;
            return it;
        }

        //  sort by y so things lower on the screen are drawn on top...
        //  insertion sort keeps the order of things with the same y...
        void SortItems()
        {
            for (int i = 1; i < items.Count; i++)
            {
                Item item = items[i];
                int j = i - 1;
                while (j >= 0 && items[j].y.CompareTo(item.y) > 0)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = item;
            }
        }

        public void Render(OrchardGame g, double dt)
        {
            Begin();
            vt = vt + dt;
            if (g.grid == null)
            {
                FillRect(0, 0, C.VW, C.VH, U.hex(0x5f9552));
                End();
                return;
            }

            //  ground things first...
            DrawGround();
            DrawMud(g);
            for (int i = 0; i < g.tufts.Count; i++) DrawTuft(g.tufts[i].x, g.tufts[i].y);
            DrawBranches(g);
            DrawHills(g);
            DrawLogs(g);

            //  list of things that must be sorted by y...
            items.Clear();
            for (int x = 0; x < C.GW; x++)
            {
                for (int y = 0; y < C.GH; y++)
                {
                    if (g.grid[x, y] == C.BUSH)
                    {
                        Pos c = U.cc(x, y);
                        items.Add(MakeItem(c.y, K_BUSH, c.x, 0));
                    }
                }
            }
            for (int i = 0; i < g.baits.Count; i++)
            {
                items.Add(MakeItem(g.baits[i].y, K_APPLE, g.baits[i].x, 20));
            }
            for (int i = 0; i < g.trees.Count; i++)
            {
                Item it = MakeItem(g.trees[i].y, K_TREE, 0, 0);
                it.tree = g.trees[i];
                items.Add(it);
            }
            for (int i = 0; i < g.flyingFruit.Count; i++)
            {
                items.Add(MakeItem(g.flyingFruit[i].y, K_APPLE, g.flyingFruit[i].x, 18));
            }
            for (int i = 0; i < g.enemies.Count; i++)
            {
                Item it = MakeItem(g.enemies[i].y, K_ENEMY, 0, 0);
                it.enemy = g.enemies[i];
                items.Add(it);
            }
            Item ip = MakeItem(g.pip.y, K_DOG, 0, 0);
            ip.dog = g.pip;
            ip.imgName = "pip";
            items.Add(ip);
            Item ith = MakeItem(g.thistle.y, K_DOG, 0, 0);
            ith.dog = g.thistle;
            ith.imgName = "thistle";
            items.Add(ith);
            Item ifa = MakeItem(g.farmer.y, K_FARMER, 0, 0);
            ifa.farmer = g.farmer;
            items.Add(ifa);
            SortItems();

            DrawStalkerRings(g);
            DrawPipLine(g);
            DrawPackLines(g);

            Color dark = Col(20, 16, 10, .75);
            Color track = Col(255, 255, 255, .12);
            Color shine = Col(255, 255, 255, .35);

            for (int i = 0; i < items.Count; i++)
            {
                Item it = items[i];
                if (it.k == K_BUSH)
                {
                    DrawBush(it.x, it.y);
                }
                else if (it.k == K_APPLE)
                {
                    Spr("apple", it.x, it.y, it.h, false, false);
                }
                else if (it.k == K_TREE)
                {
                    DrawTree(it.tree, dark, track, shine);
                }
                else if (it.k == K_ENEMY)
                {
                    DrawEnemy(it.enemy, g.showGoals);
                }
                else if (it.k == K_DOG)
                {
                    DrawDog(it.dog, it.imgName);
                }
                else if (it.k == K_FARMER)
                {
                    DrawFarmer(it.farmer);
                }
            }

            for (int i = 0; i < g.tornados.Count; i++)
            {
                DrawTornado(g.tornados[i]);
            }

            End();
        }

        //  red dashed ring around a stalker that sees the farmer...
        void DrawStalkerRings(OrchardGame g)
        {
            Color ring = U.rgba(210, 70, 55, .8f);
            double rx = 220 * 0.55;
            double ry = 220 * 0.45;
            int nd = 63;
            for (int i = 0; i < g.enemies.Count; i++)
            {
                if (g.enemies[i].type != "stalker") continue;
                Stalker st = (Stalker)g.enemies[i];
                if (!st.sees) continue;
                for (int k = 0; k < nd; k++)
                {
                    double a0 = 2 * Math.PI * k / nd;
                    double a1 = a0 + 2 * Math.PI / nd * 6.0 / 11.0;
                    Line(st.x + Math.Cos(a0) * rx, st.y + Math.Sin(a0) * ry,
                         st.x + Math.Cos(a1) * rx, st.y + Math.Sin(a1) * ry, 2, ring);
                }
            }
        }

        //  green dots from pip to the tree he repairs...
        void DrawPipLine(OrchardGame g)
        {
            TreeEntity pt = g.pip.target;
            if (pt == null) return;
            if (!pt.alive) return;
            if (pt.hp >= pt.maxhp - 0.01) return;
            double d = U.hypot(pt.x - g.pip.x, pt.y - g.pip.y);
            if (d <= 30) return;
            int n = (int)Math.Floor(d / 14);
            for (int i = 1; i < n; i++)
            {
                double u = (double)i / n;
                double a = 0.25 + 0.35 * Math.Max(0, Math.Sin(vt * 6 - i));
                E(g.pip.x + (pt.x - g.pip.x) * u, g.pip.y + (pt.y - g.pip.y) * u, 1.8, 1.8, Col(156, 240, 138, a));
            }
        }

        //  amber dots from shamblers to the dog they attack, red ring when 2 or more bite...
        void DrawPackLines(OrchardGame g)
        {
            Mover[] dogs = new Mover[] { g.pip, g.thistle };
            for (int di = 0; di < 2; di++)
            {
                Mover dog = dogs[di];
                int bitingNow = g.PackSize(dog);
                for (int i = 0; i < g.enemies.Count; i++)
                {
                    if (g.enemies[i].type != "shambler") continue;
                    Shambler s = (Shambler)g.enemies[i];
                    if (s.act != ShamAct.PackDog) continue;
                    if (s.dogT != dog) continue;
                    double d = U.hypot(dog.x - s.x, dog.y - s.y);
                    int n = (int)Math.Floor(d / 10);
                    for (int k = 1; k < n; k++)
                    {
                        double u = (double)k / n;
                        double a = 0.35 + 0.35 * Math.Max(0, Math.Sin(vt * 8 - k));
                        E(s.x + (dog.x - s.x) * u, s.y + (dog.y - s.y) * u, 1.6, 1.6, Col(233, 184, 80, a));
                    }
                }
                if (bitingNow >= 2)
                {
                    double pr = 26 + 3 * Math.Sin(vt * 9);
                    StrokeEllipse(dog.x, dog.y - 4, pr, pr * 0.55, 2.5, Col(240, 90, 70, 0.85));
                }
            }
        }

        void DrawTree(TreeEntity t, Color dark, Color track, Color shine)
        {
            if (t.alive)
            {
                Spr("tree", t.x, t.y + 12, 96, false, true);
                //  health bar above the tree...
                double f = t.hp / t.maxhp;
                double w = 46;
                double hx = t.x - w / 2;
                double hy = t.y - 92;
                Pill(hx - 1.5, hy - 1.5, w + 3, 9, dark);
                Pill(hx, hy, w, 6, track);
                if (f > 0)
                {
                    Pill(hx, hy, Math.Max(6, w * f), 6, HpCol(f));
                }
                Pill(hx + 2, hy + 1, Math.Max(0, w * f - 4), 1.5, shine);
            }
            else
            {
                //  fallen tree: faded tree and a stump...
                Spr("tree", t.x, t.y + 12, 74, false, true, .4);
                E(t.x, t.y + 6, 9, 4, U.hex(0x4b3622));
                E(t.x, t.y + 4, 8, 3.2, U.hex(0x7a5a3a));
            }
        }

        void DrawEnemy(Enemy e, bool showBars)
        {
            if (e.type == "scatterling" || e.type == "bad")
            {
                Bird bird = (Bird)e;
                double bob = Math.Sin(bird.bob) * 3;
                string name = "scatterling";
                if (bird.bad) name = "badscatter";
                Spr(name, e.x, e.y - bird.flyH + bob, 28, e.dir < 0, true);

                //  red eye of the black bird...
                if (bird.bad)
                {
                    double ex = 5;
                    if (e.dir < 0) ex = -5;
                    E(e.x + ex, e.y - bird.flyH + bob - 17, 2.2, 2.2, Col(255, 60, 50, .9));
                }

                //  small crossed apple: dizzy or cannot eat...
                if (bird.confT > 0 || bird.noEatT > 0)
                {
                    double nx = e.x + 13;
                    double ny = e.y - bird.flyH + bob - 28;
                    E(nx, ny, 4, 4, Col(20, 16, 10, .6));
                    E(nx, ny + 0.4, 2.6, 2.4, Col(230, 90, 70, .85));
                    Line(nx - 3.4, ny + 3.4, nx + 3.4, ny - 3.4, 1.3, Col(255, 255, 255, .9));
                }
                return;
            }
            // char rendering and shapes
            //  shambler and stalker...
            double wob = 3;
            if (e.type == "shambler") wob = 4;
            double bob2 = Math.Abs(Math.Sin(e.walk)) * wob;
            double h = 44;
            if (e.type == "stalker") h = 34;
            Spr(e.type, e.x, e.y + 5 - bob2, h, e.dir < 0, true);

            //  health bar...
            if (e.hp < 6)
            {
                double w = 26;
                Pill(e.x - w / 2 - 1, e.y - h - 5, w + 2, 5, Col(20, 16, 10, .7));
                Pill(e.x - w / 2, e.y - h - 4, w * Math.Max(0, e.hp) / 6, 3, U.hex(0xe0573e));
            }

            if (e.type == "shambler")
            {
                DrawIntent((Shambler)e, e.x, e.y - h - 15, showBars);
            }

            //  red ! over a stalker that sees the farmer...
            if (e.type == "stalker" && ((Stalker)e).sees)
            {
                double by = e.y - h - 12 + Math.Sin(vt * 10) * 1.5;
                E(e.x, by, 7, 7, U.hex(0xe0573e));
                FillRect(e.x - 1.2, by - 4.5, 2.4, 6, Color.white);
                E(e.x, by + 3.8, 1.3, 1.3, Color.white);
            }
        }

        void DrawDog(Mover m, string name)
        {
            int dir = 1;
            double dhp = 5;
            bool retreating = false;
            ThistleState tstate = ThistleState.Wander;
            bool isPip = m is Pip;
            if (isPip)
            {
                Pip p = (Pip)m;
                dir = p.dir;
                dhp = p.hp;
                retreating = p.retreating;
            }
            else
            {
                Thistle t = (Thistle)m;
                dir = t.dir;
                dhp = t.hp;
                tstate = t.state;
            }

            Spr(name, m.x, m.y + 4, 34, dir < 0, true);

            //  health bar when hurt...
            if (dhp < 4.99)
            {
                double w = 24;
                Pill(m.x - w / 2 - 1, m.y - 41, w + 2, 5, Col(20, 16, 10, .75));
                Pill(m.x - w / 2, m.y - 40, w * Math.Max(0, dhp) / 5, 3, HpCol(dhp / 5));
            }

            //  blue drop over pip while he runs to the farmer...
            if (isPip && retreating)
            {
                double by = m.y - 50 + Math.Sin(vt * 8) * 1.5;
                E(m.x, by, 6.5, 6.5, Col(20, 16, 10, .75));
                E(m.x, by + 1, 3.2, 3.6, U.hex(0x7fb4e6));
                E(m.x, by - 2.2, 1.6, 2.2, U.hex(0x7fb4e6));
            }

            //  small dot for thistle state...
            if (!isPip && tstate != ThistleState.Wander)
            {
                Color c = U.hex(0x7fb4e6);
                if (tstate == ThistleState.Feed) c = U.hex(0xe3b341);
                E(m.x + 12, m.y - 34, 4, 4, Col(20, 16, 10, .7));
                E(m.x + 12, m.y - 34, 2.6, 2.6, c);
            }
        }

        void DrawFarmer(Farmer f)
        {
            double bob = 0;
            if (f.moving)
            {
                bob = Math.Abs(Math.Sin(f.walk)) * 3;
            }
            Spr("farmer", f.x, f.y + 5 - bob, 46, f.dir < 0, true);
            DrawStick(f, bob);

            //  health bar when hurt...
            if (f.hp < C.FARMER_HP - 0.01)
            {
                double w = 30;
                double fr = f.hp / C.FARMER_HP;
                double hy = f.y - 58;
                Pill(f.x - w / 2 - 1, hy - 1, w + 2, 6, Col(20, 16, 10, .8));
                Pill(f.x - w / 2, hy, Math.Max(4, w * fr), 4, HpCol(fr));
            }
        }

        //  the farmer's stick. it starts at his fist and turns round that point...
        //  rest: pointing up. when he hits (space) it swings down and back up...
        void DrawStick(Farmer f, double bob)
        {
            double p = 0;
            if (f.swing > 0)
            {
                p = U.clamp(1 - f.swing / 0.32, 0, 1);
            }
            //  angle goes from -1.05 (up) to 0.9 (down) and back...
            double a = -1.05 + 1.95 * Math.Sin(Math.PI * p);
            double d = 1;
            if (f.dir < 0) d = -1;
            double L = 30;

            //  fist of the sprite: 6.6 pixels to the side, 16 pixels above the feet line...
            double hx = f.x + d * 6.6;
            double hy = f.y - 16 - bob;
            double tx = hx + d * Math.Cos(a) * L;
            double ty = hy + Math.Sin(a) * L;

            Line(hx, hy, tx, ty, 5, U.hex(0x5a3a22));
            Line(hx, hy, tx, ty, 3, U.hex(0x9a6a3c));
            E(tx, ty, 5, 5, U.hex(0x5a3a22));
            E(tx, ty, 3.6, 3.6, U.hex(0xe9ecdf));

            //  fingers over the stick, so it looks held...
            E(hx, hy, 4, 4, U.hex(0x3a2418));
            E(hx, hy, 3, 3, U.hex(0xf6bf8e));
        }

        //  tornado: a ring on the ground (the dizzy zone) and a small core...
        void DrawTornado(Tornado t)
        {
            double life = t.life;
            double age = C.TORNADO_LIFE - life;
            double fade = Math.Min(1, Math.Min(age / 0.5, life / 0.8));
            StrokeEllipse(t.x, t.y, t.r * 0.95, t.r * 0.95 * 0.42, 2, Col(230, 215, 180, 0.7 * fade));
            E(t.x, t.y, 9, 4, Col(170, 150, 115, 0.9 * fade));
        }

        //shambler thought bubble

        public static Color[] ACT_COL = {
            new Color(0x8f / 255f, 0xd3 / 255f, 0x6a / 255f, 1),
            new Color(1f, 0x9f / 255f, 0x43 / 255f, 1),
            new Color(0xe9 / 255f, 0xb8 / 255f, 0x50 / 255f, 1),
            new Color(0xf0 / 255f, 0x6a / 255f, 0x55 / 255f, 1),
            new Color(0x7f / 255f, 0xb4 / 255f, 0xe6 / 255f, 1),
            new Color(0xd8 / 255f, 0xc8 / 255f, 0xa8 / 255f, 1) };

        // small bubble with an icon for what the shambler decided.
        //  with Tab: 6 bars for the scores of the 6 options...
        void DrawIntent(Shambler s, double x, double y, bool bars) //george worked on goaldriven score and these scores as 6 bars on tab press..
        {
            Color dark = Col(20, 16, 10, .78);
            E(x, y, 8.5, 8.5, dark);
            E(x - 3, y + 8, 2.4, 2.4, dark);
            Color c = ACT_COL[(int)s.act];

            if (s.act == ShamAct.RaidTree)
            {
                FillRect(x - 1, y + 0.5, 2, 4.5, U.hex(0x7a5230));
                E(x, y - 1.5, 4.6, 4.2, c);
            }
            else if (s.act == ShamAct.EatFruit)
            {
                E(x, y + 0.8, 4.2, 4, c);
                E(x + 1.8, y - 3.8, 1.8, 1, U.hex(0x8fd36a));
            }
            else if (s.act == ShamAct.PackDog)
            {
                //  paw...
                E(x, y + 1.6, 3.2, 2.6, c);
                E(x - 3.4, y - 2, 1.4, 1.6, c);
                E(x - 1.2, y - 3.6, 1.4, 1.6, c);
                E(x + 1.2, y - 3.6, 1.4, 1.6, c);
                E(x + 3.4, y - 2, 1.4, 1.6, c);
            }
            else if (s.act == ShamAct.HitFarmer)
            {
                FillRect(x - 1.2, y - 5, 2.4, 6.2, c);
                E(x, y + 3.6, 1.4, 1.4, c);
            }
            else if (s.act == ShamAct.Whirl)
            {
                //  little swirl...
                double a0 = -vt * 7;
                Arc(x, y, 4.6, a0, a0 + 4.2, 1.6, c);
                Arc(x, y, 2.2, a0 + 1.5, a0 + 5.2, 1.4, c);
            }
            else
            {
                //  sweat drop...
                E(x, y + 1, 3.2, 3.6, c);
                E(x, y - 2.4, 1.6, 2.2, c);
            }

            if (!bars) return;

            int na = Shambler.N_ACT;
            double bw = 4.2;
            double gap = 1.3;
            double bh = 22;
            double bx = x - (na * bw + (na - 1) * gap) / 2;
            double by = y - 13;
            FillRect(bx - 2, by - bh - 2, na * bw + (na - 1) * gap + 4, bh + 4, Col(20, 16, 10, .78));
            for (int i = 0; i < na; i++)
            {
                double v = Math.Max(0.02, Math.Min(1, s.scores[i]));
                double cx = bx + i * (bw + gap);
                FillRect(cx, by - bh, bw, bh, Col(255, 255, 255, .08));
                FillRect(cx, by - bh * v, bw, bh * v, ACT_COL[i]);
                if (i == (int)s.act)
                {
                    FillRect(cx, by + 1, bw, 1.4, Color.white);
                }
            }
        }
    }

    //  small textures made in code...
    public static class Shapes
    {
        static Sprite circleSprite;
        static Sprite pixelSprite;
        static Sprite glowSprite;

        //  white soft-edged disc, scaled to any circle or ellipse...
        public static Sprite Circle()
        {
            if (circleSprite != null) return circleSprite;
            int N = 256;
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, true);
            Color32[] px = new Color32[N * N];
            float rr = N / 2f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - rr;
                    float dy = y + 0.5f - rr;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rr - d);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
            }
            t.SetPixels32(px);
            t.Apply(true);
            t.filterMode = FilterMode.Trilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            circleSprite = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N, 0, SpriteMeshType.FullRect);
            circleSprite.name = "circle";
            return circleSprite;
        }

        // white disc that gets more see-through towards the edge (for glow)
        public static Sprite Glow()
        {
            if (glowSprite != null) return glowSprite;
            int N = 128;
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + .5f) / N * 2 - 1;
                    float dy = (y + .5f) / N * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float q = Mathf.Max(0, 1 - d);
                    float a = q * q;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255));
                }
            }
            t.SetPixels32(px);
            t.Apply(false);
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            glowSprite = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N, 0, SpriteMeshType.FullRect);
            glowSprite.name = "glow";
            return glowSprite;
        }

        //  white square, used for rectangles and lines...
        public static Sprite Pixel()
        {
            if (pixelSprite != null) return pixelSprite;
            Texture2D t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[16];
            for (int i = 0; i < 16; i++)
            {
                px[i] = new Color32(255, 255, 255, 255);
            }
            t.SetPixels32(px);
            t.Apply();
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            pixelSprite = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4, 0, SpriteMeshType.FullRect);
            return pixelSprite;
        }
    }
}
