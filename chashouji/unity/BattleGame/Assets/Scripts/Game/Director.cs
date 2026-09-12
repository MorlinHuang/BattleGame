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
        public float struggle = 1f, actorX = 0f, jit = 0f;
    }

    public static Params P = new Params();
    public static State S = new State();
    public static Fx FX = new Fx();
    public static Director I;

    FrameView frames;
    LineView line, lineOver;
    GroundView ground;
    RulerView ruler;
    HudView hud;
    Camera cam;
    MeshObj bgObj;

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
        line = new LineView(root, 20);

        frames = new FrameView();
        frames.Init(root, 30);
        FrameInfo = $"关键帧 {frames.Loaded}/{FrameView.N} 张 · 每 {FrameView.STEP}%";
        GameLog.Line(FrameInfo);

        lineOver = new LineView(root, 40, 0.42f);   // 角色排 30/31，这一遍盖在他们身上
        ruler = new RulerView(root, 50);
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

        FX.actorX = (FX.phoneX - MID) * P.drag;

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
    public static float FrontAt(float y) => FX.phoneX + SampleRow(FX.rowOff, y);
    public static float HeatAt(float y) => MathX.Clamp(SampleRow(FX.rowHeat, y), 0f, 1f);
    public static Vector2 PhonePos() => new Vector2(FrontAt(FX.phoneY) + FX.jit, FX.phoneY);

    void Update() {
        float dt = Mathf.Min(0.05f, Time.deltaTime);
        S.t += dt; frCount++; frAcc += dt;
        if (frAcc >= 0.5f) { fps = frCount / frAcc; frCount = 0; frAcc = 0f; }

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
        ground.Rebuild((S.p - 50f) / 50f);
        line.Rebuild();
        frames.Rebuild(S.p, FX.actorX);
        lineOver.Rebuild();
        ruler.Rebuild();
        hud.Rebuild(S.p);
    }

    public void Nudge(float d) { S.auto = false; S.p = MathX.Clamp(S.p + d, 0f, 100f); }

    void OnApplicationQuit() { GameLog.Close(); }
}
}
