using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace Orchard
{
    // the page around the board. exactly 16:9: a slim sidebar (15%) + the landscape board (85%).
    // sidebar: header, night track, stat cards (incl. tree-revive progress), live ai agents, event log.
    // board area: start / win / lose card and the sunrise -> sunset scene between nights.
    public class OrchardUI
    {
        OrchardGame g;
        Font sans, mono;
        CanvasScaler scaler;
        RectTransform wrap, shadowRT, sideBg, leftBg, overlay, overlayContent, transitionRT, timeFill, logLayer, card, reviveFill;
        Text nightText, seasonText, timeText;
        Text[] statVal = new Text[4];
        Text reviveLab;
        Image[] pips = new Image[13];
        RectTransform pipGlow;
        Image pipGlowImg;
        List<Text> logTexts = new List<Text>();
        const float H = 704;
        bool layoutDirty = true;
        int lastNight = -1;

        class Chip { public RectTransform bg; public Image bgImg; public Text label; public string cur = ""; public float right, y; }
        Chip[] chips = new Chip[7];

        RectTransform btnFace;
        float btnY;
        bool btnPressed;
        System.Action btnAction;
        bool overlayVisible;

        // 188 + 1064 = 1252 x 704 = 16:9, and 188 / 1252 = 15% sidebar, 85% game
        const float SIDE_W = 188, LEFT_W = 1064, PAD = 12, CW = 164;
        const float N_SANS = 1.161f;
        const float NIGHT_Y = 50, SEASON_Y = 64, PIPS_Y = 84, TIME_Y = 98, BAR_Y = 114, CARD_Y = 128, CARD_H = 46, CARD_GAP = 6;
        const float AGENTS_Y = 236, ROW_Y0 = 252, ROW_H = 31, ROW_STEP = 33;
        const float LOGWRAP_Y = 490, LOGTTL_Y = 498, LOG_Y = 514, LOG_LINE = 13;
        const int LOG_CHARS = 27, LOG_MAX_LINES = 14;

        static readonly Color INK = new Color(0xf0 / 255f, 0xe9 / 255f, 0xdc / 255f, 1);
        static readonly Color MUTED = new Color(0xad / 255f, 0xa4 / 255f, 0x90 / 255f, 1);
        static readonly Color AMBER = new Color(0xe9 / 255f, 0xb8 / 255f, 0x50 / 255f, 1);

        static Dictionary<string, Sprite> shapeCache = new Dictionary<string, Sprite>();
        Dictionary<string, float> widthCache = new Dictionary<string, float>();

        public OrchardUI(OrchardGame g, RenderTexture boardRT)
        {
            this.g = g;
            sans = LoadFont(new string[] { "Trebuchet MS", "Segoe UI", "Helvetica Neue", "Arial" });
            mono = LoadFont(new string[] { "Courier New", "Courier", "Menlo", "Consolas" });
            Build(boardRT);
        }

        static Font LoadFont(string[] names)
        {
            HashSet<string> installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            List<string> have = new List<string>();
            foreach (string n in names) if (installed.Contains(n)) have.Add(n);
            if (have.Count > 0) return Font.CreateDynamicFontFromOSFont(have.ToArray(), 32);
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ---------------- building ----------------
        void Build(RenderTexture boardRT)
        {
            GameObject cgo = new GameObject("Orchard UI");
            Canvas cv = cgo.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 100;
            scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(SIDE_W + LEFT_W, H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100;
            Transform canvasT = cgo.transform;

            RectTransform bg = NewRect("body", canvasT, 0, 0, 0, 0);
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one; bg.pivot = new Vector2(.5f, .5f);
            bg.offsetMin = Vector2.zero; bg.offsetMax = Vector2.zero;
            AddImage(bg, U.hex(0x14110d), null, false, 1);

            shadowRT = NewRect("shadow", canvasT, 0, 0, 0, 0);
            shadowRT.anchorMin = shadowRT.anchorMax = shadowRT.pivot = new Vector2(.5f, .5f);

            wrap = NewRect("wrap", canvasT, 0, 0, 0, 0);
            wrap.anchorMin = wrap.anchorMax = wrap.pivot = new Vector2(.5f, .5f);
            AddImage(wrap, Color.white, null, false, 1);
            Mask m = wrap.gameObject.AddComponent<Mask>();
            m.showMaskGraphic = false;

            // ---- sidebar ----
            sideBg = NewRect("side", wrap, 0, 0, SIDE_W, H);
            AddImage(sideBg, Color.white, Gradient(U.hex(0x2c251d), U.hex(0x1f1a15), true), false, 1);
            AddImage(NewRect("sideedge", wrap, SIDE_W - 1, 0, 1, H), U.hex(0x3a3228), null, false, 1);

            Sprite disc = Shapes.Circle(), glow = Shapes.Radial(128, 0);
            AddImage(NewRect("badgeglow", wrap, PAD - 9, 6, 44, 44), U.rgba(240, 228, 196, .22f), glow, false, 1);
            AddImage(NewRect("badge", wrap, PAD, 15, 26, 26), U.hex(0xf0e4c4), disc, false, 1);
            AddImage(NewRect("cr1", wrap, PAD + 6, 21, 7, 6), U.rgba(190, 175, 140, .6f), disc, false, 1);
            AddImage(NewRect("cr2", wrap, PAD + 15, 30, 5, 4), U.rgba(190, 175, 140, .6f), disc, false, 1);
            MakeText(wrap, "title1", PAD + 33, 11, 150, 17, 14, U.hex(0xf4ecdd), true, sans, TextAnchor.MiddleLeft, "Orchard of the");
            MakeText(wrap, "title2", PAD + 33, 27, 150, 17, 14, U.hex(0xf4ecdd), true, sans, TextAnchor.MiddleLeft, "Last Season");

            nightText = MakeSpaced(wrap, PAD, NIGHT_Y, "NIGHT 1 / 13", 10, AMBER, true, 1.1f);
            seasonText = MakeText(wrap, "season", PAD, SEASON_Y, CW, 13, 10, MUTED, false, sans, TextAnchor.MiddleLeft, "Early Autumn");

            pipGlow = NewRect("pipglow", wrap, 0, PIPS_Y - 7, 22, 22);
            pipGlowImg = AddImage(pipGlow, AMBER, glow, false, 1);
            for (int i = 0; i < 13; i++)
            {
                float cx = PAD + 4 + i * (CW - 8) / 12f;
                pips[i] = AddImage(NewRect("pip", wrap, cx - 4, PIPS_Y, 8, 8), U.hex(0x3a3228), disc, false, 1);
            }

            MakeText(wrap, "timelab", PAD, TIME_Y, 100, 13, 9.5f, MUTED, false, sans, TextAnchor.MiddleLeft, "Dawn in");
            timeText = MakeText(wrap, "time", PAD, TIME_Y, CW, 13, 10.5f, INK, true, sans, TextAnchor.MiddleRight, "1:00");
            RectTransform bar = NewRect("timebar", wrap, PAD, BAR_Y, CW, 6);
            AddImage(bar, U.hex(0x3a3228), Rounded(3), true, 4);
            bar.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            timeFill = NewRect("timefill", bar, 0, 0, CW, 6);
            AddImage(timeFill, Color.white, Gradient(U.hex(0xc76b3f), U.hex(0xe9b850), false), false, 1);

            // 2 x 2 stat cards: saved, lost, trees, tree revive
            string[] icons = { "icon_apple", "icon_heart", "icon_tree", "scatterling" };
            string[] labs = { "fruit saved", "lost to birds", "trees left", "to revive a tree" };
            string[] init = { "0 / 0", "0", "6 / 6", "0 / " + C.REVIVE_COST };
            Color[] vcol = { U.hex(0x8fd36a), U.hex(0xe8806a), INK, U.hex(0xf1d23a) };
            float cw2 = (CW - CARD_GAP) / 2f;
            for (int i = 0; i < 4; i++)
            {
                float x = PAD + (i % 2) * (cw2 + CARD_GAP), y = CARD_Y + (i / 2) * (CARD_H + CARD_GAP);
                Color tint = i == 3 ? U.rgba(241, 210, 58, .07f) : U.rgba(255, 255, 255, .045f);
                AddImage(NewRect("card", wrap, x, y, cw2, CARD_H), tint, Rounded(8), true, 4);
                AddImage(NewRect("cardring", wrap, x, y, cw2, CARD_H), i == 3 ? U.rgba(241, 210, 58, .22f) : U.rgba(255, 255, 255, .07f), Ring(8, 1), true, 4);
                Sprite ic;
                if (g.img.TryGetValue(icons[i], out ic))
                {
                    float ih = i == 3 ? 15 : 12, iw = ih * ic.rect.width / ic.rect.height;
                    AddImage(NewRect("ic", wrap, x + 7, y + 6 + (i == 3 ? -1 : 0), iw, ih), Color.white, ic, false, 1);
                }
                statVal[i] = MakeText(wrap, "val", x + 7, y + 19, cw2 - 9, 17, i == 3 ? 12.5f : 14.5f, vcol[i], true, sans, TextAnchor.MiddleLeft, init[i]);
                Text lb = MakeText(wrap, "lab", x + 7, y + 33, cw2 - 8, 10, 8, MUTED, false, sans, TextAnchor.MiddleLeft, labs[i]);
                if (i == 3)
                {
                    reviveLab = lb;
                    RectTransform rb = NewRect("revbar", wrap, x + cw2 - 30, y + 9, 23, 4);
                    AddImage(rb, U.rgba(255, 255, 255, .10f), Rounded(2), true, 4);
                    rb.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                    reviveFill = NewRect("revfill", rb, 0, 0, 0, 4);
                    AddImage(reviveFill, U.hex(0xf1d23a), null, false, 1);
                }
            }

            MakeSpaced(wrap, PAD, AGENTS_Y, "AI AGENTS", 9, U.hex(0x8fa878), false, 1.4f);
            AddImage(NewRect("livedot", wrap, PAD + CW - 29, AGENTS_Y + 4, 5, 5), U.hex(0x7ec850), disc, false, 1);
            MakeText(wrap, "live", PAD, AGENTS_Y, CW, 13, 8.5f, U.hex(0x7ec850), true, sans, TextAnchor.MiddleRight, "LIVE");
            BuildAgents();

            AddImage(NewRect("logwrap", wrap, 0, LOGWRAP_Y, SIDE_W - 1, H - LOGWRAP_Y), U.hex(0x14120e), null, false, 1);
            AddImage(NewRect("logborder", wrap, 0, LOGWRAP_Y, SIDE_W - 1, 2), U.hex(0x3a3228), null, false, 1);
            MakeSpaced(wrap, PAD, LOGTTL_Y, "AI EVENT LOG", 9, U.hex(0x8fa878), false, 1.4f);
            logLayer = NewRect("loglines", wrap, 0, 0, SIDE_W, 10);

            // ---- board ----
            leftBg = NewRect("left", wrap, SIDE_W, 0, LEFT_W, H);
            AddImage(leftBg, U.hex(0x4e7c3c), null, false, 1);
            RectTransform boardR = NewRect("board", wrap, SIDE_W, 0, LEFT_W, H);
            RawImage raw = boardR.gameObject.AddComponent<RawImage>();
            raw.texture = boardRT;
            raw.raycastTarget = false;
            boardRaw = raw;

            // sunrise / sunset scene between nights
            BuildTabHint();
            transitionRT = NewRect("transition", wrap, SIDE_W, 0, LEFT_W, H);
            transitionRT.gameObject.AddComponent<RectMask2D>();   // keep the scene inside the board
            BuildScene();
            transitionRT.gameObject.SetActive(false);

            overlay = NewRect("overlay", wrap, SIDE_W, 0, LEFT_W, H);
            AddImage(overlay, U.rgba(12, 9, 6, .72f), null, false, 1);
            card = NewRect("card", overlay, 0, 0, 10, 10);
            overlayContent = NewRect("content", overlay, 0, 0, 10, 10);
            overlay.gameObject.SetActive(false);

            SetNight(1);
            Relayout();
        }

        // ---------------- sunrise -> day -> sunset scene ----------------
        Image skyTop, skyHor, sun, sunGlow, groundImg, vign;
        Image[] stars = new Image[34], clouds = new Image[9], hillsBack = new Image[3], hillsFront = new Image[2], sceneTrees = new Image[5];
        Color[] starBase = new Color[34];
        Text scCap, scTitle, scSub, scTitleSh, scSubSh;
        float[] cloudX = new float[9];
        const float HORIZON = 500;

        void BuildScene()
        {
            Sprite disc = Shapes.Circle(), glow = Shapes.Radial(128, 0);
            skyTop = AddImage(NewRect("skytop", transitionRT, 0, 0, LEFT_W, H), Color.black, null, false, 1);
            skyHor = AddImage(NewRect("skyhor", transitionRT, 0, 0, LEFT_W, HORIZON + 60), Color.white, Gradient(new Color(1, 1, 1, 0), Color.white, true), false, 1);
            for (int i = 0; i < stars.Length; i++)
            {
                float sx = (float)(Hash(i, 1) * LEFT_W), sy = (float)(Hash(i, 2) * (HORIZON - 120)), sz = 1.6f + (float)Hash(i, 3) * 1.8f;
                stars[i] = AddImage(NewRect("star", transitionRT, sx, sy, sz, sz), Color.white, disc, false, 1);
                starBase[i] = new Color(1, 0.97f, 0.88f, 0.5f + (float)Hash(i, 4) * 0.5f);
            }
            sunGlow = AddImage(NewRect("sunglow", transitionRT, 0, 0, 300, 300), Color.white, glow, false, 1);
            sun = AddImage(NewRect("sun", transitionRT, 0, 0, 68, 68), Color.white, disc, false, 1);
            float[,] cl = { { 150, 140 }, { 520, 96 }, { 870, 170 } };
            for (int c = 0; c < 3; c++)
                for (int k = 0; k < 3; k++)
                {
                    float w = k == 0 ? 96 : (k == 1 ? 70 : 62), h = k == 0 ? 44 : (k == 1 ? 40 : 30);
                    float ox = k == 0 ? 0 : (k == 1 ? -38 : 40), oy = k == 0 ? 0 : (k == 1 ? 6 : 8);
                    cloudX[c * 3 + k] = cl[c, 0] + ox - w / 2;
                    clouds[c * 3 + k] = AddImage(NewRect("cloud", transitionRT, cloudX[c * 3 + k], cl[c, 1] + oy - h / 2, w, h), Color.white, disc, false, 1);
                }
            float[,] hb = { { 180, 560, 330, 150 }, { 600, 580, 380, 160 }, { 980, 556, 320, 140 } };
            for (int i = 0; i < 3; i++)
                hillsBack[i] = AddImage(NewRect("hillb", transitionRT, hb[i, 0] - hb[i, 2], hb[i, 1] - hb[i, 3], hb[i, 2] * 2, hb[i, 3] * 2), Color.white, disc, false, 1);
            float[,] hf = { { 280, 700, 520, 190 }, { 860, 706, 560, 200 } };
            for (int i = 0; i < 2; i++)
                hillsFront[i] = AddImage(NewRect("hillf", transitionRT, hf[i, 0] - hf[i, 2], hf[i, 1] - hf[i, 3], hf[i, 2] * 2, hf[i, 3] * 2), Color.white, disc, false, 1);
            groundImg = AddImage(NewRect("ground", transitionRT, 0, 650, LEFT_W, H - 650), Color.white, null, false, 1);
            Sprite ts;
            if (g.img.TryGetValue("tree", out ts))
            {
                float[,] tp = { { 150, 556, 78 }, { 300, 534, 92 }, { 560, 548, 70 }, { 780, 528, 96 }, { 950, 552, 80 } };
                for (int i = 0; i < 5; i++)
                {
                    float th = tp[i, 2], tw = th * ts.rect.width / ts.rect.height;
                    sceneTrees[i] = AddImage(NewRect("stree", transitionRT, tp[i, 0] - tw / 2, tp[i, 1] - th, tw, th), Color.white, ts, false, 1);
                }
            }
            vign = AddImage(NewRect("vign", transitionRT, -LEFT_W * 0.25f, -H * 0.4f, LEFT_W * 1.5f, H * 1.8f), Color.black, Shapes.Radial(256, 2), false, 1);
            scCap = MakeText(transitionRT, "cap", 0, 104, LEFT_W, 18, 13, Color.white, true, sans, TextAnchor.MiddleCenter, "");
            scTitleSh = MakeText(transitionRT, "titlesh", 2, 128, LEFT_W, 56, 46, Color.black, true, sans, TextAnchor.MiddleCenter, "");
            scTitle = MakeText(transitionRT, "title", 0, 126, LEFT_W, 56, 46, Color.white, true, sans, TextAnchor.MiddleCenter, "");
            scSubSh = MakeText(transitionRT, "subsh", 1, 189, LEFT_W, 24, 18, Color.black, false, sans, TextAnchor.MiddleCenter, "");
            scSub = MakeText(transitionRT, "sub", 0, 188, LEFT_W, 24, 18, Color.white, false, sans, TextAnchor.MiddleCenter, "");
        }

        static double Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9e3779b9u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return h / 4294967296.0;
            }
        }

        // sky colour keyframes: night, dawn, noon, dusk, night
        static readonly Color[] TOP = { new Color(.05f, .07f, .19f), new Color(.24f, .30f, .58f), new Color(.45f, .72f, .93f), new Color(.30f, .22f, .48f), new Color(.05f, .07f, .19f) };
        static readonly Color[] HOR = { new Color(.16f, .18f, .36f), new Color(.98f, .62f, .44f), new Color(1f, .90f, .68f), new Color(.96f, .50f, .34f), new Color(.16f, .18f, .36f) };

        static Color Key(Color[] k, float p)
        {
            float f = Mathf.Clamp01(p) * (k.Length - 1);
            int i = Mathf.Min((int)f, k.Length - 2);
            return Color.Lerp(k[i], k[i + 1], f - i);
        }

        static Color A(Color c, float a) { return new Color(c.r, c.g, c.b, c.a * a); }

        void UpdateScene(float p, float fade, string msg, string sub)
        {
            float day = Mathf.Sin(p * Mathf.PI);             // 0 at the night ends, 1 at noon
            float night = Mathf.Pow(1 - day, 3);
            Color top = Key(TOP, p), hor = Key(HOR, p);
            skyTop.color = A(top, fade);
            skyHor.color = A(hor, fade);
            for (int i = 0; i < stars.Length; i++)
            {
                float tw = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3 + i);
                stars[i].color = A(starBase[i], fade * night * tw);
            }
            // the sun climbs from the left horizon, peaks, and sets on the right
            float sx = LEFT_W * (0.10f + 0.80f * p), sy = HORIZON + 30 - day * 340;
            Color sc = Color.Lerp(new Color(1f, .52f, .28f), new Color(1f, .93f, .62f), day);
            sun.rectTransform.anchoredPosition = new Vector2(sx - 34, -(sy - 34));
            sun.color = A(sc, fade);
            sunGlow.rectTransform.anchoredPosition = new Vector2(sx - 150, -(sy - 150));
            sunGlow.color = A(new Color(sc.r, sc.g * 0.95f, sc.b * 0.8f, 0.55f + 0.2f * (1 - day)), fade);
            // clouds drift and catch the light
            Color cloud = Color.Lerp(Color.Lerp(new Color(.20f, .22f, .40f), hor, 0.55f), Color.white, day * 0.85f);
            for (int i = 0; i < clouds.Length; i++)
            {
                RectTransform rt = clouds[i].rectTransform;
                rt.anchoredPosition = new Vector2(cloudX[i] + p * 60, rt.anchoredPosition.y);
                clouds[i].color = A(new Color(cloud.r, cloud.g, cloud.b, 0.92f), fade);
            }
            Color hillB = Color.Lerp(new Color(.10f, .12f, .24f), new Color(.42f, .62f, .38f), day);
            Color hillF = Color.Lerp(new Color(.06f, .08f, .16f), new Color(.31f, .50f, .26f), day);
            for (int i = 0; i < hillsBack.Length; i++) hillsBack[i].color = A(Color.Lerp(hillB, hor, 0.18f * (1 - day)), fade);
            for (int i = 0; i < hillsFront.Length; i++) hillsFront[i].color = A(hillF, fade);
            groundImg.color = A(hillF, fade);
            Color treeTint = Color.Lerp(new Color(.10f, .10f, .18f), Color.white, Mathf.Clamp01(day * 1.15f - 0.05f));
            foreach (Image t in sceneTrees) if (t != null) t.color = A(treeTint, fade);
            vign.color = A(new Color(0, 0, 0, 0.55f), fade);

            // words: sunrise first (the night is survived), then sunset into the next night
            bool rising = p < 0.5f;
            float tf = Mathf.Clamp01(Mathf.Abs(p - 0.5f) * 8f);     // quick cross-fade at noon
            Color ink = new Color(1f, .97f, .90f, fade * tf);
            Color shade = new Color(0, 0, 0, 0.35f * fade * tf);
            SetT(scCap, rising ? "S U N R I S E" : "S U N S E T", new Color(1f, .86f, .55f, fade * tf));
            string title = rising ? "Dawn" : msg;
            string st = rising ? sub : "The rain returns. Get ready!";
            SetT(scTitle, title, ink); SetT(scTitleSh, title, shade);
            SetT(scSub, st, new Color(1f, .95f, .85f, 0.92f * fade * tf)); SetT(scSubSh, st, shade);
        }

        static void SetT(Text t, string s, Color c)
        {
            if (t.text != s) t.text = s;
            t.color = c;
        }

        // letter-spaced label built glyph by glyph like css letter-spacing
        Text MakeSpaced(Transform parent, float x, float y, string s, float px, Color col, bool bold, float spacing)
        {
            RectTransform holder = NewRect("spaced", parent, x, y, 160, 13);
            float lx = 0;
            Text first = null;
            foreach (char ch in s)
            {
                string c = ch.ToString();
                Text t = MakeText(holder, "ch", lx, 0, 16, 13, px, col, bold, sans, TextAnchor.MiddleLeft, c);
                if (first == null) first = t;
                lx += Measure(c, sans, px, bold) + spacing;
            }
            spacedHolders[first] = holder;
            spacedStyle[first] = new object[] { px, col, bold, spacing };
            return first;
        }
        Dictionary<Text, RectTransform> spacedHolders = new Dictionary<Text, RectTransform>();
        Dictionary<Text, object[]> spacedStyle = new Dictionary<Text, object[]>();

        void SetSpaced(Text key, string s)
        {
            RectTransform holder = spacedHolders[key];
            object[] st = spacedStyle[key];
            float px = (float)st[0]; Color col = (Color)st[1]; bool bold = (bool)st[2]; float spacing = (float)st[3];
            for (int i = holder.childCount - 1; i >= 1; i--) Object.Destroy(holder.GetChild(i).gameObject);
            float lx = 0;
            for (int i = 0; i < s.Length; i++)
            {
                string c = s[i].ToString();
                if (i == 0) key.text = c;
                else MakeText(holder, "ch", lx, 0, 16, 13, px, col, bold, sans, TextAnchor.MiddleLeft, c);
                lx += Measure(c, sans, px, bold) + spacing;
            }
        }

        static readonly string[][] AGENTS = {
            new string[] { "farmer", "Farmer", "YOU  ·  SCYTHE", "d98a3d" },
            new string[] { "pip", "Pip", "BEHAVIOUR TREE", "7ec850" },
            new string[] { "thistle", "Thistle", "STATE MACHINE", "e3b341" },
            new string[] { "shambler", "Shambler", "UTILITY AI  ·  A*", "b9a6d9" },
            new string[] { "stalker", "Stalker", "PERCEPTION", "e0796a" },
            new string[] { "scatterling", "Scatterlings", "FLOCKING", "f1d23a" },
            new string[] { "badscatter", "Bad birds", "FLOCKING  ·  HUNT", "b4a6c6" } };

        void BuildAgents()
        {
            for (int i = 0; i < AGENTS.Length; i++)
            {
                string[] a = AGENTS[i];
                float ry = ROW_Y0 + i * ROW_STEP;
                Color tc = U.hex(System.Convert.ToInt32(a[3], 16));
                if (i == 0)
                {
                    AddImage(NewRect("selbg", wrap, PAD - 4, ry - 1, CW + 8, ROW_H + 2), U.rgba(217, 138, 61, .10f), Rounded(7), true, 4);
                    AddImage(NewRect("selborder", wrap, PAD - 4, ry - 1, CW + 8, ROW_H + 2), U.rgba(217, 138, 61, .75f), Ring(7, 1.25f), true, 4);
                }
                RectTransform thumb = NewRect("thumb", wrap, PAD, ry + 2.5f, 26, 26);
                AddImage(thumb, U.rgba(255, 255, 255, .05f), Rounded(6), true, 4);
                thumb.gameObject.AddComponent<RectMask2D>();
                Sprite s;
                if (g.img.TryGetValue(a[0], out s))
                {
                    float h = 23, w = h * s.rect.width / s.rect.height;
                    if (w > 25) { w = 25; h = w * s.rect.height / s.rect.width; }
                    AddImage(NewRect("img", thumb, 13 - w / 2, 25 - h, w, h), Color.white, s, false, 1);
                }
                float tx = PAD + 32;
                MakeText(wrap, "name", tx, ry + 2, 100, 14, 11, U.hex(0xf4ecdd), true, sans, TextAnchor.MiddleLeft, a[1]);
                MakeText(wrap, "tag", tx, ry + 16, CW - 32, 12, 7.5f, tc, true, sans, TextAnchor.MiddleLeft, a[2]);

                Chip c = new Chip();
                c.right = PAD + CW; c.y = ry + 3;
                c.bg = NewRect("chip", wrap, c.right - 36, c.y, 36, 12);
                c.bgImg = AddImage(c.bg, U.rgba(255, 255, 255, .08f), Rounded(6), true, 4);
                c.label = MakeText(c.bg, "chipt", 0, 0, 36, 12, 7.5f, INK, true, sans, TextAnchor.MiddleCenter, "");
                chips[i] = c;
            }
        }

        void SetChip(int i, string text, Color col)
        {
            Chip c = chips[i];
            if (c.cur == text) return;
            c.cur = text;
            float w = Measure(text, sans, 7.5f, true) + 10;
            c.bg.anchoredPosition = new Vector2(c.right - w, -c.y);
            c.bg.sizeDelta = new Vector2(w, 12);
            c.label.rectTransform.sizeDelta = new Vector2(w, 12);
            c.label.text = text;
            c.label.color = col;
            c.bgImg.color = new Color(col.r, col.g, col.b, .16f);
        }

        // reads the game each frame (never changes it) to show what every ai is doing right now
        void UpdateLive()
        {
            if (g.farmer == null) return;
            Color you = U.hex(0xd98a3d);
            if (g.farmer.swing > 0) SetChip(0, "Swing", you);
            else if (g.farmer.moving) SetChip(0, "Moving", you);
            else SetChip(0, "Idle", MUTED);

            if (g.pip != null && g.pip.retreating) SetChip(1, "Hurt!", U.hex(0x7fb4e6));
            else if (g.pip != null && g.pip.target != null && g.pip.target.alive) SetChip(1, "Repairing", U.hex(0x7ec850)); else SetChip(1, "Following", MUTED);

            if (g.thistle != null)
            {
                ThistleState st = g.thistle.state;
                SetChip(2, st.ToString(), st == ThistleState.Feed ? U.hex(0xe3b341) : (st == ThistleState.Flee ? U.hex(0x7fb4e6) : U.hex(0x9cc98a)));
            }

            int sh = 0, sk = 0, yb = 0, bb = 0; bool sees = false, searching = false;
            foreach (Enemy e in g.enemies)
            {
                if (e.type == "shambler") sh++;
                else if (e.type == "stalker") { sk++; if (((Stalker)e).sees) sees = true; if (((Stalker)e).Searching) searching = true; }
                else if (e.type == "scatterling") yb++;
                else if (e.type == "bad") bb++;
            }
            // the most interesting thing the shamblers are deciding right now
            int pack = 0, flee = 0, eat = 0, hitf = 0;
            foreach (Enemy e in g.enemies)
            {
                Shambler s = e as Shambler;
                if (s == null) continue;
                if (s.act == ShamAct.PackDog) pack++;
                else if (s.act == ShamAct.Flee) flee++;
                else if (s.act == ShamAct.EatFruit) eat++;
                else if (s.act == ShamAct.HitFarmer) hitf++;
            }
            if (sh == 0) SetChip(3, "None", MUTED);
            else if (pack >= 2) SetChip(3, "Pack of " + pack + "!", GameRenderer.ACT_COL[2]);
            else if (hitf > 0) SetChip(3, "Hitting you!", GameRenderer.ACT_COL[3]);
            else if (flee > 0) SetChip(3, flee + " fleeing", GameRenderer.ACT_COL[4]);
            else if (eat > 0) SetChip(3, eat + " eating", GameRenderer.ACT_COL[1]);
            else SetChip(3, sh + " raiding", U.hex(0xb9a6d9));
            UpdateTabHint();
            if (sees) SetChip(4, "Sees you!", U.hex(0xf06a55));
            else if (searching) SetChip(4, "Searching", U.hex(0xe3b341));
            else SetChip(4, sk > 0 ? sk + " out" : "None", sk > 0 ? U.hex(0xe0796a) : MUTED);
            SetChip(5, yb > 0 ? yb + " alive" : "All lost", yb > 0 ? U.hex(0xf1d23a) : U.hex(0xe8806a));
            SetChip(6, bb > 0 ? bb + " out" : "None", bb > 0 ? U.hex(0xc7b4d6) : MUTED);

            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
            pipGlowImg.color = new Color(AMBER.r, AMBER.g, AMBER.b, 0.35f + 0.35f * k);

            double left = System.Math.Max(0, C.NIGHT_LEN - g.nightTime);
            int secs = (int)System.Math.Ceiling(left);
            string ts = (secs / 60) + ":" + (secs % 60).ToString("00");
            if (timeText.text != ts) timeText.text = ts;

            // tree revive progress (fruit saved by scatterlings)
            int bank = g.ReviveBank;
            bool waiting = bank >= C.REVIVE_COST;
            string rv = waiting ? "Ready!" : bank + " / " + C.REVIVE_COST;
            if (statVal[3].text != rv) statVal[3].text = rv;
            string rl = waiting ? "revives next fallen tree" : (g.revives > 0 ? "revived " + g.revives + "  ·  next tree" : "to revive a tree");
            if (reviveLab.text != rl) reviveLab.text = rl;
            reviveFill.sizeDelta = new Vector2(23f * Mathf.Clamp01(bank / (float)C.REVIVE_COST), 4);
        }

        void SetNight(int night)
        {
            if (night == lastNight) return;
            lastNight = night;
            SetSpaced(nightText, "NIGHT " + night + " / 13");
            for (int i = 0; i < 13; i++)
            {
                if (i < night - 1) pips[i].color = AMBER;
                else if (i == night - 1) pips[i].color = U.hex(0xf4ecdd);
                else pips[i].color = U.hex(0x3a3228);
            }
            float cx = PAD + 4 + (night - 1) * (CW - 8) / 12f;
            pipGlow.anchoredPosition = new Vector2(cx - 11, -(PIPS_Y - 7));
        }

        // ---------------- public api used by the game ----------------
        public void SetSeason(string s)
        {
            seasonText.text = s;
        }

        public void SetHud(int night, string saved, string lost, int trees, double frac)
        {
            SetNight(night);
            if (statVal[0].text != saved) statVal[0].text = saved;
            if (statVal[1].text != lost) statVal[1].text = lost;
            string tv = trees + " / 6";
            if (statVal[2].text != tv) statVal[2].text = tv;
            timeFill.sizeDelta = new Vector2((float)(CW * frac), 6);
        }

        static string LogColor(string msg)
        {
            if (msg.StartsWith("===")) return "f4ecdd";
            if (msg.Contains("tree has fallen")) return "f06a55";
            if (msg.Contains("revived")) return "ffe08a";
            if (msg.StartsWith("Pip")) return "9cd98a";
            if (msg.StartsWith("Thistle")) return "e3c06a";
            if (msg.StartsWith("Shambler")) return "c3b2e0";
            if (msg.StartsWith("Pack of")) return "e9b850";
            if (msg.StartsWith("Stalker")) return "ec8b7b";
            if (msg.StartsWith("Scatterling")) return "f1d86a";
            if (msg.StartsWith("Bad bird")) return "c7b4d6";
            if (msg.StartsWith("Map built")) return "8fd3c8";
            return "d8d0c0";
        }

        // short forms for the slim sidebar (the full text is what the game logs)
        static string Short(string m)
        {
            if (m.StartsWith("=== Night ")) return m.Replace("=== ", "").Replace(" ===", "");
            if (m.StartsWith("Thistle switched to ")) return "Thistle: " + m.Substring(20);
            if (m == "Shambler routed around terrain") return "Shambler: A* reroute";
            if (m.StartsWith("Shamblers pack up on ")) return "Pack forms on " + m.Substring(21);
            if (m == "Shambler hurt, fleeing to heal") return "Shambler flees, hurt";
            if (m == "Shambler ate a fruit to heal") return "Shambler ate fruit +3hp";
            if (m == "Shambler goes for fruit to heal") return "Shambler wants fruit";
            if (m == "Shambler turns on the farmer") return "Shambler hits back!";
            if (m == "Shambler knocked the farmer back!") return "You got knocked back!";
            if (m == "Pip is hurt, running to the farmer") return "Pip hurt, runs to you";
            if (m == "Pip repairs the weakest tree") return "Pip: fix weakest tree";
            if (m == "Stalker detected target") return "Stalker spotted you!";
            if (m == "Stalker lost sight, searching") return "Stalker: searching";
            if (m == "Scatterling saved a fruit") return "Scatterling saved fruit";
            if (m == "Bad bird caught a scatterling") return "Bad bird caught one!";
            if (m == "Bad bird stole a fruit") return "Bad bird stole fruit";
            if (m == "Scatterlings revived a tree!") return "Birds revived a tree!";
            if (m.StartsWith("Map built")) return "Map built + validated";
            Match nm = Regex.Match(m, @"^Night (\d+) survived, (\d+)/6 trees stand$");
            if (nm.Success) return "Night " + nm.Groups[1].Value + " survived " + nm.Groups[2].Value + "/6";
            return m;
        }

        public void SetLog(List<string> lines)
        {
            List<string> visual = new List<string>();
            foreach (string entry in lines)
            {
                Match whole = Regex.Match(entry, @"^\[[^\]]+\]\s(.*)$");
                string msg = whole.Success ? whole.Groups[1].Value : entry;
                string col = LogColor(msg);
                msg = Short(msg);
                bool head = msg.StartsWith("===");
                // compact form for the slim sidebar: "00:12 message"
                Match ts = Regex.Match(entry, @"^\[([^\]]+)\]\s");
                string stamp = ts.Success ? ts.Groups[1].Value : "";
                List<string> wrapped = WrapMono(msg, LOG_CHARS - (stamp.Length + 1));
                for (int i = 0; i < wrapped.Count; i++)
                {
                    string ln = wrapped[i];
                    ln = Regex.Replace(ln, @"\bOK\b", "<color=#7ec850><b>OK</b></color>");
                    if (head) ln = "<b>" + ln + "</b>";
                    string pre = i == 0 ? "<color=#6f6a58>" + stamp + "</color> " : new string(' ', stamp.Length + 1);
                    visual.Add(pre + "<color=#" + col + ">" + ln + "</color>");
                }
            }
            if (visual.Count > LOG_MAX_LINES) visual.RemoveRange(0, visual.Count - LOG_MAX_LINES);
            while (logTexts.Count < visual.Count)
            {
                Text t = MakeText(logLayer, "log", PAD - 2, 0, SIDE_W - PAD, LOG_LINE, 10, INK, false, mono, TextAnchor.MiddleLeft, "");
                logTexts.Add(t);
            }
            for (int i = 0; i < logTexts.Count; i++)
            {
                Text t = logTexts[i];
                if (i < visual.Count)
                {
                    t.gameObject.SetActive(true);
                    t.text = visual[i];
                    t.rectTransform.anchoredPosition = new Vector2(PAD - 2, -(LOG_Y + i * LOG_LINE));
                }
                else t.gameObject.SetActive(false);
            }
        }

        static List<string> WrapMono(string s, int max)
        {
            List<string> outp = new List<string>();
            string[] words = s.Split(' ');
            string cur = null;
            foreach (string w in words)
            {
                if (cur == null) cur = w;
                else if (cur.Length + 1 + w.Length <= max) cur += " " + w;
                else { outp.Add(cur); cur = w; }
            }
            if (cur != null) outp.Add(cur);
            return outp;
        }

        public void ShowTitle()
        {
            BuildOverlay("Orchard of the Last Season", U.hex(0xe9b850),
                "Defend your <b>6 trees</b> for <b>13 nights</b> (1 min each). <b>Yellow birds</b> save fallen fruit by eating it; <b>black birds</b> steal it and hunt the yellows. Every <b>" + C.REVIVE_COST + "</b> fruit the yellows save brings a fallen tree back to life. <b>Pip</b> repairs your weakest tree; <b>Thistle</b> hunts. Rain brings mud, hills and bushes the enemies can't cross — but you can.",
                true, "Begin Night 1", g.StartClicked);
        }

        public void ShowWin(int s, int saved, int lost)
        {
            BuildOverlay("You made it!", U.hex(0x8fd36a),
                "You defended the orchard through all 13 nights.<br><b>" + s + "/6</b> trees stand · <b>" + saved + "</b> fruit saved, <b>" + lost + "</b> lost.",
                false, "Play again", g.Restart);
        }

        public void ShowLose(int night, int saved, int lost)
        {
            BuildOverlay("The last tree has fallen", U.hex(0xe9b850),
                "You held to night <b>" + night + "</b>.<br><b>" + saved + "</b> saved · <b>" + lost + "</b> lost.",
                false, "Try again", g.Restart);
        }

        public void HideOverlay()
        {
            overlay.gameObject.SetActive(false);
            overlayVisible = false;
            btnPressed = false;
        }

        public void ShowTransition(double alpha, string msg, string sub)
        {
            if (!transitionRT.gameObject.activeSelf) transitionRT.gameObject.SetActive(true);
            float T = (float)C.TRANS_LEN, t = (float)g.trans.t;
            float p = Mathf.Clamp01(1 - t / T);
            float fade = Mathf.Clamp01(Mathf.Min((T - t) / 0.35f, t / 0.35f));
            UpdateScene(p, fade, msg, sub);
        }

        // the board's size in real screen pixels (same maths as the canvas scaler's Expand mode),
        // so the board can be rendered 1:1 with no stretching
        public void BoardPixels(out int w, out int h)
        {
            float s = Mathf.Min(Screen.width / (SIDE_W + LEFT_W), Screen.height / H);
            w = Mathf.Max(64, Mathf.RoundToInt(LEFT_W * s));
            h = Mathf.Max(64, Mathf.RoundToInt(H * s));
        }

        RawImage boardRaw;
        public void SetBoardTexture(RenderTexture rt) { if (boardRaw != null) boardRaw.texture = rt; }

        public void HideTransition()
        {
            if (transitionRT.gameObject.activeSelf) transitionRT.gameObject.SetActive(false);
        }

        // ---- "Tab: shambler desirability" hint and legend, bottom-left of the board ----
        RectTransform tabHint, tabLegend;
        bool tabShown;

        void BuildTabHint()
        {
            tabHint = NewRect("tabhint", wrap, SIDE_W + 10, H - 30, 220, 20);
            AddImage(tabHint, U.rgba(20, 16, 10, .72f), Rounded(10), true, 4);
            AddImage(NewRect("key", tabHint, 6, 3, 26, 14), U.hex(0xf4ecdd), Rounded(4), true, 4);
            MakeText(tabHint, "k", 6, 3, 26, 14, 8.5f, U.hex(0x4a3420), true, sans, TextAnchor.MiddleCenter, "Tab");
            MakeText(tabHint, "t", 38, 0, 180, 20, 9.5f, INK, false, sans, TextAnchor.MiddleLeft, "show what each Shambler wants");

            tabLegend = NewRect("tablegend", wrap, SIDE_W + 10, H - 30 - 26, 360, 22);
            AddImage(tabLegend, U.rgba(20, 16, 10, .78f), Rounded(10), true, 4);
            string[] names = { "Raid tree", "Eat fruit", "Pack a dog", "Hit farmer", "Flee" };
            float lx = 10;
            for (int i = 0; i < 5; i++)
            {
                AddImage(NewRect("sw", tabLegend, lx, 7, 8, 8), GameRenderer.ACT_COL[i], Rounded(2), true, 4);
                MakeText(tabLegend, "n", lx + 11, 0, 80, 22, 9.5f, INK, true, sans, TextAnchor.MiddleLeft, names[i]);
                lx += 11 + Measure(names[i], sans, 9.5f, true) + 12;
            }
            tabLegend.sizeDelta = new Vector2(lx, 22);
            tabLegend.gameObject.SetActive(false);
        }

        void UpdateTabHint()
        {
            if (g.showUtility == tabShown) return;
            tabShown = g.showUtility;
            tabLegend.gameObject.SetActive(tabShown);
        }

        public void Tick()
        {
            UpdateLive();
            if (layoutDirty) Relayout();
        }

        // ---------------- start / win / lose card ----------------
        const float CARD_W = 500, CARD_PAD = 30;

        void BuildOverlay(string h1, Color h1c, string pRich, bool title, string btnLabel, System.Action action)
        {
            for (int i = overlayContent.childCount - 1; i >= 0; i--)
                Object.Destroy(overlayContent.GetChild(i).gameObject);
            for (int i = card.childCount - 1; i >= 0; i--)
                Object.Destroy(card.GetChild(i).gameObject);
            Image old = card.GetComponent<Image>();

            float cw = CARD_W - 2 * CARD_PAD;
            float y = 0;
            Sprite disc = Shapes.Circle(), glow = Shapes.Radial(128, 0);

            // moon emblem
            float mx = cw / 2f;
            AddImage(NewRect("glow", overlayContent, mx - 40, y - 12, 80, 80), new Color(h1c.r, h1c.g, h1c.b, .25f), glow, false, 1);
            AddImage(NewRect("moon", overlayContent, mx - 20, y + 8, 40, 40), U.hex(0xf0e4c4), disc, false, 1);
            AddImage(NewRect("c1", overlayContent, mx - 11, y + 16, 10, 8), U.rgba(190, 175, 140, .55f), disc, false, 1);
            AddImage(NewRect("c2", overlayContent, mx + 4, y + 30, 7, 6), U.rgba(190, 175, 140, .55f), disc, false, 1);
            y += 62;

            float h1H = 28 * N_SANS;
            MakeText(overlayContent, "h1", 0, y, cw, h1H, 28, h1c, true, sans, TextAnchor.MiddleCenter, h1);
            y += h1H + 12;

            List<string> plines = WrapRich(pRich, 14, 430);
            foreach (string ln in plines)
            {
                MakeText(overlayContent, "p", 0, y, cw, 14 * 1.6f, 14, U.hex(0xe7d8bb), false, sans, TextAnchor.MiddleCenter, ln);
                y += 14 * 1.6f;
            }
            y += 16;

            if (title)
            {
                // the ai techniques on show, as chips in two centred rows
                string[][] rows = { new string[] { "Behaviour tree", "State machine", "A* pathfinding" },
                                    new string[] { "Perception", "Flocking", "Procedural map" } };
                Color[] cc = { U.hex(0x7ec850), U.hex(0xe3b341), U.hex(0xb9a6d9), U.hex(0xe0796a), U.hex(0xf1d23a), U.hex(0x8fd3c8) };
                int ci = 0;
                foreach (string[] row in rows)
                {
                    float total = 0;
                    float[] ws = new float[row.Length];
                    for (int i = 0; i < row.Length; i++) { ws[i] = Measure(row[i], sans, 11, true) + 26; total += ws[i]; }
                    total += (row.Length - 1) * 8;
                    float x = (cw - total) / 2;
                    for (int i = 0; i < row.Length; i++)
                    {
                        Color c = cc[ci++];
                        AddImage(NewRect("chip", overlayContent, x, y, ws[i], 22), new Color(c.r, c.g, c.b, .13f), Rounded(11), true, 4);
                        AddImage(NewRect("dot", overlayContent, x + 8, y + 8, 6, 6), c, disc, false, 1);
                        MakeText(overlayContent, "chipt", x + 14, y, ws[i] - 16, 22, 11, c, true, sans, TextAnchor.MiddleCenter, row[i]);
                        x += ws[i] + 8;
                    }
                    y += 28;
                }
                y += 10;
                AddImage(NewRect("rule", overlayContent, 40, y, cw - 80, 1), U.rgba(255, 255, 255, .08f), null, false, 1);
                y += 14;
                BuildCtrls(cw, y);
                y += 13 * 1.9f + 16;
            }

            float bh = 16 * N_SANS + 24;
            float bw = Measure(btnLabel, sans, 16, true) + 64;
            float bx = (cw - bw) / 2;
            AddImage(NewRect("btnshadow", overlayContent, bx, y + 4, bw, bh), U.hex(0x8f4a28), Rounded(10), true, 4);
            btnFace = NewRect("btn", overlayContent, bx, y, bw, bh);
            AddImage(btnFace, U.hex(0xcd6d3c), Rounded(10), true, 4);
            AddImage(NewRect("btnhi", btnFace, 6, 2, bw - 12, 2), U.rgba(255, 255, 255, .25f), Rounded(1), true, 4);
            MakeText(btnFace, "label", 0, 0, bw, bh, 16, Color.white, true, sans, TextAnchor.MiddleCenter, btnLabel);
            btnY = y;
            y += bh + 4;

            float contentH = y;
            float cardH = contentH + 2 * CARD_PAD;
            float top = (H - cardH) / 2;
            float left = (LEFT_W - CARD_W) / 2;
            card.anchoredPosition = new Vector2(left, -top);
            card.sizeDelta = new Vector2(CARD_W, cardH);
            if (old == null) AddImage(card, U.rgba(33, 28, 22, .97f), Rounded(16), true, 4);
            AddImage(NewRect("ring", card, 0, 0, CARD_W, cardH), U.rgba(233, 184, 80, .22f), Ring(16, 1.5f), true, 4);
            overlayContent.anchoredPosition = new Vector2(left + CARD_PAD, -(top + CARD_PAD));
            overlayContent.sizeDelta = new Vector2(cw, contentH);
            btnAction = action;
            btnPressed = false;
            overlay.gameObject.SetActive(true);
            overlayVisible = true;
        }

        void BuildCtrls(float cw, float y)
        {
            string[] seg = { "W A S D", " move   ", "Space", " scythe (pluck fruit)   ", "F", " catch fruit" };
            bool[] isKbd = { true, false, true, false, true, false };
            float[] wd = new float[seg.Length];
            float total = 0;
            for (int i = 0; i < seg.Length; i++)
            {
                wd[i] = isKbd[i] ? Measure(seg[i], sans, 11, true) + 14 : Measure(seg[i], sans, 13, false);
                total += wd[i];
            }
            float x = (cw - total) / 2;
            float lineH = 13 * 1.9f;
            for (int i = 0; i < seg.Length; i++)
            {
                if (isKbd[i])
                {
                    float top = y + 2, h = lineH - 4;
                    AddImage(NewRect("kbd", overlayContent, x, top + 2, wd[i], h), U.hex(0x8a7a5a), Rounded(5), true, 4);
                    AddImage(NewRect("kbdin", overlayContent, x, top, wd[i], h), U.hex(0xf4ecdd), Rounded(5), true, 4);
                    MakeText(overlayContent, "kbdt", x, top, wd[i], h, 11, U.hex(0x4a3420), true, sans, TextAnchor.MiddleCenter, seg[i]);
                }
                else
                {
                    MakeText(overlayContent, "ctl", x, y, wd[i] + 4, lineH, 13, U.hex(0xcbb892), false, sans, TextAnchor.MiddleLeft, seg[i]);
                }
                x += wd[i];
            }
        }

        // word-wrap a string with <b> and <br> into centred lines (max width in px)
        List<string> WrapRich(string rich, float px, float maxW)
        {
            List<char> cs = new List<char>();
            List<bool> bs = new List<bool>();
            bool bold = false;
            int i = 0;
            while (i < rich.Length)
            {
                if (string.CompareOrdinal(rich, i, "<b>", 0, 3) == 0) { bold = true; i += 3; continue; }
                if (string.CompareOrdinal(rich, i, "</b>", 0, 4) == 0) { bold = false; i += 4; continue; }
                if (string.CompareOrdinal(rich, i, "<br>", 0, 4) == 0) { cs.Add('\n'); bs.Add(false); i += 4; continue; }
                cs.Add(rich[i]); bs.Add(bold); i++;
            }

            List<string> lines = new List<string>();
            List<int> line = new List<int>();     // indices into cs
            List<int> word = new List<int>();
            float lineW = 0;
            System.Action flushLine = () =>
            {
                // trim spaces at both ends of the visual line
                int a = 0, b = line.Count - 1;
                while (a <= b && cs[line[a]] == ' ') a++;
                while (b >= a && cs[line[b]] == ' ') b--;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                bool cur = false;
                for (int k = a; k <= b; k++)
                {
                    bool bb = bs[line[k]];
                    if (bb != cur) { sb.Append(bb ? "<b>" : "</b>"); cur = bb; }
                    sb.Append(cs[line[k]]);
                }
                if (cur) sb.Append("</b>");
                lines.Add(sb.ToString());
                line.Clear();
                lineW = 0;
            };
            System.Action placeWord = () =>
            {
                if (word.Count == 0) return;
                float ww = 0;
                foreach (int k in word) ww += CharW(cs[k], bs[k], px);
                float spaceW = 0;
                if (line.Count > 0) spaceW = CharW(' ', bs[line[line.Count - 1]], px);
                if (line.Count > 0 && lineW + spaceW + ww > maxW) flushLine();
                if (line.Count > 0)
                {
                    cs.Add(' '); bs.Add(bs[line[line.Count - 1]]);
                    line.Add(cs.Count - 1);
                    lineW += spaceW;
                }
                foreach (int k in word) line.Add(k);
                lineW += ww;
                word.Clear();
            };
            int n = cs.Count;
            for (int k = 0; k < n; k++)
            {
                char c = cs[k];
                if (c == '\n') { placeWord(); flushLine(); continue; }
                if (c == ' ') { placeWord(); continue; }
                word.Add(k);
            }
            placeWord();
            if (line.Count > 0) flushLine();
            return lines;
        }

        float CharW(char c, bool bold, float px)
        {
            string key = c + (bold ? "b" : "n") + px;
            float w;
            if (widthCache.TryGetValue(key, out w)) return w;
            w = Measure(c.ToString(), sans, px, bold);
            widthCache[key] = w;
            return w;
        }

        // text width in css px, measured at 10x size for sub-pixel precision
        float Measure(string s, Font f, float px, bool bold)
        {
            int size = Mathf.RoundToInt(px * 10);
            FontStyle st = bold ? FontStyle.Bold : FontStyle.Normal;
            f.RequestCharactersInTexture(s, size, st);
            float w = 0;
            foreach (char ch in s)
            {
                CharacterInfo ci;
                if (f.GetCharacterInfo(ch, out ci, size, st)) w += ci.advance;
            }
            return w / 10f;
        }

        // ---------------- pointer (button) ----------------
        public void PointerDown(Vector2 screen)
        {
            if (!overlayVisible || btnFace == null) return;
            if (RectTransformUtility.RectangleContainsScreenPoint(btnFace, screen, null))
            {
                btnPressed = true;
                btnFace.anchoredPosition = new Vector2(btnFace.anchoredPosition.x, -(btnY + 2)); // :active translateY(2px)
            }
        }

        public void PointerUp(Vector2 screen)
        {
            if (!btnPressed) return;
            btnPressed = false;
            if (btnFace != null) btnFace.anchoredPosition = new Vector2(btnFace.anchoredPosition.x, -btnY);
            if (overlayVisible && btnFace != null && RectTransformUtility.RectangleContainsScreenPoint(btnFace, screen, null))
            {
                if (btnAction != null) btnAction();
            }
        }

        // enter key on the title screen
        public void ClickStartButton()
        {
            if (overlayVisible && btnAction != null) btnAction();
        }

        // ---------------- layout ----------------
        void Relayout()
        {
            layoutDirty = false;
            wrap.sizeDelta = new Vector2(SIDE_W + LEFT_W, H);
            shadowRT.sizeDelta = Vector2.zero;   // no drop shadow: the game fills the screen edge to edge
            scaler.referenceResolution = new Vector2(SIDE_W + LEFT_W, H);
        }

        // ---------------- ui helpers ----------------
        static RectTransform NewRect(string name, Transform parent, float x, float y, float w, float h)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static Image AddImage(RectTransform rt, Color c, Sprite s, bool sliced, float ppuMul)
        {
            Image im = rt.gameObject.AddComponent<Image>();
            im.color = c;
            im.raycastTarget = false;
            if (s != null)
            {
                im.sprite = s;
                if (sliced)
                {
                    im.type = Image.Type.Sliced;
                    im.fillCenter = true;
                    im.pixelsPerUnitMultiplier = ppuMul;
                }
            }
            return im;
        }

        // text at its true size: unity rasterises the glyphs at fontSize x the canvas scale,
        // so it stays pin sharp on retina / 1080p / 4k
        static Text MakeText(Transform parent, string name, float x, float y, float w, float h, float px, Color col, bool bold, Font f, TextAnchor anchor, string s)
        {
            RectTransform rt = NewRect(name, parent, x, y, w, h);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = f;
            t.fontSize = Mathf.Max(1, Mathf.RoundToInt(px));
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = col;
            t.alignment = anchor;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.lineSpacing = 1;
            t.text = s;
            return t;
        }

        // ---------------- generated ui textures ----------------
        static float BoxSdf(float px, float py, float cx, float cy, float hw, float hh, float rad)
        {
            float qx = Mathf.Abs(px - cx) - (hw - rad);
            float qy = Mathf.Abs(py - cy) - (hh - rad);
            float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - rad;
        }

        // 9-sliced rounded rectangle (supersampled 4x; use pixelsPerUnitMultiplier = 4)
        static Sprite Rounded(float r)
        {
            string key = "round" + r;
            Sprite cached;
            if (shapeCache.TryGetValue(key, out cached)) return cached;
            int SS = 4;
            int N = Mathf.CeilToInt((2 * r + 2) * SS);
            float rad = r * SS;
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = BoxSdf(x + .5f, y + .5f, N / 2f, N / 2f, N / 2f, N / 2f, rad);
                    float a = Mathf.Clamp01(0.5f - d);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            float b = r * SS;
            Sprite s = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s.name = key;
            shapeCache[key] = s;
            return s;
        }

        // 9-sliced rounded border of the given width
        static Sprite Ring(float r, float width)
        {
            string key = "ring" + r + "_" + width;
            Sprite cached;
            if (shapeCache.TryGetValue(key, out cached)) return cached;
            int SS = 4;
            int N = Mathf.CeilToInt((2 * r + 2) * SS);
            float rad = r * SS, wd = width * SS;
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dOut = BoxSdf(x + .5f, y + .5f, N / 2f, N / 2f, N / 2f, N / 2f, rad);
                    float dIn = BoxSdf(x + .5f, y + .5f, N / 2f, N / 2f, N / 2f - wd, N / 2f - wd, Mathf.Max(rad - wd, 0));
                    float a = Mathf.Clamp01(0.5f - dOut) * (1 - Mathf.Clamp01(0.5f - dIn));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            float b = r * SS;
            Sprite s = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s.name = key;
            shapeCache[key] = s;
            return s;
        }

        static Sprite Gradient(Color a, Color b, bool vertical)
        {
            int w = vertical ? 1 : 256, h = vertical ? 256 : 1;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int i = 0; i < 256; i++)
            {
                Color c = Color.Lerp(a, b, i / 255f);
                if (vertical) t.SetPixel(0, 255 - i, c); else t.SetPixel(i, 0, c);
            }
            t.Apply();
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            Sprite s = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100);
            s.name = "grad:" + (vertical ? "v:" : "h:") + ColorUtility.ToHtmlStringRGBA(a) + ":" + ColorUtility.ToHtmlStringRGBA(b);
            return s;
        }

        // gaussian box-shadow (blur 60px, rgba(0,0,0,.6)), 9-sliced, 1 texel = 1px
        static Sprite SoftShadow()
        {
            int B = 120;
            int N = B * 2 + 2;
            float sigma = 30f;
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[N * N];
            float inset = 60;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = BoxSdf(x + .5f, y + .5f, N / 2f, N / 2f, N / 2f - inset, N / 2f - inset, 10);
                    float a = 0.6f * 0.5f * Erfc(d / (sigma * 1.41421356f));
                    px[y * N + x] = new Color32(0, 0, 0, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255), 0, 255));
                }
            t.SetPixels32(px);
            t.Apply();
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            Sprite ss = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(B, B, B, B));
            ss.name = "shadow";
            return ss;
        }

        static float Erfc(float x)
        {
            // abramowitz & stegun 7.1.26
            float z = Mathf.Abs(x);
            float t = 1f / (1f + 0.3275911f * z);
            float y = t * (0.254829592f + t * (-0.284496736f + t * (1.421413741f + t * (-1.453152027f + t * 1.061405429f)))) * Mathf.Exp(-z * z);
            return x >= 0 ? y : 2f - y;
        }
    }
}
