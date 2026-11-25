using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

// Opt-in standalone player harness. Normal play never creates this component.
[DefaultExecutionOrder(1000)]
public sealed class GameSelfTest : MonoBehaviour
{
    [Serializable] sealed class Report
    {
        public string game, graphics, resolution;
        public bool passed;
        public int errors, frames, gcCollections, peakEnemies, peakProjectiles, peakEffects;
        public float averageMs, p95Ms, p99Ms, maxMs;
        public List<string> checks = new List<string>();
        public List<string> failures = new List<string>();
    }
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    MonoBehaviour game;
    Type type;
    Camera renderCamera;
    RenderTexture target;
    bool flight, measuring, finished;
    string output;
    double previous;
    int gcStart;
    readonly List<float> samples = new List<float>(6000);
    readonly Report report = new Report();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-selfTest") >= 0)
            new GameObject("Automated game verification").AddComponent<GameSelfTest>();
    }
    object Get(string name) { return type.GetField(name, Flags).GetValue(game); }
    void Set(string name, object value) { type.GetField(name, Flags).SetValue(game, value); }
    void State(string value)
    {
        var field = type.GetField("state", Flags);
        field.SetValue(game, Enum.Parse(field.FieldType, value));
    }
    object Call(string name, params object[] values) { return type.GetMethod(name, Flags).Invoke(game, values); }
    void EnumCall(string method, string value)
    {
        var info = type.GetMethod(method, Flags);
        info.Invoke(game, new[] { Enum.Parse(info.GetParameters()[0].ParameterType, value) });
    }
    IList List(string name) { return (IList)Get(name); }
    GameObject ObjectAt(string name, int index = 0)
    {
        var entity = List(name)[index];
        return (GameObject)entity.GetType().GetField("go").GetValue(entity);
    }
    void Check(bool passed, string name)
    {
        report.checks.Add((passed ? "PASS " : "FAIL ") + name);
        if (!passed) report.failures.Add(name);
    }
    void OnLog(string message, string stack, LogType kind)
    {
        if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
        { report.errors++; report.failures.Add(message); }
    }
    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        Application.runInBackground = true;
        output = Argument("-selfTestOutput", Path.Combine(Application.persistentDataPath, "selftest.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        yield return null;
        foreach (var candidate in FindObjectsOfType<MonoBehaviour>())
            if (candidate.GetType().Name == "PixelAirBattle" || candidate.GetType().Name == "SquashShooterAdventure")
            { game = candidate; break; }
        if (game == null) { Check(false, "bootstrap"); Finish(); yield break; }
        type = game.GetType();
        flight = type.Name == "PixelAirBattle";
        report.game = type.Name;
        string[] requiredArt = flight
            ? new[] { "ui_panel", "ui_button", "ui_close", "digit0", "digit9", "terrain_tree", "terrain_road", "player_shield", "fighter6", "tank3" }
            : new[] { "pause", "continue", "restart", "player_shadow", "ghost_shadow", "skeleton_shadow", "skeleton_head", "skeleton_body", "cloud3", "cloud4", "pea2", "pea3", "corn_shot2", "corn_shot3", "sparkle1", "sparkle2" };
        foreach (var art in requiredArt) Check(Resources.Load<Texture2D>("Art/" + art) != null, "required art available: " + art);
        report.graphics = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName;
        report.resolution = Screen.width + "x" + Screen.height;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        renderCamera = Camera.main;
        target = new RenderTexture(Screen.width, Screen.height, 24);
        target.Create();
        if (flight)
        {
            var landscape = (PixelTerrain)Get("terrainMap");
            Check(landscape.ValidateLayout(), "map road ports connect including loop boundary; island belongs to water");
            bool coverage = true;
            for (int i = 0; i < 12000; i++)
            {
                landscape.Scroll(1f / 60f);
                coverage &= landscape.ValidateCoverage();
            }
            Check(coverage, "map has no missing rows across 200 simulated seconds and multiple wraps");
            landscape.ResetScroll();
            for (int scene = 0; scene < 3; scene++)
            {
                if (scene > 0) landscape.Scroll(20f / .65f);
                renderCamera.targetTexture = target;
                renderCamera.Render();
                renderCamera.targetTexture = null;
                var mapImage = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                var savedTarget = RenderTexture.active;
                RenderTexture.active = target;
                mapImage.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                mapImage.Apply();
                RenderTexture.active = savedTarget;
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output), "PixelAir-map-" + scene + ".png"), mapImage.EncodeToPNG());
                Destroy(mapImage);
            }
            landscape.ResetScroll();
        }
        Check(Get("state").ToString() == "Start", "start screen");
        Call("UpdateViewport");
        var clearCamera = (Camera)Get("backgroundCamera");
        Check(clearCamera.rect == new Rect(0, 0, 1, 1) && clearCamera.cullingMask == 0 &&
            clearCamera.clearFlags == CameraClearFlags.SolidColor && clearCamera.depth < renderCamera.depth,
            "background camera clears full screen before game camera");
        Check(renderCamera.orthographicSize == 8f && !(bool)Get("viewportBattle"),
            "startup preserves previous full-height menu viewport");
        int viewportWrites = (int)Get("viewportUpdates");
        for (int frame = 0; frame < 120; frame++) Call("UpdateViewport");
        Check((int)Get("viewportUpdates") == viewportWrites, "stable display does not rewrite viewport every frame");
        var viewportMethod = type.GetMethod("BattlePixelRect", BindingFlags.Static | BindingFlags.NonPublic);
        bool viewportClear = true;
        foreach (var size in new[] { new Vector2(540, 960), new Vector2(1080, 2400), new Vector2(1280, 720) })
        {
            var battleRect = (Rect)viewportMethod.Invoke(null, new object[] { size.x, size.y });
            float uiScale = Mathf.Min(size.x / 540f, size.y / 960f);
            float hudBottom = size.y - ((size.y - 960 * uiScale) * .5f + 112 * uiScale);
            viewportClear &= Mathf.Abs(battleRect.yMax - hudBottom) < .01f;
            viewportClear &= !battleRect.Contains(new Vector2(battleRect.center.x, hudBottom + 20 * uiScale));
            viewportClear &= Mathf.Abs(battleRect.width / battleRect.height - 540f / 848f) < .001f;
        }
        Check(viewportClear, "combat viewport excludes HUD at portrait, tall and landscape sizes without stretching");
        Check(SpriteNumbers.Load(), "all ten supplied digit sprites and decimal point are bundled");
        Check(SpriteNumbers.Countdown(10f, 2) == "10.0" && SpriteNumbers.Countdown(.01f, 2) == "00.1" &&
            SpriteNumbers.Countdown(0f, 1) == "0.0", "sprite countdown formatting keeps precision and fixed slots");
        var pixelFont = Resources.Load<Font>("Fonts/fusion-pixel-12px-proportional-zh_hans");
        bool chineseGlyphs = pixelFont != null;
        if (pixelFont != null)
            foreach (char glyph in "像素空战开始出击暂时休整继续战斗重新任务结束最佳当前得分护盾秒倭瓜射手大冒险豌豆机枪玉米炮积分")
                chineseGlyphs &= pixelFont.HasCharacter(glyph);
        Check(chineseGlyphs, "bundled pixel font supports game Chinese labels");
        if (flight)
        {
            Check(Resources.Load<Texture2D>("Art/ui_panel_v2") != null && Resources.Load<Texture2D>("Art/ui_button_v2") != null,
                "generated menu textures are included in player build");
            ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(output), "PixelAir-ui-start.png"));
        }
        yield return new WaitForSecondsRealtime(.5f);
        if (flight && Array.IndexOf(Environment.GetCommandLineArgs(), "-requireVisibleUI") >= 0)
        {
            Check(Get("player") == null, "start menu renders before player exists");
            Check((int)Get("startMenuRepaints") > 0, "start menu repaint reaches start button without exception");
            Check(Get("menuPlaneArt") != null, "start menu uses independent plane texture");
        }
        Call("StartRound");
        UnityEngine.Random.InitState(4271);
        Check((bool)Get("viewportBattle") && renderCamera.orthographicSize < 8f,
            "starting play switches to separate HUD combat viewport");
        if (flight)
        {
            Call("SpawnTank");
            var tankEntity = List("enemies")[0];
            var tankType = tankEntity.GetType();
            var hull = (SpriteRenderer)tankType.GetField("renderer").GetValue(tankEntity);
            var turret = (SpriteRenderer)tankType.GetField("turret").GetValue(tankEntity);
            Check(hull.sprite.texture.name == "tank2" && turret != null && turret.sprite.texture.name == "tank3" && turret.gameObject.activeSelf,
                "tank is assembled from hull and turret, not isolated parts");
            Check((int)tankType.GetField("value").GetValue(tankEntity) == 100, "tank uses fixed 100 point enemy score");
            ObjectAt("enemies").transform.position = new Vector3(-2, 3, 0);
            tankType.GetField("timer").SetValue(tankEntity, 0f);
            Call("UpdateEnemies", .1f);
            var shellEntity = List("shots")[0];
            var shellType = shellEntity.GetType();
            var velocity = new Vector3((float)shellType.GetField("vx").GetValue(shellEntity), -(float)shellType.GetField("speed").GetValue(shellEntity), 0);
            Check(Vector3.Dot(velocity.normalized, turret.transform.rotation * Vector3.down) > .999f &&
                ObjectAt("shots").transform.position.y < ObjectAt("enemies").transform.position.y, "tank shell exits muzzle in turret direction");
            Check(Mathf.Abs(ObjectAt("enemies").transform.position.x + 2f) < .001f &&
                Mathf.Abs(ObjectAt("enemies").transform.position.y - 2.935f) < .001f, "tank stays fixed on scrolling ground");
            renderCamera.targetTexture = target;
            renderCamera.Render();
            renderCamera.targetTexture = null;
            var picture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            var oldTarget = RenderTexture.active;
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            picture.Apply();
            RenderTexture.active = oldTarget;
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output), "PixelAir-tank.png"), picture.EncodeToPNG());
            Destroy(picture);
            Call("StartRound");
            Check(!turret.gameObject.activeSelf, "pooled tank turret is hidden on restart");
            for (int attempt = 0; attempt < 100; attempt++) Call("SpawnTank");
            Check(List("enemies").Count == 2, "tank burst spawning fills two lanes without stacking");
            Call("UpdateEnemies", 3f);
            Call("SpawnTank");
            Check(List("enemies").Count == 2, "tank spacing includes hull and turret clearance");
            Set("enemyAppearance", 0);
            Call("SpawnEnemy");
            Check(List("enemies").Count == 3 && List("enemies")[2].GetType().GetField("kind").GetValue(List("enemies")[2]).ToString() == "Fighter",
                "blocked tank lanes fall back to fighter without blocking enemy waves");
            Call("UpdateEnemies", 1f);
            for (int attempt = 0; attempt < 100; attempt++) Call("SpawnTank");
            Check(List("enemies").Count == 5, "tank lanes reopen once previous tanks have sufficient clearance");
            Call("StartRound");
            Call("SpawnTank");
            Check(List("enemies").Count == 1, "restart clears tank spawn occupancy");
            Call("StartRound");
        }
        Check((int)Get("score") == 0, "new round resets score");
        if (flight)
        {
            var pickupPlayer = ((GameObject)Get("player")).transform.position;
            Call("SpawnPickup", pickupPlayer + Vector3.right);
            var testPickup = List("pickups")[0];
            var pickupType = testPickup.GetType();
            var pickupObject = ObjectAt("pickups");
            Check(pickupObject.transform.localScale == Vector3.one &&
                Mathf.Abs((float)pickupType.GetField("radius").GetValue(testPickup) - .26f) < .001f,
                "pickup starts at unit scale with proportionally reduced collision radius");
            foreach (float phase in new[] { Mathf.PI / 12f, Mathf.PI / 4f })
            {
                pickupType.GetField("age").SetValue(testPickup, phase);
                Call("UpdatePickups", 0f);
                float expectedScale = phase < .5f ? 1.12f : .88f;
                Check(Mathf.Abs(pickupObject.transform.localScale.x - expectedScale) < .001f &&
                    Mathf.Abs((float)pickupType.GetField("radius").GetValue(testPickup) - .26f * expectedScale) < .001f,
                    "pickup breathing scale and collision radius stay synchronized: " + expectedScale);
            }
            pickupType.GetField("age").SetValue(testPickup, 0f);
            Call("UpdatePickups", 0f);
            pickupObject.transform.position = pickupPlayer + Vector3.right * .70f;
            Call("ResolveCollisions");
            Check(List("pickups").Count == 1, "smaller pickup is not collected at old oversized distance");
            pickupObject.transform.position = pickupPlayer + Vector3.right * .64f;
            Call("ResolveCollisions");
            Check(List("pickups").Count == 0, "pickup is collected inside resized collision boundary");
            Call("StartRound");
        }
        // Isolate deterministic collision tests from random spawning.
        Set("spawnClock", 100f);
        if (flight)
        {
            Call("DamagePlayer");
            bool redHit = false;
            foreach (var effect in List("effects")) redHit |= (int)effect.GetType().GetField("value").GetValue(effect) == 5;
            Check(redHit, "player damage emits supplied red impact animation");
            Call("SpawnEnemyBullet", new Vector3(0, 5, 0), 3.6f, 0f);
            Call("SpawnEnemyBullet", new Vector3(1, 5, 0), 2.5f, .5f);
            Check(ObjectAt("shots", 0).GetComponent<SpriteRenderer>().sprite.texture.name == "fx_tile_0000_red",
                "fighter fires red bar projectile instead of impact spark");
            Call("SpriteImpact", Vector3.zero, true);
            Call("UpdateEffects", .12f);
            bool animatedImpact = false, smokeVisible = false;
            foreach (var effect in List("effects"))
            {
                var effectObject = (GameObject)effect.GetType().GetField("go").GetValue(effect);
                string effectTexture = effectObject.GetComponent<SpriteRenderer>().sprite.texture.name;
                animatedImpact |= effectTexture == "fx_tile_0005";
                smokeVisible |= effectTexture == "fx_tile_0008";
            }
            Check(animatedImpact && smokeVisible, "explosion advances through supplied impact frames and emits smoke");
            Check(ObjectAt("shots", 0).transform.localScale == Vector3.one && ObjectAt("shots", 1).transform.localScale == Vector3.one,
                "fighter bullets and tank shells use unit scale");
            Check((int)Get("lives") == 2, "damage removes one life");
            int damageEffects = List("effects").Count;
            Call("DamagePlayer");
            Check(List("effects").Count == damageEffects, "invulnerability suppresses repeated damage flashes");
            Check((int)Get("lives") == 2, "post-hit invulnerability");
            Set("invulnerableTime", 0f);
            EnumCall("ApplyPickup", "Shield");
            Call("DamagePlayer");
            Check((int)Get("lives") == 2 && (float)Get("shieldTime") == 3f, "shield protects for three seconds");
            Check((float)type.GetProperty("ShieldProgress", Flags).GetValue(game) == 1f, "shield HUD starts full");
            Call("UpdatePlayer", 1.5f);
            Check(Mathf.Abs((float)type.GetProperty("ShieldProgress", Flags).GetValue(game) - .5f) < .001f,
                "shield HUD drains with actual remaining duration");
            Set("shieldTime", .01f);
            Check((int)type.GetProperty("ShieldDisplayTenths", Flags).GetValue(game) == 1,
                "active shield timer never rounds down to zero early");
            Call("UpdatePlayer", .02f);
            Check((float)Get("shieldTime") == 0f && (float)type.GetProperty("ShieldProgress", Flags).GetValue(game) == 0f,
                "expired shield clears HUD condition and progress");
            EnumCall("ApplyPickup", "Shield");
            Check((float)type.GetProperty("ShieldProgress", Flags).GetValue(game) == 1f, "shield pickup refreshes HUD duration");
            EnumCall("ApplyPickup", "Life");
            Check((int)Get("lives") == 3, "life pickup");
            for (int i = 0; i < 8; i++) EnumCall("ApplyPickup", "Life");
            Check((int)Get("lives") == 5, "life cap");
            for (int i = 0; i < 8; i++) Call("SpawnEnemy");
            foreach (var visibleEnemy in List("enemies"))
                ((GameObject)visibleEnemy.GetType().GetField("go").GetValue(visibleEnemy)).transform.position = Vector3.zero;
            Call("SpawnEnemy");
            var outsideTop = ObjectAt("enemies", List("enemies").Count - 1);
            outsideTop.transform.position = new Vector3(0, 20, 0);
            Call("SpawnEnemy");
            var outsideSide = ObjectAt("enemies", List("enemies").Count - 1);
            outsideSide.transform.position = new Vector3(20, 0, 0);
            Call("SpawnEnemy");
            ObjectAt("enemies", List("enemies").Count - 1).transform.position = new Vector3(4.5f, 0, 0);
            int expectedBombScore = (int)Get("score");
            foreach (var bombTarget in List("enemies"))
            {
                var targetObject = (GameObject)bombTarget.GetType().GetField("go").GetValue(bombTarget);
                if (targetObject != outsideTop && targetObject != outsideSide)
                    expectedBombScore += (int)bombTarget.GetType().GetField("value").GetValue(bombTarget);
            }
            EnumCall("ApplyPickup", "Bomb");
            bool clearWave = false;
            foreach (var effect in List("effects")) clearWave |= (int)effect.GetType().GetField("value").GetValue(effect) == 6;
            Check(clearWave, "bomb creates translucent expanding shockwave");
            Check(List("enemies").Count == 2 && outsideTop.activeSelf && outsideSide.activeSelf,
                "bomb clears visible and partially visible enemies but preserves offscreen enemies");
            Check((int)Get("score") == expectedBombScore, "bomb awards only onscreen enemy scores");
        }
        else
        {
            var jump = type.GetMethod("TryJump", Flags);
            if (jump != null)
            {
                Set("jumps", 0); jump.Invoke(game, null); jump.Invoke(game, null);
                Check((int)Get("jumps") == 2, "double jump accepted");
                jump.Invoke(game, null);
                Check((int)Get("jumps") == 2, "third jump rejected");
            }
            else Check(false, "jump needs a testable shared input entry");
            Call("SpawnPickup", -4.75f, (float)Get("playerY"));
            Call("ResolveCollisions");
            Check((float)Get("weaponTime") == 10f && Get("weapon").ToString() != "Pea", "weapon collected for ten seconds");
            Set("weaponTime", .01f);
            Call("UpdatePlayer", .02f);
            Check(Get("weapon").ToString() == "Pea", "weapon expires");
        }
        State("Paused");
        var buffField = flight ? "shieldTime" : "weaponTime";
        Set(buffField, 2f);
        if (List("enemies").Count == 0) { if (flight) Call("SpawnEnemy"); else Call("SpawnEnemyGroup"); }
        if (flight) Call("SpawnEnemyBullet", Vector3.zero, 3.6f, 0f); else Call("SpawnBullet");
        var pausedEnemy = ObjectAt("enemies");
        var pausedBullet = ObjectAt(flight ? "shots" : "bullets");
        var pausedEnemyPosition = pausedEnemy.transform.position;
        var pausedBulletPosition = pausedBullet.transform.position;
        var pausedSpawnClock = (float)Get("spawnClock");
        var time = (float)Get("elapsed");
        var pos = ((GameObject)Get("player")).transform.position;
        yield return new WaitForSecondsRealtime(.4f);
        Check((float)Get("elapsed") == time && ((GameObject)Get("player")).transform.position == pos, "pause freezes simulation");
        Check((float)Get(buffField) == 2f && (float)Get("spawnClock") == pausedSpawnClock &&
            pausedEnemy.transform.position == pausedEnemyPosition && pausedBullet.transform.position == pausedBulletPosition,
            "pause freezes buff duration, spawn timer, enemies and projectiles");
        State("Playing");
        yield return null;
        Check((float)Get("elapsed") > time, "resume advances simulation");
        Check((float)Get(buffField) < 2f, "resume continues buff countdown");
        Call("StartRound");
        Set("spawnClock", 100f);
        if (flight) Call("SpawnEnemy"); else Call("SpawnEnemyGroup");
        // Put a real bullet onto a real enemy and resolve through production collision code.
        var enemy = ObjectAt("enemies");
        if (flight) Call("SpawnPlayerBullet", enemy.transform.position);
        else
        {
            var weaponField = type.GetField("weapon", Flags);
            weaponField.SetValue(game, Enum.Parse(weaponField.FieldType, "Corn"));
            Set("weaponTime", 10f);
            Call("SpawnBullet");
            ObjectAt("bullets", List("bullets").Count - 1).transform.position = enemy.transform.position;
        }
        if (flight)
        {
            for (int i = 0; i < 4; i++) Call("SpawnPlayerBullet", enemy.transform.position);
        }
        Call("ResolveCollisions");
        Check((int)Get("score") > 0, "projectile collision awards score");
        Call("StartRound");
        Set("spawnClock", 100f);
        if (flight) { Set("lives", 1); Call("SpawnEnemy"); } else Call("SpawnEnemyGroup");
        ObjectAt("enemies").transform.position = ((GameObject)Get("player")).transform.position;
        Call("ResolveCollisions");
        Check(Get("state").ToString() == "GameOver", "enemy contact causes game over");
        Call("StartRound");
        Check((int)Get("score") == 0 && List("enemies").Count == 0 &&
            List(flight ? "shots" : "bullets").Count == 0 && List("effects").Count == 0,
            "restart clears enemies projectiles particles and score");
        // Crossing tests deliberately put both segment endpoints outside the target.
        Call("StartRound");
        if (flight)
        {
            var center = ((GameObject)Get("player")).transform.position;
            Call("SpawnEnemyBullet", center + Vector3.up * 2f, 3.6f, 0f);
            ObjectAt("shots").transform.position = center + Vector3.down * 2f;
            Call("ResolveCollisions");
            Check((int)Get("lives") == 2 && List("shots").Count == 0, "fast enemy bullet cannot tunnel through player");
            Call("StartRound");
            Call("SpawnEnemy");
            var targetEnemy = List("enemies")[0];
            targetEnemy.GetType().GetField("hp").SetValue(targetEnemy, 1);
            var centerEnemy = ObjectAt("enemies").transform.position;
            Call("SpawnPlayerBullet", centerEnemy + Vector3.down * 2f);
            ObjectAt("shots").transform.position = centerEnemy + Vector3.up * 2f;
            Call("ResolveCollisions");
            Check((int)Get("score") > 0, "fast player bullet hits crossed enemy");
        }
        else
        {
            Call("SpawnEnemyGroup");
            ObjectAt("enemies").transform.position = Vector3.zero;
            var wf = type.GetField("weapon", Flags);
            wf.SetValue(game, Enum.Parse(wf.FieldType, "Corn"));
            Call("SpawnBullet");
            var bulletEntity = List("bullets")[0];
            bulletEntity.GetType().GetField("previous").SetValue(bulletEntity, Vector3.left * 3f);
            ObjectAt("bullets").transform.position = Vector3.right * 3f;
            Call("ResolveCollisions");
            Check((int)Get("score") > 0 && List("enemies").Count == 0, "corn explosion stays at crossed target instead of segment endpoint");
            Call("StartRound");
            Set("playerY", (float)Get("groundY") + .01f);
            Set("verticalSpeed", -8f);
            Call("UpdatePlayer", .02f);
            Check((int)Get("jumps") == 0 && (float)Get("landingSquash") > 0f, "hard landing resets jumps and triggers squash");
        }
        bool cleanRestarts = true;
        for (int cycle = 0; cycle < 12; cycle++)
        {
            if (flight) Call("SpawnEnemy"); else Call("SpawnEnemyGroup");
            Call("StartRound");
            cleanRestarts &= List("enemies").Count == 0 && List("pickups").Count == 0 &&
                List(flight ? "shots" : "bullets").Count == 0 && List("effects").Count == 0 &&
                (int)Get("score") == 0 && Get("state").ToString() == "Playing";
            yield return null;
        }
        Check(cleanRestarts, "twelve consecutive restarts clear all dynamic state");
        // Warm up resources, then sample actual rendered frame intervals (not clamped deltaTime).
        yield return new WaitForSecondsRealtime(2f);
        measuring = true; previous = Time.realtimeSinceStartupAsDouble; gcStart = GC.CollectionCount(0);
        double deadline = previous + 22;
        double nextWave = previous;
        while (Time.realtimeSinceStartupAsDouble < deadline)
        {
            if (Get("state").ToString() == "GameOver") Call("StartRound");
            if (flight)
            {
                Set("shieldTime", 3f);
                var p = (GameObject)Get("player");
                p.transform.position = new Vector3(Mathf.Sin(Time.unscaledTime) * 2.8f, -5.8f, 0f);
            }
            if (Time.realtimeSinceStartupAsDouble >= nextWave)
            {
                nextWave += 2;
                if (flight)
                {
                    for (int i = 0; i < 12; i++) Call("SpawnEnemy");
                    EnumCall("ApplyPickup", "Bomb");
                }
                else
                {
                    Call("SpawnEnemyGroup");
                    var f = type.GetField("weapon", Flags);
                    f.SetValue(game, Enum.Parse(f.FieldType, ((int)nextWave % 4 == 0) ? "Machine" : "Corn"));
                    Set("weaponTime", 10f);
                }
            }
            yield return null;
        }
        measuring = false;
        var created = type.GetField("createdEntities", Flags);
        if (created != null) Check((int)created.GetValue(game) < 400, "pooled object count remains bounded under repeated explosions");
        report.gcCollections = GC.CollectionCount(0) - gcStart;
        yield return new WaitForEndOfFrame();
        var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        var previousTarget = RenderTexture.active;
        RenderTexture.active = target;
        texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previousTarget;
        File.WriteAllBytes(Path.ChangeExtension(output, ".png"), texture.EncodeToPNG());
        Destroy(texture);
        Finish();
    }
    void LateUpdate()
    {
        if (game == null || !measuring) return;
        renderCamera.targetTexture = target;
        renderCamera.Render();
        renderCamera.targetTexture = null;
        double now = Time.realtimeSinceStartupAsDouble;
        samples.Add((float)((now - previous) * 1000)); previous = now;
        report.peakEnemies = Mathf.Max(report.peakEnemies, List("enemies").Count);
        report.peakProjectiles = Mathf.Max(report.peakProjectiles, List(flight ? "shots" : "bullets").Count);
        report.peakEffects = Mathf.Max(report.peakEffects, List("effects").Count);
    }
    void Finish()
    {
        if (finished) return; finished = true;
        report.frames = samples.Count;
        if (samples.Count > 0)
        {
            float total = 0; foreach (var v in samples) total += v;
            samples.Sort();
            report.averageMs = total / samples.Count;
            report.p95Ms = samples[(int)((samples.Count - 1) * .95f)];
            report.p99Ms = samples[(int)((samples.Count - 1) * .99f)];
            report.maxMs = samples[samples.Count - 1];
        }
        Check(report.errors == 0, "no runtime errors");
        Check(report.frames > 200, "rendered workload sampled");
        report.passed = report.failures.Count == 0;
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        Application.logMessageReceived -= OnLog;
        Application.Quit(report.passed ? 0 : 2);
    }
    static string Argument(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i+1];
        return fallback;
    }
}
