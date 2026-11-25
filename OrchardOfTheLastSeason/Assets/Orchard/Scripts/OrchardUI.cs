using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Orchard
{
    //  the screen around the board. 16:9: sidebar 15 percent, board 85 percent...
    //  sidebar: title, night, stat cards, ai agents, event log...
    //  board area: start, win and lose cards, and the card between nights...
    public class OrchardUI //george manu zian
    {
        OrchardGame g;
        Font sans;
        Font mono;
        CanvasScaler scaler;
        RectTransform wrap;
        RectTransform overlay;
        RectTransform overlayContent;
        RectTransform transitionRT;
        RectTransform timeFill;
        RectTransform logLayer;
        RectTransform card;
        RectTransform pipGlow;
        Image pipGlowImg;
        Text nightText;
        Text seasonText;
        Text timeText;
        Text[] statVal = new Text[4];
        Image[] pips = new Image[13];
        List<Text> logTexts = new List<Text>();
        int lastNight = -1;
        RawImage boardRaw;

        //  small label at the right of every agent row...
        class Chip
        {
            public RectTransform bg;
            public Image bgImg;
            public Text label;
            public string cur = "";
            public float right;
            public float y;
        }
        Chip[] chips = new Chip[7];

        //  button of the cards...
        RectTransform btnFace;
        float btnY;
        bool btnPressed = false;
        bool overlayVisible = false;
        int btnAction = 0;

        //  sizes in pixels. 188 + 1064 = 1252, and 1252 x 704 is 16:9...
        const float H = 704;
        const float SIDE_W = 188;
        const float LEFT_W = 1064;
        const float PAD = 12;
        const float CW = 164;
        const float N_SANS = 1.161f;
        const float NIGHT_Y = 50;
        const float SEASON_Y = 64;
        const float PIPS_Y = 84;
        const float TIME_Y = 98;
        const float BAR_Y = 114;
        const float CARD_Y = 128;
        const float CARD_H = 46;
        const float CARD_GAP = 6;
        const float AGENTS_Y = 236;
        const float ROW_Y0 = 252;
        const float ROW_H = 31;
        const float ROW_STEP = 33;
        const float LOGWRAP_Y = 490;
        const float LOGTTL_Y = 498;
        const float LOG_Y = 514;
        const float LOG_LINE = 13;
        const int LOG_CHARS = 27;
        const int LOG_MAX_LINES = 14;

        static Color INK = new Color(0xf0 / 255f, 0xe9 / 255f, 0xdc / 255f, 1);
        static Color MUTED = new Color(0xad / 255f, 0xa4 / 255f, 0x90 / 255f, 1);
        static Color AMBER = new Color(0xe9 / 255f, 0xb8 / 255f, 0x50 / 255f, 1);

        static Dictionary<string, Sprite> shapeCache = new Dictionary<string, Sprite>();
        Dictionary<string, float> widthCache = new Dictionary<string, float>();

        //  the 7 agents shown in the sidebar...
        string[] aImg = { "farmer", "pip", "thistle", "shambler", "stalker", "scatterling", "badscatter" };
        string[] aName = { "Farmer", "Pip", "Thistle", "Shambler", "Stalker", "Scatterlings", "Bad birds" };
        string[] aTag = { "YOU  ·  SCYTHE", "BEHAVIOUR TREE", "STATE MACHINE", "GOAL DRIVEN  ·  NAV", "PERCEPTION", "FLOCKING", "FLOCKING  ·  HUNT" };
        int[] aCol = { 0xd98a3d, 0x7ec850, 0xe3b341, 0xb9a6d9, 0xe0796a, 0xf1d23a, 0xb4a6c6 };

        public OrchardUI(OrchardGame g, RenderTexture boardRT) //george manu zian
        {
            this.g = g;
            sans = LoadFont(new string[] { "Trebuchet MS", "Segoe UI", "Helvetica Neue", "Arial" });
            mono = LoadFont(new string[] { "Courier New", "Courier", "Menlo", "Consolas" });
            Build(boardRT);
        }

        //  first font from the list that is installed, else the built in font...
        static Font LoadFont(string[] names)
        {
            try
            {
                string[] installed = Font.GetOSInstalledFontNames();
                List<string> have = new List<string>();
                for (int i = 0; i < names.Length; i++)
                {
                    for (int j = 0; j < installed.Length; j++)
                    {
                        if (installed[j] == names[i])
                        {
                            have.Add(names[i]);
                            break;
                        }
                    }
                }
                if (have.Count > 0)
                {
                    return Font.CreateDynamicFontFromOSFont(have.ToArray(), 32);
                }
            }
            catch (Exception)
            {
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // build

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

            //  dark background for the whole screen...
            RectTransform bg = NewRect("body", canvasT, 0, 0, 0, 0);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.pivot = new Vector2(.5f, .5f);
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;
            AddImage(bg, U.hex(0x1b1822), null, false, 1);

            //  wrap = the 16:9 area in the middle...
            wrap = NewRect("wrap", canvasT, 0, 0, SIDE_W + LEFT_W, H);
            wrap.anchorMin = new Vector2(.5f, .5f);
            wrap.anchorMax = new Vector2(.5f, .5f);
            wrap.pivot = new Vector2(.5f, .5f);
            wrap.anchoredPosition = Vector2.zero;
            AddImage(wrap, Color.white, null, false, 1);
            Mask m = wrap.gameObject.AddComponent<Mask>();
            m.showMaskGraphic = false;

            BuildSidebar();

            //  board...
            RectTransform leftBg = NewRect("left", wrap, SIDE_W, 0, LEFT_W, H);
            AddImage(leftBg, U.hex(0x5f9552), null, false, 1);
            RectTransform boardR = NewRect("board", wrap, SIDE_W, 0, LEFT_W, H);
            RawImage raw = boardR.gameObject.AddComponent<RawImage>();
            raw.texture = boardRT;
            raw.raycastTarget = false;
            boardRaw = raw;

            BuildTabHint();

            //  card between nights...
            transitionRT = NewRect("transition", wrap, SIDE_W, 0, LEFT_W, H);
            transitionRT.gameObject.AddComponent<RectMask2D>();
            BuildScene();
            transitionRT.gameObject.SetActive(false);

            //  start, win and lose cards...
            overlay = NewRect("overlay", wrap, SIDE_W, 0, LEFT_W, H);
            AddImage(overlay, U.rgba(12, 9, 6, .72f), null, false, 1);
            card = NewRect("card", overlay, 0, 0, 10, 10);
            overlayContent = NewRect("content", overlay, 0, 0, 10, 10);
            overlay.gameObject.SetActive(false);

            SetNight(1);
        }

        void BuildSidebar()
        {
            RectTransform sideBg = NewRect("side", wrap, 0, 0, SIDE_W, H);
            AddImage(sideBg, Color.white, Gradient(U.hex(0x37314a), U.hex(0x262234), true), false, 1);
            AddImage(NewRect("sideedge", wrap, SIDE_W - 1, 0, 1, H), U.hex(0x4a4360), null, false, 1);

            Sprite disc = Shapes.Circle();
            Sprite glow = Shapes.Glow();

            //  moon badge and title...
            AddImage(NewRect("badgeglow", wrap, PAD - 9, 6, 44, 44), U.rgba(240, 228, 196, .22f), glow, false, 1);
            AddImage(NewRect("badge", wrap, PAD, 15, 26, 26), U.hex(0xf0e4c4), disc, false, 1);
            AddImage(NewRect("cr1", wrap, PAD + 6, 21, 7, 6), U.rgba(190, 175, 140, .6f), disc, false, 1);
            AddImage(NewRect("cr2", wrap, PAD + 15, 30, 5, 4), U.rgba(190, 175, 140, .6f), disc, false, 1);
            MakeText(wrap, "title1", PAD + 33, 11, 150, 17, 14, U.hex(0xf4ecdd), true, sans, TextAnchor.MiddleLeft, "Orchard of the");
            MakeText(wrap, "title2", PAD + 33, 27, 150, 17, 14, U.hex(0xf4ecdd), true, sans, TextAnchor.MiddleLeft, "Last Season");

            //  night and season...
            nightText = MakeText(wrap, "night", PAD, NIGHT_Y, CW, 13, 10, AMBER, true, sans, TextAnchor.MiddleLeft, "NIGHT 1 / 13");
            seasonText = MakeText(wrap, "season", PAD, SEASON_Y, CW, 13, 10, MUTED, false, sans, TextAnchor.MiddleLeft, "Early Autumn");

            //  13 dots, one for every night...
            pipGlow = NewRect("pipglow", wrap, 0, PIPS_Y - 7, 22, 22);
            pipGlowImg = AddImage(pipGlow, AMBER, glow, false, 1);
            for (int i = 0; i < 13; i++)
            {
                float cx = PAD + 4 + i * (CW - 8) / 12f;
                pips[i] = AddImage(NewRect("pip", wrap, cx - 4, PIPS_Y, 8, 8), U.hex(0x4a4360), disc, false, 1);
            }

            //  time left...
            MakeText(wrap, "timelab", PAD, TIME_Y, 100, 13, 9.5f, MUTED, false, sans, TextAnchor.MiddleLeft, "Dawn in");
            timeText = MakeText(wrap, "time", PAD, TIME_Y, CW, 13, 10.5f, INK, true, sans, TextAnchor.MiddleRight, "1:00");
            RectTransform bar = NewRect("timebar", wrap, PAD, BAR_Y, CW, 6);
            AddImage(bar, U.hex(0x4a4360), Rounded(3), true, 4);
            bar.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            timeFill = NewRect("timefill", bar, 0, 0, CW, 6);
            AddImage(timeFill, Color.white, Gradient(U.hex(0xc76b3f), U.hex(0xe9b850), false), false, 1);

            //  4 stat cards: saved, lost, trees, tornados...
            string[] icons = { "icon_apple", "icon_heart", "icon_tree", "scatterling" };
            string[] labs = { "fruit saved", "lost to birds", "trees left", "tornados" };
            string[] init = { "0 / 0", "0", "6 / 6", "0" };
            Color[] vcol = { U.hex(0x8fd36a), U.hex(0xe8806a), INK, U.hex(0xf1d23a) };
            float cw2 = (CW - CARD_GAP) / 2f;
            for (int i = 0; i < 4; i++)
            {
                float x = PAD + (i % 2) * (cw2 + CARD_GAP);
                float y = CARD_Y + (i / 2) * (CARD_H + CARD_GAP);
                Color tint = U.rgba(255, 255, 255, .045f);
                Color edge = U.rgba(255, 255, 255, .07f);
                if (i == 3)
                {
                    tint = U.rgba(241, 210, 58, .07f);
                    edge = U.rgba(241, 210, 58, .22f);
                }
                AddImage(NewRect("card", wrap, x, y, cw2, CARD_H), tint, Rounded(8), true, 4);
                AddImage(NewRect("cardring", wrap, x, y, cw2, CARD_H), edge, Ring(8, 1), true, 4);

                Sprite ic;
                if (g.img.TryGetValue(icons[i], out ic))
                {
                    float ih = 12;
                    float iy = y + 6;
                    if (i == 3)
                    {
                        ih = 15;
                        iy = y + 5;
                    }
                    float iw = ih * ic.rect.width / ic.rect.height;
                    AddImage(NewRect("ic", wrap, x + 7, iy, iw, ih), Color.white, ic, false, 1);
                }
                float vsize = 14.5f;
                if (i == 3) vsize = 12.5f;
                statVal[i] = MakeText(wrap, "val", x + 7, y + 19, cw2 - 9, 17, vsize, vcol[i], true, sans, TextAnchor.MiddleLeft, init[i]);
                MakeText(wrap, "lab", x + 7, y + 33, cw2 - 8, 10, 8, MUTED, false, sans, TextAnchor.MiddleLeft, labs[i]);
            }

            //  agents...
            MakeText(wrap, "agents", PAD, AGENTS_Y, CW, 13, 9, U.hex(0x8fa878), false, sans, TextAnchor.MiddleLeft, "AI AGENTS");
            AddImage(NewRect("livedot", wrap, PAD + CW - 29, AGENTS_Y + 4, 5, 5), U.hex(0x7ec850), disc, false, 1);
            MakeText(wrap, "live", PAD, AGENTS_Y, CW, 13, 8.5f, U.hex(0x7ec850), true, sans, TextAnchor.MiddleRight, "LIVE");
            BuildAgents();

            //  event log...
            AddImage(NewRect("logwrap", wrap, 0, LOGWRAP_Y, SIDE_W - 1, H - LOGWRAP_Y), U.hex(0x1d1a29), null, false, 1);
            AddImage(NewRect("logborder", wrap, 0, LOGWRAP_Y, SIDE_W - 1, 2), U.hex(0x4a4360), null, false, 1);
            MakeText(wrap, "logtitle", PAD, LOGTTL_Y, CW, 13, 9, U.hex(0x8fa878), false, sans, TextAnchor.MiddleLeft, "AI EVENT LOG");
            logLayer = NewRect("loglines", wrap, 0, 0, SIDE_W, 10);
        }

        void BuildAgents()
        {
            for (int i = 0; i < 7; i++)
            {
                float ry = ROW_Y0 + i * ROW_STEP;
                Color tc = U.hex(aCol[i]);

                //  first row (farmer) has a frame...
                if (i == 0)
                {
                    AddImage(NewRect("selbg", wrap, PAD - 4, ry - 1, CW + 8, ROW_H + 2), U.rgba(217, 138, 61, .10f), Rounded(7), true, 4);
                    AddImage(NewRect("selborder", wrap, PAD - 4, ry - 1, CW + 8, ROW_H + 2), U.rgba(217, 138, 61, .75f), Ring(7, 1.25f), true, 4);
                }

                //  small picture...
                RectTransform thumb = NewRect("thumb", wrap, PAD, ry + 2.5f, 26, 26);
                AddImage(thumb, U.rgba(255, 255, 255, .05f), Rounded(6), true, 4);
                thumb.gameObject.AddComponent<RectMask2D>();
                Sprite s;
                if (g.img.TryGetValue(aImg[i], out s))
                {
                    float h = 23;
                    float w = h * s.rect.width / s.rect.height;
                    if (w > 25)
                    {
                        w = 25;
                        h = w * s.rect.height / s.rect.width;
                    }
                    AddImage(NewRect("img", thumb, 13 - w / 2, 25 - h, w, h), Color.white, s, false, 1);
                }

                float tx = PAD + 32;
                MakeText(wrap, "name", tx, ry + 2, 100, 14, 11, U.hex(0xf4ecdd), true, sans, TextAnchor.MiddleLeft, aName[i]);
                MakeText(wrap, "tag", tx, ry + 16, CW - 32, 12, 7.5f, tc, true, sans, TextAnchor.MiddleLeft, aTag[i]);

                //  chip at the right...
                Chip c = new Chip();
                c.right = PAD + CW;
                c.y = ry + 3;
                c.bg = NewRect("chip", wrap, c.right - 36, c.y, 36, 12);
                c.bgImg = AddImage(c.bg, U.rgba(255, 255, 255, .08f), Rounded(6), true, 4);
                c.label = MakeText(c.bg, "chipt", 0, 0, 36, 12, 7.5f, INK, true, sans, TextAnchor.MiddleCenter, "");
                chips[i] = c;
            }
        }

        // card between nights

        Image skyTop;
        Text scTitle;
        Text scSub;

        void BuildScene()
        {
            skyTop = AddImage(NewRect("skytop", transitionRT, 0, 0, LEFT_W, H), U.hex(0x1b1822), null, false, 1);
            scTitle = MakeText(transitionRT, "title", 0, 300, LEFT_W, 56, 40, Color.white, true, sans, TextAnchor.MiddleCenter, "");
            scSub = MakeText(transitionRT, "sub", 0, 362, LEFT_W, 24, 18, Color.white, false, sans, TextAnchor.MiddleCenter, "");
        }

        public void ShowTransition(string msg, string sub)
        {
            if (!transitionRT.gameObject.activeSelf)
            {
                transitionRT.gameObject.SetActive(true);
            }
            //  fade in at the start and fade out at the end...
            float T = (float)C.TRANS_LEN;
            float t = (float)g.transT;
            float fade = Mathf.Clamp01(Mathf.Min((T - t) / 0.35f, t / 0.35f));
            skyTop.color = new Color(0.106f, 0.094f, 0.133f, fade);
            SetT(scTitle, msg, new Color(1f, .97f, .90f, fade));
            SetT(scSub, sub, new Color(1f, .95f, .85f, 0.92f * fade));
        }

        public void HideTransition()
        {
            if (transitionRT.gameObject.activeSelf)
            {
                transitionRT.gameObject.SetActive(false);
            }
        }

        static void SetT(Text t, string s, Color c)
        {
            if (t.text != s) t.text = s;
            t.color = c;
        }

        // live info

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

        //  reads the game every frame (never changes it) and shows what every ai does...
        void UpdateLive()
        {
            if (g.farmer == null) return;

            Color you = U.hex(0xd98a3d);
            if (g.farmer.swing > 0) SetChip(0, "Swing", you);
            else if (g.farmer.moving) SetChip(0, "Moving", you);
            else SetChip(0, "Idle", MUTED);

            if (g.pip != null && g.pip.retreating)
            {
                SetChip(1, "Hurt!", U.hex(0x7fb4e6));
            }
            else if (g.pip != null && g.pip.target != null && g.pip.target.alive)
            {
                SetChip(1, "Repairing", U.hex(0x7ec850));
            }
            else
            {
                SetChip(1, "Following", MUTED);
            }

            if (g.thistle != null)
            {
                ThistleState st = g.thistle.state;
                Color tcol = U.hex(0x9cc98a);
                if (st == ThistleState.Feed) tcol = U.hex(0xe3b341);
                if (st == ThistleState.Flee) tcol = U.hex(0x7fb4e6);
                SetChip(2, st.ToString(), tcol);
            }

            //  count the enemies...
            int sh = 0;
            int sk = 0;
            int yb = 0;
            int bb = 0;
            bool sees = false;
            bool searching = false;
            int pack = 0;
            int flee = 0;
            int eat = 0;
            int hitf = 0;
            int whirl = 0;
            for (int i = 0; i < g.enemies.Count; i++)
            {
                Enemy e = g.enemies[i];
                if (e.type == "shambler")
                {
                    sh++;
                    Shambler s = (Shambler)e;
                    if (s.act == ShamAct.PackDog) pack++;
                    else if (s.act == ShamAct.Flee) flee++;
                    else if (s.act == ShamAct.EatFruit) eat++;
                    else if (s.act == ShamAct.HitFarmer) hitf++;
                    else if (s.act == ShamAct.Whirl) whirl++;
                }
                else if (e.type == "stalker")
                {
                    sk++;
                    Stalker st = (Stalker)e;
                    if (st.sees) sees = true;
                    if (st.Searching) searching = true;
                }
                else if (e.type == "scatterling")
                {
                    yb++;
                }
                else if (e.type == "bad")
                {
                    bb++;
                }
            }

            //  the most interesting thing the shamblers do now...
            if (sh == 0) SetChip(3, "None", MUTED);
            else if (g.tornados.Count > 0) SetChip(3, "Tornado!", GameRenderer.ACT_COL[5]);
            else if (whirl >= 2) SetChip(3, whirl + " whirling", GameRenderer.ACT_COL[5]);
            else if (pack >= 2) SetChip(3, "Pack of " + pack + "!", GameRenderer.ACT_COL[2]);
            else if (hitf > 0) SetChip(3, "Hitting you!", GameRenderer.ACT_COL[3]);
            else if (flee > 0) SetChip(3, flee + " fleeing", GameRenderer.ACT_COL[4]);
            else if (eat > 0) SetChip(3, eat + " eating", GameRenderer.ACT_COL[1]);
            else SetChip(3, sh + " raiding", U.hex(0xb9a6d9));
            UpdateTabHint();

            if (sees) SetChip(4, "Sees you!", U.hex(0xf06a55));
            else if (searching) SetChip(4, "Searching", U.hex(0xe3b341));
            else if (sk > 0) SetChip(4, sk + " out", U.hex(0xe0796a));
            else SetChip(4, "None", MUTED);

            if (yb > 0) SetChip(5, yb + " alive", U.hex(0xf1d23a));
            else SetChip(5, "All lost", U.hex(0xe8806a));

            if (bb > 0) SetChip(6, bb + " out", U.hex(0xc7b4d6));
            else SetChip(6, "None", MUTED);

            //  glow under the current night dot...
            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
            pipGlowImg.color = new Color(AMBER.r, AMBER.g, AMBER.b, 0.35f + 0.35f * k);

            //  time left...
            double left = Math.Max(0, C.NIGHT_LEN - g.nightTime);
            int secs = (int)Math.Ceiling(left);
            string ts = (secs / 60) + ":" + (secs % 60).ToString("00");
            if (timeText.text != ts) timeText.text = ts;

            string tv = g.tornadoCount.ToString();
            if (statVal[3].text != tv) statVal[3].text = tv;
        }

        void SetNight(int night)
        {
            if (night == lastNight) return;
            lastNight = night;
            nightText.text = "NIGHT " + night + " / 13";
            for (int i = 0; i < 13; i++)
            {
                if (i < night - 1) pips[i].color = AMBER;
                else if (i == night - 1) pips[i].color = U.hex(0xf4ecdd);
                else pips[i].color = U.hex(0x4a4360);
            }
            float cx = PAD + 4 + (night - 1) * (CW - 8) / 12f;
            pipGlow.anchoredPosition = new Vector2(cx - 11, -(PIPS_Y - 7));
        }

        // called by game

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

        public void Tick()
        {
            UpdateLive();
        }

        //  board size in screen pixels, so the board is drawn 1 to 1 and stays sharp...
        public void BoardPixels(out int w, out int h)
        {
            float s = Mathf.Min(Screen.width / (SIDE_W + LEFT_W), Screen.height / H);
            w = Mathf.Max(64, Mathf.RoundToInt(LEFT_W * s));
            h = Mathf.Max(64, Mathf.RoundToInt(H * s));
        }

        public void SetBoardTexture(RenderTexture rt)
        {
            if (boardRaw != null) boardRaw.texture = rt;
        }

        // event log

        //  colour of a log line...
        static string LogColor(string msg)
        {
            if (msg.StartsWith("===")) return "f4ecdd";
            if (msg.Contains("tree has fallen")) return "f06a55";
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

        //  shorter texts for the narrow sidebar...
        static string Short(string m)
        {
            if (m.StartsWith("=== Night "))
            {
                m = m.Replace("=== ", "");
                m = m.Replace(" ===", "");
                return m;
            }
            if (m.StartsWith("Thistle switched to ")) return "Thistle: " + m.Substring(20);
            if (m == "Shambler routed around terrain") return "Shambler: A* reroute";
            if (m.StartsWith("Shamblers pack up on ")) return "Pack forms on " + m.Substring(21);
            if (m == "Shambler hurt, fleeing to heal") return "Shambler flees, hurt";
            if (m == "Shambler ate a fruit to heal") return "Shambler ate fruit +3hp";
            if (m == "Shambler goes for fruit to heal") return "Shambler wants fruit";
            if (m == "Shambler turns on the farmer") return "Shambler hits back!";
            if (m == "Pip is hurt, running to the farmer") return "Pip hurt, runs to you";
            if (m == "Pip repairs the weakest tree") return "Pip: fix weakest tree";
            if (m == "Stalker detected target") return "Stalker spotted you!";
            if (m == "Stalker lost sight, searching") return "Stalker: searching";
            if (m == "Scatterling saved a fruit") return "Scatterling saved fruit";
            if (m == "Bad bird caught a scatterling") return "Bad bird caught one!";
            if (m == "Bad bird stole a fruit") return "Bad bird stole fruit";
            if (m.StartsWith("Map built")) return "Map built + validated";

            //  "Night 3 survived, 5/6 trees stand"...
            if (m.StartsWith("Night ") && m.Contains(" survived, "))
            {
                string[] parts = m.Split(' ');
                string num = parts[1];
                string trees = parts[3].Replace("/6", "");
                trees = trees.Replace(",", "");
                return "Night " + num + " survived " + trees + "/6";
            }
            return m;
        }

        public void SetLog(List<string> lines)
        {
            List<string> visual = new List<string>();
            for (int n = 0; n < lines.Count; n++)
            {
                string entry = lines[n];

                //  entry looks like "[00:12] message"...
                string stamp = "";
                string msg = entry;
                if (entry.StartsWith("["))
                {
                    int close = entry.IndexOf("] ");
                    if (close > 0)
                    {
                        stamp = entry.Substring(1, close - 1);
                        msg = entry.Substring(close + 2);
                    }
                }

                string col = LogColor(msg);
                msg = Short(msg);
                bool head = msg.StartsWith("===");

                List<string> wrapped = WrapMono(msg, LOG_CHARS - (stamp.Length + 1));
                for (int i = 0; i < wrapped.Count; i++)
                {
                    string ln = wrapped[i];
                    if (head) ln = "<b>" + ln + "</b>";
                    string pre;
                    if (i == 0)
                    {
                        pre = "<color=#6f6a58>" + stamp + "</color> ";
                    }
                    else
                    {
                        pre = new string(' ', stamp.Length + 1);
                    }
                    visual.Add(pre + "<color=#" + col + ">" + ln + "</color>");
                }
            }
            if (visual.Count > LOG_MAX_LINES)
            {
                visual.RemoveRange(0, visual.Count - LOG_MAX_LINES);
            }

            //  make more text objects if needed...
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
                else
                {
                    t.gameObject.SetActive(false);
                }
            }
        }

        //  cut a text into lines with at most max letters...
        static List<string> WrapMono(string s, int max)
        {
            List<string> result = new List<string>();
            string[] words = s.Split(' ');
            string cur = "";
            for (int i = 0; i < words.Length; i++)
            {
                if (cur == "")
                {
                    cur = words[i];
                }
                else if (cur.Length + 1 + words[i].Length <= max)
                {
                    cur = cur + " " + words[i];
                }
                else
                {
                    result.Add(cur);
                    cur = words[i];
                }
            }
            if (cur != "") result.Add(cur);
            return result;
        }

        // tab hint

        RectTransform tabHint;
        RectTransform tabLegend;
        bool tabShown = false;

        void BuildTabHint()
        {
            tabHint = NewRect("tabhint", wrap, SIDE_W + 10, H - 30, 220, 20);
            AddImage(tabHint, U.rgba(20, 16, 10, .72f), Rounded(10), true, 4);
            AddImage(NewRect("key", tabHint, 6, 3, 26, 14), U.hex(0xf4ecdd), Rounded(4), true, 4);
            MakeText(tabHint, "k", 6, 3, 26, 14, 8.5f, U.hex(0x4a3420), true, sans, TextAnchor.MiddleCenter, "Tab");
            MakeText(tabHint, "t", 38, 0, 180, 20, 9.5f, INK, false, sans, TextAnchor.MiddleLeft, "show what each Shambler wants");

            //  legend with the 6 colours...
            tabLegend = NewRect("tablegend", wrap, SIDE_W + 10, H - 30 - 26, 360, 22);
            AddImage(tabLegend, U.rgba(20, 16, 10, .78f), Rounded(10), true, 4);
            string[] names = { "Raid tree", "Eat fruit", "Pack a dog", "Hit farmer", "Flee", "Whirl" };
            float lx = 10;
            for (int i = 0; i < 6; i++)
            {
                AddImage(NewRect("sw", tabLegend, lx, 7, 8, 8), GameRenderer.ACT_COL[i], Rounded(2), true, 4);
                MakeText(tabLegend, "n", lx + 11, 0, 80, 22, 9.5f, INK, true, sans, TextAnchor.MiddleLeft, names[i]);
                lx = lx + 11 + Measure(names[i], sans, 9.5f, true) + 12;
            }
            tabLegend.sizeDelta = new Vector2(lx, 22);
            tabLegend.gameObject.SetActive(false);
        }

        void UpdateTabHint()
        {
            if (g.showGoals == tabShown) return;
            tabShown = g.showGoals;
            tabLegend.gameObject.SetActive(tabShown);
        }

        // start, win, lose cards

        const float CARD_W = 500;
        const float CARD_PAD = 30;

        public void ShowTitle()
        {
            string text = "Defend your <b>6 trees</b> for <b>13 nights</b> (1 min each). <b>Yellow birds</b> save fallen fruit by eating it; <b>black birds</b> steal it and hunt the yellows. <b>Pip</b> repairs your weakest tree; <b>Thistle</b> hunts. Rain brings mud, hills and bushes that the enemies can't cross, but you can.";
            BuildOverlay("Orchard of the Last Season", U.hex(0xe9b850), text, true, "Begin Night 1", 1);
        }

        public void ShowWin(int s, int saved, int lost)
        {
            string text = "You defended the orchard through all 13 nights.<br><b>" + s + "/6</b> trees stand, <b>" + saved + "</b> fruit saved, <b>" + lost + "</b> lost.";
            BuildOverlay("You made it!", U.hex(0x8fd36a), text, false, "Play again", 2);
        }

        public void ShowLose(int night, int saved, int lost)
        {
            string text = "You held to night <b>" + night + "</b>.<br><b>" + saved + "</b> saved, <b>" + lost + "</b> lost.";
            BuildOverlay("The last tree has fallen", U.hex(0xe9b850), text, false, "Try again", 2);
        }

        public void ShowFarmerDown(int night, int saved, int lost)
        {
            string text = "A stalker caught you on night <b>" + night + "</b>.<br><b>" + saved + "</b> saved, <b>" + lost + "</b> lost.";
            BuildOverlay("The farmer has fallen", U.hex(0xe9b850), text, false, "Try again", 2);
        }

        public void HideOverlay()
        {
            overlay.gameObject.SetActive(false);
            overlayVisible = false;
            btnPressed = false;
        }

        //  action 1 = start the game, action 2 = restart...
        void RunAction()
        {
            if (btnAction == 1) g.StartClicked();
            if (btnAction == 2) g.Restart();
        }

        void BuildOverlay(string h1, Color h1c, string rich, bool title, string btnLabel, int action)
        {
            //  remove the old content...
            for (int i = overlayContent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(overlayContent.GetChild(i).gameObject);
            }
            for (int i = card.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(card.GetChild(i).gameObject);
            }
            Image old = card.GetComponent<Image>();

            float cw = CARD_W - 2 * CARD_PAD;
            float y = 0;
            Sprite disc = Shapes.Circle();
            Sprite glow = Shapes.Glow();

            //  moon...
            float mx = cw / 2f;
            AddImage(NewRect("glow", overlayContent, mx - 40, y - 12, 80, 80), new Color(h1c.r, h1c.g, h1c.b, .25f), glow, false, 1);
            AddImage(NewRect("moon", overlayContent, mx - 20, y + 8, 40, 40), U.hex(0xf0e4c4), disc, false, 1);
            AddImage(NewRect("c1", overlayContent, mx - 11, y + 16, 10, 8), U.rgba(190, 175, 140, .55f), disc, false, 1);
            AddImage(NewRect("c2", overlayContent, mx + 4, y + 30, 7, 6), U.rgba(190, 175, 140, .55f), disc, false, 1);
            y = y + 62;

            //  heading...
            float h1H = 28 * N_SANS;
            MakeText(overlayContent, "h1", 0, y, cw, h1H, 28, h1c, true, sans, TextAnchor.MiddleCenter, h1);
            y = y + h1H + 12;

            //  text lines...
            List<string> plines = WrapRich(rich, 14, 430);
            for (int i = 0; i < plines.Count; i++)
            {
                MakeText(overlayContent, "p", 0, y, cw, 14 * 1.6f, 14, U.hex(0xe7d8bb), false, sans, TextAnchor.MiddleCenter, plines[i]);
                y = y + 14 * 1.6f;
            }
            y = y + 16;

            if (title)
            {
                //   techniques as tiny miny chips
                //  they wrap onto as many rows as needed and each row is centred...
                string[] names = { "Finite State machine", "Goal driven", "Behaviour tree", "Navigation", "Perception", "Flocking", "Procedural gen" };
                Color[] cc = { U.hex(0xe3b341), U.hex(0xb9a6d9), U.hex(0x7ec850), U.hex(0x8fd3c8), U.hex(0xe0796a), U.hex(0xf1d23a), U.hex(0x7fb4e6) };
                int n = names.Length;
                float gap = 8;
                float[] cw2 = new float[n];
                for (int i = 0; i < n; i++)
                {
                    cw2[i] = Measure(names[i], sans, 11, true) + 26;
                }
                //  pack chips into rows that fit the card width...
                int start = 0;
                while (start < n)
                {
                    //  how many chips fit on this row...
                    int end = start;
                    float rowW = 0;
                    while (end < n)
                    {
                        float add = cw2[end];
                        if (end > start) add = add + gap;
                        if (rowW + add > cw && end > start) break;
                        rowW = rowW + add;
                        end++;
                    }
                    //  centre the row and place its chips...
                    float x = (cw - rowW) / 2;
                    for (int i = start; i < end; i++)
                    {
                        Color c = cc[i];
                        AddImage(NewRect("chip", overlayContent, x, y, cw2[i], 22), new Color(c.r, c.g, c.b, .13f), Rounded(11), true, 4);
                        AddImage(NewRect("dot", overlayContent, x + 8, y + 8, 6, 6), c, disc, false, 1);
                        MakeText(overlayContent, "chipt", x + 14, y, cw2[i] - 16, 22, 11, c, true, sans, TextAnchor.MiddleCenter, names[i]);
                        x = x + cw2[i] + gap;
                    }
                    y = y + 28;
                    start = end;
                }
                y = y + 10;
                AddImage(NewRect("rule", overlayContent, 40, y, cw - 80, 1), U.rgba(255, 255, 255, .08f), null, false, 1);
                y = y + 14;
                BuildCtrls(cw, y);
                y = y + 13 * 1.9f + 16;
            }

            //  button...
            float bh = 16 * N_SANS + 24;
            float bw = Measure(btnLabel, sans, 16, true) + 64;
            float bx = (cw - bw) / 2;
            AddImage(NewRect("btnshadow", overlayContent, bx, y + 4, bw, bh), U.hex(0x8f4a28), Rounded(10), true, 4);
            btnFace = NewRect("btn", overlayContent, bx, y, bw, bh);
            AddImage(btnFace, U.hex(0xcd6d3c), Rounded(10), true, 4);
            AddImage(NewRect("btnhi", btnFace, 6, 2, bw - 12, 2), U.rgba(255, 255, 255, .25f), Rounded(1), true, 4);
            MakeText(btnFace, "label", 0, 0, bw, bh, 16, Color.white, true, sans, TextAnchor.MiddleCenter, btnLabel);
            btnY = y;
            y = y + bh + 4;

            //  size and place of the card...
            float contentH = y;
            float cardH = contentH + 2 * CARD_PAD;
            float top = (H - cardH) / 2;
            float left = (LEFT_W - CARD_W) / 2;
            card.anchoredPosition = new Vector2(left, -top);
            card.sizeDelta = new Vector2(CARD_W, cardH);
            if (old == null)
            {
                AddImage(card, U.rgba(42, 36, 58, .97f), Rounded(16), true, 4);
            }
            AddImage(NewRect("ring", card, 0, 0, CARD_W, cardH), U.rgba(233, 184, 80, .22f), Ring(16, 1.5f), true, 4);
            overlayContent.anchoredPosition = new Vector2(left + CARD_PAD, -(top + CARD_PAD));
            overlayContent.sizeDelta = new Vector2(cw, contentH);

            btnAction = action;
            btnPressed = false;
            overlay.gameObject.SetActive(true);
            overlayVisible = true;
        }

        //  the keys line: W A S D move, Space scythe, F catch...
        void BuildCtrls(float cw, float y)
        {
            string[] seg = { "W A S D", " move   ", "Space", " scythe (pluck fruit)   ", "F", " catch fruit" };
            bool[] isKbd = { true, false, true, false, true, false };
            float[] wd = new float[6];
            float total = 0;
            for (int i = 0; i < 6; i++)
            {
                if (isKbd[i])
                {
                    wd[i] = Measure(seg[i], sans, 11, true) + 14;
                }
                else
                {
                    wd[i] = Measure(seg[i], sans, 13, false);
                }
                total = total + wd[i];
            }
            float x = (cw - total) / 2;
            float lineH = 13 * 1.9f;
            for (int i = 0; i < 6; i++)
            {
                if (isKbd[i])
                {
                    float top = y + 2;
                    float h = lineH - 4;
                    AddImage(NewRect("kbd", overlayContent, x, top + 2, wd[i], h), U.hex(0x8a7a5a), Rounded(5), true, 4);
                    AddImage(NewRect("kbdin", overlayContent, x, top, wd[i], h), U.hex(0xf4ecdd), Rounded(5), true, 4);
                    MakeText(overlayContent, "kbdt", x, top, wd[i], h, 11, U.hex(0x4a3420), true, sans, TextAnchor.MiddleCenter, seg[i]);
                }
                else
                {
                    MakeText(overlayContent, "ctl", x, y, wd[i] + 4, lineH, 13, U.hex(0xcbb892), false, sans, TextAnchor.MiddleLeft, seg[i]);
                }
                x = x + wd[i];
            }
        }

        //  one word of the card text...
        class Word
        {
            public string text = "";
            public bool bold = false;
            public bool lineBreak = false;
            public float w = 0;
        }

        //  cut a text with <b> and <br> into lines that are not wider than maxW...
        List<string> WrapRich(string rich, float px, float maxW)
        {
            //  step 1: split into words...
            List<Word> words = new List<Word>();
            bool bold = false;
            Word cur = new Word();
            cur.bold = false;
            int i = 0;
            while (i < rich.Length)
            {
                if (string.CompareOrdinal(rich, i, "<b>", 0, 3) == 0)
                {
                    bold = true;
                    i = i + 3;
                    continue;
                }
                if (string.CompareOrdinal(rich, i, "</b>", 0, 4) == 0)
                {
                    bold = false;
                    i = i + 4;
                    continue;
                }
                if (string.CompareOrdinal(rich, i, "<br>", 0, 4) == 0)
                {
                    if (cur.text != "") words.Add(cur);
                    Word br = new Word();
                    br.lineBreak = true;
                    words.Add(br);
                    cur = new Word();
                    i = i + 4;
                    continue;
                }
                char c = rich[i];
                if (c == ' ')
                {
                    if (cur.text != "") words.Add(cur);
                    cur = new Word();
                }
                else
                {
                    //  the whole word is bold if its first letter is bold...
                    if (cur.text == "") cur.bold = bold;
                    cur.text = cur.text + c;
                    cur.w = cur.w + CharW(c, cur.bold, px);
                }
                i++;
            }
            if (cur.text != "") words.Add(cur);

            //  step 2: fill the lines...
            List<string> lines = new List<string>();
            string line = "";
            float lineW = 0;
            bool lineHas = false;
            for (int k = 0; k < words.Count; k++)
            {
                Word w = words[k];
                if (w.lineBreak)
                {
                    lines.Add(line);
                    line = "";
                    lineW = 0;
                    lineHas = false;
                    continue;
                }
                float spaceW = CharW(' ', false, px);
                if (lineHas && lineW + spaceW + w.w > maxW)
                {
                    lines.Add(line);
                    line = "";
                    lineW = 0;
                    lineHas = false;
                }
                if (lineHas)
                {
                    line = line + " ";
                    lineW = lineW + spaceW;
                }
                if (w.bold)
                {
                    line = line + "<b>" + w.text + "</b>";
                }
                else
                {
                    line = line + w.text;
                }
                lineW = lineW + w.w;
                lineHas = true;
            }
            if (lineHas) lines.Add(line);
            return lines;
        }

        float CharW(char c, bool bold, float px)
        {
            string style = "n";
            if (bold) style = "b";
            string key = c + style + px;
            float w;
            if (widthCache.TryGetValue(key, out w)) return w;
            w = Measure(c.ToString(), sans, px, bold);
            widthCache[key] = w;
            return w;
        }

        // width of a text in pixels (measured at 10 times the size to be exact)
        float Measure(string s, Font f, float px, bool bold)
        {
            int size = Mathf.RoundToInt(px * 10);
            FontStyle st = FontStyle.Normal;
            if (bold) st = FontStyle.Bold;
            f.RequestCharactersInTexture(s, size, st);
            float w = 0;
            for (int i = 0; i < s.Length; i++)
            {
                CharacterInfo ci;
                if (f.GetCharacterInfo(s[i], out ci, size, st))
                {
                    w = w + ci.advance;
                }
            }
            return w / 10f;
        }

        // button

        public void PointerDown(Vector2 screen)
        {
            if (!overlayVisible || btnFace == null) return;
            if (RectTransformUtility.RectangleContainsScreenPoint(btnFace, screen, null))
            {
                btnPressed = true;
                //  button moves 2 pixels down when pressed...
                btnFace.anchoredPosition = new Vector2(btnFace.anchoredPosition.x, -(btnY + 2));
            }
        }

        public void PointerUp(Vector2 screen)
        {
            if (!btnPressed) return;
            btnPressed = false;
            if (btnFace != null)
            {
                btnFace.anchoredPosition = new Vector2(btnFace.anchoredPosition.x, -btnY);
            }
            if (overlayVisible && btnFace != null && RectTransformUtility.RectangleContainsScreenPoint(btnFace, screen, null))
            {
                RunAction();
            }
        }

        //  enter key on the title card...
        public void ClickStartButton()
        {
            if (overlayVisible) RunAction();
        }

        // ui helpers

        //  new empty rectangle. x, y from the top left corner of the parent...
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

        static Text MakeText(Transform parent, string name, float x, float y, float w, float h, float px, Color col, bool bold, Font f, TextAnchor anchor, string s)
        {
            RectTransform rt = NewRect(name, parent, x, y, w, h);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = f;
            t.fontSize = Mathf.Max(1, Mathf.RoundToInt(px));
            if (bold)
            {
                t.fontStyle = FontStyle.Bold;
            }
            else
            {
                t.fontStyle = FontStyle.Normal;
            }
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

        //  textures

        // distance to a rounded box (negative inside)
        static float BoxSdf(float px, float py, float cx, float cy, float hw, float hh, float rad)
        {
            float qx = Mathf.Abs(px - cx) - (hw - rad);
            float qy = Mathf.Abs(py - cy) - (hh - rad);
            float ox = Mathf.Max(qx, 0);
            float oy = Mathf.Max(qy, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - rad;
        }

        //  rounded rectangle that can be stretched (9 slice), drawn 4 times bigger for smooth edges...
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
            {
                for (int x = 0; x < N; x++)
                {
                    float d = BoxSdf(x + .5f, y + .5f, N / 2f, N / 2f, N / 2f, N / 2f, rad);
                    float a = Mathf.Clamp01(0.5f - d);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
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

        //  only the border of a rounded rectangle...
        static Sprite Ring(float r, float width)
        {
            string key = "ring" + r + "_" + width;
            Sprite cached;
            if (shapeCache.TryGetValue(key, out cached)) return cached;
            int SS = 4;
            int N = Mathf.CeilToInt((2 * r + 2) * SS);
            float rad = r * SS;
            float wd = width * SS;
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dOut = BoxSdf(x + .5f, y + .5f, N / 2f, N / 2f, N / 2f, N / 2f, rad);
                    float dIn = BoxSdf(x + .5f, y + .5f, N / 2f, N / 2f, N / 2f - wd, N / 2f - wd, Mathf.Max(rad - wd, 0));
                    float a = Mathf.Clamp01(0.5f - dOut) * (1 - Mathf.Clamp01(0.5f - dIn));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
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

        //  colour fade from a to b. vertical = top to bottom, else left to right...
        static Sprite Gradient(Color a, Color b, bool vertical)
        {
            string key = "grad" + a.ToString() + b.ToString() + vertical;
            Sprite cached;
            if (shapeCache.TryGetValue(key, out cached)) return cached;

            int w = 256;
            int h = 1;
            if (vertical)
            {
                w = 1;
                h = 256;
            }
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int i = 0; i < 256; i++)
            {
                Color c = Color.Lerp(a, b, i / 255f);
                if (vertical)
                {
                    t.SetPixel(0, 255 - i, c);
                }
                else
                {
                    t.SetPixel(i, 0, c);
                }
            }
            t.Apply();
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            Sprite s = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100);
            shapeCache[key] = s;
            return s;
        }
    }
}
