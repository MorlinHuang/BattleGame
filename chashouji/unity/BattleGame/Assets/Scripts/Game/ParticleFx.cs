using System.Collections.Generic;
using UnityEngine;

namespace Chashouji {

/* ParticleFx.cs —— 粒子池与打击感，与网页版 web/fx.js 同构。
 * ============================================================================
 * 这里只有"怎么画"，没有"画什么"：具体某件礼物炸出羽毛还是星星，由 Recipes.cs
 * 决定。形态刻意留成通用的六种，换题材皮不用动这个文件。
 *
 * 一般不需要改这里。要调数值去 FxTuning.cs，要改某个配方炸出什么去 Recipes.cs。
 *
 * 打击感的四个手段都不需要改角色帧素材 —— 角色是预渲染帧，做不了受击变形，
 * 但这四样都作用在贴图之外：
 *   HitStop  命中瞬间冻住游戏逻辑几十毫秒，打击感有一半来自这个
 *   AddShake 屏幕震动（Director 读 Off 去平移那几层）
 *   AddFlash 全屏白闪
 *   Punch    角色层的缩放脉冲与位移（Director 读走）
 * ============================================================================ */
public static class ParticleFx {

    public const int MAX = 1200;

    /* 六种形态，都是形状而非题材：
         Dot   光斑，会从 r 涨到 r1
         Spark 沿速度方向的短亮线，速度越快拉得越长
         Ring  扩散的椭圆环（贴地看所以压扁）
         Chip  翻滚的小片，羽毛 / 塑料碎 / 纸屑都用它
         Star  五角星，会旋转、会缩小
         Soft  绒絮，普通混合，用作灰尘与绒毛

       Chip 和 Star 吃 edge 描边。底图是明亮客厅，实体碎片不描边就糊在浅绿墙和
       米色地板里 —— 这跟角色睡衣有线稿是同一个道理，赛璐璐风格里"看得见"靠的
       是轮廓不是亮度。发光那三种没有描边，它们本来就该是光。 */
    public enum Kind { Dot, Spark, Ring, Chip, Star, Soft }

    /// 一次 Spawn 的参数。用对象初始化器写，跟网页版的字面量一一对应
    public struct Req {
        public Kind kind;
        public float x, y, vx, vy;
        public float g;        // 重力，像素/秒²
        public float drag;     // 每帧速度留存率，1 = 不衰减
        public float life;     // 存活秒数
        public float r, r1;    // 半径从 r 涨（或缩）到 r1
        public Color rgb;      // 主色，alpha 位无视，用下面的 a
        public float a;        // 不透明度 0~1，默认 1
        public float rot, vrot;
        public float w, h;     // Chip 的宽高
        public float lw;       // Spark/Ring 的线宽，Chip/Star 的描边宽
        public float sway;     // 横向摆幅，羽毛飘落用
        public float spin;     // 绕出生点公转的半径，星星绕头转用
        public Color edge;     // 描边色
        public bool hasEdge;
    }

    class P {
        public Kind kind;
        public float x, y, vx, vy, g, drag;
        public float life, maxLife, r, r1, a;
        public float rot, vrot, w, h, lw, sway, spin, seed;
        public Color rgb, edge;
        public bool hasEdge;
    }

    static readonly List<P> act = new List<P>(MAX);
    static readonly Stack<P> pool = new Stack<P>(MAX);

    /* 延迟生成队列。配方里"第二道环晚 70 毫秒出场，读起来才是砰—砰两下"这种
       需要它；网页版用的是 setTimeout，这里不走协程 —— 协程绑在 MonoBehaviour
       上，而这个类是静态的，而且协程走 Time.deltaTime，顿帧时会跟粒子脱节。 */
    struct Delayed { public float t; public Req req; }
    static readonly List<Delayed> waits = new List<Delayed>();

    static float shake, flash, stop;
    /// 当前震动偏移，Director 读它去平移"正在发生冲突"的那几层
    public static Vector2 Off;

    public static int Count => act.Count;

    // ---------- 绘制资源 ----------

    static readonly Draw2D dSolid = new Draw2D();
    static readonly Draw2D dGlow = new Draw2D();
    static MeshObj solidObj, glowObj, flashObj;

    /// order：实体层、发光层、白闪层各占一个
    public static void Init(Transform parent, int orderSolid, int orderGlow, Transform flashParent, int orderFlash) {
        solidObj = Gfx.NewMesh("fx_solid", parent, Gfx.NewAlphaMat(), orderSolid);
        glowObj = Gfx.NewMesh("fx_glow", parent, Gfx.NewAddMat(), orderGlow);
        flashObj = Gfx.NewMesh("fx_flash", flashParent, Gfx.NewAlphaMat(), orderFlash);
        act.Clear(); pool.Clear(); waits.Clear();
        shake = flash = stop = 0f; Off = Vector2.zero;
    }

    // ---------- 生成与推进 ----------

    public static void Spawn(Req o) {
        if (act.Count >= MAX) return;
        var p = pool.Count > 0 ? pool.Pop() : new P();
        p.kind = o.kind; p.x = o.x; p.y = o.y; p.vx = o.vx; p.vy = o.vy;
        p.g = o.g; p.drag = o.drag == 0f ? 1f : o.drag;
        p.life = p.maxLife = o.life;
        p.r = o.r == 0f ? 4f : o.r;
        p.r1 = o.r1 == 0f ? p.r : o.r1;
        p.rgb = o.rgb; p.a = o.a == 0f ? 1f : o.a;
        p.rot = o.rot; p.vrot = o.vrot; p.w = o.w; p.h = o.h;
        p.lw = o.lw == 0f ? 3f : o.lw;
        p.sway = o.sway; p.spin = o.spin;
        p.edge = o.edge; p.hasEdge = o.hasEdge;
        p.seed = Random.value * 6.283f;
        act.Add(p);
    }

    /// 过 sec 秒之后再生成。与粒子同一个时钟，都走真实时间
    public static void SpawnLater(float sec, Req o) {
        waits.Add(new Delayed { t = sec, req = o });
    }

    /* 粒子按真实时间推进，不受顿帧影响 —— 调用方传的应该是原始 dt。 */
    public static void Step(float dt) {
        for (int i = waits.Count - 1; i >= 0; i--) {
            var w = waits[i];
            w.t -= dt;
            if (w.t <= 0f) { waits.RemoveAt(i); Spawn(w.req); }
            else waits[i] = w;
        }
        for (int i = act.Count - 1; i >= 0; i--) {
            var p = act[i];
            p.life -= dt;
            if (p.life <= 0f) { act.RemoveAt(i); pool.Push(p); continue; }
            p.vy += p.g * dt;
            if (p.drag != 1f) {
                float k = Mathf.Pow(p.drag, dt * 60f);
                p.vx *= k; p.vy *= k;
            }
            p.x += (p.vx + (p.sway != 0f ? Mathf.Cos(p.seed + p.life * 3.1f) * p.sway : 0f)) * dt;
            p.y += p.vy * dt;
            p.rot += p.vrot * dt;
        }
        /* 衰减写成 pow(k, dt*60) 而不是 k*dt：后者会让节奏随帧率漂移。 */
        shake *= Mathf.Pow(FxTuning.ShakeDecay, dt * 60f);
        if (shake < 0.15f) shake = 0f;
        flash *= Mathf.Pow(FxTuning.FlashDecay, dt * 60f);
        if (flash < 0.004f) flash = 0f;

        float ang = Random.value * 6.283f;
        Off = new Vector2(Mathf.Cos(ang) * shake, Mathf.Sin(ang) * shake * 0.6f);
    }

    /* 淡入是绝对时间，淡出才按寿命比例。
       两头都按比例的话，短命的火花看不出问题，但羽毛活两秒、15% 就是 300 毫秒
       —— 枕头炸开之后要等三分之一秒羽毛才显形，命中最该看见东西的那一刻画面
       是空的。"入场"是一个固定的瞬间，与这个粒子打算活多久没关系。 */
    const float FADE_IN = 0.045f;
    static float Fade(P p) {
        float inn = Mathf.Min(1f, (p.maxLife - p.life) / FADE_IN);
        float outt = Mathf.Min(1f, p.life / (p.maxLife * 0.34f));
        return inn * outt;
    }

    // ---------- 打击感 ----------

    /// 返回本帧游戏逻辑该走多少时间。顿帧期间返回 0：画面照常刷新，只是一切不动
    public static float Tick(float dt) {
        if (stop > 0f) { stop -= dt; return 0f; }
        return dt;
    }

    public static void HitStop(float sec) { stop = Mathf.Max(stop, sec); }
    public static void AddShake(float v) { shake = Mathf.Min(FxTuning.ShakeMax, shake + v); }
    public static void AddFlash(float v) { flash = Mathf.Max(flash, v); }

    public static void Clear() {
        for (int i = act.Count - 1; i >= 0; i--) pool.Push(act[i]);
        act.Clear(); waits.Clear();
        shake = flash = stop = 0f; Off = Vector2.zero;
    }

    // ---------- 绘制 ----------

    public static void Rebuild() {
        dSolid.Clear(); dGlow.Clear();

        // 第一趟：绒絮、碎片、星星 —— 它们是实体，走普通混合
        for (int i = 0; i < act.Count; i++) {
            var p = act[i];
            if (p.kind != Kind.Soft && p.kind != Kind.Chip && p.kind != Kind.Star) continue;
            float k = p.life / p.maxLife;
            float alpha = p.a * Fade(p);
            if (alpha <= 0.01f) continue;

            if (p.kind == Kind.Soft) {
                float r = p.r + (p.r1 - p.r) * (1f - k);
                dSolid.Glow(p.x, p.y, r,
                    new Color(p.rgb.r, p.rgb.g, p.rgb.b, 0.9f * alpha),
                    new Color(p.rgb.r, p.rgb.g, p.rgb.b, 0.42f * alpha), 0.5f, 20);

            } else if (p.kind == Kind.Star) {
                float r = p.r + (p.r1 - p.r) * (1f - k);
                float ph = p.seed + (1f - k) * 7.4f;
                float cx = p.x + (p.spin != 0f ? Mathf.Cos(ph) * p.spin : 0f);
                float cy = p.y + (p.spin != 0f ? Mathf.Sin(ph) * p.spin * 0.42f : 0f);
                var pts = Draw2D.StarPts(cx, cy, r, p.rot);
                dSolid.PolyFan(cx, cy, pts, new Color(p.rgb.r, p.rgb.g, p.rgb.b, alpha));
                if (p.hasEdge)
                    dSolid.StrokeClosed(pts, p.lw, new Color(p.edge.r, p.edge.g, p.edge.b, alpha));

            } else {
                // 翻滚时按 cos 压扁，读起来是一片薄东西在空中打转
                float hh = Mathf.Max(0.6f, p.h * 0.5f * Mathf.Abs(Mathf.Cos(p.rot * 1.7f)));
                dSolid.Push(p.x, p.y, p.rot);
                // 圆角给到半高的满值：长宽比大的时候读成羽毛，接近正方时读成碎片
                var pts = Draw2D.RoundRectPts(-p.w * 0.5f, -hh, p.w, hh * 2f, Mathf.Min(p.w, hh * 2f) * 0.5f);
                dSolid.Poly(pts, new Color(p.rgb.r, p.rgb.g, p.rgb.b, alpha));
                if (p.hasEdge)
                    dSolid.StrokeClosed(pts, p.lw, new Color(p.edge.r, p.edge.g, p.edge.b, alpha));
                dSolid.Pop();
            }
        }

        // 第二趟：发光部分，相加混合
        for (int i = 0; i < act.Count; i++) {
            var p = act[i];
            if (p.kind == Kind.Soft || p.kind == Kind.Chip || p.kind == Kind.Star) continue;
            float k = p.life / p.maxLife;
            float alpha = p.a * Fade(p);
            if (alpha <= 0.01f) continue;

            if (p.kind == Kind.Dot) {
                float r = p.r + (p.r1 - p.r) * (1f - k);
                dGlow.Glow(p.x, p.y, r,
                    new Color(1f, 1f, 1f, alpha),
                    new Color(p.rgb.r, p.rgb.g, p.rgb.b, 0.38f * alpha), 0.42f, 22);

            } else if (p.kind == Kind.Spark) {
                float sp = MathX.Hypot(p.vx, p.vy);
                float len = Mathf.Min(26f, 4f + sp * 0.028f);
                float nx = sp > 0f ? p.vx / sp : 1f, ny = sp > 0f ? p.vy / sp : 0f;
                dGlow.Segment(new Vector2(p.x, p.y), new Vector2(p.x - nx * len, p.y - ny * len),
                              p.lw, new Color(p.rgb.r, p.rgb.g, p.rgb.b, alpha));

            } else {   // Ring
                float r = p.r + (p.r1 - p.r) * (1f - k);
                dGlow.EllipseRing(p.x, p.y, r, r * 0.5f, p.lw * k + 0.6f,
                                  new Color(p.rgb.r, p.rgb.g, p.rgb.b, alpha));
            }
        }

        dSolid.Apply(solidObj.mesh);
        dGlow.Apply(glowObj.mesh);
        RebuildFlash();
    }

    /* 全屏白闪。画在最上层，连 HUD 一起罩住 —— 只罩画面的话会显得闪光是"场景
       里的光"，而它要的是"这一下很重"。
       ⚠ 用普通混合而不是相加：底图是明亮的客厅，相加叠上去立刻过曝成一片白，
       人物全糊。可用的强度是底图亮度定的，暗色战场能给 0.5，这里 0.22 就到顶。 */
    static readonly Draw2D dFlash = new Draw2D();
    static void RebuildFlash() {
        dFlash.Clear();
        if (flash > 0.004f)
            dFlash.Rect(0, 0, Director.W, Director.H, new Color(1f, 0.988f, 0.965f, flash));
        dFlash.Apply(flashObj.mesh);
    }
}
}
