using System.Collections.Generic;
using UnityEngine;
using FT = Chashouji.FxTuning;

namespace Chashouji {

/* AmmoView.cs —— 礼物弹幕：飞过去，撞到对抗线才算数。与 web/ammo.js 同构。
 * ============================================================================
 * 分两层，跟 ParticleFx 的"形态 vs 题材"是同一个分法：
 *   样式（Volley / Single / Heavy）—— 三种，管发射节奏与体量
 *   物品（发卡 / 抱枕 / 手柄 …）—— 可以无限加，只是皮
 * 观众不需要认出飞过来的是什么，光看节奏就知道这一发有多重。
 *
 * 想改什么：
 *   飞多快、转多快、速度差多大、预警多久、拖尾多长 → FxTuning.cs 的 Ammo 区
 *   加一件新礼物                                   → FxTuning.cs 的 GIFTS 表
 *   物品长什么样                                   → 本文件最下面的 ITEM 区
 *   命中炸出什么                                   → Recipes.cs
 *
 * 命中判定用的是 FrontAt(y) —— 对抗线在弹幕**自己那个高度**上的真实横坐标，
 * 不是中点。所以不同高度飞来的弹幕会在不同的行注入冲量，那条线才活得起来。
 *
 * 物品是代码画的：几何剪影 + 粗描边 + 高对比色。飞行物在画面上只有 60~160px
 * 而且高速移动，这个精度足够验证节奏。之后要换成美术贴图的话，把 ITEM 区那
 * 几个方法换成画一个带贴图的四边形即可，飞行逻辑一行都不用动。
 * ============================================================================ */
public class AmmoView {

    class Shot {
        public FT.Gift g;
        public int from;
        public float x, y, vx, ax, rot, vrot;
        /* 轨迹环形缓冲：拖尾画的是这个东西**真正走过**的地方。写死在本体后方
           的几条速度线跟实际路径无关，飞得快飞得慢看上去一个样；记下真实轨迹
           之后，拖尾长度自己就跟速度挂上钩了。 */
        public readonly float[] hx = new float[FT.TrailFrames];
        public readonly float[] hr = new float[FT.TrailFrames];
        public int hi;
    }

    struct Pending { public float t; public FT.Gift g; public float y; }
    struct Warn { public float t, max, y; public int from; }

    static readonly List<Shot> act = new List<Shot>(FT.AmmoMax);
    static readonly Stack<Shot> pool = new Stack<Shot>(FT.AmmoMax);
    static readonly List<Pending> queue = new List<Pending>();
    static readonly List<Warn> warns = new List<Warn>();

    readonly Draw2D d = new Draw2D();
    MeshObj obj;

    public static int Count => act.Count + queue.Count;

    public void Init(Transform parent, int order) {
        obj = Gfx.NewMesh("ammo", parent, Gfx.NewAlphaMat(), order);
        act.Clear(); pool.Clear(); queue.Clear(); warns.Clear();
    }

    // ---------------------------------------------------------------- 发射

    /// fixedY < 0 表示按 FxTuning 的范围随机取高度
    public static void Launch(FT.Gift g, float fixedY = -1f) {
        float y0 = fixedY >= 0f ? fixedY
                 : FT.AmmoYMin + Random.value * (FT.AmmoYMax - FT.AmmoYMin);

        if (g.style == FT.Style.Volley) {
            // 连珠：排成一串。每一颗单独判定、单独触发一次小命中，读起来是
            // "哒哒哒"一串轻击而不是一下
            int n = g.n > 0 ? g.n : 8;
            for (int i = 0; i < n; i++)
                queue.Add(new Pending {
                    t = i * FT.VolleyGap, g = g,
                    y = ClampY(y0 + (Random.value - 0.5f) * FT.VolleySpreadY) });

        } else if (g.style == FT.Style.Heavy) {
            /* 重投先预警再发射。大礼物的价值一半在"全场都看见有人刷了大的"——
               冲进来之前得先让观众知道它要来，否则一个大件突然出现在画面里，
               观众只来得及看到它已经砸上了。 */
            warns.Add(new Warn { from = g.from, y = y0, t = FT.WarnTime, max = FT.WarnTime });
            queue.Add(new Pending { t = FT.WarnTime, g = g, y = y0 });

        } else {
            queue.Add(new Pending { t = 0f, g = g, y = y0 });
        }
    }

    static float ClampY(float y) => MathX.Clamp(y, FT.AmmoYClampMin, FT.AmmoYClampMax);

    static void Fire(Pending q) {
        if (act.Count >= FT.AmmoMax) return;
        var g = q.g;
        var p = pool.Count > 0 ? pool.Pop() : new Shot();
        p.g = g; p.from = g.from; p.y = q.y;
        p.x = g.from > 0 ? -g.r - 40f : Director.W + g.r + 40f;
        p.vx = g.from * FT.Speed(g.style) * (1f + (Random.value - 0.5f) * 2f * FT.Jitter(g.style));
        p.ax = FT.Acc(g.style);
        p.rot = Random.value * 6.283f;
        // 转速跟着体量走，小东西翻得快。方向也随机，一批里有顺时针有逆时针
        p.vrot = (Random.value - 0.5f) * FT.Spin(g.style);
        for (int i = 0; i < FT.TrailFrames; i++) { p.hx[i] = p.x; p.hr[i] = p.rot; }
        p.hi = 0;
        act.Add(p);
    }

    // ---------------------------------------------------------------- 推进

    /* dt 走的是顿帧之后的逻辑时间，跟角色和对抗线一起冻。这和粒子走真实时间
       并不矛盾：爆炸是"刚刚这一下"的可视化，冻住它就迟到了；而正在飞的弹幕是
       **下一下**的前奏，顿帧的意思就是全世界停下来看这一击。 */
    public static void Step(float dt) {
        for (int i = queue.Count - 1; i >= 0; i--) {
            var q = queue[i];
            q.t -= dt;
            if (q.t <= 0f) { queue.RemoveAt(i); Fire(q); }
            else queue[i] = q;
        }
        for (int i = warns.Count - 1; i >= 0; i--) {
            var w = warns[i];
            w.t -= dt;
            if (w.t <= 0f) warns.RemoveAt(i);
            else warns[i] = w;
        }
        for (int i = act.Count - 1; i >= 0; i--) {
            var p = act[i];
            if (p.ax != 0f) p.vx += p.from * p.ax * dt;
            p.x += p.vx * dt;
            p.rot += p.vrot * dt;
            /* 重投在途中让画面一直轻轻发抖。每帧加的这一点点会被震动衰减吃掉，
               稳态就在 1px 上下 —— 不是震动，是压迫感。 */
            if (p.g.style == FT.Style.Heavy) ParticleFx.AddShake(FT.HeavyFlyShake);

            p.hi = (p.hi + 1) % FT.TrailFrames;
            p.hx[p.hi] = p.x; p.hr[p.hi] = p.rot;

            float fx = Director.FrontAt(p.y);
            bool hit = p.from > 0 ? p.x >= fx : p.x <= fx;
            if (hit) {
                var g = p.g; float y = p.y;
                act.RemoveAt(i); pool.Push(p);
                Director.OnAmmoHit(g, y);
            } else if (p.x < -400f || p.x > Director.W + 400f) {
                // 对抗线被推到极端位置时弹幕可能追不上，别让它永远飞下去
                act.RemoveAt(i); pool.Push(p);
            }
        }
    }

    public static void ClearAll() {
        for (int i = act.Count - 1; i >= 0; i--) pool.Push(act[i]);
        act.Clear(); queue.Clear(); warns.Clear();
    }

    // ---------------------------------------------------------------- 绘制

    public void Rebuild() {
        d.Clear();

        // 预警箭头：贴着发射方的屏幕边缘，闪两下
        for (int i = 0; i < warns.Count; i++) {
            var w = warns[i];
            float k = w.t / w.max;
            float blink = 0.5f + Mathf.Abs(Mathf.Sin(k * 18f)) * 0.5f;
            float x = w.from > 0 ? 58f : Director.W - 58f;
            float sc = 1.1f + (1f - k) * 0.7f;     // 越接近发射越大，读成"正在逼近"
            Color col = w.from > 0 ? Director.GREEN : Director.RED;
            // 三重箭头，越靠后越淡：一个静止的三角读不出方向，一串才读得出"来了"
            for (int j = 0; j < 3; j++) {
                float a = blink * (1f - j * 0.3f);
                float ox = -j * 26f * sc * w.from;
                var p0 = new Vector2(x + ox + 26f * sc * w.from, w.y);
                var p1 = new Vector2(x + ox - 12f * sc * w.from, w.y - 25f * sc);
                var p2 = new Vector2(x + ox - 12f * sc * w.from, w.y + 25f * sc);
                d.Tri3(p0, p1, p2, new Color(col.r, col.g, col.b, a));
                var tri = new[] { p0, p1, p2 };
                d.StrokeClosed(tri, 4.5f, new Color(0.08f, 0.06f, 0.09f, 0.8f * a));
            }
        }

        for (int i = 0; i < act.Count; i++) {
            var p = act[i];
            float r = p.g.r;
            float tail = p.hx[(p.hi + FT.TrailFrames - FT.TrailBandBack + 1) % FT.TrailFrames];

            /* 拖尾带：从轨迹最老的那一点收拢到本体。用物品自己主色的暗调，不是
               统一的黑 —— 统一深色拖在浅粉抱枕后面读起来像一团影子或者污渍，
               那是"另一个东西"，而拖尾应该是它自己甩出来的。
               它的长度不是写死的，是这一发实际飞过的距离，所以速度抖动一上来
               就看得出谁快谁慢。 */
            Color tc = TailColor(p.g.item);
            tc.a = FT.TrailBandAlpha;
            d.QuadPts(new Vector2(tail, p.y - r * 0.06f), new Vector2(p.x, p.y - r * 0.5f),
                      new Vector2(p.x, p.y + r * 0.5f), new Vector2(tail, p.y + r * 0.06f),
                      tc, tc, tc, tc);

            /* 残影：在它前几帧待过的地方，把同一个东西再画一遍，越 old 越淡越小。
               带描边一起画 —— 这是赛璐璐里的速度残影，不是发光拖影，少了那圈线
               就糊成一片。
               取连续帧而不是隔帧：隔帧的话残影之间的空隙比物体本身还宽，读出来
               是"一串独立的小东西"而不是"一个东西拖出来的影"。长度交给上面那条
               拖尾带去表达，残影只负责把中间填实。 */
            for (int k = FT.GhostCount; k >= 1; k--) {
                int idx = (p.hi + FT.TrailFrames - k) % FT.TrailFrames;
                float a = FT.GhostAlphaBase - k * FT.GhostAlphaStep;
                if (a <= 0.01f) continue;
                d.Push(p.hx[idx], p.y, p.hr[idx], 1f - k * FT.GhostShrink);
                DrawItem(d, p.g.item, r, a);
                d.Pop();
            }

            d.Push(p.x, p.y, p.rot);
            DrawItem(d, p.g.item, r, 1f);
            d.Pop();
        }

        d.Apply(obj.mesh);
    }

    // ================================ ITEM 区 ================================
    /* 物品画法。都在原点画，调用方已经 Push 了位置、旋转和缩放，r 是半宽，
       a 是整体不透明度（残影会传小于 1 的值）。
       每一件都是"填充 + 深色粗描边"：底图是明亮客厅，实体靠轮廓而不是靠亮度
       才看得见 —— 跟角色睡衣有线稿是同一个道理。
       要换成美术贴图的话，把这几个方法整个换掉就行，飞行逻辑不依赖它们。 */

    static Color C(int r, int g, int b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);
    static Color Ink(float a) => C(52, 40, 36, a);

    /// 拖尾带的颜色：每件物品主色的暗调
    static Color TailColor(FT.Item it) {
        switch (it) {
            case FT.Item.Hairpin: return C(176, 64, 112, 1f);
            case FT.Item.Seed:    return C(58, 42, 26, 1f);
            case FT.Item.Pillow:  return C(196, 116, 150, 1f);
            case FT.Item.Gamepad: return C(38, 46, 58, 1f);
            case FT.Item.Quilt:   return C(198, 112, 148, 1f);
            default:              return C(126, 82, 48, 1f);
        }
    }

    static void DrawItem(Draw2D d, FT.Item it, float r, float a) {
        switch (it) {
            case FT.Item.Hairpin: Hairpin(d, r, a); break;
            case FT.Item.Seed:    Seed(d, r, a);    break;
            case FT.Item.Pillow:  Pillow(d, r, a);  break;
            case FT.Item.Gamepad: Gamepad(d, r, a); break;
            case FT.Item.Quilt:   Quilt(d, r, a);   break;
            default:              Box(d, r, a);     break;
        }
    }

    // 发卡：一根粉色小棒加一颗珠子，连珠用
    static void Hairpin(Draw2D d, float r, float a) {
        var pts = Draw2D.RoundRectPts(-r, -r * 0.3f, r * 2f, r * 0.6f, r * 0.3f);
        d.Poly(pts, C(255, 143, 184, a));
        d.StrokeClosed(pts, r * 0.26f, Ink(a));
        var c = Draw2D.EllipsePts(-r * 0.62f, 0f, r * 0.46f, r * 0.46f, 14);
        d.PolyFan(-r * 0.62f, 0f, c, C(255, 217, 232, a));
        d.StrokeClosed(c, r * 0.22f, Ink(a));
    }

    // 瓜子壳：一颗水滴，深棕。男方的连珠，嗑瓜子看戏顺手就弹过去了
    static readonly List<Vector2> seedBuf = new List<Vector2>();
    static void Seed(Draw2D d, float r, float a) {
        seedBuf.Clear();
        const int SEG = 9;
        // 上下两段二次贝塞尔拼成水滴：(r,0) -> (0,±0.66r) -> (-r,0)
        for (int half = 0; half < 2; half++) {
            float sgn = half == 0 ? 1f : -1f;
            for (int i = 0; i < SEG; i++) {
                float t = (float)i / SEG;
                float u = 1f - t;
                float x0 = half == 0 ? r : -r, x2 = half == 0 ? -r : r;
                float px = u * u * x0 + 2f * u * t * 0f + t * t * x2;
                float py = u * u * 0f + 2f * u * t * (sgn * r * 0.66f) + t * t * 0f;
                seedBuf.Add(new Vector2(px, py));
            }
        }
        d.PolyFan(0f, 0f, seedBuf, C(107, 81, 54, a));
        d.StrokeClosed(seedBuf, r * 0.24f, Ink(a));
        d.Segment(new Vector2(r * 0.5f, 0f), new Vector2(-r * 0.62f, 0f), r * 0.16f, C(255, 240, 220, 0.5f * a));
    }

    // 抱枕：圆角方 + 缝线 + 两点兔耳暗示，跟沙发上那只兔抱枕呼应
    static void Pillow(Draw2D d, float r, float a) {
        var pts = Draw2D.RoundRectPts(-r, -r * 0.84f, r * 2f, r * 1.68f, r * 0.4f);
        d.Poly(pts, C(253, 242, 246, a));
        d.StrokeClosed(pts, r * 0.12f, Ink(a));
        var inner = Draw2D.RoundRectPts(-r * 0.78f, -r * 0.64f, r * 1.56f, r * 1.28f, r * 0.3f);
        d.StrokeClosed(inner, r * 0.055f, C(255, 160, 195, 0.85f * a));
        for (int s = -1; s <= 1; s += 2)
            d.Ellipse(s * r * 0.26f, -r * 0.16f, r * 0.1f, r * 0.26f, C(255, 179, 205, a), 12);
    }

    // 游戏手柄：深色机身 + 一个摇杆一个按键，小但好认
    static void Gamepad(Draw2D d, float r, float a) {
        var pts = Draw2D.RoundRectPts(-r, -r * 0.48f, r * 2f, r * 0.96f, r * 0.44f);
        d.Poly(pts, C(61, 68, 80, a));
        d.StrokeClosed(pts, r * 0.12f, Ink(a));
        var st = Draw2D.EllipsePts(-r * 0.46f, 0f, r * 0.22f, r * 0.22f, 12);
        d.PolyFan(-r * 0.46f, 0f, st, C(143, 227, 255, a));
        d.StrokeClosed(st, r * 0.08f, Ink(a));
        var bt = Draw2D.EllipsePts(r * 0.44f, -r * 0.08f, r * 0.16f, r * 0.16f, 12);
        d.PolyFan(r * 0.44f, -r * 0.08f, bt, C(255, 122, 122, a));
        d.StrokeClosed(bt, r * 0.07f, Ink(a));
    }

    // 整条被子：最大的一件，格纹让它在翻滚时读得出体积
    static void Quilt(Draw2D d, float r, float a) {
        var pts = Draw2D.RoundRectPts(-r, -r * 0.7f, r * 2f, r * 1.4f, r * 0.18f);
        d.Poly(pts, C(255, 230, 238, a));
        d.StrokeClosed(pts, r * 0.085f, Ink(a));
        Color grid = C(255, 146, 183, 0.8f * a);
        float lw = r * 0.05f;
        foreach (float t in new[] { -0.34f, 0.34f })
            d.Segment(new Vector2(t * r * 2f, -r * 0.7f), new Vector2(t * r * 2f, r * 0.7f), lw, grid);
        foreach (float t in new[] { -0.3f, 0.3f })
            d.Segment(new Vector2(-r, t * r * 1.4f), new Vector2(r, t * r * 1.4f), lw, grid);
        // 翻出来的一角，不然大色块读成一块板子
        var corner = new[] {
            new Vector2(r, -r * 0.7f), new Vector2(r * 0.5f, -r * 0.7f), new Vector2(r, -r * 0.2f) };
        d.Tri3(corner[0], corner[1], corner[2], C(255, 248, 251, a));
        d.StrokeClosed(corner, r * 0.07f, Ink(a));
    }

    // 外卖箱：牛皮纸色 + 胶带十字，一眼是个箱子
    static void Box(Draw2D d, float r, float a) {
        var pts = Draw2D.RoundRectPts(-r, -r * 0.72f, r * 2f, r * 1.44f, r * 0.1f);
        d.Poly(pts, C(207, 151, 96, a));
        d.StrokeClosed(pts, r * 0.085f, Ink(a));
        var lid = Draw2D.RoundRectPts(-r, -r * 0.72f, r * 2f, r * 0.4f, r * 0.1f);
        d.Poly(lid, C(184, 127, 75, a));
        d.StrokeClosed(lid, r * 0.07f, Ink(a));
        Color tape = C(246, 238, 222, 0.9f * a);
        d.Segment(new Vector2(0f, -r * 0.72f), new Vector2(0f, r * 0.72f), r * 0.13f, tape);
        d.Segment(new Vector2(-r, r * 0.1f), new Vector2(r, r * 0.1f), r * 0.13f, tape);
    }
}
}
