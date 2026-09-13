using System.IO;
using UnityEngine;

namespace Chashouji {

/* 《查手机》Unity 版 —— 单一真源驱动，与网页版同构。
 *
 * 全场唯一状态是 S.p（查岗党进度 0~100）。它派生出 FX.phoneX，对抗线、刻度尺、
 * 地面辉光、HUD、角色取哪一帧，全部读它。画面上没有第二个战况来源，所以
 * "对抗线对不上画面"在构造上不可能发生。
 *
 * 场景整个由代码搭：相机、各层网格、字，全在 Awake 里造出来。于是空场景也能跑，
 * 也不会出现"场景文件里的引用丢了、Play 一下白屏"这种事。
 */
public class Director : MonoBehaviour {

    public const int W = 960, H = 1334;
    public const int TOP = 128, BOT = 1232, MID = 480;
    public const int ROWS = 15;
    public static readonly Color GREEN = new Color(126 / 255f, 217 / 255f, 87 / 255f, 1f);
    public static readonly Color RED   = new Color(255 / 255f,  72 / 255f, 72 / 255f, 1f);

    public class Params {
        public float curve = 1.55f;   // 进度→位移的非线性，中段慢、末段快
        /* 关键帧本身已经把"谁被拖过去"画进姿态里了，half/drag 管的是在此之上
           整组人物平移多少：中段那几档姿态差别很小，全靠这段平移把"手机正在被
           拽走"读出来，两头则相反 —— 姿态已经够夸张，再平移就该出画了。 */
        public float half = 108f;     // 对抗线最大偏移
        public float drag = 0.58f;    // 角色整体跟随对抗线的比例
        public float tilt = 1.55f, bulge = 46f, linkW = 0.80f, shapeRate = 2.6f;
        public float phoneY = 560f;   // 对抗线上"手机所在高度"，刻度与辉光的锚
        public float rugTop = 738f, rugBot = 1128f, rugTL = 88f, rugTR = 872f, rugBL = 28f, rugBR = 912f;
    }

    public class State { public float p = 50f, t = 0f; public bool auto = true; }

    public class Fx {
        public float phoneX = MID, phoneY = 560f;
        public float[] rowOff = new float[ROWS];
        public float[] rowHeat = new float[ROWS];
        /* 冲击波，独立于常规形变。rowOff 管"谁在推"（慢、由进度决定），这个管
           "刚刚挨了一下"（快、由事件决定）。混进一个数组的话，一次命中会被
           shapeRate 的趋近吃掉大半，读不出撞击。 */
        public float[] rowImp = new float[ROWS];
        public float struggle = 1f, actorX = 0f, jit = 0f;
        public float hitX = 0f, hitV = 0f;      // 角色被推开的位移与速度
        public float punch = 0f;                // 缩放脉冲
        public Color tint = Color.white;        // 命中染色
        public float tintA = 0f;
    }

    /* 对抗线画法：0 全都不画 / 1 原发光柱 / 2 地面战线+顶部指针 / 3 只要指针。
       默认 3，理由见 FrontMarkView.cs 顶部。 */
    public static int LineMode = 3;

    public static Params P = new Params();
    public static State S = new State();
    public static Fx FX = new Fx();
    public static Director I;

    FrameView frames;
    LineView line, lineOver;
    FrontMarkView frontMark;
    AmmoView ammo;
    GroundView ground;
    RulerView ruler;
    HudView hud;
    Camera cam;
    MeshObj bgObj;
    /* 震动只作用在"正在发生冲突的东西"上 —— 对抗线、角色、弹幕、粒子、刻度尺
       全挂在 shakeRoot 下。房间和地毯不动：机位是固定的，整幅画面一起震就得把
       背景放大做 overscan 才不露边，而背景一放大，地毯四角那组标定坐标就全偏
       了。HUD 和白闪也不震，它们不在场景里。 */
    Transform shakeRoot;

    float fps, frAcc; int frCount; int sweepDir = 1;
    public float Fps => fps;
    public string FrameInfo { get; private set; } = "";
    public FrameView Frames => frames;

    /* 场景里没放任何东西也能跑：没有 Director 就自己造一个。 */
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBoot() {
        if (FindObjectOfType<Director>() == null)
            new GameObject("Director").AddComponent<Director>();
    }

    void Awake() {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        P = new Params(); S = new State(); FX = new Fx();
        FX.phoneY = P.phoneY;

        GameLog.Init();
        GameLog.Line($"查手机 · Unity {Application.unityVersion} · 色彩空间 {QualitySettings.activeColorSpace}");

        cam = BuildCamera();
        var root = new GameObject("stage").transform;

        var bg = LoadTex("bg.jpg");
        if (bg == null) {
            GameLog.Line("素材没读到，StreamingAssets/art 下应有 bg.jpg 与 frames/f000~f100.png");
            return;
        }
        bgObj = Gfx.NewMesh("bg", root, Gfx.NewAlphaMat(), 0);
        bgObj.SetTexture(bg);
        BuildBgQuad(bgObj.mesh);

        ground = new GroundView(root, 10);

        shakeRoot = new GameObject("shake").transform;
        shakeRoot.SetParent(root, false);

        line = new LineView(shakeRoot, 20);
        frontMark = new FrontMarkView();
        frontMark.Init(shakeRoot, 22, 40);

        frames = new FrameView();
        frames.Init(shakeRoot, 30);
        FrameInfo = $"{frames.Loaded}/{FrameView.N} 档 · 每 1%";
        GameLog.Line(FrameInfo);

        lineOver = new LineView(shakeRoot, 40, 0.42f);   // 角色排 30，这一遍盖在他们身上
        ruler = new RulerView(shakeRoot, 50);
        ammo = new AmmoView();
        ammo.Init(shakeRoot, 52);
        // 实体粒子 54、发光粒子 55；白闪挂在不震的 root 上，排最顶
        ParticleFx.Init(shakeRoot, 54, 55, root, 70);
        hud = new HudView(root, 60);

        gameObject.AddComponent<DebugPanel>();

        for (int i = 0; i < 90; i++) Derive(1f / 60f);   // 预热，让指数趋近收敛到位
    }

    Camera BuildCamera() {
        var c = Camera.main;
        if (c == null) {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            c = go.AddComponent<Camera>();
        }
        c.orthographic = true;
        c.transform.position = new Vector3(W * 0.5f, -H * 0.5f, -100f);
        c.transform.rotation = Quaternion.identity;
        c.nearClipPlane = 0.1f; c.farClipPlane = 500f;
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.047f, 0.055f, 0.071f, 1f);   // #0c0e12
        c.allowHDR = false; c.allowMSAA = true;
        return c;
    }

    /// 竖屏 960x1334 永远完整可见：窗口更宽就按高度撑满，更窄就按宽度收
    void FitCamera() {
        float want = (float)W / H;
        float have = (float)Screen.width / Mathf.Max(1, Screen.height);
        cam.orthographicSize = have >= want ? H * 0.5f : (W * 0.5f) / have;
    }

    static Texture2D LoadTex(string file) {
        string path = Path.Combine(Application.streamingAssetsPath, "art", file);
        if (!File.Exists(path)) { GameLog.Line("找不到素材 " + path); return null; }
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
        if (!t.LoadImage(File.ReadAllBytes(path))) { GameLog.Line("解码失败 " + path); return null; }
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.anisoLevel = 4;
        t.Apply(true, false);
        return t;
    }

    void BuildBgQuad(Mesh m) {
        m.Clear();
        m.vertices = new[] {
            new Vector3(0, 0, 0), new Vector3(W, 0, 0), new Vector3(W, -H, 0), new Vector3(0, -H, 0),
        };
        m.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
        m.colors32 = new[] { (Color32)Color.white, (Color32)Color.white, (Color32)Color.white, (Color32)Color.white };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
    }

    // ---------- 单一真源 ----------
    public static void Derive(float dt) {
        float bias = (S.p - 50f) / 50f;
        // p 大 = 查岗党(女方,在左)占优 = 手机被拽向左
        float target = MID - Mathf.Sign(bias) * Mathf.Pow(Mathf.Abs(bias), P.curve) * P.half;
        if (bias == 0f) target = MID;
        FX.phoneX += (target - FX.phoneX) * MathX.Approach(dt, 4.2f);

        FX.struggle = 1f - Mathf.Abs(bias) * 0.78f;        // 僵持度：五五开时最高
        FX.jit = Mathf.Sin(S.t * 47f) * 2.4f * FX.struggle;
        FX.phoneY = P.phoneY + Mathf.Sin(S.t * 9.3f) * 6f * FX.struggle - Mathf.Abs(bias) * 14f;

        /* 角色被推开又站回来：弹簧-阻尼，不是单纯衰减 —— 单纯衰减只有"飘回去"，
           看不出"被推动了"。挨一下给的是速度不是位移。 */
        FX.hitV += -FX.hitX * FxTuning.HitK * dt;
        FX.hitV *= Mathf.Pow(FxTuning.HitDamp, dt * 60f);
        FX.hitX += FX.hitV * dt;
        FX.actorX = (FX.phoneX - MID) * P.drag + FX.hitX;

        FX.punch *= Mathf.Pow(FxTuning.PunchDecay, dt * 60f);
        if (FX.punch < 0.002f) FX.punch = 0f;
        FX.tintA *= Mathf.Pow(FxTuning.TintDecay, dt * 60f);
        if (FX.tintA < 0.004f) FX.tintA = 0f;

        // 对抗线上的冲击波：命中那一行注入冲量，随后沿线上下传播并衰减
        var im = FX.rowImp;
        var nim = new float[ROWS];
        for (int r = 0; r < ROWS; r++) {
            float nb = ((r > 0 ? im[r - 1] : im[r]) + (r < ROWS - 1 ? im[r + 1] : im[r])) * 0.5f;
            nim[r] = (im[r] + (nb - im[r]) * MathX.Approach(dt, FxTuning.WaveSpread))
                     * Mathf.Pow(FxTuning.WaveDecay, dt * 60f);
        }
        for (int r = 0; r < ROWS; r++) im[r] = Mathf.Abs(nim[r]) < 0.05f ? 0f : nim[r];

        var o = FX.rowOff;
        var next = new float[ROWS];
        for (int r = 0; r < ROWS; r++) {
            float d = (float)r / (ROWS - 1);
            float tilt = (0.5f - d) * 2f * bias * P.tilt;
            float wob = Mathf.Sin(S.t * 0.41f + r * 0.78f) * 0.62f + Mathf.Sin(S.t * 0.83f + r * 1.7f) * 0.31f;
            float desire = (tilt * 0.62f + wob * 0.42f) * P.bulge;
            float nb = ((r > 0 ? o[r - 1] : o[r]) + (r < ROWS - 1 ? o[r + 1] : o[r])) * 0.5f;
            float goal = (desire + P.linkW * nb) / (1f + P.linkW);
            next[r] = o[r] + (goal - o[r]) * MathX.Approach(dt, P.shapeRate);
        }
        for (int r = 0; r < ROWS; r++) o[r] = MathX.Clamp(next[r], -110f, 110f);

        float hot = 1f - Mathf.Abs(bias) * 0.42f;
        for (int r = 0; r < ROWS; r++) {
            float d = (float)r / (ROWS - 1);
            float g = Mathf.Exp(-Mathf.Pow((d - 0.36f) / 0.44f, 2f));
            FX.rowHeat[r] += (MathX.Clamp(g * 1.3f * hot, 0f, 1f) - FX.rowHeat[r]) * MathX.Approach(dt, 3.4f);
        }
    }

    static float SampleRow(float[] arr, float y) {
        float d = MathX.Clamp((y - TOP) / (BOT - TOP), 0f, 1f) * (ROWS - 1);
        int i = (int)MathX.Clamp(Mathf.Floor(d), 0, ROWS - 2);
        float f = d - i;
        float p0 = arr[Mathf.Max(0, i - 1)], p1 = arr[i], p2 = arr[i + 1], p3 = arr[Mathf.Min(ROWS - 1, i + 2)];
        return p1 + 0.5f * f * (p2 - p0 + f * (2f * p0 - 5f * p1 + 4f * p2 - p3 + f * (3f * (p1 - p2) + p3 - p0)));
    }

    // 对抗线在高度 y 处的横坐标 —— 光柱、刻度、地面辉光全读这一个函数
    public static float FrontAt(float y) =>
        FX.phoneX + SampleRow(FX.rowOff, y) + SampleRow(FX.rowImp, y);
    public static float HeatAt(float y) => MathX.Clamp(SampleRow(FX.rowHeat, y), 0f, 1f);
    public static Vector2 PhonePos() => new Vector2(FrontAt(FX.phoneY) + FX.jit, FX.phoneY);

    // ---------- 命中：一次礼物落地时发生的全部事情 ----------

    /* 一次命中同时动五样东西：粒子、对抗线冲量、角色位移与染色、屏幕震动、顿帧。
       写成单一入口而不是散在各处，是因为这五样的强度必须一起缩放 —— 分开调的话
       小礼物会震得比大礼物还狠，而观众读到的"这一下有多重"正是它们的合力。

       side: +1 打向查岗党(左/女方)，-1 打向灭迹党(右/男方)
       power: 1 点赞级  2 普通礼物  3 大礼物
       数值全在 FxTuning.cs 的 Hit 区。 */
    public static void Impact(int side, float y, int power, FxTuning.Recipe recipe) {
        float s = FxTuning.PowerScale(power);
        float x = FrontAt(y);

        // 冲量注入命中高度那一行，方向朝被打的一侧
        float d = MathX.Clamp((y - TOP) / (BOT - TOP), 0f, 1f) * (ROWS - 1);
        int i0 = (int)MathX.Clamp(Mathf.Floor(d), 0, ROWS - 1);
        FX.rowImp[i0] += -side * FxTuning.WaveInject * s;
        if (i0 > 0) FX.rowImp[i0 - 1] += -side * FxTuning.WaveInjectSide * s;
        if (i0 < ROWS - 1) FX.rowImp[i0 + 1] += -side * FxTuning.WaveInjectSide * s;

        FX.hitV += -side * FxTuning.HitPush * s;
        FX.punch = Mathf.Max(FX.punch, FxTuning.PunchAmount * s);
        /* 染色只是"挨了一下"的提示，不是照明。超过 0.2 角色的线稿和睡衣花纹就被
           洗掉了，而那正是这个玩法唯一能看的东西。 */
        FX.tint = Recipes.TintOf(recipe);
        FX.tintA = Mathf.Max(FX.tintA, FxTuning.TintAmount * Mathf.Min(1.4f, s));

        ParticleFx.AddShake(FxTuning.ShakePerHit * s);
        ParticleFx.AddFlash(FxTuning.Flash(power));
        ParticleFx.HitStop(FxTuning.HitStop(power));

        Recipes.Burst(recipe, x, y, side, s);
    }

    /* 弹幕撞上对抗线时由 AmmoView 回调。进度和特效都等真的撞上才结算 ——
       玩法和演出走同一个事件，观众看到的因果关系才对得上。 */
    public static void OnAmmoHit(FxTuning.Gift g, float y) {
        S.p = MathX.Clamp(S.p + g.from * g.gain, 0f, 100f);
        Impact(-g.from, y, g.power, g.recipe);
    }

    /// 面板上点一件礼物：只负责发射，撞上了才算数
    public void SendGift(FxTuning.Gift g) {
        S.auto = false;
        AmmoView.Launch(g);
    }

    void Update() {
        float raw = Mathf.Min(0.05f, Time.deltaTime);
        frCount++; frAcc += raw;
        if (frAcc >= 0.5f) { fps = frCount / frAcc; frCount = 0; frAcc = 0f; }

        /* 顿帧冻住的是游戏逻辑（角色姿态、对抗线、进度、正在飞的弹幕），粒子照
           真实时间走。两者用的是不同的时钟：定格是为了让观众多看两眼"他被打中
           了"，而火花和闪光正是这一下的可视化 —— 把它们一起冻住，爆炸就会迟到
           一百毫秒，读起来是"闪了一下、卡住、然后才炸开"。 */
        float dt = ParticleFx.Tick(raw);
        ParticleFx.Step(raw);
        AmmoView.Step(dt);
        S.t += dt;

        if (S.auto) {
            S.p += sweepDir * dt * 9f * (0.35f + Mathf.Abs(Mathf.Sin(S.t * 0.27f)) * 1.5f);
            if (S.p > 97f) { S.p = 97f; sweepDir = -1; }
            if (S.p < 3f) { S.p = 3f; sweepDir = 1; }
        }
        Derive(dt);
        Render();
    }

    void Render() {
        if (frames == null) return;
        FitCamera();
        float bias = (S.p - 50f) / 50f;

        // 震动：把"正在发生冲突"的那几层整体平移。画布 y 向下，落到世界要取负
        shakeRoot.localPosition = new Vector3(ParticleFx.Off.x, -ParticleFx.Off.y, 0f);

        ground.Rebuild(bias);

        /* LineMode==1 的发光柱要画两遍：一遍在角色之下当背景光柱，一遍以更低
           强度叠在角色之上 —— 两个人正好在中间抢东西，只画在下面的话它全程被
           两具身体挡死。其余模式下这两层都藏起来。 */
        bool useLine = LineMode == 1;
        line.Visible = useLine;
        lineOver.Visible = useLine;
        if (useLine) { line.Rebuild(); lineOver.Rebuild(); }
        frontMark.Rebuild(bias, LineMode);

        frames.Rebuild(S.p, FX.actorX, FX.punch, FX.tint, FX.tintA);
        ruler.Rebuild();
        ammo.Rebuild();
        ParticleFx.Rebuild();
        hud.Rebuild(S.p);
    }

    public void Nudge(float d) { S.auto = false; S.p = MathX.Clamp(S.p + d, 0f, 100f); }

    void OnApplicationQuit() { GameLog.Close(); }
}
}
