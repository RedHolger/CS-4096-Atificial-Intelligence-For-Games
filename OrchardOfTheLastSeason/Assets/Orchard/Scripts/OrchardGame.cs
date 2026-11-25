using System.Collections.Generic;
using UnityEngine;

namespace Orchard
{
    public enum Phase { Play, Transition }

    public class TransitionInfo
    {
        public bool active;
        public double t;
        public string msg = "", sub = "";
    }

    // main controller: same state, same loop, same rules as the html game
    public class OrchardGame : MonoBehaviour
    {
        public static OrchardGame I;

        // ---- state (names match the html) ----
        public int[,] grid;
        public int[,] baseGrid;
        public List<Cell> treeCells;
        public bool staticBuilt = false;
        public bool[,] reach;   // cells joined to the orchard (from map validation)
        public List<Tuft> tufts = new List<Tuft>();
        public List<Branch> branches = new List<Branch>();
        public List<FallenLog> logs = new List<FallenLog>();
        public List<RainDrop> rain = new List<RainDrop>();
        public List<TreeEntity> trees = new List<TreeEntity>();
        public List<Enemy> enemies = new List<Enemy>();
        public List<Bait> baits = new List<Bait>();
        public List<FlyingFruit> flyingFruit = new List<FlyingFruit>();
        public Farmer farmer;
        public Pip pip;
        public Thistle thistle;
        public int night = 1;
        public double nightTime = 0;
        public int fruitSaved = 0, fruitLost = 0, fruitFallen = 0;
        public int birdSaved = 0, revives = 0;          // fruit saved by scatterlings, trees brought back
        public List<FallenBird> fallenBirds = new List<FallenBird>();
        public bool running = false, gameOver = false;
        public List<SpawnEvent> spawnQueue = new List<SpawnEvent>();
        public Phase phase = Phase.Play;
        public TransitionInfo trans = new TransitionInfo();
        public List<string> logLines = new List<string>();
        public HashSet<string> keys = new HashSet<string>();

        public Dictionary<string, Sprite> img;
        GameRenderer rend;
        OrchardUI ui;

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
            InitRain();
            Boot();
        }

        // keep any scene camera from seeing our world, and make sure something clears the screen
        void SetupCameras()
        {
            bool hasScreenCam = false;
            foreach (Camera c in Camera.allCameras)
            {
                c.cullingMask &= ~(1 << GameRenderer.LAYER);
                if (c.targetTexture == null) hasScreenCam = true;
            }
            if (!hasScreenCam)
            {
                GameObject go = new GameObject("Orchard Screen Camera");
                Camera cam = go.AddComponent<Camera>();
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = U.hex(0x14110d);
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

        public void SpawnActors()
        {
            Pos c = U.cc(C.GW >> 1, C.GH >> 1);
            farmer = new Farmer(c.x, c.y);
            pip = new Pip(c.x - C.CELL, c.y);
            thistle = new Thistle(c.x + C.CELL, c.y);
        }

        // ---- main loop ----
        void Update()
        {
            double dt = System.Math.Min(0.033, Time.unscaledDeltaTime);
            if (running && !gameOver)
            {
                if (phase == Phase.Transition)
                {
                    trans.t -= dt;
                    if (trans.t <= 0) { phase = Phase.Play; StartNight(); }
                }
                else UpdateGame(dt);
            }
            int bw, bh;
            ui.BoardPixels(out bw, out bh);
            if (rend.Resize(bw, bh)) ui.SetBoardTexture(rend.rt);
            rend.Render(this, dt);
            if (phase == Phase.Transition)
                ui.ShowTransition(U.clamp(1 - System.Math.Abs(trans.t - 1.2) / 1.2, 0.35, 1), trans.msg, trans.sub);
            else
                ui.HideTransition();
            ui.Tick();
        }

        void UpdateGame(double dt)
        {
            nightTime += dt;
            while (spawnQueue.Count > 0 && spawnQueue[0].t <= nightTime)
            {
                SpawnEvent ev = spawnQueue[0];
                spawnQueue.RemoveAt(0);
                Fire(ev);
            }
            farmer.update(dt);
            pip.update(dt);
            thistle.update(dt);
            foreach (Enemy e in enemies) { Shambler s = e as Shambler; if (s != null) s.biting = false; }
            for (int i = 0; i < enemies.Count; i++) enemies[i].update(dt);
            PackBites(pip, dt);
            PackBites(thistle, dt);
            for (int i = enemies.Count - 1; i >= 0; i--) if (enemies[i].dead) enemies.RemoveAt(i);
            for (int i = flyingFruit.Count - 1; i >= 0; i--)
            {
                FlyingFruit f = flyingFruit[i];
                f.life -= dt;
                f.y += f.vy * dt;
                f.vy += 50 * dt;
                if (f.life <= 0)
                {
                    baits.Add(new Bait(f.x, f.y));
                    flyingFruit.RemoveAt(i);
                    LogEvent("Fruit dropped, now bait");
                }
            }
            foreach (FallenBird fb in fallenBirds)
            {
                fb.t += dt;
                if (fb.landed) continue;
                fb.vh += 520 * dt;
                fb.h -= fb.vh * dt;
                fb.rot += fb.spin * dt;
                if (fb.h <= 0) { fb.h = 0; fb.landed = true; fb.rot = System.Math.PI; }   // lands on its back
            }
            foreach (TreeEntity t in trees)
            {
                if (t.flash > 0) t.flash -= dt;
                if (t.revived > 0) t.revived -= dt;
                if (t.healGlow > 0) t.healGlow -= dt;
            }
            bool allDead = true;
            foreach (TreeEntity t in trees) if (t.alive) allDead = false;
            if (allDead) { Lose(); return; }
            if (nightTime >= C.NIGHT_LEN && spawnQueue.Count == 0 && CountGround() == 0) EndNight();
            if (nightTime >= C.NIGHT_LEN + 8) EndNight();
            Hud();
        }

        int CountGround()
        {
            int c = 0;
            foreach (Enemy e in enemies) if (e.type != "scatterling") c++;
            return c;
        }

        int CountYellow()
        {
            int c = 0;
            foreach (Enemy e in enemies) if (e.type == "scatterling") c++;
            return c;
        }

        int AliveTrees()
        {
            int c = 0;
            foreach (TreeEntity t in trees) if (t.alive) c++;
            return c;
        }

        void KeepOnlyYellow()
        {
            List<Enemy> keep = new List<Enemy>();
            foreach (Enemy e in enemies) if (e.type == "scatterling") keep.Add(e);
            enemies = keep;
        }

        void Hud()
        {
            if (ui != null) ui.SetHud(night, fruitSaved + " / " + fruitFallen, fruitLost.ToString(), AliveTrees(),
                U.clamp(1 - nightTime / C.NIGHT_LEN, 0, 1));
        }

        // ---- fruit ----
        public void TryCatch()
        {
            if (!running) return;
            FlyingFruit b = null;
            double bd = C.CATCH_RANGE;
            foreach (FlyingFruit f in flyingFruit)
            {
                double d = U.dist(f, farmer);
                if (d < bd) { bd = d; b = f; }
            }
            if (b != null)
            {
                flyingFruit.Remove(b);
                fruitSaved++;
                LogEvent("Farmer caught a fruit");
            }
        }

        public void TrySwing()
        {
            if (!running || farmer.stun > 0) return;
            farmer.swing = 0.32;
            foreach (Enemy e in enemies)
            {
                if (e.type == "scatterling") continue; // yellow birds can't be killed by the farmer
                double d = U.dist(e, farmer);
                if (d > C.SCYTHE_RANGE) continue;
                double dd = d == 0 ? 1 : d;
                double dx = (e.x - farmer.x) / dd, dy = (e.y - farmer.y) / dd;
                if (dx * farmer.faceX + dy * farmer.faceY > 0.15) { e.hit(C.SCYTHE_DMG); e.lastHitByFarmer = nightTime; }
            }
            foreach (TreeEntity t in trees)
            {
                if (t.alive && t.fruit > 0 && U.dist(t, farmer) < C.CELL * 1.6)
                {
                    t.fruit--;
                    fruitFallen++;
                    FlyingFruit ff = new FlyingFruit();
                    ff.x = t.x; ff.y = t.y - C.CELL * 0.5; ff.vy = -28; ff.life = 2.4;
                    flyingFruit.Add(ff);
                    LogEvent("Farmer plucked a fruit");
                    break;
                }
            }
        }

        // ---- night flow ----
        void StartNight()
        {
            nightTime = 0;
            baits = new List<Bait>();
            flyingFruit = new List<FlyingFruit>();
            if (!staticBuilt) MapGenerator.BuildStatic(this);   // hills + trees fixed for the whole game
            MapGenerator.NewNightTerrain(this);                    // only mud + branches are new tonight
            SpawnActors();
            foreach (TreeEntity t in trees)
            {
                if (t.alive)
                {
                    t.hp = t.maxhp; t.fruit = 4; t.firstHit = double.PositiveInfinity; t.flash = 0; t.healGlow = 0;
                }
            }
            KeepOnlyYellow(); // only the scatterlings that survived fly again tonight
            fallenBirds = new List<FallenBird>();
            CheckRevive();
            while (night == 1 && CountYellow() < C.YELLOW_TARGET)
            {
                Pos p = U.randSky();
                enemies.Add(new Bird(p.x, p.y, false));
            }
            BuildWave();
            if (ui != null) ui.SetSeason(U.season(night));
            LogEvent("=== Night " + night + " of " + C.TOTAL_NIGHTS + " begins ===");
            if (logs.Count > 0) LogEvent("Fallen logs block " + logs.Count + " paths");
        }

        void BuildWave()
        {
            List<SpawnEvent> evs = new List<SpawnEvent>();
            int sh = 4 + night, st = night;  // night1: 5 shamblers, 1 stalker; +1 each per night
            for (int i = 0; i < sh; i++) evs.Add(MakeEv("shambler", 1));
            for (int i = 0; i < st; i++) evs.Add(MakeEv("stalker", 1));
            evs.Add(MakeEv("bad", 4));      // a flock of 4 black birds flies in each night
            double a = 3, b = C.NIGHT_LEN - 12;
            for (int i = 0; i < evs.Count; i++)
            {
                double frac = evs.Count > 1 ? (double)i / (evs.Count - 1) : 0;
                evs[i].t = System.Math.Max(1, a + (b - a) * frac + (U.R() - .5) * 4);
            }
            U.StableSort(evs, (x, y) => x.t.CompareTo(y.t));
            spawnQueue = evs;
        }

        SpawnEvent MakeEv(string type, int n)
        {
            SpawnEvent e = new SpawnEvent();
            e.type = type; e.n = n;
            return e;
        }

        Pos EdgePos()
        {
            int cx = 0, cy = 0, g = 0;
            do
            {
                int e = (int)(U.R() * 4);
                if (e == 0) { cx = 0; cy = (int)(U.R() * C.GH); }
                else if (e == 1) { cx = C.GW - 1; cy = (int)(U.R() * C.GH); }
                else if (e == 2) { cy = 0; cx = (int)(U.R() * C.GW); }
                else { cy = C.GH - 1; cx = (int)(U.R() * C.GW); }
                g++;
            } while ((U.groundBlocked(cx, cy) || (reach != null && !reach[cx, cy])) && g < 200);   // only spawn where the orchard can be reached
            return U.cc(cx, cy);
        }

        void Fire(SpawnEvent ev)
        {
            Pos o = EdgePos();
            for (int i = 0; i < ev.n; i++)
            {
                double ox = o.x + (U.R() - .5) * C.CELL * 2;
                double oy = o.y + (U.R() - .5) * C.CELL * 2;
                if (ev.type == "shambler") enemies.Add(new Shambler(o.x, o.y));
                else if (ev.type == "stalker") enemies.Add(new Stalker(o.x, o.y));
                else enemies.Add(new Bird(ox, oy, true));
            }
        }

        void EndNight()
        {
            int s = AliveTrees();
            LogEvent("Night " + night + " survived, " + s + "/6 trees stand");
            KeepOnlyYellow(); // clear everything except the ambient yellow flock
            baits = new List<Bait>();
            flyingFruit = new List<FlyingFruit>();
            if (night >= C.TOTAL_NIGHTS) { Win(s); return; }
            night++;
            trans = new TransitionInfo();
            trans.active = true; trans.t = C.TRANS_LEN; trans.msg = "Night " + night; trans.sub = s + " / 6 trees still stand";
            phase = Phase.Transition;
        }

        void Win(int s)
        {
            running = false; gameOver = true;
            if (ui != null) ui.ShowWin(s, fruitSaved, fruitLost);
        }

        void Lose()
        {
            running = false; gameOver = true;
            if (ui != null) ui.ShowLose(night, fruitSaved, fruitLost);
        }

        // "Begin Night 1" button
        public void StartClicked()
        {
            if (ui != null) ui.HideOverlay();
            night = 1; fruitSaved = 0; fruitLost = 0; fruitFallen = 0; birdSaved = 0; revives = 0;
            gameOver = false; phase = Phase.Play;
            enemies = new List<Enemy>();
            StartNight();
            running = true;
        }

        // "Play again" / "Try again" button
        public void Restart()
        {
            night = 1; fruitSaved = 0; fruitLost = 0; fruitFallen = 0; birdSaved = 0; revives = 0;
            gameOver = false; phase = Phase.Play;
            enemies = new List<Enemy>();
            staticBuilt = false;
            logLines.Clear();
            if (ui != null) ui.HideOverlay();
            StartNight();
            running = true;
        }

        // ---- scatterlings bring trees back ----
        // every REVIVE_COST fruit the scatterlings save buys one fallen tree back to life.
        // if no tree is down the credit is kept until one falls.
        public int ReviveBank { get { return birdSaved - revives * C.REVIVE_COST; } }

        public void CheckRevive()
        {
            while (ReviveBank >= C.REVIVE_COST)
            {
                TreeEntity dead = null;
                foreach (TreeEntity t in trees)
                    if (!t.alive && (dead == null || t.diedAt < dead.diedAt)) dead = t;   // longest-fallen first
                if (dead == null) return;
                dead.alive = true; dead.hp = dead.maxhp; dead.fruit = 4;
                dead.firstHit = double.PositiveInfinity; dead.flash = 0; dead.healGlow = 0.6; dead.revived = 2.0;
                revives++;
                LogEvent("Scatterlings revived a tree!");
            }
        }

        public void BirdCaught(Bird b)
        {
            FallenBird fb = new FallenBird();
            fb.x = b.x; fb.y = b.y; fb.h = b.flyH - System.Math.Sin(b.bob) * 3; fb.vh = -60;
            fb.dir = b.dir; fb.spin = (b.dir < 0 ? -1 : 1) * 9; fb.rot = 0;
            fallenBirds.Add(fb);
        }

        // ---- pack rule: a dog is only hurt while 2 or more shamblers bite it at the same time ----
        public int PackSize(Mover dog)
        {
            int n = 0;
            foreach (Enemy e in enemies)
            {
                Shambler s = e as Shambler;
                if (s != null && !s.dead && s.biting && s.dogT == dog && U.dist(s, dog) < 26) n++;
            }
            return n;
        }

        void PackBites(Mover dog, double dt)
        {
            int n = PackSize(dog);
            if (n < 2) return;
            double dmg = 0.7 * n * dt;
            if (dog is Pip) ((Pip)dog).Hurt(dmg);
            else ((Thistle)dog).Hurt(dmg);
            LogOnce("bite" + (dog is Pip ? "p" : "t"), "Pack of " + n + " bites " + (dog is Pip ? "Pip" : "Thistle") + "!");
        }

        public void ShamblerAte(Shambler s)
        {
            LogOnce("ate", "Shambler ate a fruit to heal");
        }

        // a log line that can only repeat after a few seconds (stops one busy event flooding the log)
        Dictionary<string, double> logCd = new Dictionary<string, double>();
        public void LogOnce(string key, string msg)
        {
            double t;
            if (logCd.TryGetValue(key, out t) && nightTime - t < 3 && nightTime >= t) return;
            logCd[key] = nightTime;
            LogEvent(msg);
        }

        public bool showUtility = false;   // Tab: show each shambler's desirability scores

        // ---- log ----
        public void LogEvent(string t)
        {
            int s = (int)System.Math.Floor(nightTime);
            string mm = U.two(s / 60), ss = U.two(s % 60);
            logLines.Add("[" + mm + ":" + ss + "] " + t);
            while (logLines.Count > 12) logLines.RemoveAt(0);
            if (ui != null) ui.SetLog(logLines);
        }

        // ---- rain ----
        void InitRain()
        {
            rain = new List<RainDrop>();
            for (int i = 0; i < 195; i++)
            {
                RainDrop r = new RainDrop();
                r.x = U.R() * C.VW; r.y = U.R() * C.VH; r.l = 8 + U.R() * 10; r.s = 420 + U.R() * 220;
                rain.Add(r);
            }
        }

        // ---- input ----
        // keyboard + mouse come through unity's gui events, so it works with either
        // input setting (old input manager, new input system, or both)
        void OnGUI()
        {
            Event e = Event.current;
            if (e == null) return;
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                string k = KeyName(e.keyCode);
                if (k != null)
                {
                    keys.Add(k);
                    if (k == " ") TrySwing();
                    if (k == "f") TryCatch();
                    if (k == "tab") showUtility = !showUtility;
                    if (k == "enter" && !running && !gameOver) ui.ClickStartButton();
                    e.Use();
                }
            }
            else if (e.type == EventType.KeyUp && e.keyCode != KeyCode.None)
            {
                string k = KeyName(e.keyCode);
                if (k != null) { keys.Remove(k); e.Use(); }
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
            switch (kc)
            {
                case KeyCode.W: return "w";
                case KeyCode.A: return "a";
                case KeyCode.S: return "s";
                case KeyCode.D: return "d";
                case KeyCode.F: return "f";
                case KeyCode.Tab: return "tab";
                case KeyCode.UpArrow: return "arrowup";
                case KeyCode.DownArrow: return "arrowdown";
                case KeyCode.LeftArrow: return "arrowleft";
                case KeyCode.RightArrow: return "arrowright";
                case KeyCode.Space: return " ";
                case KeyCode.Return: return "enter";
                case KeyCode.KeypadEnter: return "enter";
            }
            return null;
        }

        void OnApplicationFocus(bool focus)
        {
            // avoid a stuck key if the window loses focus while a key is held
            if (!focus) keys.Clear();
        }
    }

    // sprites ship as raw png bytes so unity never recompresses them (pixel-identical to the html)
    public static class SpriteLoader
    {
        public static readonly string[] Names = {
            "farmer", "pip", "thistle", "shambler", "stalker", "scatterling", "badscatter",
            "tree", "apple", "rock", "grass", "icon_apple", "icon_heart", "icon_tree" };

        public static Dictionary<string, Sprite> LoadAll()
        {
            Dictionary<string, Sprite> d = new Dictionary<string, Sprite>();
            foreach (string n in Names)
            {
                TextAsset ta = Resources.Load<TextAsset>("OrchardSprites/" + n);
                if (ta == null)
                {
                    Debug.LogError("Orchard: missing sprite Resources/OrchardSprites/" + n + ".bytes");
                    continue;
                }
                // pixel art keeps hard pixel edges (point filtering, like css image-rendering: pixelated);
                // the emoji icons are smooth artwork shown small, so they get mipmaps + trilinear
                bool icon = n.StartsWith("icon_");
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                tex.LoadImage(ta.bytes);
                tex.filterMode = icon ? FilterMode.Trilinear : FilterMode.Point;
                tex.anisoLevel = 0;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.name = n;
                Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
                s.name = n;
                d[n] = s;
            }
            return d;
        }
    }

    // starts the game automatically when you press play, in any scene
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
            if (Object.FindAnyObjectByType<OrchardGame>() != null) return;
            GameObject go = new GameObject("Orchard of the Last Season");
            go.AddComponent<OrchardGame>();
        }
    }
}
