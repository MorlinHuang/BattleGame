using System.Collections.Generic;
using Godot;

namespace Chashouji {

/* 粒子与打击感 —— 逐字取自网页版 web/fx.js。
 *
 * 全部特效走同一个粒子池。网页版那边是"预渲染贴图 + drawImage"，因为 canvas 每
 * 帧重算径向渐变太贵；这里改走顶点色三角形（Draw2D.Radial），结果逐像素相同，
 * 而且整池粒子进同一批一次提交 —— 比贴图还省，也不必为每种颜色各烘一张。
 *
 * 这里只有"怎么画"，没有"画什么"：具体某件礼物炸出羽毛还是星星，由 Recipes.cs
 * 的配方表决定。粒子形态刻意留成通用的那几种，换题材皮不用动这个文件。
 *
 * 打击感的四个手段都不需要改角色帧素材 —— 角色是预渲染帧，做不了受击变形，
 * 但顿帧、整体位移、染色、缩放这四样都作用在贴图之外：
 *   HitStop  命中瞬间冻住整个世界几十毫秒，打击感有一半来自这个
 *   AddShake 屏幕震动
 *   AddFlash 全屏白闪
 *   FX.punch 给角色层的冲击位移与缩放脉冲（由 Director 读走）
 */

/* kind 都是形态而非题材：
     Dot   光斑，会从 r 涨到 r1
     Spark 沿速度方向的短亮线，速度越快拉得越长
     Ring  扩散的椭圆环（贴地看所以压扁）
     Chip  翻滚的小片，羽毛/塑料碎/纸屑都用它
     Star  五角星，会旋转、会缩小
     Soft  绒絮，普通混合，用作灰尘与绒毛
     Heart 爱心，会往上飘
     Card  照片，始终是个有直角的矩形

   Chip、Star、Heart、Card 都吃 edge（描边色）。底图是明亮客厅，实体不描边就糊
   在浅绿墙和米色地板里 —— 这跟角色睡衣有线稿是同一个道理，赛璐璐风格里
   "看得见"靠的是轮廓不是亮度。发光那三种（Dot/Spark/Ring）没有描边，它们本来
   就该是光。 */
public enum PKind { Dot, Spark, Ring, Chip, Star, Soft, Heart, Card }

public class Particle {
    public PKind kind;
    public float x, y, vx, vy;
    public float g;          // 重力
    public float drag;       // 每帧速度衰减（按 pow(drag, dt*60) 施加）
    public float life, maxLife;
    public float r, r1;      // 半径：出生时 r，寿终时 r1
    public Color rgb;
    public float a;
    public float rot, vrot;
    public float w, h;       // Chip / Card 的尺寸
    public float lw;         // 描边宽度 / Spark 的线宽
    public Color edge; public bool hasEdge;
    public float spin;       // 绕出生点公转的半径，星星绕头转用
    public float sway;       // 横向摆幅，羽毛飘落用
    public float seed;
    /* 出场延迟。网页版那几道"晚 70ms 再荡开"的环用的是 setTimeout，这里让粒子
       自己晚一点出生 —— 同一件事，但不必为每一下命中分配一个闭包和一个定时器。
       延迟期间 life 不减，所以它的寿命是从真正出场那一刻算起的。 */
    public float delay;

    public void Reset(PKind k, float px, float py, float lf) {
        kind = k; x = px; y = py;
        vx = vy = 0f; g = 0f; drag = 1f;
        life = maxLife = lf;
        r = r1 = 4f;
        rgb = Colors.White; a = 1f;
        rot = vrot = 0f;
        w = h = 0f; lw = 3f;
        hasEdge = false; edge = Colors.White;
        spin = sway = delay = 0f;
        seed = Particles.Rnd() * 6.283f;
    }

    /// 网页版那些 [r,g,b] 字面量都是 0~255，这里统一从那个区间进来
    public Particle Rgb(float r255, float g255, float b255) { rgb = MathX.C255(r255, g255, b255); return this; }
    public Particle Rgb(Color c) { rgb = c; return this; }
    public Particle Edge(float r255, float g255, float b255) { edge = MathX.C255(r255, g255, b255); hasEdge = true; return this; }
    public Particle Edge(Color c) { edge = c; hasEdge = true; return this; }
}

public static class Particles {
    const int MAX = 1200;
    static readonly List<Particle> act = new List<Particle>(MAX);
    static readonly Stack<Particle> pool = new Stack<Particle>();
    /* 池满时的黑洞。返回它而不是 null，调用方就不必每 spawn 一颗判一次空 ——
       配方里一次爆炸要 spawn 七八十颗，那是七八十个 if，而且漏掉一个就是崩溃。
       它永远不进 act，写进去的东西不会被画出来。 */
    static readonly Particle sink = new Particle();

    static float shake, flash, stop, cool, lvl;
    /// 当前震动偏移，Director 读它来平移那几层
    public static Vector2 Off;
    public static float FlashAmt => flash;
    public static int Count => act.Count;

    /* 固定种子。网页版用 Math.random()，这里给定种子是为了诊断截图可复现 ——
       同样的参数跑两次要能截出同一张图，否则"这一版比上一版好"没法比。 */
    static readonly System.Random rng = new System.Random(20260917);
    public static float Rnd() => (float)rng.NextDouble();
    public static float Rnd(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    public static Particle Spawn(PKind k, float x, float y, float life) {
        if (act.Count >= MAX) { sink.Reset(k, x, y, life); return sink; }
        var p = pool.Count > 0 ? pool.Pop() : new Particle();
        p.Reset(k, x, y, life);
        act.Add(p);
        return p;
    }

    public static void Update(float dt) {
        for (int i = act.Count - 1; i >= 0; i--) {
            var p = act[i];
            if (p.delay > 0f) { p.delay -= dt; continue; }
            p.life -= dt;
            if (p.life <= 0f) { act.RemoveAt(i); pool.Push(p); continue; }
            p.vy += p.g * dt;
            if (p.drag != 1f) { float d = Mathf.Pow(p.drag, dt * 60f); p.vx *= d; p.vy *= d; }
            p.x += (p.vx + (p.sway != 0f ? Mathf.Cos(p.seed + p.life * 3.1f) * p.sway : 0f)) * dt;
            p.y += p.vy * dt;
            p.rot += p.vrot * dt;
        }
        /* 衰减都写成 pow(k, dt*60) 而不是 k*dt：后者会让节奏随帧率漂移，
           这个坑在对抗线那边已经踩过一次。 */
        shake *= Mathf.Pow(0.86f, dt * 60f);
        if (shake < 0.15f) shake = 0f;
        flash *= Mathf.Pow(0.80f, dt * 60f);
        if (flash < 0.004f) flash = 0f;

        float ang = Rnd() * 6.283f;
        // 竖屏，横向震得多一点更像撞击
        Off = new Vector2(Mathf.Cos(ang) * shake, Mathf.Sin(ang) * shake * 0.6f);
    }

    /* 淡入是绝对时间，淡出才按寿命比例。
       原来两头都按比例（前 15% 淡入），短命的火花看不出问题，但羽毛活 2 秒，
       15% 就是 300 毫秒 —— 枕头炸开之后要等三分之一秒羽毛才显形，命中最该看
       见东西的那一刻画面是空的。"入场"是一个固定的瞬间，与这个粒子打算活多久
       没有任何关系。 */
    const float FADE_IN = 0.045f;
    static float Fade(Particle p) {
        float inn = Mathf.Min(1f, (p.maxLife - p.life) / FADE_IN);
        float outt = Mathf.Min(1f, p.life / (p.maxLife * 0.34f));
        return inn * outt;
    }

    /* 顿帧：返回本帧实际该走多少时间。命中瞬间把世界冻住，其余照常。
       注意冻的是游戏时间不是渲染 —— 画面照常刷新，只是一切都不动。 */
    public static float Tick(float dt) {
        if (cool > 0f) { cool -= dt; if (cool <= 0f) lvl = 0f; }
        if (stop > 0f) { stop -= dt; return 0f; }
        return dt;
    }

    /* 顿帧必须有不应期。
       "冻住"之所以有力，是因为它打断了流动 —— 而连点时命中本来就是连续的，
       每一击都冻的话顿帧首尾相接，世界就长期停摆：帧率一点没掉，观众却读成
       "卡了"。实测连点时有 65% 的帧是冻住的，把礼物间隔放宽三倍还是 65%，
       说明撑起它的不是礼物密度，是"每一击都冻"这条规则本身。
       所以冻完之后，要让世界正常流动更长的一段时间。比例给到 1:2 而不是 1:1：
       命中越密集，顿帧越该退让 —— 每秒有九件礼物落地的时候根本没有"这一击"
       可言，观众要看的是整体的热闹，而一半时间不动的画面只会读成掉帧。
       更重的一击可以打断正在演的那一次 —— 大礼物压过点赞，这个优先级跟别处
       是一致的。 */
    public static void HitStop(float sec) {
        if (sec <= 0f) return;
        if (cool > 0f && sec <= lvl) return;
        stop = Mathf.Max(stop, sec);
        lvl = sec;
        cool = sec * 3f;          // 冻 sec，再流动 2*sec —— 占空比到顶三分之一
    }

    public static void AddShake(float v) { shake = Mathf.Min(30f, shake + v); }
    public static void AddFlash(float v) { flash = Mathf.Max(flash, v); }

    public static void Clear() {
        while (act.Count > 0) { pool.Push(act[act.Count - 1]); act.RemoveAt(act.Count - 1); }
        shake = flash = stop = cool = lvl = 0f;
        Off = Vector2.Zero;
    }

    // ---------- 绘制 ----------

    static Color A(Color c, float a) => new Color(c.R, c.G, c.B, a);

    /// 第一趟：绒絮、碎片、星星、爱心、照片 —— 普通混合，它们是实体
    public static void DrawSolid(Draw2D d) {
        for (int i = 0; i < act.Count; i++) {
            var p = act[i];
            if (p.delay > 0f) continue;
            if (p.kind != PKind.Soft && p.kind != PKind.Chip && p.kind != PKind.Star
                && p.kind != PKind.Heart && p.kind != PKind.Card) continue;
            float k = p.life / p.maxLife;
            float alpha = p.a * Fade(p);
            if (alpha <= 0.01f) continue;

            if (p.kind == PKind.Soft) {
                float r = p.r + (p.r1 - p.r) * (1f - k);
                /* 网页版是三档渐变 0.9 / 0.5→0.42 / 1→0，中间那一档是线性插值
                   算出来的，补上只为凑满四档，形状一模一样。 */
                d.Radial(p.x, p.y, r, r,
                         A(p.rgb, 0.90f * alpha), 0.50f, A(p.rgb, 0.42f * alpha),
                         0.75f, A(p.rgb, 0.21f * alpha), 1f, A(p.rgb, 0f), 20);

            } else if (p.kind == PKind.Star || p.kind == PKind.Heart) {
                float r = p.r + (p.r1 - p.r) * (1f - k);
                float ph = p.seed + (1f - k) * 7.4f;
                d.Push(p.x + (p.spin != 0f ? Mathf.Cos(ph) * p.spin : 0f),
                       p.y + (p.spin != 0f ? Mathf.Sin(ph) * p.spin * 0.42f : 0f), p.rot);
                /* StarPts / HeartPts 返回的是共享缓冲，填完就被下一次调用覆盖，
                   所以描边前要重新取一次 —— 两次取到的内容相同。 */
                d.PolyFan(0f, 0f, p.kind == PKind.Star ? Draw2D.StarPts(0f, 0f, r, 0f)
                                                       : Draw2D.HeartPts(0f, 0f, r), A(p.rgb, alpha));
                if (p.hasEdge)
                    d.StrokeClosed(p.kind == PKind.Star ? Draw2D.StarPts(0f, 0f, r, 0f)
                                                        : Draw2D.HeartPts(0f, 0f, r), p.lw, A(p.edge, alpha));
                d.Pop();

            } else if (p.kind == PKind.Card) {
                /* 卡片：一张照片。它跟 Chip 的区别不是参数而是**语义** —— Chip 是
                   "空中翻滚的薄片"，带描边时圆角被拉到半高满值、短边收成半圆，
                   再配合按 cos(rot) 的压扁，无论怎么调参数都只会读成一颗胶囊。
                   而照片必须始终是个有直角的矩形，否则"是不是照片"就没了，那正是
                   这个粒子全部的意义。所以它不压扁、不圆角，只是斜着飘。
                   内芯那一块是相纸中间的画面：没有它就只是一张白纸。 */
                d.Push(p.x, p.y, p.rot);
                float hw = p.w / 2f, hh2 = p.h / 2f;
                d.Rect(-hw, -hh2, p.w, p.h, A(p.rgb, alpha));
                if (p.hasEdge) {
                    var ec = A(p.edge, alpha);
                    var a0 = new Vector2(-hw, -hh2); var a1 = new Vector2(hw, -hh2);
                    var a2 = new Vector2(hw, hh2);   var a3 = new Vector2(-hw, hh2);
                    d.Segment(a0, a1, p.lw, ec); d.Segment(a1, a2, p.lw, ec);
                    d.Segment(a2, a3, p.lw, ec); d.Segment(a3, a0, p.lw, ec);
                    d.Rect(-hw * 0.74f, -hh2 * 0.82f, p.w * 0.74f, p.h * 0.58f, A(p.edge, alpha * 0.5f));
                }
                d.Pop();

            } else {
                // 翻滚时按 cos 压扁，读起来是一片薄东西在空中打转
                float hh = p.h / 2f * Mathf.Abs(Mathf.Cos(p.rot * 1.7f));
                d.Push(p.x, p.y, p.rot);
                if (p.hasEdge) {
                    /* 胶囊形：羽毛和碎屑都不是方砖，描边一上去直角就很扎眼。圆角给到
                       半高的满值，短边直接收成半圆 —— 长宽比大的时候读成羽毛，接近
                       正方的时候读成碎片，一个形状覆盖两种题材。 */
                    float rr = Mathf.Min(p.w, hh * 2f) * 0.5f;
                    d.RoundRect(-p.w / 2f, -hh, p.w, hh * 2f, rr, A(p.rgb, alpha));
                    d.StrokeClosed(Draw2D.RoundRectPts(-p.w / 2f, -hh, p.w, hh * 2f, rr),
                                   p.lw, A(p.edge, alpha));
                } else {
                    d.Rect(-p.w / 2f, -hh, p.w, hh * 2f, A(p.rgb, alpha));
                }
                d.Pop();
            }
        }
    }

    /// 第二趟：发光部分，相加混合（对应网页版的 globalCompositeOperation='lighter'）
    public static void DrawGlow(Draw2D d) {
        for (int i = 0; i < act.Count; i++) {
            var p = act[i];
            if (p.delay > 0f) continue;
            if (p.kind != PKind.Dot && p.kind != PKind.Spark && p.kind != PKind.Ring) continue;
            float k = p.life / p.maxLife;
            float alpha = p.a * Fade(p);
            if (alpha <= 0.01f) continue;

            if (p.kind == PKind.Dot) {
                float r = p.r + (p.r1 - p.r) * (1f - k);
                // 中心是白的（高光），0.22 之后才是粒子自己的颜色
                d.Radial(p.x, p.y, r, r,
                         new Color(1f, 1f, 1f, alpha), 0.22f, A(p.rgb, 0.95f * alpha),
                         0.55f, A(p.rgb, 0.35f * alpha), 1f, A(p.rgb, 0f), 20);

            } else if (p.kind == PKind.Spark) {
                float sp = MathX.Hypot(p.vx, p.vy);
                float len = Mathf.Min(26f, 4f + sp * 0.028f);
                float nx = sp != 0f ? p.vx / sp : 1f, ny = sp != 0f ? p.vy / sp : 0f;
                var c = A(p.rgb, alpha);
                var q0 = new Vector2(p.x, p.y);
                var q1 = new Vector2(p.x - nx * len, p.y - ny * len);
                d.Segment(q0, q1, p.lw, c);
                // 网页版这条线是 lineCap='round'，两头各补一个圆头
                d.Ellipse(q0.X, q0.Y, p.lw * 0.5f, p.lw * 0.5f, c, 6);
                d.Ellipse(q1.X, q1.Y, p.lw * 0.5f, p.lw * 0.5f, c, 6);

            } else {
                float r = p.r + (p.r1 - p.r) * (1f - k);
                d.EllipseRing(p.x, p.y, r, r * 0.5f, p.lw * k + 0.6f, A(p.rgb, alpha), 30);
            }
        }
    }
}

/* 粒子的两层画布 + 全屏白闪。
   实体与发光分成两个 MeshLayer，因为它们的混合模式不同 —— 对应网页版 draw()
   里那两趟循环。 */
public partial class ParticleFx : Node2D {
    MeshLayer solid, glow;
    FlashLayer flash;

    public void Init(Node parent, int z, Node flashParent, int flashZ) {
        parent.AddChild(this);
        ZIndex = z;
        solid = Gfx.NewLayer(this, "fx_solid", false, 0);
        glow  = Gfx.NewLayer(this, "fx_glow", true, 2);
        /* 白闪单独挂在震动容器**外面**：它要盖住 HUD，而且不能跟着画面抖 ——
           一块全屏的板子跟着抖，边上就会露出没盖到的一条。 */
        flash = new FlashLayer();
        flash.Setup(flashParent, "fx_flash", false, flashZ);
    }

    public void Rebuild() {
        var ds = solid.D; ds.Clear(); Particles.DrawSolid(ds); solid.Commit();
        var dg = glow.D;  dg.Clear(); Particles.DrawGlow(dg);  glow.Commit();
        flash.QueueRedraw();
    }

    /* 全屏白闪。画在最上层，连 HUD 一起罩住 —— 只罩画面的话会显得闪光是
       "场景里的光"，而它要的是"这一下很重"。
       用普通混合而不是相加：底图是明亮的客厅（浅绿墙、米色地板），相加叠上去
       立刻过曝成一片白，人物全糊。可用的白闪强度是底图亮度定的，暗色战场能用
       0.5，这里 0.22 就到顶了。 */
    public partial class FlashLayer : Node2D {
        public override void _Draw() {
            float f = Particles.FlashAmt;
            if (f <= 0.004f) return;
            DrawRect(new Rect2(0f, 0f, K.W, K.H), MathX.C255(255f, 252f, 246f, f));
        }
    }
}
}
