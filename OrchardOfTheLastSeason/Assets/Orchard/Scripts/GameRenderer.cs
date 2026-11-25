using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orchard
{
    // draws the board exactly like the html canvas: every frame, in the same order,
    // into a 704x704 render texture. each draw call gets the next sorting order (painter's algorithm).
    // canvas coords (x right, y down) map to world (x, -y).
    public class GameRenderer
    {
        public const int LAYER = 31;
        public RenderTexture rt;

        Camera cam;
        Transform root;
        Material mat;
        Dictionary<string, Sprite> img;
        Sprite circle, pixel, tuft;

        List<SpriteRenderer> pool = new List<SpriteRenderer>();
        int used;
        List<LineRenderer> lpool = new List<LineRenderer>();
        int lused;
        int order;
        Vector3[] arcPts = new Vector3[21];

        // dashed stalker ring, precomputed relative to the stalker (setLineDash([6,5]))
        List<Vector4> dashes = new List<Vector4>();

        struct Item
        {
            public double y;
            public int seq;
            public int k;      // 0 hill, 1 bush, 2 apple, 3 tree, 4 enemy, 5 dog, 6 farmer
            public double x, h;
            public object refObj;
            public string imgName;
        }
        List<Item> items = new List<Item>();

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
            cam.backgroundColor = U.hex(0x2f4a27);
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
                sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            mat = new Material(sh);

            circle = Shapes.Circle();
            pixel = Shapes.Pixel();
            tuft = Shapes.Tuft();
            BuildDashes(220 * 0.55f, 220 * 0.45f, 6, 5);
            texGlow = Shapes.Radial(128, 0);
            texDark = Shapes.Radial(256, 1);
            texVign = Shapes.Radial(256, 2);
        }

        static RenderTexture MakeRT(int w, int h)
        {
            RenderTexture t = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            t.antiAliasing = 4;
            t.filterMode = FilterMode.Bilinear;
            t.Create();
            return t;
        }

        // render the board at exactly the pixels it covers on screen (retina, 1080p, 4k...),
        // so nothing is ever stretched and blurred. the camera keeps showing the same 704 px of
        // world height, it just draws them with more pixels.
        public bool Resize(int w, int h)
        {
            if (rt != null && rt.width == w && rt.height == h) return false;
            RenderTexture old = rt;
            rt = MakeRT(w, h);
            cam.targetTexture = rt;
            if (old != null) { old.Release(); Object.Destroy(old); }
            return true;
        }

        // ---------- immediate-mode drawing helpers ----------
        SpriteRenderer Next()
        {
            SpriteRenderer r;
            if (used < pool.Count) r = pool[used];
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
            r.sortingOrder = order++;
            return r;
        }

        LineRenderer NextLine()
        {
            LineRenderer l;
            if (lused < lpool.Count) l = lpool[lused];
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
            l.sortingOrder = order++;
            return l;
        }

        // ctx.drawImage(img, sx, sy, w, h) (optionally mirrored)
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

        // image centred on (cx, cy), rotated by rot radians (clockwise on screen, like canvas)
        void DrawImageRot(Sprite s, double cx, double cy, double w, double h, double rot, bool flip, double alpha)
        {
            SpriteRenderer r = Next();
            r.sprite = s;
            r.flipX = flip;
            r.color = new Color(1, 1, 1, (float)alpha);
            Transform t = r.transform;
            t.localPosition = new Vector3((float)cx, (float)(-cy), 0);
            t.localRotation = Quaternion.Euler(0, 0, (float)(-rot * 180.0 / System.Math.PI));
            t.localScale = new Vector3((float)(w / s.rect.width), (float)(h / s.rect.height), 1);
        }

        // the html spr(): width from aspect, top-left rounded like Math.round
        void Spr(string name, double x, double y, double h, bool flip, bool anchorBottom, double alpha = 1)
        {
            Sprite s;
            if (!img.TryGetValue(name, out s) || s == null) return;
            double w = h * s.rect.width / s.rect.height;
            double sx = U.jsRound(x - w / 2);
            double sy = U.jsRound(anchorBottom ? y - h : y - h / 2);
            DrawImage(s, sx, sy, w, h, flip, alpha);
        }

        void FillEllipse(double cx, double cy, double rx, double ry, Color c)
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

        // a straight stroke with butt caps (canvas default)
        void Line(double x1, double y1, double x2, double y2, double width, Color c)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double len = System.Math.Sqrt(dx * dx + dy * dy);
            if (len <= 0) return;
            SpriteRenderer r = Next();
            r.sprite = pixel;
            r.flipX = false;
            r.color = c;
            Transform t = r.transform;
            t.localPosition = new Vector3((float)((x1 + x2) / 2.0), (float)(-(y1 + y2) / 2.0), 0);
            t.localRotation = Quaternion.Euler(0, 0, (float)(System.Math.Atan2(-dy, dx) * 180.0 / System.Math.PI));
            t.localScale = new Vector3((float)len, (float)width, 1);
        }

        // ctx.arc(cx, cy, R, a0, a1) stroked
        void Arc(double cx, double cy, double R, double a0, double a1, double width, Color c)
        {
            LineRenderer l = NextLine();
            int n = arcPts.Length - 1;
            for (int i = 0; i <= n; i++)
            {
                double a = a0 + (a1 - a0) * i / n;
                arcPts[i] = new Vector3((float)(cx + System.Math.Cos(a) * R), (float)(-(cy + System.Math.Sin(a) * R)), 0);
            }
            l.positionCount = arcPts.Length;
            l.SetPositions(arcPts);
            l.startWidth = (float)width;
            l.endWidth = (float)width;
            l.startColor = c;
            l.endColor = c;
        }

        void Shadow(double x, double y, double rx)
        {
            FillEllipse(x, y, rx, rx * 0.45f, new Color(0, 0, 0, .22f));
        }

        void DrawTuft(double x, double y)
        {
            SpriteRenderer r = Next();
            r.sprite = tuft;
            r.flipX = false;
            r.color = Color.white;
            Transform t = r.transform;
            t.localPosition = new Vector3((float)x, (float)(-y), 0);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }

        void BuildDashes(float rx, float ry, float on, float off)
        {
            int M = 4000;
            Vector2[] p = new Vector2[M + 1];
            float[] s = new float[M + 1];
            for (int i = 0; i <= M; i++)
            {
                float a = Mathf.PI * 2 * i / M;
                p[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i > 0) s[i] = s[i - 1] + Vector2.Distance(p[i - 1], p[i]);
            }
            float total = s[M];
            for (float s0 = 0; s0 < total; s0 += on + off)
            {
                float s1 = Mathf.Min(s0 + on, total);
                Vector2 a = PointAt(p, s, s0), b = PointAt(p, s, s1);
                dashes.Add(new Vector4(a.x, a.y, b.x, b.y));
            }
        }

        static Vector2 PointAt(Vector2[] p, float[] s, float d)
        {
            for (int i = 1; i < s.Length; i++)
            {
                if (s[i] >= d)
                {
                    float seg = s[i] - s[i - 1];
                    float t = seg > 0 ? (d - s[i - 1]) / seg : 0;
                    return Vector2.Lerp(p[i - 1], p[i], t);
                }
            }
            return p[p.Length - 1];
        }

        void Begin()
        {
            used = 0;
            lused = 0;
            order = -32000;
        }

        void End()
        {
            for (int i = used; i < pool.Count; i++) if (pool[i].enabled) pool[i].enabled = false;
            for (int i = lused; i < lpool.Count; i++) if (lpool[i].enabled) lpool[i].enabled = false;
        }

        // ================= visual layer (draw only, never touches game state) =================
        class Particle { public double x, y, vx, vy, life, max, size, g; public Color col; public bool leaf, splash; }
        class Firefly { public double x, y, a, p, s; }
        List<Particle> parts = new List<Particle>();
        List<Firefly> flies = new List<Firefly>();
        Dictionary<Enemy, double> lastHp = new Dictionary<Enemy, double>();
        List<Enemy> goneTmp = new List<Enemy>();
        HashSet<Enemy> seenTmp = new HashSet<Enemy>();
        List<Bait> lastBaits = new List<Bait>();
        HashSet<TreeEntity> reviveBurst = new HashSet<TreeEntity>();
        int lastSaved, lastLost;
        double vt, bolt, boltNext = 9;
        uint vseed = 987654321;
        Sprite texGlow, texDark, texVign;

        double vr()
        {
            unchecked { vseed = vseed * 1103515245u + 12345u; }
            return ((vseed >> 8) & 0xffffff) / 16777216.0;
        }

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

        static Color Col(int r, int g, int b, double a = 1) { return new Color(r / 255f, g / 255f, b / 255f, (float)a); }
        static Color WithA(Color c, double mul) { return new Color(c.r, c.g, c.b, (float)(c.a * mul)); }

        void E(double cx, double cy, double rx, double ry, Color c) { FillEllipse(cx, cy, rx, ry, c); }

        void Soft(Sprite tex, Color c, double alpha, double cx, double cy, double rx, double ry)
        {
            SpriteRenderer r = Next();
            r.sprite = tex;
            r.flipX = false;
            r.color = new Color(c.r, c.g, c.b, (float)alpha);
            Transform t = r.transform;
            t.localPosition = new Vector3((float)cx, (float)(-cy), 0);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3((float)(rx * 2), (float)(ry * 2), 1);
        }

        void StrokeEllipse(double cx, double cy, double rx, double ry, double width, Color c)
        {
            LineRenderer l = NextLine();
            int n = arcPts.Length - 1;
            for (int i = 0; i <= n; i++)
            {
                double a = System.Math.PI * 2 * i / n;
                arcPts[i] = new Vector3((float)(cx + System.Math.Cos(a) * rx), (float)(-(cy + System.Math.Sin(a) * ry)), 0);
            }
            l.positionCount = arcPts.Length;
            l.SetPositions(arcPts);
            l.startWidth = (float)width;
            l.endWidth = (float)width;
            l.startColor = c;
            l.endColor = c;
        }

        void Pill(double x, double y, double w, double h, Color c)
        {
            double r = h / 2;
            if (w < h) { E(x + w / 2, y + r, w / 2, r, c); return; }
            FillRect(x + r, y, w - h, h, c);
            E(x + r, y + r, r, r, c);
            E(x + w - r, y + r, r, r, c);
        }

        static Color HpCol(double f) { return f > 0.6 ? U.hex(0x8fd36a) : (f > 0.3 ? U.hex(0xe3b341) : U.hex(0xe0573e)); }

        void Emit(double x, double y, int n, Color col, double spd, double life, double size, double up, double grav)
        {
            for (int i = 0; i < n; i++)
            {
                double a = vr() * 6.283, s = spd * (0.4 + vr() * 0.6);
                Particle p = new Particle();
                p.x = x; p.y = y; p.vx = System.Math.Cos(a) * s; p.vy = System.Math.Sin(a) * s - up;
                p.life = life; p.max = life; p.size = size * (0.6 + vr() * 0.6); p.col = col; p.g = grav;
                parts.Add(p);
            }
        }

        static bool CellIs(OrchardGame g, int x, int y, int v) { return x >= 0 && y >= 0 && x < C.GW && y < C.GH && g.grid[x, y] == v; }

        // a cell plus bridges to its right / lower / diagonal neighbours, so blobs merge into one shape
        void ForBlobs(OrchardGame g, int v, System.Action<double, double, int, int, int> fn)
        {
            for (int x = 0; x < C.GW; x++)
                for (int y = 0; y < C.GH; y++)
                    if (CellIs(g, x, y, v))
                    {
                        Pos c = U.cc(x, y);
                        fn(c.x, c.y, x, y, 1);
                        if (CellIs(g, x + 1, y, v)) fn(c.x + C.CELL / 2, c.y, x, y, 2);
                        if (CellIs(g, x, y + 1, v)) fn(c.x, c.y + C.CELL / 2, x, y, 3);
                        if (CellIs(g, x + 1, y + 1, v) && CellIs(g, x + 1, y, v) && CellIs(g, x, y + 1, v)) fn(c.x + C.CELL / 2, c.y + C.CELL / 2, x, y, 4);
                    }
        }

        void DrawGround()
        {
            Color ga = U.hex(0x4a7535), gb = U.hex(0x4f7c39);
            for (int x = 0; x < C.GW; x++)
                for (int y = 0; y < C.GH; y++)
                    FillRect(x * C.CELL, y * C.CELL, C.CELL, C.CELL, (x + y) % 2 == 1 ? ga : gb);
            for (int i = 0; i < 69; i++)
            {
                double bx = vhash(i, 1, 7) * C.VW, by = vhash(i, 2, 7) * C.VH, r = 26 + vhash(i, 3, 7) * 52;
                E(bx, by, r, r * 0.7, vhash(i, 4, 7) < 0.55 ? Col(28, 58, 24, .16) : Col(140, 175, 80, .09));
            }
            for (int i = 0; i < 135; i++)
            {
                double x = vhash(i, 5, 9) * C.VW, y = vhash(i, 6, 9) * C.VH;
                E(x, y, 1.6, 1.6, vhash(i, 7, 9) < 0.5 ? Col(235, 225, 150, .55) : Col(240, 240, 240, .45));
            }
        }

        void DrawMud(OrchardGame g)
        {
            Color rim = U.hex(0x3c2b18), body = U.hex(0x5b4327), inner = U.hex(0x4a3820), sheen = Col(170, 190, 215, .22);
            ForBlobs(g, C.MUD, (x, y, cx, cy, k) => E(x, y + 2, 25, 18, rim));
            ForBlobs(g, C.MUD, (x, y, cx, cy, k) => E(x, y, 23, 16, body));
            ForBlobs(g, C.MUD, (x, y, cx, cy, k) => E(x, y + 1, 17, 11, inner));
            ForBlobs(g, C.MUD, (x, y, cx, cy, k) => { if (k == 1) E(x - 6, y - 4, 9, 3.5, sheen); });
            for (int x = 0; x < C.GW; x++)
                for (int y = 0; y < C.GH; y++)
                    if (g.grid[x, y] == C.MUD)
                    {
                        Pos c = U.cc(x, y);
                        for (int k = 0; k < 2; k++)
                        {
                            double per = 1.6 + vhash(x, y, k + 20) * 1.2;
                            double ph = ((vt + vhash(x, y, k + 30) * per) % per) / per;
                            double ox = (vhash(x, y, k + 40) - .5) * 20, oy = (vhash(x, y, k + 50) - .5) * 10;
                            double rr = 2 + ph * 10;
                            StrokeEllipse(c.x + ox, c.y + oy, rr, rr, 1, Col(190, 205, 225, 0.5 * (1 - ph)));
                        }
                    }
        }

        // fallen logs: a bark-brown trunk across two cells, cut rings at both ends, a leafy twig
        void DrawLogs(OrchardGame g)
        {
            if (g.logs == null) return;
            Color sh = Col(0, 0, 0, .28), dark = U.hex(0x4a3019), bark = U.hex(0x6e4a28), hi = U.hex(0x8c6438);
            Color ring = U.hex(0xd9b77e), ringIn = U.hex(0xb48a52), leaf = U.hex(0x5f8f3a), leaf2 = U.hex(0x7aa84a);
            foreach (FallenLog lg in g.logs)
            {
                Pos a = U.cc(lg.cx, lg.cy);
                Pos b = lg.horiz ? U.cc(lg.cx + 1, lg.cy) : U.cc(lg.cx, lg.cy + 1);
                double tilt = (lg.seed - 0.5) * 8;                 // a little crooked, not ruler-straight
                double ax = a.x + (lg.horiz ? -17 : tilt), ay = a.y + (lg.horiz ? tilt : -17);
                double bx = b.x + (lg.horiz ? 17 : -tilt), by = b.y + (lg.horiz ? -tilt : 17);
                Line(ax + 4, ay + 8, bx + 4, by + 8, 24, sh);       // shadow
                Line(ax, ay + 2, bx, by + 2, 24, dark);              // underside
                Line(ax, ay, bx, by, 20, bark);                      // trunk
                Line(ax - (lg.horiz ? 0 : 5), ay - (lg.horiz ? 5 : 0), bx - (lg.horiz ? 0 : 5), by - (lg.horiz ? 5 : 0), 5, hi);                 // light on top
                double len = U.hypot(bx - ax, by - ay), ux = (bx - ax) / len, uy = (by - ay) / len;
                for (int k = 1; k <= 3; k++)                         // bark grooves
                {
                    double t = k / 4.0, px = ax + (bx - ax) * t, py = ay + (by - ay) * t;
                    Line(px - uy * 8 - ux * 4, py + ux * 8 - uy * 4, px + uy * 6 + ux * 3, py - ux * 6 + uy * 3, 2, dark);
                }
                E(bx, by, 10.5, 10.5, ring);                           // cut end
                E(bx, by, 6.5, 6.5, ringIn);
                E(bx, by, 2.2, 2.2, dark);
                E(ax, ay, 10, 10, dark);                               // broken end
                double mx = (ax + bx) / 2 + uy * 9, my = (ay + by) / 2 - ux * 9;
                Line((ax + bx) / 2, (ay + by) / 2, mx - ux * 6 + uy * 6, my - uy * 6 - ux * 6, 2.4, bark);   // twig
                E(mx - ux * 6 + uy * 8, my - uy * 6 - ux * 8, 6.5, 4.5, leaf);
                E(mx - ux * 1 + uy * 11, my - uy * 1 - ux * 11, 4.5, 3.2, leaf2);
            }
        }

        void DrawHills(OrchardGame g)
        {
            Color sh = Col(0, 0, 0, .30), rim = U.hex(0x5a5246), body = U.hex(0x857b6a), top = U.hex(0x9b917d), hi = U.hex(0xb3a993), moss = Col(80, 120, 50, .75);
            ForBlobs(g, C.HILL, (x, y, cx, cy, k) => E(x + 3, y + 9, 27, 19, sh));
            ForBlobs(g, C.HILL, (x, y, cx, cy, k) => E(x, y + 3, 26, 20, rim));
            ForBlobs(g, C.HILL, (x, y, cx, cy, k) => E(x, y, 24, 18, body));
            ForBlobs(g, C.HILL, (x, y, cx, cy, k) => E(x - 3, y - 4, 18, 12, top));
            ForBlobs(g, C.HILL, (x, y, cx, cy, k) => { if (k == 1) { E(x - 8, y - 8, 8, 4, hi); E(x + 10, y + 6, 6, 3, moss); E(x - 12, y + 9, 5, 2.5, moss); } });
            Color s1 = U.hex(0x4f483d), s2 = U.hex(0xa8a08c), s3 = U.hex(0xc8c0aa);
            for (int x = 0; x < C.GW; x++)
                for (int y = 0; y < C.GH; y++)
                    if (g.grid[x, y] == C.HILL)
                    {
                        Pos c = U.cc(x, y);
                        int n = 1 + (int)(vhash(x, y, 3) * 2);
                        for (int k = 0; k < n; k++)
                        {
                            double rx = c.x + (vhash(x, y, k * 3 + 4) - .5) * 26, ry = c.y + (vhash(x, y, k * 3 + 5) - .5) * 18, s = 4 + vhash(x, y, k * 3 + 6) * 3;
                            E(rx + 1, ry + 2, s, s * 0.7, s1);
                            E(rx, ry, s, s * 0.7, s2);
                            E(rx - 1, ry - 1, s * 0.5, s * 0.3, s3);
                        }
                    }
        }

        void DrawBush(double x, double y)
        {
            Shadow(x, y + 6, 20);
            Color d = U.hex(0x244f21), m = U.hex(0x2b5f27), l1 = U.hex(0x3f8438), l2 = U.hex(0x3a7a33), l3 = U.hex(0x55a046), berry = U.hex(0xc83d3d);
            E(x - 9, y - 6, 12, 11, d); E(x + 9, y - 6, 12, 11, d); E(x, y - 14, 14, 13, m);
            E(x - 5, y - 12, 8, 7, l1); E(x + 6, y - 9, 6, 5, l2); E(x - 2, y - 19, 5, 4, l3);
            E(x - 8, y - 4, 2, 2, berry); E(x + 5, y - 15, 2, 2, berry); E(x + 10, y - 3, 2, 2, berry);
        }

        void InitFlies()
        {
            flies.Clear();
            for (int i = 0; i < 24; i++)
            {
                Firefly f = new Firefly();
                f.x = vr() * C.VW; f.y = vr() * C.VH; f.a = vr() * 6.28; f.p = vr() * 6.28; f.s = 10 + vr() * 14;
                flies.Add(f);
            }
        }

        // turns game events into particles by comparing with last frame (read only)
        void Watch(OrchardGame g, double dt)
        {
            seenTmp.Clear();
            foreach (Enemy e in g.enemies)
            {
                seenTmp.Add(e);
                double h;
                if (lastHp.TryGetValue(e, out h) && e.hp < h - 0.01 && vr() < 0.5) Emit(e.x, e.y - 16, 3, U.hex(0xfff3c4), 90, 0.3, 2.2, 20, 0);
                lastHp[e] = e.hp;
            }
            goneTmp.Clear();
            foreach (KeyValuePair<Enemy, double> kv in lastHp) if (!seenTmp.Contains(kv.Key)) goneTmp.Add(kv.Key);
            foreach (Enemy e in goneTmp)
            {
                lastHp.Remove(e);
                if (e.type == "scatterling") Emit(e.x, e.y - 26, 10, U.hex(0xf1d23a), 70, 0.7, 3, 30, 60);
                else if (e.type == "bad") Emit(e.x, e.y - 26, 10, U.hex(0x2a2a30), 70, 0.7, 3, 30, 60);
                else { Emit(e.x, e.y - 14, 12, Col(150, 140, 170), 60, 0.8, 6, 25, 0); Emit(e.x, e.y - 14, 6, U.hex(0xfff3c4), 110, 0.35, 2, 0, 0); }
            }
            foreach (Bait b in lastBaits)
                if (!g.baits.Contains(b))
                {
                    if (g.fruitSaved > lastSaved) Emit(b.x, b.y, 9, U.hex(0xf6e27a), 60, 0.6, 2.5, 40, 0);
                    else if (g.fruitLost > lastLost) Emit(b.x, b.y, 9, U.hex(0x3a3040), 60, 0.6, 2.5, 40, 0);
                }
            lastBaits.Clear(); lastBaits.AddRange(g.baits);
            lastSaved = g.fruitSaved; lastLost = g.fruitLost;
            foreach (TreeEntity t in g.trees)
            {
                if (t.revived > 1.9 && !reviveBurst.Contains(t))
                {
                    reviveBurst.Add(t);
                    Emit(t.x, t.y - 40, 26, U.hex(0xffe08a), 120, 1.1, 3, 60, 40);
                    Emit(t.x, t.y - 40, 14, U.hex(0x9cf08a), 90, 1.0, 2.5, 50, 30);
                }
                if (t.revived <= 0) reviveBurst.Remove(t);
                if (t.alive && t.flash > 0 && vr() < dt * 6)
                {
                    Particle p = new Particle();
                    p.x = t.x + (vr() - .5) * 50; p.y = t.y - 50 - vr() * 20; p.vx = (vr() - .5) * 20; p.vy = 20;
                    p.life = 1.4; p.max = 1.4; p.size = 3; p.col = vr() < .5 ? U.hex(0xd9772b) : U.hex(0xb5442a); p.g = 10; p.leaf = true;
                    parts.Add(p);
                }
                if (t.alive && t.healGlow > 0 && vr() < dt * 10)
                {
                    Particle p = new Particle();
                    p.x = t.x + (vr() - .5) * 40; p.y = t.y - 20 - vr() * 30; p.vx = 0; p.vy = -30;
                    p.life = 0.9; p.max = 0.9; p.size = 2.5; p.col = U.hex(0x9cf08a); p.g = 0;
                    parts.Add(p);
                }
            }
        }

        public void Render(OrchardGame g, double dt)
        {
            Begin();
            vt += dt;
            if (g.grid == null) { FillRect(0, 0, C.VW, C.VH, U.hex(0x4e7c3c)); End(); return; }
            if (flies.Count == 0) InitFlies();

            DrawGround();
            DrawMud(g);
            foreach (Tuft t in g.tufts) DrawTuft(t.x, t.y);
            Color bsh = Col(0, 0, 0, .25), bc = U.hex(0x5a3f24), bc2 = U.hex(0x6e4e2e);
            foreach (Branch br in g.branches)
            {
                double dx = System.Math.Cos(br.a) * 10, dy = System.Math.Sin(br.a) * 10;
                Line(br.x - dx + 1, br.y - dy + 2, br.x + dx + 1, br.y + dy + 2, 3, bsh);
                Line(br.x - dx, br.y - dy, br.x + dx, br.y + dy, 3, bc);
                Line(br.x, br.y, br.x + System.Math.Cos(br.a + 0.6) * 6, br.y + System.Math.Sin(br.a + 0.6) * 6, 2, bc2);
            }
            DrawHills(g);
            DrawLogs(g);
            Watch(g, dt);

            // depth list
            items.Clear();
            int seq = 0;
            for (int x = 0; x < C.GW; x++)
                for (int y = 0; y < C.GH; y++)
                    if (g.grid[x, y] == C.BUSH) { Pos c = U.cc(x, y); items.Add(MakeItem(c.y, seq++, 1, c.x, 0, null, null)); }
            foreach (Bait b in g.baits) items.Add(MakeItem(b.y, seq++, 7, b.x, 20, null, null));
            foreach (TreeEntity t in g.trees) items.Add(MakeItem(t.y, seq++, 3, 0, 0, t, null));
            foreach (FlyingFruit f in g.flyingFruit) items.Add(MakeItem(f.y, seq++, 2, f.x, 18, null, null));
            foreach (Enemy e in g.enemies) items.Add(MakeItem(e.y, seq++, 4, 0, 0, e, null));
            foreach (FallenBird fb in g.fallenBirds) items.Add(MakeItem(fb.y, seq++, 8, 0, 0, fb, null));
            items.Add(MakeItem(g.pip.y, seq++, 5, 0, 0, g.pip, "pip"));
            items.Add(MakeItem(g.thistle.y, seq++, 5, 0, 0, g.thistle, "thistle"));
            items.Add(MakeItem(g.farmer.y, seq++, 6, 0, 0, g.farmer, null));
            U.StableSort(items, (a, b) => a.y.CompareTo(b.y));

            // stalker detection: red glow + dashed ring
            Color ring = U.rgba(210, 70, 55, .8f), red = Col(220, 60, 45);
            foreach (Enemy e in g.enemies)
            {
                Stalker st = e as Stalker;
                if (st != null && st.sees)
                {
                    Soft(texGlow, red, 0.18, e.x, e.y, st.sight * 0.55, st.sight * 0.45);
                    foreach (Vector4 d in dashes) Line(e.x + d.x, e.y + d.y, e.x + d.z, e.y + d.w, 2, ring);
                }
            }

            // pip's repair tether (dotted line to the tree it is fixing)
            TreeEntity pt = g.pip.target;
            if (pt != null && pt.alive && pt.hp < pt.maxhp - 0.01)
            {
                double d = U.hypot(pt.x - g.pip.x, pt.y - g.pip.y);
                if (d > 30)
                {
                    int n = (int)System.Math.Floor(d / 14);
                    for (int i = 1; i < n; i++)
                    {
                        double u = (double)i / n;
                        E(g.pip.x + (pt.x - g.pip.x) * u, g.pip.y + (pt.y - g.pip.y) * u, 1.8, 1.8, Col(156, 240, 138, 0.25 + 0.35 * System.Math.Max(0, System.Math.Sin(vt * 6 - i))));
                    }
                }
            }

            // shamblers ganging up on a dog: amber lines to the dog, a red ring once 2+ are biting
            Mover[] dogs = { g.pip, g.thistle };
            foreach (Mover dog in dogs)
            {
                int bitingNow = g.PackSize(dog);
                foreach (Enemy e in g.enemies)
                {
                    Shambler s = e as Shambler;
                    if (s == null || s.act != ShamAct.PackDog || s.dogT != dog) continue;
                    double d = U.hypot(dog.x - s.x, dog.y - s.y);
                    int n = (int)System.Math.Floor(d / 10);
                    for (int i = 1; i < n; i++)
                    {
                        double u = (double)i / n;
                        E(s.x + (dog.x - s.x) * u, s.y + (dog.y - s.y) * u, 1.6, 1.6, Col(233, 184, 80, 0.35 + 0.35 * System.Math.Max(0, System.Math.Sin(vt * 8 - i))));
                    }
                }
                if (bitingNow >= 2)
                {
                    double pr = 26 + 3 * System.Math.Sin(vt * 9);
                    StrokeEllipse(dog.x, dog.y - 4, pr, pr * 0.55, 2.5, Col(240, 90, 70, 0.85));
                }
            }

            Color shadowC = Col(0, 0, 0, .25), dark = Col(20, 16, 10, .75), track = Col(255, 255, 255, .12), shine = Col(255, 255, 255, .35);
            foreach (Item it in items)
            {
                if (it.k == 1) DrawBush(it.x, it.y);
                else if (it.k == 7)
                {
                    Soft(texGlow, Col(255, 214, 120), 0.35 + 0.2 * System.Math.Sin(vt * 4 + it.x), it.x, it.y, 18, 12);
                    E(it.x, it.y + 8, 7, 3, shadowC);
                    Spr("apple", it.x, it.y, it.h, false, false);
                }
                else if (it.k == 2)
                {
                    E(it.x, it.y + 14, 6, 2.5, Col(0, 0, 0, .18));
                    Spr("apple", it.x, it.y, it.h, false, false);
                }
                else if (it.k == 3)
                {
                    TreeEntity t = (TreeEntity)it.refObj;
                    E(t.x + 4, t.y + 9, 32, 11, Col(0, 0, 0, .26));
                    if (t.alive)
                    {
                        if (t.revived > 0)
                        {
                            // brought back by the scatterlings: golden bloom + expanding ring
                            double u = 1 - t.revived / 2.0;
                            Soft(texGlow, Col(255, 220, 120), 0.75 * (1 - u), t.x, t.y - 38, 70 + 40 * u, 70 + 40 * u);
                            StrokeEllipse(t.x, t.y + 4, 20 + 70 * u, (20 + 70 * u) * 0.45, 3, Col(255, 230, 140, 0.8 * (1 - u)));
                        }
                        if (t.healGlow > 0) Soft(texGlow, Col(120, 240, 120), 0.55, t.x, t.y - 38, 52, 52);
                        Spr("tree", t.x, t.y + 12, 96, false, true);
                        if (t.flash > 0) Soft(texGlow, Col(255, 90, 60), 0.45, t.x, t.y - 38, 48, 48);
                        double f = t.hp / t.maxhp, w = 46, hx = t.x - w / 2, hy = t.y - 92;
                        Pill(hx - 1.5, hy - 1.5, w + 3, 9, dark);
                        Pill(hx, hy, w, 6, track);
                        if (f > 0) Pill(hx, hy, System.Math.Max(6, w * f), 6, HpCol(f));
                        Pill(hx + 2, hy + 1, System.Math.Max(0, w * f - 4), 1.5, shine);
                    }
                    else
                    {
                        Spr("tree", t.x, t.y + 12, 74, false, true, .4);
                        E(t.x, t.y + 6, 9, 4, U.hex(0x4b3622));
                        E(t.x, t.y + 4, 8, 3.2, U.hex(0x7a5a3a));
                    }
                }
                else if (it.k == 4)
                {
                    Enemy e = (Enemy)it.refObj;
                    Bird bird = e as Bird;
                    if (bird != null)
                    {
                        E(e.x, e.y, 11, 4.5, Col(0, 0, 0, .2));
                        double bob = System.Math.Sin(bird.bob) * 3;
                        if (!bird.bad) Soft(texGlow, Col(255, 230, 120), 0.22, e.x, e.y - bird.flyH + bob - 12, 22, 22);
                        Spr(bird.bad ? "badscatter" : "scatterling", e.x, e.y - bird.flyH + bob, 28, e.dir < 0, true);
                        if (bird.bad) E(e.x + (e.dir < 0 ? -5 : 5), e.y - bird.flyH + bob - 17, 2.2, 2.2, Col(255, 60, 50, .9));
                    }
                    else
                    {
                        double bob = System.Math.Abs(System.Math.Sin(e.walk)) * (e.type == "shambler" ? 4 : 3);
                        Shadow(e.x, e.y, 14);
                        double h = e.type == "stalker" ? 34 : 44;
                        Stalker st = e as Stalker;
                        if (st != null) Soft(texGlow, Col(255, 50, 40), st.sees ? 0.55 : 0.25, e.x + (e.dir < 0 ? -8 : 8), e.y - 20, 12, 10);
                        Spr(e.type, e.x, e.y + 5 - bob, h, e.dir < 0, true);
                        if (e.hp < 6)
                        {
                            double w = 26;
                            Pill(e.x - w / 2 - 1, e.y - h - 5, w + 2, 5, Col(20, 16, 10, .7));
                            Pill(e.x - w / 2, e.y - h - 4, w * System.Math.Max(0, e.hp) / 6, 3, U.hex(0xe0573e));
                        }
                        Shambler sh = e as Shambler;
                        if (sh != null) DrawIntent(sh, e.x, e.y - h - 15, g.showUtility);
                        if (st != null && st.sees)
                        {
                            double by = e.y - h - 12 + System.Math.Sin(vt * 10) * 1.5;
                            E(e.x, by, 7, 7, U.hex(0xe0573e));
                            FillRect(e.x - 1.2, by - 4.5, 2.4, 6, Color.white);
                            E(e.x, by + 3.8, 1.3, 1.3, Color.white);
                        }
                    }
                }
                else if (it.k == 8)
                {
                    FallenBird fb = (FallenBird)it.refObj;
                    Sprite ys;
                    if (img.TryGetValue("scatterling", out ys))
                    {
                        double hh = 26, ww = hh * ys.rect.width / ys.rect.height;
                        double k = 1 - System.Math.Min(1, fb.h / 40);
                        E(fb.x, fb.y + 2, 6 + 6 * k, (6 + 6 * k) * 0.42, Col(0, 0, 0, 0.12 + 0.12 * k));
                        if (fb.landed)
                        {
                            // little feathers around it on the grass
                            E(fb.x - 13, fb.y + 3, 3.2, 1.6, Col(241, 210, 58, .9));
                            E(fb.x + 12, fb.y + 1, 2.8, 1.4, Col(241, 210, 58, .8));
                            E(fb.x + 4, fb.y + 7, 2.4, 1.2, Col(230, 190, 40, .8));
                            Soft(texGlow, Col(255, 220, 110), 0.28, fb.x, fb.y - 6, 26, 18);
                            DrawImageRot(ys, fb.x, fb.y - 8, ww, hh * 0.92, fb.rot, fb.dir < 0, 0.95);
                            // cartoon "dizzy" stars circling above it
                            for (int si = 0; si < 3; si++)
                            {
                                double ang = vt * 3 + si * 2.094;
                                double sx2 = fb.x + System.Math.Cos(ang) * 11, sy2 = fb.y - 24 + System.Math.Sin(ang) * 4;
                                double sz = 2.2 + 0.6 * System.Math.Sin(ang);
                                Color sc2 = Col(255, 236, 140, 0.95);
                                FillRect(sx2 - sz, sy2 - 0.6, sz * 2, 1.2, sc2);
                                FillRect(sx2 - 0.6, sy2 - sz, 1.2, sz * 2, sc2);
                            }
                        }
                        else DrawImageRot(ys, fb.x, fb.y - fb.h - 13, ww, hh, fb.rot, fb.dir < 0, 1);
                    }
                }
                else if (it.k == 5)
                {
                    Mover m = (Mover)it.refObj;
                    int dir = (m is Pip) ? ((Pip)m).dir : ((Thistle)m).dir;
                    Shadow(m.x, m.y, 12);
                    Spr(it.imgName, m.x, m.y + 4, 34, dir < 0, true);
                    Pip pp = m as Pip;
                    double dhp = pp != null ? pp.hp : ((Thistle)m).hp;
                    double flash = pp != null ? pp.hurtFlash : ((Thistle)m).hurtFlash;
                    if (flash > 0) Soft(texGlow, Col(255, 70, 50), 0.6, m.x, m.y - 14, 22, 18);
                    if (dhp < 4.99)
                    {
                        double w = 24;
                        Pill(m.x - w / 2 - 1, m.y - 41, w + 2, 5, Col(20, 16, 10, .75));
                        Pill(m.x - w / 2, m.y - 40, w * System.Math.Max(0, dhp) / 5, 3, HpCol(dhp / 5));
                    }
                    if (pp != null && pp.retreating)
                    {
                        // little blue "help!" drop over pip while he runs to the farmer
                        double by = m.y - 50 + System.Math.Sin(vt * 8) * 1.5;
                        E(m.x, by, 6.5, 6.5, Col(20, 16, 10, .75));
                        E(m.x, by + 1, 3.2, 3.6, U.hex(0x7fb4e6));
                        E(m.x, by - 2.2, 1.6, 2.2, U.hex(0x7fb4e6));
                    }
                    Thistle th = m as Thistle;
                    if (th != null && th.state != ThistleState.Wander)
                    {
                        Color c = th.state == ThistleState.Feed ? U.hex(0xe3b341) : U.hex(0x7fb4e6);
                        E(m.x + 12, m.y - 34, 4, 4, Col(20, 16, 10, .7));
                        E(m.x + 12, m.y - 34, 2.6, 2.6, c);
                    }
                }
                else if (it.k == 6)
                {
                    Farmer f = (Farmer)it.refObj;
                    Shadow(f.x, f.y, 14);
                    double bob = f.moving ? System.Math.Abs(System.Math.Sin(f.walk)) * 3 : 0;
                    Spr("farmer", f.x, f.y + 5 - bob, 46, f.dir < 0, true);
                    DrawScythe(f);
                    if (f.stun > 0)
                    {
                        for (int si = 0; si < 3; si++)
                        {
                            double ang = vt * 4 + si * 2.094;
                            double sx2 = f.x + System.Math.Cos(ang) * 12, sy2 = f.y - 46 + System.Math.Sin(ang) * 4;
                            Color sc2 = Col(255, 236, 140, 0.95);
                            FillRect(sx2 - 2.6, sy2 - 0.6, 5.2, 1.2, sc2);
                            FillRect(sx2 - 0.6, sy2 - 2.6, 1.2, 5.2, sc2);
                        }
                    }
                }
            }

            // particles
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                Particle p = parts[i];
                p.life -= dt;
                if (p.life <= 0) { parts.RemoveAt(i); continue; }
                p.vy += p.g * dt; p.x += p.vx * dt; p.y += p.vy * dt;
                if (p.leaf) p.x += System.Math.Sin(vt * 4 + p.y * 0.1) * 0.6;
                double ga = System.Math.Min(1, p.life / p.max * 1.5);
                E(p.x, p.y, p.size * (p.leaf ? 1.3 : 1), p.size * (p.leaf ? 0.8 : 1), WithA(p.col, ga));
            }

            // drifting ground fog
            Color fogC = Col(200, 215, 235);
            for (int i = 0; i < 10; i++)
            {
                double fx = (vhash(i, 11, 3) * (C.VW + 320) + vt * (5 + vhash(i, 12, 3) * 6)) % (C.VW + 320) - 160;
                double fy = vhash(i, 13, 3) * C.VH;
                Soft(texGlow, fogC, 0.07, fx, fy, 170, 80);
            }

            // night lighting: darkness with a lantern hole around the farmer, vignette, warm glows
            double lx = g.farmer.x, ly = g.farmer.y - 18;
            Soft(texDark, Col(10, 14, 38), 0.55, lx, ly, 1450, 1450);   // big enough to cover the far corner
            Soft(texVign, Col(5, 6, 16), 1, C.VW / 2.0, C.VH / 2.0, C.VW * 0.72, C.VH * 0.8);
            Soft(texGlow, Col(255, 196, 120), 0.16, lx, ly, 130, 130);
            foreach (TreeEntity t in g.trees) if (t.alive) Soft(texGlow, Col(255, 190, 120), 0.06, t.x, t.y - 40, 70, 70);

            // fireflies
            Color ffG = Col(220, 255, 140);
            foreach (Firefly f in flies)
            {
                f.a += (vr() - .5) * dt * 2;
                f.x += System.Math.Cos(f.a) * f.s * dt; f.y += System.Math.Sin(f.a) * f.s * dt;
                if (f.x < 0) f.x += C.VW; if (f.x > C.VW) f.x -= C.VW;
                if (f.y < 0) f.y += C.VH; if (f.y > C.VH) f.y -= C.VH;
                double k = 0.5 + 0.5 * System.Math.Sin(vt * 2.2 + f.p);
                Soft(texGlow, ffG, 0.5 * k, f.x, f.y, 9, 9);
                E(f.x, f.y, 1.3, 1.3, Col(240, 255, 190, 0.4 + 0.6 * k));
            }

            if (g.running && !g.gameOver)
            {
                DrawRain(g, dt);
                boltNext -= dt;
                if (boltNext <= 0) { bolt = 0.45; boltNext = 14 + vr() * 16; }
                if (bolt > 0)
                {
                    bolt -= dt;
                    double a = bolt > 0.3 ? 0.35 : (bolt > 0.2 ? 0.05 : (bolt > 0.1 ? 0.22 : System.Math.Max(0, bolt) * 1.5));
                    FillRect(0, 0, C.VW, C.VH, Col(220, 230, 255, a));
                }
            }

            End();
        }

        // ---- shambler decision display ----
        public static readonly Color[] ACT_COL = {
            new Color(0x8f / 255f, 0xd3 / 255f, 0x6a / 255f, 1),   // RaidTree  green
            new Color(1f, 0x9f / 255f, 0x43 / 255f, 1),            // EatFruit  orange
            new Color(0xe9 / 255f, 0xb8 / 255f, 0x50 / 255f, 1),   // PackDog   amber
            new Color(0xf0 / 255f, 0x6a / 255f, 0x55 / 255f, 1),   // HitFarmer red
            new Color(0x7f / 255f, 0xb4 / 255f, 0xe6 / 255f, 1) }; // Flee      blue

        // a small speech bubble over the shambler showing what it decided, plus (Tab) its desirability bars
        void DrawIntent(Shambler s, double x, double y, bool bars)
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
                E(x, y + 1.6, 3.2, 2.6, c);                     // paw pad
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
            else
            {
                E(x, y + 1, 3.2, 3.6, c);                        // sweat drop
                E(x, y - 2.4, 1.6, 2.2, c);
            }
            if (!bars) return;
            // five bars = how much it wants each option right now (chosen one outlined)
            double bx = x - 13, by = y - 13, bw = 4.2, gap = 1.3, bh = 22;
            FillRect(bx - 2, by - bh - 2, 5 * bw + 4 * gap + 4, bh + 4, Col(20, 16, 10, .78));
            for (int i = 0; i < 5; i++)
            {
                double v = System.Math.Max(0.02, System.Math.Min(1, s.scores[i]));
                double cx = bx + i * (bw + gap);
                FillRect(cx, by - bh, bw, bh, Col(255, 255, 255, .08));
                FillRect(cx, by - bh * v, bw, bh * v, ACT_COL[i]);
                if (i == (int)s.act) FillRect(cx, by + 1, bw, 1.4, Color.white);
            }
        }

        Item MakeItem(double y, int seq, int k, double x, double h, object refObj, string imgName)
        {
            Item it = new Item();
            it.y = y; it.seq = seq; it.k = k; it.x = x; it.h = h; it.refObj = refObj; it.imgName = imgName;
            return it;
        }

        void DrawScythe(Farmer f)
        {
            if (f.swing <= 0) return;
            double dur = 0.32;
            double p = U.clamp(1 - f.swing / dur, 0, 1);
            double bas = System.Math.Atan2(f.faceY, f.faceX);
            double cx = f.x, cy = f.y - 15, R = C.SCYTHE_RANGE * 0.72; // ~3/4 of the old arc
            // soft swoosh trail behind the blade
            double aNow = bas - 0.9 + 1.8 * p;
            for (int j = 0; j < 5; j++)
                Arc(cx, cy, R - 2 - j * 2.5, bas - 0.9, aNow, 2.5, U.rgba(235, 240, 230, 0.10 - j * 0.015));
            for (int g = 0; g < 3; g++)
            {
                double pp = U.clamp(p - g * 0.12, 0, 1);
                double a2 = bas - 0.9 + 1.8 * pp;
                Color col = g == 0 ? U.rgba(238, 240, 232, .95) : U.rgba(220, 225, 215, 0.28 - g * 0.11);
                Arc(cx, cy, R, a2 - 0.45, a2 + 0.08, g == 0 ? 5 : 3, col);
            }
            double a = bas - 0.9 + 1.8 * p;
            Soft(texGlow, U.hex(0xffffff), 0.55, cx + System.Math.Cos(a) * R, cy + System.Math.Sin(a) * R, 11, 11);
            FillEllipse(cx + System.Math.Cos(a) * R, cy + System.Math.Sin(a) * R, 4, 4, U.hex(0xcfd4cc));
        }

        void DrawRain(OrchardGame g, double dt)
        {
            Color c = U.rgba(190, 212, 240, .42f);
            foreach (RainDrop r in g.rain)
            {
                r.y += r.s * dt;
                r.x += r.s * 0.22 * dt;
                if (r.y > C.VH) { r.y = -10; r.x = U.R() * C.VW; }
                Line(r.x, r.y, r.x - 2.5, r.y - r.l, 1, c);
            }
            for (int i = 0; i < 6; i++)
                if (vr() < dt * 30)
                {
                    Particle p = new Particle();
                    p.x = vr() * C.VW; p.y = vr() * C.VH; p.life = 0.25; p.max = 0.25; p.size = 1; p.col = Col(200, 220, 245, .7); p.splash = true;
                    parts.Add(p);
                }
            foreach (Particle p in parts)
                if (p.splash)
                {
                    double u = 1 - p.life / p.max;
                    StrokeEllipse(p.x, p.y, 1 + u * 5, 0.5 + u * 2, 1, Col(200, 220, 245, 0.6 * (1 - u)));
                }
        }
    }

    // procedural textures for the canvas shapes
    public static class Shapes
    {
        // white anti-aliased disc, scaled per draw for circles/ellipses
        public static Sprite Circle()
        {
            int N = 256;
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, true);
            Color32[] px = new Color32[N * N];
            float rr = N / 2f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - rr, dy = y + 0.5f - rr;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rr - d);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
            t.SetPixels32(px);
            t.Apply(true);
            t.filterMode = FilterMode.Trilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            Sprite cs = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N, 0, SpriteMeshType.FullRect);
            cs.name = "circle";
            return cs;
        }

        static float SStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        // white soft disc, alpha from distance: 0 glow, 1 darkness with a clear centre, 2 vignette
        public static Sprite Radial(int N, int mode)
        {
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + .5f) / N * 2 - 1, dy = (y + .5f) / N * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (mode == 0) { float q = Mathf.Max(0, 1 - d); a = q * q; }
                    else if (mode == 1) a = SStep(0.05f, 0.30f, d);
                    else a = SStep(0.55f, 1.25f, d) * 0.85f;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255));
                }
            t.SetPixels32(px);
            t.Apply(false);
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            Sprite rs = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N, 0, SpriteMeshType.FullRect);
            rs.name = "radial" + mode;
            return rs;
        }

        public static Sprite Pixel()
        {
            Texture2D t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px);
            t.Apply();
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4, 0, SpriteMeshType.FullRect);
        }

        // one grass tuft: 3 strokes (width 2) from the base, drawn as a single path at 50% alpha
        public static Sprite Tuft()
        {
            int SS = 8;                       // texels per canvas pixel
            float minX = -4, maxX = 4, minY = -9, maxY = 1;
            int W = (int)((maxX - minX) * SS), H = (int)((maxY - minY) * SS);
            Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, true);
            Color32[] px = new Color32[W * H];
            Vector2[] ends = { new Vector2(-3, -7), new Vector2(0, -8), new Vector2(3, -7) };
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                {
                    int hits = 0;
                    for (int sj = 0; sj < 2; sj++)
                        for (int si = 0; si < 2; si++)
                        {
                            float cxp = minX + (i + 0.25f + si * 0.5f) / SS;
                            float cyp = minY + (j + 0.25f + sj * 0.5f) / SS;   // j counts down from the top
                            bool inside = false;
                            foreach (Vector2 e in ends)
                                if (OnSegment(new Vector2(cxp, cyp), Vector2.zero, e, 1f)) { inside = true; break; }
                            if (inside) hits++;
                        }
                    byte a = (byte)Mathf.RoundToInt(hits / 4f * 0.5f * 255);
                    int row = H - 1 - j;   // texture rows go bottom-up
                    px[row * W + i] = new Color32(40, 80, 30, a);
                }
            t.SetPixels32(px);
            t.Apply(true);
            t.filterMode = FilterMode.Trilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            // pivot at the tuft base (canvas 0,0)
            Vector2 pivot = new Vector2((0 - minX) / (maxX - minX), (maxY - 0) / (maxY - minY));
            return Sprite.Create(t, new Rect(0, 0, W, H), pivot, SS, 0, SpriteMeshType.FullRect);
        }

        // inside a stroke of half-width hw with butt caps
        static bool OnSegment(Vector2 p, Vector2 a, Vector2 b, float hw)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float u = Vector2.Dot(p - a, ab) / len2;
            if (u < 0 || u > 1) return false;
            Vector2 proj = a + ab * u;
            return (p - proj).sqrMagnitude <= hw * hw;
        }
    }
}
