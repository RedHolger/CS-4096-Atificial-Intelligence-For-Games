using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orchard //george manu zian
{
    // main game scrip handles all game state, main loop, nights and player input... 
    public class OrchardGame : MonoBehaviour
    {
        public static OrchardGame I;

        //  map...
        public int[,] grid;
        public int[,] baseGrid;
        public List<Cell> treeCells;
        public bool staticBuilt = false;
        public bool[,] reach;
        public List<Tuft> tufts = new List<Tuft>();
        public List<Branch> branches = new List<Branch>();
        public List<FallenLog> logs = new List<FallenLog>();

        //  things in the game...
        public List<TreeEntity> trees = new List<TreeEntity>();
        public List<Enemy> enemies = new List<Enemy>();
        public List<Bait> baits = new List<Bait>();
        public List<FlyingFruit> flyingFruit = new List<FlyingFruit>();
        public List<Tornado> tornados = new List<Tornado>();
        public Farmer farmer;
        public Pip pip;
        public Thistle thistle;

        //  numbers...
        public int tornadoCount = 0;
        public double lastTornadoAt = -99;
        public int night = 1;
        public double nightTime = 0;
        public int fruitSaved = 0;
        public int nextRevive = 200;   // next fruit count that brings a tree back...
        public int fruitLost = 0;
        public int fruitFallen = 0;
        public bool running = false;
        public bool gameOver = false;
        public List<SpawnEvent> spawnQueue = new List<SpawnEvent>();

        //  transition between nights...
        public bool inTransition = false;
        public double transT = 0;
        public string transMsg = "";
        public string transSub = "";

        public List<string> logLines = new List<string>();
        public HashSet<string> keys = new HashSet<string>();
        public bool showGoals = false;

        public Dictionary<string, Sprite> img;
        GameRenderer rend;
        OrchardUI ui;
        Dictionary<string, double> logCd = new Dictionary<string, double>();

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            SetupCameras();
        }

        void Start()
        {
            img = SpriteLoader.LoadAll();
            rend = new GameRenderer(img);
            ui = new OrchardUI(this, rend.rt);
            Boot();
        }

        //  scene cameras must not see the board. make sure one camera clears the screen...
        void SetupCameras() 
        {
            bool hasScreenCam = false;
            foreach (Camera c in Camera.allCameras)
            {
                c.cullingMask = c.cullingMask & ~(1 << GameRenderer.LAYER);
                if (c.targetTexture == null)
                {
                    hasScreenCam = true;
                }
            }
            if (!hasScreenCam)
            {
                GameObject go = new GameObject("Orchard Screen Camera");
                Camera cam = go.AddComponent<Camera>();
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = U.hex(0x1b1822);
                cam.depth = -100;
            }
        }

        void Boot()
        {
            MapGenerator.BuildStatic(this);
            MapGenerator.NewNightTerrain(this);
            SpawnActors();
            logLines.Clear();
            ui.SetLog(logLines);
            ui.SetSeason(U.season(1));
            ui.ShowTitle();
        }

        //  farmer and dogs start in the middle...
        public void SpawnActors()
        {
            Pos c = U.cc(C.GW / 2, C.GH / 2);
            farmer = new Farmer(c.x, c.y);
            pip = new Pip(c.x - C.CELL, c.y);
            thistle = new Thistle(c.x + C.CELL, c.y);
        }

        // main loop

        void Update()
        {
            double dt = Math.Min(0.033, Time.unscaledDeltaTime);
            if (running && !gameOver)
            {
                if (inTransition)
                {
                    transT = transT - dt;
                    if (transT <= 0)
                    {
                        inTransition = false;
                        StartNight();
                    }
                }
                else
                {
                    UpdateGame(dt);
                }
            }

            int bw;
            int bh;
            ui.BoardPixels(out bw, out bh);
            if (rend.Resize(bw, bh))
            {
                ui.SetBoardTexture(rend.rt);
            }
            rend.Render(this, dt);

            if (inTransition)
            {
                ui.ShowTransition(transMsg, transSub);
            }
            else
            {
                ui.HideTransition();
            }
            ui.Tick();
        }

        void UpdateGame(double dt)
        {
            nightTime = nightTime + dt;

            //  new enemies arrive...
            while (spawnQueue.Count > 0 && spawnQueue[0].t <= nightTime)
            {
                SpawnEvent ev = spawnQueue[0];
                spawnQueue.RemoveAt(0);
                Fire(ev);
            }

            farmer.update(dt);
            pip.update(dt);
            thistle.update(dt);

            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].type == "shambler")
                {
                    ((Shambler)enemies[i]).biting = false;
                }
            }
            for (int i = 0; i < enemies.Count; i++)
            {
                enemies[i].update(dt);
            }

            PackBites(pip, dt);
            PackBites(thistle, dt);
            UpdateTornados(dt);

            //  remove dead enemies...
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                if (enemies[i].dead)
                {
                    enemies.RemoveAt(i);
                }
            }

            //  fruit in the air falls down and becomes bait...
            for (int i = flyingFruit.Count - 1; i >= 0; i--)
            {
                FlyingFruit f = flyingFruit[i];
                f.life = f.life - dt;
                f.y = f.y + f.vy * dt;
                f.vy = f.vy + 50 * dt;
                if (f.life <= 0)
                {
                    baits.Add(new Bait(f.x, f.y));
                    flyingFruit.RemoveAt(i);
                    LogEvent("Fruit dropped, now bait");
                }
            }

            for (int i = 0; i < trees.Count; i++)
            {
                if (trees[i].flash > 0) trees[i].flash = trees[i].flash - dt;
                if (trees[i].healGlow > 0) trees[i].healGlow = trees[i].healGlow - dt;
            }

            //  every 200 fruit the birds save, one fallen tree comes back... //george manu zian
            while (fruitSaved >= nextRevive)
            {
                reviveOneTree();
                nextRevive = nextRevive + 200;
            }

            if (AliveTrees() == 0)
            {
                Lose();
                return;
            }


            //  night ends when time is over and all ground enemies are gone...
            if (nightTime >= C.NIGHT_LEN && spawnQueue.Count == 0 && CountGround() == 0)
            {
                EndNight();
            }
            if (nightTime >= C.NIGHT_LEN + 8)
            {
                EndNight();
            }
            Hud();
        }

        int CountGround()
        {
            int n = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].type != "scatterling") n++;
            }
            return n;
        }

        int CountYellow()
        {
            int n = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].type == "scatterling") n++;
            }
            return n;
        }

        int AliveTrees()
        {
            int n = 0;
            for (int i = 0; i < trees.Count; i++)
            {
                if (trees[i].alive) n++;
            }
            return n;
        }

        //  bring back the first fallen tree we find... //george manu zian
        void reviveOneTree()
        {
            for (int i = 0; i < trees.Count; i++)
            {
                if (!trees[i].alive)
                {
                    trees[i].revive();
                    LogEvent("A tree came back! (200 fruit saved)");
                    return;
                }
            }
        }

        //  only yellow birds stay for the next night...
        void KeepOnlyYellow()
        {
            List<Enemy> keep = new List<Enemy>();
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].type == "scatterling")
                {
                    keep.Add(enemies[i]);
                }
            }
            enemies = keep;
        }

        void Hud()
        {
            if (ui == null) return;
            double frac = U.clamp(1 - nightTime / C.NIGHT_LEN, 0, 1);
            ui.SetHud(night, fruitSaved + " / " + fruitFallen, fruitLost.ToString(), AliveTrees(), frac);
        }

        // fruit

        //  key F: catch the nearest fruit that is in the air...
        public void TryCatch()
        {
            if (!running) return;
            FlyingFruit best = null;
            double bd = C.CATCH_RANGE;
            for (int i = 0; i < flyingFruit.Count; i++)
            {
                double d = U.dist(flyingFruit[i], farmer);
                if (d < bd)
                {
                    bd = d;
                    best = flyingFruit[i];
                }
            }
            if (best != null)
            {
                flyingFruit.Remove(best);
                fruitSaved++;
                LogEvent("Farmer caught a fruit");
            }
        }

        //  key space: hit enemies in front, or pluck fruit from a tree...
        public void TrySwing()
        {
            if (!running) return;
            farmer.swing = 0.32;

            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy e = enemies[i];
                //  yellow birds cannot be hit...
                if (e.type == "scatterling") continue;
                double d = U.dist(e, farmer);
                if (d > C.SCYTHE_RANGE) continue;
                double dd = d;
                if (d == 0) dd = 1;
                double dx = (e.x - farmer.x) / dd;
                double dy = (e.y - farmer.y) / dd;
                //  only enemies in front of the farmer...
                if (dx * farmer.faceX + dy * farmer.faceY > 0.15)
                {
                    e.hit(C.SCYTHE_DMG);
                    e.lastHit = nightTime;
                }
            }

            for (int i = 0; i < trees.Count; i++)
            {
                TreeEntity t = trees[i];
                if (t.alive && t.fruit > 0 && U.dist(t, farmer) < C.CELL * 1.6)
                {
                    t.fruit--;
                    fruitFallen++;
                    FlyingFruit ff = new FlyingFruit();
                    ff.x = t.x;
                    ff.y = t.y - C.CELL * 0.5;
                    ff.vy = -28;
                    ff.life = 2.4;
                    flyingFruit.Add(ff);
                    LogEvent("Farmer plucked a fruit");
                    break;
                }
            }
        }

        // nights

        void StartNight()
        {
            nightTime = 0;
            //  farmer is fully rested...
            farmer.hp = C.FARMER_HP;
            farmer.invuln = 0;
            farmer.sinceHit = 99;
            baits = new List<Bait>();
            flyingFruit = new List<FlyingFruit>();
            tornados = new List<Tornado>();
            lastTornadoAt = -99;

            if (!staticBuilt)
            {
                MapGenerator.BuildStatic(this);
            }
            MapGenerator.NewNightTerrain(this);
            SpawnActors();

            for (int i = 0; i < trees.Count; i++)
            {
                TreeEntity t = trees[i];
                if (t.alive)
                {
                    t.hp = t.maxhp;
                    t.fruit = 4;
                    t.firstHit = 1e18;
                    t.flash = 0;
                    t.healGlow = 0;
                }
            }

            KeepOnlyYellow();
            //  night 1: make the yellow birds...
            while (night == 1 && CountYellow() < C.YELLOW_TARGET)
            {
                Pos p = U.randSky();
                enemies.Add(new Bird(p.x, p.y, false));
            }
            BuildWave();
            if (ui != null)
            {
                ui.SetSeason(U.season(night));
            }
            LogEvent("=== Night " + night + " of " + C.TOTAL_NIGHTS + " begins ===");
            if (logs.Count > 0)
            {
                LogEvent("Fallen logs block " + logs.Count + " paths");
            }
        }

        //  night n: 4+n shamblers, n stalkers, 4 black birds...
        void BuildWave()
        {
            List<SpawnEvent> evs = new List<SpawnEvent>();
            int sh = 4 + night;
            int st = night;
            for (int i = 0; i < sh; i++) evs.Add(MakeEv("shambler", 1));
            for (int i = 0; i < st; i++) evs.Add(MakeEv("stalker", 1));
            evs.Add(MakeEv("bad", 4));

            //  spread the arrival times over the night...
            double a = 3;
            double b = C.NIGHT_LEN - 12;
            for (int i = 0; i < evs.Count; i++)
            {
                double frac = 0;
                if (evs.Count > 1)
                {
                    frac = (double)i / (evs.Count - 1);
                }
                evs[i].t = Math.Max(1, a + (b - a) * frac + (U.R() - .5) * 4);
            }
            U.SortEvents(evs);
            spawnQueue = evs;
        }

        SpawnEvent MakeEv(string type, int n)
        {
            SpawnEvent e = new SpawnEvent();
            e.type = type;
            e.n = n;
            return e;
        }

        //  random spot on the board edge that the orchard can be reached from...
        Pos EdgePos()
        {
            int cx = 0;
            int cy = 0;
            int g = 0;
            bool bad = true;
            while (bad && g < 200)
            {
                int e = (int)(U.R() * 4);
                if (e == 0)
                {
                    cx = 0;
                    cy = (int)(U.R() * C.GH);
                }
                else if (e == 1)
                {
                    cx = C.GW - 1;
                    cy = (int)(U.R() * C.GH);
                }
                else if (e == 2)
                {
                    cy = 0;
                    cx = (int)(U.R() * C.GW);
                }
                else
                {
                    cy = C.GH - 1;
                    cx = (int)(U.R() * C.GW);
                }
                g++;
                bad = U.groundBlocked(cx, cy) || (reach != null && !reach[cx, cy]);
            }
            return U.cc(cx, cy);
        }

        void Fire(SpawnEvent ev)
        {
            Pos o = EdgePos();
            for (int i = 0; i < ev.n; i++)
            {
                double ox = o.x + (U.R() - .5) * C.CELL * 2;
                double oy = o.y + (U.R() - .5) * C.CELL * 2;
                if (ev.type == "shambler")
                {
                    enemies.Add(new Shambler(o.x, o.y));
                }
                else if (ev.type == "stalker")
                {
                    enemies.Add(new Stalker(o.x, o.y));
                }
                else
                {
                    enemies.Add(new Bird(ox, oy, true));
                }
            }
        }

        void EndNight()
        {
            int s = AliveTrees();
            LogEvent("Night " + night + " survived, " + s + "/6 trees stand");
            KeepOnlyYellow();
            baits = new List<Bait>();
            flyingFruit = new List<FlyingFruit>();
            if (night >= C.TOTAL_NIGHTS)
            {
                Win(s);
                return;
            }
            night++;
            transT = C.TRANS_LEN;
            transMsg = "Night " + night;
            transSub = s + " / 6 trees still stand";
            inTransition = true;
        }

        void Win(int s)
        {
            running = false;
            gameOver = true;
            if (ui != null) ui.ShowWin(s, fruitSaved, fruitLost);
        }

        void Lose()
        {
            running = false;
            gameOver = true;
            if (ui != null) ui.ShowLose(night, fruitSaved, fruitLost);
        }

        public void FarmerDown()
        {
            running = false;
            gameOver = true;
            if (ui != null) ui.ShowFarmerDown(night, fruitSaved, fruitLost);
        }

        void ResetNumbers()
        {
            night = 1;
            fruitSaved = 0;
            nextRevive = 200;
            fruitLost = 0;
            fruitFallen = 0;
            tornadoCount = 0;
            gameOver = false;
            inTransition = false;
            enemies = new List<Enemy>();
        }

        //  button: Begin Night 1...
        public void StartClicked()
        {
            if (ui != null) ui.HideOverlay();
            ResetNumbers();
            StartNight();
            running = true;
        }

        //  button: Play again / Try again...
        public void Restart()
        {
            ResetNumbers();
            staticBuilt = false;
            logLines.Clear();
            if (ui != null) ui.HideOverlay();
            StartNight();
            running = true;
        }

        // pack rule (emergent behaviour) //george worked on goal-driven behaviour that contributes to emergent pack

        public int PackSize(Mover dog)
        {
            int n = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].type != "shambler") continue;
                Shambler s = (Shambler)enemies[i];
                if (!s.dead && s.biting && s.dogT == dog && U.dist(s, dog) < 26)
                {
                    n++;
                }
            }
            return n;
        }

        void PackBites(Mover dog, double dt)
        {
            int n = PackSize(dog);
            if (n < 2) return;
            double dmg = 0.7 * n * dt;
            if (dog is Pip)
            {
                ((Pip)dog).Hurt(dmg);
                LogOnce("bitep", "Pack of " + n + " bites Pip!");
            }
            else
            {
                ((Thistle)dog).Hurt(dmg);
                LogOnce("bitet", "Pack of " + n + " bites Thistle!");
            }
        }

        // tornado (emergent behaviour)
        //george worked on it so tornado move towards yellow birds and trigger confusion behaviour
        public void MakeTornado(Pos c)
        {
            tornados.Add(new Tornado(c.x, c.y));
            lastTornadoAt = nightTime;
            tornadoCount++;
            LogEvent("Shamblers spun a dust tornado!");
        }

        void UpdateTornados(double dt)
        {
            for (int i = tornados.Count - 1; i >= 0; i--)
            {
                Tornado t = tornados[i];
                t.life = t.life - dt;
                t.spin = t.spin + dt;
                if (t.life <= 0)
                {
                    tornados.RemoveAt(i);
                    continue;
                }

                //  drifts slowly to the nearest yellow bird it did not catch yet...
                Bird near = null;
                double nd = C.CELL * 12;
                for (int j = 0; j < enemies.Count; j++)
                {
                    if (enemies[j].type != "scatterling") continue;
                    Bird b = (Bird)enemies[j];
                    if (b.dead || t.HasHit(b)) continue;
                    double d = U.dist(b, t);
                    if (d < nd)
                    {
                        nd = d;
                        near = b;
                    }
                }
                if (near != null && nd > 1)
                {
                    t.x = t.x + (near.x - t.x) / nd * 70 * dt;
                    t.y = t.y + (near.y - t.y) / nd * 70 * dt;
                }

                //  birds inside the tornado get dizzy...
                int n = 0;
                for (int j = 0; j < enemies.Count; j++)
                {
                    if (enemies[j].type != "scatterling") continue;
                    Bird b = (Bird)enemies[j];
                    if (b.dead || t.HasHit(b)) continue;
                    if (U.dist(b, t) < t.r)
                    {
                        t.hit.Add(b);
                        b.Confuse(t);
                        n++;
                    }
                }
                if (n > 0)
                {
                    string s = "";
                    if (n > 1) s = "s";
                    LogOnce("dizzy", n + " scatterling" + s + " dizzy: no fruit for 10s");
                }
            }
        }

        public void ShamblerAte(Shambler s)
        {
            LogOnce("ate", "Shambler ate a fruit to heal");
        }

        // log

        //  same message only again after 3 seconds...
        public void LogOnce(string key, string msg)
        {
            double t;
            if (logCd.TryGetValue(key, out t))
            {
                if (nightTime - t < 3 && nightTime >= t) return;
            }
            logCd[key] = nightTime;
            LogEvent(msg);
        }

        public void LogEvent(string t)
        {
            int s = (int)Math.Floor(nightTime);
            string mm = U.two(s / 60);
            string ss = U.two(s % 60);
            logLines.Add("[" + mm + ":" + ss + "] " + t);
            while (logLines.Count > 12)
            {
                logLines.RemoveAt(0);
            }
            if (ui != null) ui.SetLog(logLines);
        }

        // input
        //  keys and mouse come from OnGUI so it works with any input setting...

        void OnGUI()
        {
            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                string k = KeyName(e.keyCode);
                if (k != "")
                {
                    keys.Add(k);
                    if (k == " ") TrySwing();
                    if (k == "f") TryCatch();
                    if (k == "tab") showGoals = !showGoals;
                    if (k == "enter" && !running && !gameOver) ui.ClickStartButton();
                    e.Use();
                }
            }
            else if (e.type == EventType.KeyUp && e.keyCode != KeyCode.None)
            {
                string k = KeyName(e.keyCode);
                if (k != "")
                {
                    keys.Remove(k);
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDown && e.button == 0)
            {
                ui.PointerDown(new Vector2(e.mousePosition.x, Screen.height - e.mousePosition.y));
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                ui.PointerUp(new Vector2(e.mousePosition.x, Screen.height - e.mousePosition.y));
            }
        }

        static string KeyName(KeyCode kc)
        {
            if (kc == KeyCode.W) return "w";
            if (kc == KeyCode.A) return "a";
            if (kc == KeyCode.S) return "s";
            if (kc == KeyCode.D) return "d";
            if (kc == KeyCode.F) return "f";
            if (kc == KeyCode.Tab) return "tab";
            if (kc == KeyCode.UpArrow) return "arrowup";
            if (kc == KeyCode.DownArrow) return "arrowdown";
            if (kc == KeyCode.LeftArrow) return "arrowleft";
            if (kc == KeyCode.RightArrow) return "arrowright";
            if (kc == KeyCode.Space) return " ";
            if (kc == KeyCode.Return) return "enter";
            if (kc == KeyCode.KeypadEnter) return "enter";
            return "";
        }

        //  no stuck keys when the window loses focus...
        void OnApplicationFocus(bool focus)
        {
            if (!focus)
            {
                keys.Clear();
            }
        }
    }

    //  sprites are stored as png bytes so unity does not change the pixels...
    public static class SpriteLoader
    {
        public static string[] Names = {
            "farmer", "pip", "thistle", "shambler", "stalker", "scatterling", "badscatter",
            "tree", "apple", "rock", "grass", "icon_apple", "icon_heart", "icon_tree" };

        public static Dictionary<string, Sprite> LoadAll()
        {
            Dictionary<string, Sprite> d = new Dictionary<string, Sprite>();
            for (int i = 0; i < Names.Length; i++)
            {
                string n = Names[i];
                TextAsset ta = Resources.Load<TextAsset>("OrchardSprites/" + n);
                if (ta == null)
                {
                    Debug.LogError("Orchard: missing sprite Resources/OrchardSprites/" + n + ".bytes");
                    continue;
                }

                //  pixel art keeps hard edges, icons are smooth...
                bool icon = n.StartsWith("icon_");
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                tex.LoadImage(ta.bytes);
                if (icon)
                {
                    tex.filterMode = FilterMode.Trilinear;
                }
                else
                {
                    tex.filterMode = FilterMode.Point;
                }
                tex.anisoLevel = 0;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.name = n;
                Rect rect = new Rect(0, 0, tex.width, tex.height);
                Sprite s = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
                s.name = n;
                d[n] = s;
            }
            return d;
        }
    }

    //  starts the game when you press play, in any scene...
    public static class OrchardBoot
    {
        static bool started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            started = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (started) return;
            started = true;
            if (UnityEngine.Object.FindAnyObjectByType<OrchardGame>() != null) return;
            GameObject go = new GameObject("Orchard of the Last Season");
            go.AddComponent<OrchardGame>();
        }
    }
}
