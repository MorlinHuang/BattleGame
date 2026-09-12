using System.IO;
using UnityEngine;

namespace Chashouji {

/* 《查手机》Unity 版 —— 单一真源驱动，与网页版 main.js 同构。
 *
 * 全场唯一状态是 S.p（查岗党进度 0~100）。它派生出 FX.phoneX，手机、双方的手
 * （IK 目标）、对抗线、刻度尺、地面辉光、HUD 全部读它。画面上没有第二个战况
 * 来源，所以"对抗线对不上画面"在构造上不可能发生。
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
        /* half/drag/站位是一组联动的解，不能单独改。手机的行程有 2×half，可手臂
           只在"肩到握点 = 0.6~0.95 倍臂长"这一小段里摆得自然：够不到就只能抻长
           上臂（IK 会拉伸到 1.15 倍，胳膊看着像橡皮），折过头则肘窝挤成一团。
           drag 让身体跟着手机走，把这 2×half 的行程压缩成 2×half×(1-drag) 落进
           那一小段里 —— boy 的手臂比 girl 短 23%，可用区间更窄，drag 是按他定的。 */
        public float half = 100f;     // 手机最大偏移
        public float drag = 0.86f;    // 角色跟随手机的比例（赢方后退、输方被拖，间距不变）
        public float tilt = 1.55f, bulge = 46f, linkW = 0.80f, shapeRate = 2.6f;
        /* 躯干最大倾角(rad)。它不只是姿态：躯干一转，肩就绕着腰划一段弧，肩到
           握点的距离跟着变 —— 倾角 0.24 时这段弧有 ±50px，比手机行程被 drag 抵消
           之后剩下的那点变化还大，两头一个折死一个抻长。 */
        public float lean = 0.12f;
        public float phoneY = 600f, phoneW = 144f, phoneH = 276f;
        /* 手机高度与两人站位是一组联动的解：手要握在机身上而不是盖在机身上，胳膊
           就得把自己那截手掌的长度让出来 —— 站得太近，肘只能折到 60 度，2D 切片的
           肘窝一折就皱。 */
        public float girlX = 177f, boyX = 753f, footY = 1125f;
        public float rugTop = 738f, rugBot = 1128f, rugTL = 88f, rugTR = 872f, rugBL = 28f, rugBR = 912f;
        public float girlH = 700f, boyH = 710f;
    }

    public class State { public float p = 50f, t = 0f; public bool auto = true; }

    public class Fx {
        public float phoneX = MID, phoneY = 600f, phoneRot = 0f;
        public float[] rowOff = new float[ROWS];
        public float[] rowHeat = new float[ROWS];
        public float struggle = 1f, girlX = 177f, boyX = 753f, jit = 0f;
    }

    public class Dbg { public bool wire, bones; }

    public static Params P = new Params();
    public static State S = new State();
    public static Fx FX = new Fx();
    public static Dbg DBG = new Dbg();
    public static Director I;

    public ActorView girl, boy;
    public ActorView[] actors;
    LineView line;
    GroundView ground;
    PhoneView phone;
    RulerView ruler;
    HudView hud;
    BonesView bonesView;
    Camera cam;
    MeshObj bgObj;

    float fps, frAcc; int frCount; int sweepDir = 1;
    public float Fps => fps;
    public string MeshInfo { get; private set; } = "";

    /* 场景里没放任何东西也能跑：没有 Director 就自己造一个。 */
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBoot() {
        if (FindObjectOfType<Director>() == null)
            new GameObject("Director").AddComponent<Director>();
    }

    void Awake() {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        P = new Params(); S = new State(); FX = new Fx(); DBG = new Dbg();
        FX.phoneY = P.phoneY; FX.girlX = P.girlX; FX.boyX = P.boyX;

        GameLog.Init();
        GameLog.Line($"查手机 · Unity {Application.unityVersion} · 色彩空间 {QualitySettings.activeColorSpace}");
        if (QualitySettings.activeColorSpace != ColorSpace.Gamma)
            GameLog.Line("提示：工程跑在 Linear 色彩空间，shader 里已做 sRGB 换算；" +
                         "要与网页版逐像素一致，可在 Player Settings 把 Color Space 设成 Gamma。");

        cam = BuildCamera();
        var root = new GameObject("stage").transform;

        var bg = LoadTex("bg.jpg");
        var gimg = LoadTex(RigData.Girl.img);
        var bimg = LoadTex(RigData.Boy.img);
        if (bg == null || gimg == null || bimg == null) {
            GameLog.Line("素材没读到，StreamingAssets/art 下应有 bg.jpg / girl.png / boy.png");
            return;
        }

        bgObj = Gfx.NewMesh("bg", root, Gfx.NewAlphaMat(), 0);
        bgObj.SetTexture(bg);
        BuildBgQuad(bgObj.mesh);

        ground = new GroundView(root, 10);
        line = new LineView(root, 20);

        girl = new ActorView(); girl.Init(RigData.Girl, gimg, -1, root, 30);
        boy  = new ActorView(); boy.Init(RigData.Boy,  bimg, +1, root, 31);
        actors = new[] { girl, boy };
        MeshInfo = $"网格 {girl.rm.triCount + boy.rm.triCount} 三角形 / {girl.rm.verts + boy.rm.verts} 顶点";
        GameLog.Line(MeshInfo);

        /* 机身排在角色之下，手才是压在它上面的 —— 这个玩法要读出来的就是"两只
           手在抢同一部手机"，机身盖在手上，两只手就成了在机身旁边虚抓。 */
        phone = new PhoneView(root, 25);
        ruler = new RulerView(root, 50);
        hud = new HudView(root, 60);
        bonesView = new BonesView(root, 70);

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
        FX.phoneRot = bias * 0.20f + Mathf.Sin(S.t * 23f) * 0.045f * FX.struggle;
        FX.phoneY = P.phoneY + Mathf.Sin(S.t * 9.3f) * 6f * FX.struggle - Mathf.Abs(bias) * 14f;

        FX.girlX = P.girlX + (FX.phoneX - MID) * P.drag;
        FX.boyX = P.boyX + (FX.phoneX - MID) * P.drag;

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

    // 对抗线在高度 y 处的横坐标 —— 手机、光柱、刻度、地面辉光全读这一个函数
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
        if (actors == null) return;
        FitCamera();
        float bias = (S.p - 50f) / 50f;

        ground.Rebuild(bias);
        line.Rebuild();

        // 劣势方压暗，不叠色相 —— 叠红会把黑发染成棕色，人物形象就变了
        foreach (var a in actors) { a.Pose(bias); a.Skin(); a.WireVisible = DBG.wire; }
        girl.SetTint(26, 24, 38, MathX.Clamp(-bias, 0f, 1f) * 0.32f);
        boy.SetTint(26, 24, 38, MathX.Clamp(bias, 0f, 1f) * 0.32f);

        phone.Rebuild();
        ruler.Rebuild();
        hud.Rebuild(S.p);
        bonesView.Rebuild(DBG.bones ? actors : null);
    }

    public void Nudge(float d) { S.auto = false; S.p = MathX.Clamp(S.p + d, 0f, 100f); }

    void OnApplicationQuit() { GameLog.Close(); }
}
}
