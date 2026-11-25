using System.Collections.Generic;
using UnityEngine;

public sealed class PixelAirBattle : MonoBehaviour
{
    private enum GameState { Start, Playing, Paused, GameOver }
    private enum Kind { Star, PlayerBullet, EnemyBullet, Fighter, Tank, Life, Shield, Bomb, Fx }

    private sealed class Entity
    {
        public GameObject go;
        public Vector3 previous;
        public SpriteRenderer renderer;
        public Kind kind;
        public float radius;
        public float speed;
        public float vx;
        public float age;
        public float timer;
        public float hit;
        public int hp;
        public int value;
        public SpriteRenderer turret;
        public float recoil;
    }

    private readonly List<Entity> enemies = new List<Entity>();
    private readonly List<Entity> shots = new List<Entity>();
    private readonly List<Entity> pickups = new List<Entity>();
    private readonly List<Entity> stars = new List<Entity>();
    private readonly List<Entity> effects = new List<Entity>();
    private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

    private readonly Stack<Entity> pool = new Stack<Entity>(320);
    private int createdEntities;
    private bool selfTesting;
    private Camera cam;
    private Camera backgroundCamera;
    private int viewportWidth = -1, viewportHeight = -1;
    private bool viewportBattle;
    private int viewportUpdates;
    private const float HudHeight = 112f;
    private const float BattleHalfHeight = (960f - HudHeight) / 120f;
    private const float PickupRadius = .26f;
    private bool showPerformance;
    private float displayClock, displayFrameTime;
    private string performanceText = "";
    private GameObject player;
    private SpriteRenderer playerRenderer;
    private GameObject shieldRing;
    private GameState state = GameState.Start;
    private float fireClock;
    private float spawnClock;
    private float elapsed;
    private float shieldTime;
    private const float ShieldDuration = 3f;
    private float ShieldProgress => Mathf.Clamp01(shieldTime / ShieldDuration);
    private int ShieldDisplayTenths => Mathf.CeilToInt(Mathf.Max(0f, shieldTime) * 10f);
    private Texture2D shieldHudArt;
    private int shieldHudTenths = -1;
    private string shieldHudText = "";
    private float invulnerableTime;
    private float shakeTime;
    private float shakeStrength;
    private int lives;
    private int score;
    private int highScore;
    private Vector3 cameraHome;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle hudStyle;
    private GUIStyle buttonStyle;
    private GUIStyle smallStyle;
    private Texture2D panelTexture;
    private Texture2D buttonTexture;
    private Texture2D buttonHotTexture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<PixelAirBattle>() == null)
            new GameObject("PixelAirBattle").AddComponent<PixelAirBattle>();
    }

    private void Awake()
    {
        selfTesting = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-selfTest") >= 0;
        Application.targetFrameRate = Application.isMobilePlatform ? 60 : -1;
        QualitySettings.vSyncCount = Application.isMobilePlatform ? 0 : 1;
        Application.runInBackground = true;
        for (int i = 0; i < 256; i++)
        {
            var item = CreateEntity();
            item.go.SetActive(false);
            pool.Push(item);
        }
        foreach (var texture in Resources.LoadAll<Texture2D>("Art"))
            LoadSprite("Art/" + texture.name, texture.name.StartsWith("player") && !texture.name.Contains("bullet") || texture.name.StartsWith("fighter") && !texture.name.Contains("bullet") ? 32f : 16f);
        digitArt = new Texture2D[10];
        for (int i = 0; i < 10; i++) digitArt[i] = Resources.Load<Texture2D>("Art/digit" + i);
        lifeArt = Resources.Load<Texture2D>("Art/life");
        closeArt = Resources.Load<Texture2D>("Art/ui_close");
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        highScore = PlayerPrefs.GetInt("PixelAirBattle.HighScore", 0);
        SetupCamera();
        CreateBackground();
    }

    private void SetupCamera()
    {
        // Clear the complete backbuffer before the cropped game camera renders.
        backgroundCamera = new GameObject("Full screen background").AddComponent<Camera>();
        backgroundCamera.depth = -100;
        backgroundCamera.cullingMask = 0;
        backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
        backgroundCamera.backgroundColor = new Color(.12f, .105f, .085f, 1f);
        backgroundCamera.rect = new Rect(0, 0, 1, 1);
        backgroundCamera.allowHDR = false;
        backgroundCamera.allowMSAA = false;
        var cameraObject = new GameObject("Main Camera");
        cam = cameraObject.AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.orthographic = true;
        cam.orthographicSize = 8f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.018f, 0.043f, 0.12f);
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraHome = cameraObject.transform.position;
        // Wait until Update for valid display dimensions; startup retains the full viewport.
    }

    private readonly List<GameObject> terrain = new List<GameObject>();
    private int enemyAppearance;
    private Texture2D[] digitArt;
    private Texture2D lifeArt, closeArt;
    private Texture2D menuPlaneArt;
    private int startMenuRepaints;
    private GUIStyle panelStyle;
    private Sprite[] impactFrames;
    private Sprite[] hurtFrames;
    private Sprite smokeSprite;

    private PixelTerrain terrainMap;
    private void CreateBackground()
    {
        impactFrames = new[] { LoadSprite("Art/fx_tile_0004", 16), LoadSprite("Art/fx_tile_0005", 16), LoadSprite("Art/fx_tile_0006", 16), LoadSprite("Art/fx_tile_0007", 16) };
        smokeSprite = LoadSprite("Art/fx_tile_0008", 16);
        hurtFrames = new[] { LoadSprite("Art/fx_tile_0004_red", 16), LoadSprite("Art/fx_tile_0005_red", 16), LoadSprite("Art/fx_tile_0006_red", 16), LoadSprite("Art/fx_tile_0007_red", 16) };
        terrainMap = new GameObject("Scrolling landscape").AddComponent<PixelTerrain>();
        terrainMap.Initialize();
    }

    private void CreateGuiResources()
    {
        panelTexture = Resources.Load<Texture2D>("Art/ui_panel_v2");
        buttonTexture = Resources.Load<Texture2D>("Art/ui_button_v2");
        buttonHotTexture = buttonTexture;
        menuPlaneArt = Resources.Load<Texture2D>("Art/player");
        shieldHudArt = Resources.Load<Texture2D>("Art/shield");
        panelStyle = new GUIStyle { border = new RectOffset(16, 16, 16, 16) };
        panelStyle.normal.background = panelTexture;
        var menuFont = Resources.Load<Font>("Fonts/fusion-pixel-12px-proportional-zh_hans");
        titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 42, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = new Color(.94f, .87f, .65f);
        bodyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, wordWrap = true };
        bodyStyle.normal.textColor = Color.white;
        hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 23, fontStyle = FontStyle.Bold };
        hudStyle.normal.textColor = Color.white;
        smallStyle = new GUIStyle(bodyStyle) { fontSize = 16 };
        buttonStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, fontSize = 24, fontStyle = FontStyle.Bold };
        buttonStyle.normal.background = buttonTexture;
        buttonStyle.hover.background = buttonHotTexture;
        buttonStyle.active.background = buttonHotTexture;
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.textColor = Color.white;
        buttonStyle.border = new RectOffset(16, 16, 16, 16);
        buttonStyle.active.textColor = new Color(1f, .9f, .55f);
        buttonStyle.contentOffset = new Vector2(0, -2);
        foreach (var style in new[] { titleStyle, bodyStyle, hudStyle, smallStyle, buttonStyle }) style.font = menuFont;
        foreach (var style in new[] { titleStyle, bodyStyle, hudStyle, smallStyle, buttonStyle }) style.fontStyle = FontStyle.Normal;
        titleStyle.fontSize = 36;
        bodyStyle.fontSize = hudStyle.fontSize = buttonStyle.fontSize = 24;
        smallStyle.fontSize = 18;
        bodyStyle.normal.textColor = new Color(.85f, .83f, .74f);
        smallStyle.normal.textColor = new Color(.85f, .83f, .74f);
    }

    private void Update()
    {
        displayFrameTime = Mathf.Lerp(displayFrameTime, Time.unscaledDeltaTime, .06f);
        displayClock -= Time.unscaledDeltaTime;
        if (Input.GetKeyDown(KeyCode.F3)) showPerformance = !showPerformance;
        if (displayClock <= 0f)
        {
            displayClock = .25f;
            performanceText = (1f / Mathf.Max(.0001f, displayFrameTime)).ToString("0") + " FPS / " + (displayFrameTime * 1000f).ToString("0.0") + " ms";
        }
        UpdateViewport();
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (state == GameState.Playing) state = GameState.Paused;
            else if (state == GameState.Paused) state = GameState.Playing;
        }
        if (state == GameState.GameOver)
        {
            UpdateEffects(Mathf.Min(Time.unscaledDeltaTime, .1f));
            UpdateCamera(Mathf.Min(Time.unscaledDeltaTime, .1f));
            return;
        }
        if (state != GameState.Playing) { dragging = false; return; }
        var dt = Mathf.Min(Time.deltaTime, .1f);
        elapsed += dt;
        UpdateStars(dt);
        UpdateInput();
        UpdatePlayer(dt);
        UpdateEnemies(dt);
        UpdateShots(dt);
        UpdatePickups(dt);
        UpdateEffects(dt);
        UpdateCamera(dt);
        ResolveCollisions();
    }

    private void UpdateStars(float dt)
    {
        terrainMap.Scroll(dt);
        foreach (var tile in terrain)
        {
            tile.transform.position += Vector3.down * .65f * dt;
            if (tile.transform.position.y < -10f) tile.transform.position += Vector3.up * 20f;
        }
        for (var i = 0; i < stars.Count; i++)
        {
            var star = stars[i];
            star.go.transform.position += Vector3.down * star.speed * dt;
            if (star.go.transform.position.y < -8.2f)
                star.go.transform.position = new Vector3(Random.Range(-4.4f, 4.4f), 8.2f, 0f);
        }
    }

    private Vector3 playerTarget, lastPointer;
    private bool dragging;
    private void UpdateInput()
    {
        bool held = Input.GetMouseButton(0);
        Vector3 screen = Input.mousePosition;
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            held = touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
            screen = touch.position;
        }
        var view = cam.pixelRect;
        var world = new Vector3((screen.x - view.x) / view.width * 9f - 4.5f,
            (screen.y - view.y) / view.height * (BattleHalfHeight * 2f) - BattleHalfHeight, 0);
        if (!view.Contains(screen)) { held = false; dragging = false; }
        if (held && !dragging && view.Contains(screen))
        {
            dragging = true; lastPointer = world; playerTarget = player.transform.position;
        }
        if (!held) dragging = false;
        if (dragging) { playerTarget += world - lastPointer; lastPointer = world; }
        var keyboard = new Vector3(
            (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
            (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0), 0);
        playerTarget += Vector3.ClampMagnitude(keyboard, 1f) * 7f * Time.deltaTime;
        playerTarget.x = Mathf.Clamp(playerTarget.x, -4f, 4f);
        playerTarget.y = Mathf.Clamp(playerTarget.y, -BattleHalfHeight + .85f, BattleHalfHeight - .85f);
        if (!selfTesting)
            player.transform.position = Vector3.Lerp(player.transform.position, playerTarget, 1f - Mathf.Exp(-35f * Time.deltaTime));
        float bank = Mathf.Clamp((playerTarget.x - player.transform.position.x) * -20f, -12f, 12f);
        player.transform.rotation = Quaternion.Lerp(player.transform.rotation, Quaternion.Euler(0, 0, bank), 1f - Mathf.Exp(-16f * Time.deltaTime));
    }

    private void UpdatePlayer(float dt)
    {
        fireClock -= dt;
        shieldTime = Mathf.Max(0f, shieldTime - dt);
        invulnerableTime = Mathf.Max(0f, invulnerableTime - dt);
        if (fireClock <= 0f)
        {
            fireClock += .20f;
            SpawnPlayerBullet(player.transform.position + Vector3.up * .52f);
        }
        playerRenderer.color = invulnerableTime > 0f && ((int)(invulnerableTime * 12f) % 2 == 0) ? new Color(1f, 1f, 1f, .35f) : Color.white;
        playerRenderer.sprite = LoadSprite(shieldTime > 0f ? "Art/player_shield" : "Art/player", 32f);
        shieldRing.SetActive(shieldTime > 0f);
        if (shieldRing.activeSelf)
        {
            shieldRing.transform.position = player.transform.position;
            shieldRing.transform.Rotate(0f, 0f, 90f * dt);
            var pulse = 1f + Mathf.Sin(elapsed * 7f) * .06f;
            shieldRing.transform.localScale = new Vector3(pulse, pulse, 1f) * .8f;
        }

        spawnClock -= dt;
        if (spawnClock <= 0f)
        {
            SpawnEnemy();
            var difficulty = Mathf.Clamp01(elapsed / 100f);
            spawnClock = Random.Range(.78f, 1.25f) * Mathf.Lerp(1f, .6f, difficulty);
        }
    }

    private void UpdateEnemies(float dt)
    {
        for (var i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            e.age += dt;
            e.timer -= dt;
            var p = e.go.transform.position;
            if (e.kind == Kind.Fighter)
            {
                p.y -= e.speed * dt;
                p.x += (e.vx + Mathf.Sin(e.age * 2.8f) * .5f) * dt;
                if (e.timer <= 0f && p.y < 6.5f)
                {
                    e.timer = Random.Range(1.25f, 2.15f);
                    SpawnEnemyBullet(p + Vector3.down * .45f, 3.6f, 0f);
                }
            }
            else
            {
                p.y -= e.speed * dt;
                var aim = player.transform.position - p;
                // Turret sprite points down at identity. Limit aiming to the forward hemisphere.
                var angle = Mathf.Clamp(Mathf.Atan2(aim.x, Mathf.Max(.2f, -aim.y)) * Mathf.Rad2Deg, -60f, 60f);
                e.turret.transform.localRotation = Quaternion.RotateTowards(e.turret.transform.localRotation, Quaternion.Euler(0, 0, angle), 100f * dt);
                var direction = e.turret.transform.rotation * Vector3.down;
                e.recoil = Mathf.Max(0, e.recoil - dt);
                e.turret.transform.localPosition = e.turret.transform.localRotation * Vector3.up * (.1f * e.recoil / .14f);
                if (e.timer <= 0f && p.y < 6.4f && aim.y < -.3f)
                {
                    e.timer = Random.Range(1.8f, 2.65f);
                    var muzzle = p + direction * .68f;
                    SpawnEnemyBullet(muzzle, -direction.y * 2.5f, direction.x * 2.5f);
                    Pulse(muzzle, new Color(1f, .7f, .25f), true);
                    e.recoil = .14f;
                }
            }
            e.go.transform.position = p;
            if (e.hit > 0f)
            {
                e.hit -= dt;
                e.renderer.color = new Color(1f, .35f, .35f);
                var punch = (e.kind == Kind.Tank ? 1.28f : 1f) * (1f + e.hit * 1.8f);
                e.go.transform.localScale = new Vector3(punch, punch, 1f);
            }
            else
            {
                e.renderer.color = Color.white;
                e.go.transform.localScale = Vector3.one * (e.kind == Kind.Tank ? 1.28f : 1f);
            }
            if (e.kind == Kind.Tank) e.turret.color = e.renderer.color;
            if (p.y < -8.8f || Mathf.Abs(p.x) > 5.8f) Remove(enemies, i);
        }
    }

    private void UpdateShots(float dt)
    {
        for (var i = shots.Count - 1; i >= 0; i--)
        {
            var s = shots[i];
            s.previous = s.go.transform.position;
            var direction = s.kind == Kind.PlayerBullet ? 1f : -1f;
            s.go.transform.position += new Vector3(s.vx, direction * s.speed, 0f) * dt;
            if (Mathf.Abs(s.go.transform.position.y) > 9f || Mathf.Abs(s.go.transform.position.x) > 5.6f)
                Remove(shots, i);
        }
    }

    private void UpdatePickups(float dt)
    {
        for (var i = pickups.Count - 1; i >= 0; i--)
        {
            var p = pickups[i];
            p.age += dt;
            var pos = p.go.transform.position;
            pos.y -= p.speed * dt;
            pos.x += Mathf.Sin(p.age * 3f) * .4f * dt;
            p.go.transform.position = pos;
            p.go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Sin(p.age * 2f) * 8f);
            var pulse = 1f + Mathf.Sin(p.age * 6f) * .12f;
            p.go.transform.localScale = new Vector3(pulse, pulse, 1f);
            p.radius = PickupRadius * pulse;
            if (pos.y < -8.7f) Remove(pickups, i);
        }
    }

    private void UpdateEffects(float dt)
    {
        for (var i = effects.Count - 1; i >= 0; i--)
        {
            var fx = effects[i];
            fx.age += dt;
            fx.go.transform.position += new Vector3(fx.vx, fx.speed, 0f) * dt;
            var life = Mathf.Clamp01(1f - fx.age / fx.timer);
            fx.renderer.color = new Color(fx.renderer.color.r, fx.renderer.color.g, fx.renderer.color.b, life);
            if (fx.value == 6)
            {
                fx.go.transform.localScale = Vector3.one * Mathf.Lerp(.15f, 8f, 1f - life * life);
                fx.renderer.color = new Color(1f, .85f, .4f, life * .45f);
                if (fx.age >= fx.timer) Remove(effects, i);
                continue;
            }
            if (fx.value == 3 || fx.value == 4 || fx.value == 5)
            {
                if (fx.value == 3) fx.renderer.sprite = impactFrames[Mathf.Min(3, (int)(fx.age / fx.timer * 4))];
                if (fx.value == 5) fx.renderer.sprite = hurtFrames[Mathf.Min(3, (int)(fx.age / fx.timer * 4))];
                fx.go.transform.localScale = Vector3.one * (fx.value == 3 ? fx.radius : fx.value == 5 ? Mathf.Lerp(.45f, .85f, 1f - life) : Mathf.Lerp(.35f, 1f, 1f - life));
                if (fx.age >= fx.timer) Remove(effects, i);
                continue;
            }
            fx.go.transform.localScale = Vector3.one * (fx.value == 1
                ? Mathf.Lerp(.12f, .75f, 1f - life)
                : fx.value == 2 ? Mathf.Lerp(.12f, .02f, 1f - life) : Mathf.Lerp(.2f, 1.35f, 1f - life));
            if (fx.age >= fx.timer) Remove(effects, i);
        }
    }

    private void ResolveCollisions()
    {
        for (var s = shots.Count - 1; s >= 0; s--)
        {
            var shot = shots[s];
            if (shot.kind == Kind.PlayerBullet)
            {
                for (var e = enemies.Count - 1; e >= 0; e--)
                {
                    var enemy = enemies[e];
                    if (!Touches(shot, enemy)) continue;
                    var impact = ClosestPoint(shot, enemy.go.transform.position);
                    SpriteImpact(impact, false);
                    Remove(shots, s);
                    enemy.hp--;
                    enemy.hit = .13f;
                    Burst(impact, new Color(.2f, .95f, 1f), 3);
                    if (enemy.hp <= 0) KillEnemy(e, true);
                    break;
                }
            }
            else if ((ClosestPoint(shot, player.transform.position) - player.transform.position).sqrMagnitude <= (shot.radius + .33f) * (shot.radius + .33f))
            {
                Remove(shots, s);
                DamagePlayer();
            }
        }

        for (var e = enemies.Count - 1; e >= 0; e--)
        {
            if (Vector2.Distance(enemies[e].go.transform.position, player.transform.position) < enemies[e].radius + .36f)
            {
                KillEnemy(e, false);
                DamagePlayer();
            }
        }

        for (var p = pickups.Count - 1; p >= 0; p--)
        {
            var pickup = pickups[p];
            if (Vector2.Distance(pickup.go.transform.position, player.transform.position) > pickup.radius + .4f) continue;
            ApplyPickup(pickup.kind);
            Burst(pickup.go.transform.position, new Color(.35f, 1f, .55f), 10);
            Remove(pickups, p);
        }
    }

    private void SpawnEnemy()
    {
        // Guarantee a visible heavy unit early, instead of relying entirely on random rolls.
        if (enemyAppearance % 4 == 0 || Random.value < .18f)
        {
            if (SpawnTank())
            {
                enemyAppearance++;
                return;
            }
        }
        var x = Random.Range(-3.75f, 3.75f);
        var e = Make(Kind.Fighter, "Art/fighter", new Vector3(x, BattleHalfHeight + .6f, 0f), .42f, 2, 100);
        int appearance = enemyAppearance++ % 6;
        e.renderer.sprite = LoadSprite("Art/fighter" + (appearance == 0 ? "" : (appearance + 1).ToString()), 32f);
        e.speed = Random.Range(1.15f, 1.75f) + elapsed * .003f;
        e.vx = Random.Range(-.25f, .25f);
        e.timer = Random.Range(.7f, 1.5f);
        e.renderer.sortingOrder = 5;
        e.go.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
        e.go.transform.localScale = Vector3.one;
        enemies.Add(e);
    }

    private bool TankSpawnClear(float x)
    {
        // Include the hull, rotated turret and hit-scale animation, not just collision radius.
        foreach (var other in enemies)
        {
            if (other.kind != Kind.Tank) continue;
            var position = other.go.transform.position;
            if (Mathf.Abs(position.x - x) < 1.8f && Mathf.Abs(position.y - (BattleHalfHeight + .8f)) < 2.2f)
                return false;
        }
        return true;
    }

    private bool SpawnTank()
    {
        // Both positions are on permanent land; -2 follows the map's main road.
        var x = Random.value < .65f ? -2f : 1f;
        if (!TankSpawnClear(x))
        {
            x = x == -2f ? 1f : -2f;
            if (!TankSpawnClear(x)) return false;
        }
        var e = Make(Kind.Tank, "Art/tank2", new Vector3(x, BattleHalfHeight + .8f, 0), .5f, 4, 100);
        e.speed = .65f; // stationary on the ground, moves with the scrolling map
        e.timer = 1.8f;
        e.renderer.sortingOrder = 2;
        e.go.transform.localScale = Vector3.one * 1.28f;
        if (e.turret == null)
        {
            var child = new GameObject("Tank turret");
            child.transform.SetParent(e.go.transform, false);
            e.turret = child.AddComponent<SpriteRenderer>();
        }
        e.turret.sprite = LoadSprite("Art/tank3", 16f);
        e.turret.sortingOrder = 3;
        e.turret.color = Color.white;
        e.turret.transform.localPosition = Vector3.zero;
        e.turret.transform.localRotation = Quaternion.identity;
        e.turret.transform.localScale = Vector3.one;
        e.turret.gameObject.SetActive(true);
        enemies.Add(e);
        return true;
    }

    private void SpawnPlayerBullet(Vector3 at)
    {
        var b = Make(Kind.PlayerBullet, "Art/player_bullet", at, .13f, 1, 0);
        b.speed = 7.8f;
        b.renderer.sortingOrder = 8;
        b.renderer.color = new Color(.4f, 1f, 1f);
        b.go.transform.localScale = new Vector3(.8f, .65f, 1f);
        shots.Add(b);
        Pulse(at, new Color(.4f, 1f, 1f), true);
    }

    private void SpawnEnemyBullet(Vector3 at, float speed, float vx)
    {
        var b = Make(Kind.EnemyBullet, speed < 3f ? "Art/enemy_bullet" : "Art/fx_tile_0000_red", at, .16f, 1, 0);
        b.speed = speed;
        b.vx = vx;
        b.renderer.sortingOrder = 7;
        b.go.transform.localScale = Vector3.one;
        b.go.transform.rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(vx, -speed, 0));
        shots.Add(b);
    }

    private void SpawnPickup(Vector3 at)
    {
        var roll = Random.value;
        var kind = roll < .34f ? Kind.Life : roll < .68f ? Kind.Shield : Kind.Bomb;
        var path = kind == Kind.Life ? "Art/life" : kind == Kind.Shield ? "Art/shield" : "Art/bomb";
        var p = Make(kind, path, at, PickupRadius, 1, 0);
        p.speed = .9f;
        p.renderer.sortingOrder = 9;
        p.go.transform.localScale = Vector3.one;
        pickups.Add(p);
    }

    private void KillEnemy(int index, bool award)
    {
        var e = enemies[index];
        var at = e.go.transform.position;
        SpriteImpact(at, true);
        Pulse(at, new Color(1f, .75f, .25f), false);
        Burst(at, e.kind == Kind.Tank ? new Color(1f, .58f, .15f) : new Color(1f, .25f, .25f), e.kind == Kind.Tank ? 16 : 10);
        if (award)
        {
            score += e.value;
            if (Random.value < .13f) SpawnPickup(at);
        }
        Remove(enemies, index);
    }

    private void SpriteImpact(Vector3 at, bool explosion)
    {
        if (effects.Count >= 190) return;
        var flash = Make(Kind.Fx, null, at, explosion ? 1.1f : .55f, 1, 3);
        flash.renderer.sprite = impactFrames[0];
        flash.renderer.sortingOrder = 20;
        flash.timer = explosion ? .32f : .18f;
        flash.go.transform.localScale = Vector3.one * flash.radius;
        effects.Add(flash);
        if (!explosion) return;
        var smoke = Make(Kind.Fx, null, at, 0, 1, 4);
        smoke.renderer.sprite = smokeSprite;
        smoke.renderer.sortingOrder = 18;
        smoke.renderer.color = new Color(.7f, .72f, .75f);
        smoke.timer = .65f;
        smoke.speed = .3f;
        smoke.go.transform.localScale = Vector3.one * .35f;
        effects.Add(smoke);
    }

    private void ApplyPickup(Kind kind)
    {
        if (kind == Kind.Life) lives = Mathf.Min(5, lives + 1);
        else if (kind == Kind.Shield) shieldTime = ShieldDuration;
        else
        {
            for (var i = enemies.Count - 1; i >= 0; i--)
                if (EnemyTouchesViewport(enemies[i])) KillEnemy(i, true);
            shakeTime = .4f;
            shakeStrength = .22f;
            FlashScreen();
        }
    }

    private bool EnemyTouchesViewport(Entity enemy)
    {
        var bounds = enemy.renderer.bounds;
        if (enemy.kind == Kind.Tank && enemy.turret != null)
            bounds.Encapsulate(enemy.turret.bounds);
        var min = cam.WorldToViewportPoint(bounds.min);
        var max = cam.WorldToViewportPoint(bounds.max);
        return max.z > 0f && max.x >= 0f && min.x <= 1f && max.y >= 0f && min.y <= 1f;
    }

    private void DamagePlayer()
    {
        if (shieldTime > 0f || invulnerableTime > 0f || state != GameState.Playing) return;
        lives--;
        invulnerableTime = 1.25f;
        shakeTime = .35f;
        shakeStrength = .18f;
        if (effects.Count < 192)
        {
            var hit = Make(Kind.Fx, null, player.transform.position + Vector3.up * .25f, 0, 1, 5);
            hit.renderer.sprite = hurtFrames[0];
            hit.renderer.sortingOrder = 19;
            hit.timer = .22f;
            hit.go.transform.localScale = Vector3.one * .45f;
            effects.Add(hit);
        }
        Burst(player.transform.position, new Color(1f, .2f, .25f), 6);
        if (lives <= 0) EndGame();
    }

    private void Burst(Vector3 at, Color color, int count)
    {
        for (var i = 0; i < count && effects.Count < 192; i++)
        {
            var fx = Make(Kind.Fx, null, at, .02f, 1, 0);
            fx.renderer.sprite = SolidSprite(Color.white);
            fx.renderer.color = color;
            fx.renderer.sortingOrder = 20;
            var angle = Random.Range(0f, Mathf.PI * 2f);
            var force = Random.Range(.8f, 3f);
            fx.vx = Mathf.Cos(angle) * force;
            fx.speed = Mathf.Sin(angle) * force;
            fx.timer = Random.Range(.28f, .62f);
            effects.Add(fx);
        }
    }

    private void FlashScreen()
    {
        // An expanding translucent outline communicates the clear without a full-screen white flash.
        if (effects.Count >= 192) Remove(effects, 0);
        var wave = Make(Kind.Fx, null, player.transform.position, 0, 1, 6);
        wave.renderer.sprite = RingSprite();
        wave.renderer.sortingOrder = 15;
        wave.renderer.color = new Color(1f, .85f, .4f, .45f);
        wave.timer = .55f;
        wave.go.transform.localScale = Vector3.one * .15f;
        effects.Add(wave);
        for (var i = 0; i < 12; i++)
            Burst(new Vector3(Random.Range(-4f, 4f), Random.Range(-7f, 7f), 0f), new Color(1f, .9f, .25f), 1);
    }

    private void UpdateCamera(float dt)
    {
        if (shakeTime <= 0f)
        {
            cam.transform.position = cameraHome;
            return;
        }
        shakeTime -= dt;
        float fade = Mathf.Clamp01(shakeTime / .35f);
        cam.transform.position = cameraHome + new Vector3(Mathf.Sin(elapsed * 75f), Mathf.Sin(elapsed * 91f), 0f) * shakeStrength * fade * .55f;
    }

    private void StartRound()
    {
        ClearDynamic();
        terrainMap.ResetScroll();
        score = 0;
        enemyAppearance = 0;
        lives = 3;
        elapsed = 0f;
        shakeTime = 0f;
        cam.transform.position = cameraHome;
        shieldTime = 0f;
        invulnerableTime = 0f;
        fireClock = .12f;
        spawnClock = .75f;
        dragging = false;
        playerTarget = new Vector3(0f, -BattleHalfHeight + 1f, 0f);
        player = NewSpriteObject("Player", LoadSprite("Art/player", 32f), playerTarget, 10);
        playerRenderer = player.GetComponent<SpriteRenderer>();
        player.transform.localScale = Vector3.one * 1.28f;
        shieldRing = NewSpriteObject("Shield Ring", RingSprite(), player.transform.position, 9);
        shieldRing.GetComponent<SpriteRenderer>().color = new Color(.25f, .9f, 1f, .72f);
        shieldRing.transform.localScale = Vector3.one * 1.6f;
        shieldRing.SetActive(false);
        state = GameState.Playing;
        UpdateViewport();
    }

    private void EndGame()
    {
        state = GameState.GameOver;
        if (!selfTesting && score > highScore)
        {
            highScore = score;
            PlayerPrefs.SetInt("PixelAirBattle.HighScore", highScore);
            PlayerPrefs.Save();
        }
        if (player != null) playerRenderer.color = new Color(1f, 1f, 1f, .25f);
    }

    private void ClearDynamic()
    {
        DestroyList(enemies);
        DestroyList(shots);
        DestroyList(pickups);
        DestroyList(effects);
        if (player != null) Destroy(player);
        if (shieldRing != null) Destroy(shieldRing);
    }

    private void DestroyList(List<Entity> list)
    {
        for (var i = 0; i < list.Count; i++) Recycle(list[i]);
        list.Clear();
    }

    private Entity Make(Kind kind, string spritePath, Vector3 at, float radius, int hp, int value)
    {
        var e = pool.Count > 0 ? pool.Pop() : CreateEntity();
        e.kind = kind; e.radius = radius; e.hp = hp;
        e.speed = e.vx = e.age = 0f;
        e.timer = e.hit = e.recoil = 0f; e.value = value;
        if (e.turret != null) e.turret.gameObject.SetActive(false);
        e.renderer.sprite = spritePath == null ? SolidSprite(Color.white) : LoadSprite(spritePath, kind == Kind.Fighter ? 32f : 16f);
        e.renderer.color = Color.white;
        e.renderer.sortingOrder = 1;
        e.go.transform.SetPositionAndRotation(at, Quaternion.identity);
        e.previous = at;
        e.go.transform.localScale = Vector3.one;
        e.go.SetActive(true);
        return e;
    }

    private Entity CreateEntity()
    {
        createdEntities++;
        var go = new GameObject("Pooled sprite");
        return new Entity { go = go, renderer = go.AddComponent<SpriteRenderer>() };
    }

    private void Recycle(Entity e)
    {
        if (e.turret != null) e.turret.gameObject.SetActive(false);
        e.go.SetActive(false);
        pool.Push(e);
    }

    private void UpdateViewport()
    {
        int width = Screen.width, height = Screen.height;
        if (width <= 0 || height <= 0) return;
        bool battle = state != GameState.Start;
        if (width == viewportWidth && height == viewportHeight && battle == viewportBattle) return;
        float scale = Mathf.Min(width / 540f, height / 960f);
        var pixels = battle ? BattlePixelRect(width, height) :
            new Rect((width - 540f * scale) * .5f, (height - 960f * scale) * .5f, 540f * scale, 960f * scale);
        cam.rect = new Rect(pixels.x / width, pixels.y / height, pixels.width / width, pixels.height / height);
        cam.orthographicSize = battle ? BattleHalfHeight : 8f;
        viewportWidth = width;
        viewportHeight = height;
        viewportBattle = battle;
        viewportUpdates++;
    }

    private static Rect BattlePixelRect(float width, float height)
    {
        float scale = Mathf.Min(width / 540f, height / 960f);
        return new Rect((width - 540f * scale) * .5f, (height - 960f * scale) * .5f,
            540f * scale, (960f - HudHeight) * scale);
    }

    private void OnApplicationFocus(bool focus)
    {
        if (!focus && !selfTesting && state == GameState.Playing) state = GameState.Paused;
    }

    private GameObject NewSpriteObject(string name, Sprite sprite, Vector3 at, int order)
    {
        var go = new GameObject(name);
        go.transform.position = at;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        return go;
    }

    private Sprite LoadSprite(string path, float pixelsPerUnit)
    {
        Sprite cached;
        if (sprites.TryGetValue(path, out cached)) return cached;
        var texture = Resources.Load<Texture2D>(path);
        if (texture == null) return SolidSprite(Color.magenta);
        texture.filterMode = FilterMode.Point;
        cached = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
        sprites[path] = cached;
        return cached;
    }

    private Sprite SolidSprite(Color color)
    {
        var key = "solid_" + ColorUtility.ToHtmlStringRGBA(color);
        Sprite cached;
        if (sprites.TryGetValue(key, out cached)) return cached;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.SetPixels(new[] { color, color, color, color });
        texture.Apply();
        cached = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 24f);
        sprites[key] = cached;
        return cached;
    }

    private Sprite RingSprite()
    {
        Sprite cached;
        if (sprites.TryGetValue("shieldRing", out cached)) return cached;
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        var pixels = new Color[size * size];
        var center = new Vector2((size - 1) * .5f, (size - 1) * .5f);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var d = Vector2.Distance(new Vector2(x, y), center);
            pixels[y * size + x] = d > 27f && d < 31f ? Color.white : Color.clear;
        }
        texture.SetPixels(pixels);
        texture.Apply();
        cached = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 32f);
        sprites["shieldRing"] = cached;
        return cached;
    }

    private static Texture2D MakeTexture(Color color)
    {
        var texture = new Texture2D(2, 2);
        texture.SetPixels(new[] { color, color, color, color });
        texture.Apply();
        return texture;
    }

    private void Pulse(Vector3 at, Color color, bool muzzle)
    {
        if (effects.Count >= 192) return;
        var fx = Make(Kind.Fx, null, at, 0f, 1, muzzle ? 2 : 1);
        fx.renderer.sprite = RingSprite();
        fx.renderer.color = color;
        fx.renderer.sortingOrder = 19;
        fx.timer = muzzle ? .075f : .3f;
        fx.go.transform.localScale = Vector3.one * .12f;
        effects.Add(fx);
    }

    private static Vector3 ClosestPoint(Entity a, Vector3 center)
    {
        var delta = a.go.transform.position - a.previous;
        float t = delta.sqrMagnitude < .000001f ? 0f : Mathf.Clamp01(Vector3.Dot(center - a.previous, delta) / delta.sqrMagnitude);
        return a.previous + delta * t;
    }

    private static bool Touches(Entity a, Entity b)
    {
        var start = (Vector2)a.previous;
        var delta = (Vector2)a.go.transform.position - start;
        var center = (Vector2)b.go.transform.position;
        var t = delta.sqrMagnitude < .000001f ? 0 : Mathf.Clamp01(Vector2.Dot(center - start, delta) / delta.sqrMagnitude);
        return (start + delta * t - center).sqrMagnitude <= (a.radius + b.radius) * (a.radius + b.radius);
    }

    private void Remove(List<Entity> list, int index)
    {
        Recycle(list[index]);
        list.RemoveAt(index);
    }

    private void OnGUI()
    {
        if (buttonStyle == null) CreateGuiResources();
        var scale = Mathf.Min(Screen.width / 540f, Screen.height / 960f);
        var offsetX = (Screen.width - 540f * scale) * .5f;
        var offsetY = (Screen.height - 960f * scale) * .5f;
        GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity, Vector3.one * scale);
        var hudColor = GUI.color;
        PaintUiRect(new Rect(0, 0, 540, HudHeight), new Color(.12f, .105f, .085f));
        PaintUiRect(new Rect(0, HudHeight - 4, 540, 4), new Color(.43f, .43f, .28f));
        GUI.color = hudColor;
        if (showPerformance) SpriteNumbers.Performance(new Rect(20, 920, 400, 30), displayFrameTime, smallStyle);

        if (state == GameState.Playing)
        {
            GUI.Label(new Rect(16, 8, 192, 26), "战斗得分", smallStyle);
            DrawDigits(new Rect(16, 38, 192, 32), score, 6);
            for (int i = 0; i < 5; i++)
            {
                GUI.color = new Color(1, 1, 1, i < lives ? 1f : .2f);
                GUI.DrawTexture(new Rect(244 + i * 36, 20, 28, 28), lifeArt, ScaleMode.ScaleToFit);
            }
            GUI.color = Color.white;
            if (shieldTime > 0f) DrawShieldStatus();
            DrawPauseButton();
        }
        else if (state == GameState.Start)
        {
            DrawPanel(new Rect(48, 205, 444, 530));
            GUI.Label(new Rect(62, 242, 416, 70), "像素空战", titleStyle);
            GUI.Label(new Rect(82, 310, 376, 30), "P I X E L   A I R   B A T T L E", smallStyle);
            if (menuPlaneArt != null)
                GUI.DrawTexture(new Rect(238, 360, 64, 64), menuPlaneArt, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(82, 438, 376, 70), "拖动飞机 · 自动射击\n躲避弹幕，收集补给", bodyStyle);
            GUI.Label(new Rect(96, 528, 120, 40), "最佳战绩", hudStyle);
            SpriteNumbers.Score(new Rect(224, 532, 204, 32), highScore);
            if (MenuButton(new Rect(115, 605, 310, 70), "开始出击")) StartRound();
            if (Event.current.type == EventType.Repaint) startMenuRepaints++;
        }
        else if (state == GameState.Paused)
        {
            DrawPanel(new Rect(65, 270, 410, 390));
            if (MenuButton(new Rect(431, 280, 32, 32), "", closeArt)) state = GameState.Playing;
            GUI.Label(new Rect(85, 310, 370, 70), "暂时休整", titleStyle);
            GUI.Label(new Rect(100, 385, 120, 40), "当前得分", bodyStyle);
            SpriteNumbers.Score(new Rect(226, 389, 194, 32), score);
            if (MenuButton(new Rect(120, 455, 300, 70), "继续战斗")) state = GameState.Playing;
            if (MenuButton(new Rect(120, 545, 300, 70), "重新出击")) StartRound();
        }
        else
        {
            DrawPanel(new Rect(60, 230, 420, 500));
            GUI.Label(new Rect(75, 270, 390, 78), "任务结束", titleStyle);
            GUI.Label(new Rect(100, 360, 340, 40), "本次战绩", bodyStyle);
            DrawDigits(new Rect(145, 418, 250, 40), score, 6);
            GUI.Label(new Rect(100, 495, 120, 40), "最佳战绩", hudStyle);
            SpriteNumbers.Score(new Rect(226, 499, 194, 32), highScore);
            if (MenuButton(new Rect(120, 595, 300, 70), "再次出击")) StartRound();
        }
    }

    private void DrawDigits(Rect rect, int number, int count)
    {
        SpriteNumbers.Draw(rect, number.ToString().PadLeft(count, '0'));
    }

    private bool MenuButton(Rect rect, string label, Texture2D art = null)
    {
        bool pressed;
        bool clicked = ButtonFeedback.Handle(rect, out pressed);
        var savedColor = GUI.color;
        GUI.color = ButtonFeedback.Tint(savedColor, pressed);
        var visual = ButtonFeedback.VisualRect(rect, pressed);
        if (art != null) GUI.DrawTexture(visual, art, ScaleMode.ScaleToFit);
        else if (Event.current.type == EventType.Repaint)
            buttonStyle.Draw(visual, new GUIContent(label), false, false, false, false);
        GUI.color = savedColor;
        return clicked;
    }

    private void DrawPauseButton()
    {
        // A generous invisible touch target around a compact, font-independent pixel icon.
        var hitRect = new Rect(452, 10, 76, 76);
        bool hovered = hitRect.Contains(Event.current.mousePosition);
        bool pressed;
        bool clicked = ButtonFeedback.Handle(hitRect, out pressed);
        float y = 18 + (pressed ? 2 : 0);
        var savedColor = GUI.color;
        PaintUiRect(new Rect(464, 22, 52, 60), new Color(.07f, .055f, .04f, .7f));
        PaintUiRect(new Rect(464, y, 52, 60), new Color(.16f, .13f, .10f));
        PaintUiRect(new Rect(460, y + 4, 60, 52), new Color(.16f, .13f, .10f));
        PaintUiRect(new Rect(464, y + 4, 52, 48), pressed ? new Color(.19f, .21f, .15f) : hovered ? new Color(.38f, .40f, .25f) : new Color(.26f, .28f, .20f));
        PaintUiRect(new Rect(464, y + 4, 52, 3), new Color(.57f, .56f, .40f));
        PaintUiRect(new Rect(464, y + 52, 52, 4), new Color(.12f, .14f, .10f));
        var iconColor = new Color(.96f, .90f, .71f);
        PaintUiRect(new Rect(478, y + 18, 8, 24), iconColor);
        PaintUiRect(new Rect(494, y + 18, 8, 24), iconColor);
        GUI.color = savedColor;
        if (clicked) { dragging = false; state = GameState.Paused; }
    }

    private static void PaintUiRect(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }

    private void DrawShieldStatus()
    {
        // Fixed slots keep the icon, label and timer stable while the fill drains smoothly.
        if (shieldHudArt != null)
            GUI.DrawTexture(new Rect(244, 64, 28, 28), shieldHudArt, ScaleMode.ScaleToFit);
        GUI.Label(new Rect(276, 62, 48, 28), "护盾", smallStyle);
        int tenths = ShieldDisplayTenths;
        if (shieldHudTenths != tenths)
        {
            shieldHudTenths = tenths;
            shieldHudText = SpriteNumbers.Countdown(shieldTime, 1);
        }
        var savedColor = GUI.color;
        var accent = shieldTime <= 1f ? new Color(1f, .74f, .32f) : new Color(.35f, .86f, .96f);
        GUI.color = accent;
        SpriteNumbers.Draw(new Rect(332, 64, 54, 24), shieldHudText);
        GUI.Label(new Rect(392, 62, 24, 28), "秒", smallStyle);
        GUI.color = new Color(.06f, .07f, .065f);
        GUI.DrawTexture(new Rect(276, 94, 144, 9), Texture2D.whiteTexture);
        GUI.color = accent;
        GUI.DrawTexture(new Rect(278, 96, 140 * ShieldProgress, 5), Texture2D.whiteTexture);
        GUI.color = savedColor;
    }

    private void DrawPanel(Rect rect)
    {
        var savedColor = GUI.color;
        GUI.color = new Color(.13f, .105f, .085f, .96f);
        GUI.DrawTexture(new Rect(rect.x + 5, rect.y + 5, rect.width - 10, rect.height - 10), Texture2D.whiteTexture);
        GUI.color = savedColor;
        GUI.Box(rect, GUIContent.none, panelStyle);
    }
}
