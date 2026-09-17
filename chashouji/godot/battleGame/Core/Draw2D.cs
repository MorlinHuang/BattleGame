using System;
using System.Collections.Generic;
using Godot;

namespace Chashouji {

/* 即时模式 2D 三角形构造器：把网页版那些 ctx.fillRect / roundRect / 渐变 / 光晕
   逐个翻成带顶点色的三角形，一帧攒一批、一次交给 RenderingServer。

   所有坐标都在画布坐标系里给（原点左上、y 向下、1 单位 = 1 像素）。Godot 的
   2D 坐标系与它完全一致，所以不像 Unity 版那样还要在落笔时把 y 取负。

   渐变全部用顶点色插值，不生成贴图：画布的线性渐变本来就是线性插值，顶点色
   插值出来的结果与它逐像素相同。

   ---- 关于缓冲区为什么不用 List ----
   RenderingServer 的接口吃的是数组，List 每帧 ToArray 一次就是三次分配；粒子
   池满的时候一帧十二万个顶点，那是每秒上百 MB 的 GC 压力。这里改用按 2 倍增
   长的数组常驻，配合 CanvasItemAddTriangleArray 的 count 参数（单位是三角形
   数）只提交前 n 个 —— 数组尾部那些没被索引引用的顶点永远不会被画出来，
   所以稳态下一次分配都没有。 */
public class Draw2D {
    Vector2[] vs = new Vector2[1024];
    Color[] cs = new Color[1024];
    int[] ts = new int[3072];
    int nv, nt;                       // 已用的顶点数 / 索引数

    /* 变换栈。物品画法里到处是"在物品自己的坐标系之上再转一层" —— 花束的叶子、
       奶茶那根斜吸管、戒指盒掀开的盖子 —— 所以 Push 必须能嵌套，与外层相乘而
       不是把外层顶掉。栈深 8：实际最深是"弹幕本体 → 物品内部零件"两层。 */
    M2 xf = M2.Id;
    readonly M2[] stack = new M2[8];
    int depth = 0;

    public int VertCount => nv;

    public void Clear() { nv = 0; nt = 0; xf = M2.Id; depth = 0; }

    /// 之后加进来的点都先过这个变换（手机要绕自己中心转、弹幕残影要缩一点）
    public void Push(float x, float y, float rot, float scale = 1f) => Push(x, y, rot, scale, scale);

    public void Push(float x, float y, float rot, float sx, float sy) {
        stack[depth] = xf;
        var m = M2.Trs(x, y, rot, sx, sy);
        xf = depth > 0 ? M2.Mul(xf, m) : m;   // 先自己再外层
        depth++;
    }
    public void Pop() { if (depth > 0) xf = stack[--depth]; }

    int V(float x, float y, Color col) {
        if (depth > 0) { var p = xf.Apply(x, y); x = p.X; y = p.Y; }
        if (nv == vs.Length) {
            Array.Resize(ref vs, nv * 2);
            Array.Resize(ref cs, nv * 2);
        }
        vs[nv] = new Vector2(x, y);
        cs[nv] = col;
        return nv++;
    }

    void Tri(int a, int b, int c) {
        if (nt + 3 > ts.Length) Array.Resize(ref ts, ts.Length * 2);
        ts[nt++] = a; ts[nt++] = b; ts[nt++] = c;
    }

    public void QuadPts(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
                        Color c0, Color c1, Color c2, Color c3) {
        int a = V(p0.X, p0.Y, c0), b = V(p1.X, p1.Y, c1);
        int c = V(p2.X, p2.Y, c2), d = V(p3.X, p3.Y, c3);
        Tri(a, b, c); Tri(a, c, d);
    }

    public void Rect(float x, float y, float w, float h, Color col) =>
        QuadPts(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h),
                col, col, col, col);

    /// 横向渐变
    public void RectH(float x, float y, float w, float h, Color left, Color right) =>
        QuadPts(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h),
                left, right, right, left);

    /// 纵向渐变
    public void RectV(float x, float y, float w, float h, Color top, Color bot) =>
        QuadPts(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h),
                top, top, bot, bot);

    public void Tri3(Vector2 p0, Vector2 p1, Vector2 p2, Color col) {
        int a = V(p0.X, p0.Y, col), b = V(p1.X, p1.Y, col), c = V(p2.X, p2.Y, col);
        Tri(a, b, c);
    }

    /// 凸多边形，逐顶点上色（扇形三角化）
    public void Poly(IList<Vector2> pts, IList<Color> cols) {
        if (pts.Count < 3) return;
        int first = V(pts[0].X, pts[0].Y, cols[0]);
        int prev = V(pts[1].X, pts[1].Y, cols[1]);
        for (int i = 2; i < pts.Count; i++) {
            int cur = V(pts[i].X, pts[i].Y, cols[i]);
            Tri(first, prev, cur);
            prev = cur;
        }
    }

    static Color[] colBuf = new Color[64];

    public void Poly(IList<Vector2> pts, Color col) {
        if (colBuf.Length < pts.Count) colBuf = new Color[pts.Count];
        for (int i = 0; i < pts.Count; i++) colBuf[i] = col;
        Poly(pts, colBuf);
    }

    static readonly List<Vector2> roundBuf = new List<Vector2>();

    /// 圆角矩形轮廓点（每个角 4 段）
    public static List<Vector2> RoundRectPts(float x, float y, float w, float h, float r) {
        r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
        roundBuf.Clear();
        const int SEG = 4;
        // (圆心 x, 圆心 y, 起始角度)
        Span<(float cx, float cy, float a0)> corners = stackalloc (float, float, float)[] {
            (x + w - r, y + r,     -90f),   // 右上
            (x + w - r, y + h - r,   0f),   // 右下
            (x + r,     y + h - r,  90f),   // 左下
            (x + r,     y + r,     180f),   // 左上
        };
        foreach (var c in corners)
            for (int i = 0; i <= SEG; i++) {
                float a = Mathf.DegToRad(c.a0 + 90f * i / SEG);
                roundBuf.Add(new Vector2(c.cx + Mathf.Cos(a) * r, c.cy + Mathf.Sin(a) * r));
            }
        return roundBuf;
    }

    public void RoundRect(float x, float y, float w, float h, float r, Color col) {
        Poly(RoundRectPts(x, y, w, h, r), col);
    }

    /// 圆角矩形 + 纵向渐变
    public void RoundRectV(float x, float y, float w, float h, float r, Color top, Color bot) {
        var pts = RoundRectPts(x, y, w, h, r);
        if (colBuf.Length < pts.Count) colBuf = new Color[pts.Count];
        for (int i = 0; i < pts.Count; i++)
            colBuf[i] = top.Lerp(bot, MathX.Clamp01((pts[i].Y - y) / Mathf.Max(1e-4f, h)));
        Poly(pts, colBuf);
    }

    /// 闭合折线描边
    public void StrokeClosed(IList<Vector2> pts, float width, Color col) {
        for (int i = 0; i < pts.Count; i++) Segment(pts[i], pts[(i + 1) % pts.Count], width, col);
    }

    public void Segment(Vector2 p0, Vector2 p1, float width, Color col) {
        Vector2 d = p1 - p0;
        float L = d.Length();
        if (L < 1e-4f) return;
        Vector2 nrm = new Vector2(-d.Y, d.X) / L * (width * 0.5f);
        QuadPts(p0 - nrm, p1 - nrm, p1 + nrm, p0 + nrm, col, col, col, col);
    }

    /// 径向光晕：中心色 -> midT 处的中间色 -> 边缘透明，两圈三角带
    public void Glow(float cx, float cy, float r, Color inner, Color mid, float midT, int seg = 28) {
        Color outer = new Color(mid.R, mid.G, mid.B, 0f);
        int c0 = V(cx, cy, inner);
        int prevA = -1, prevB = -1, firstA = -1, firstB = -1;
        for (int i = 0; i <= seg; i++) {
            float a = Mathf.Pi * 2f * i / seg;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            int ia = V(cx + ca * r * midT, cy + sa * r * midT, mid);
            int ib = V(cx + ca * r, cy + sa * r, outer);
            if (i == 0) { firstA = ia; firstB = ib; }
            else {
                Tri(c0, prevA, ia);
                Tri(prevA, prevB, ib);
                Tri(prevA, ib, ia);
            }
            prevA = ia; prevB = ib;
        }
        _ = firstA; _ = firstB;
    }

    /* 以给定圆心做扇形三角化。Poly 是从 pts[0] 出发扇形化的，只对凸多边形成立；
       五角星和爱心是凹的，必须从中心出发才不会画出错乱的三角形。 */
    public void PolyFan(float cx, float cy, IList<Vector2> pts, Color col) {
        if (pts.Count < 3) return;
        int c0 = V(cx, cy, col);
        int prev = V(pts[0].X, pts[0].Y, col);
        for (int i = 1; i <= pts.Count; i++) {
            var q = pts[i % pts.Count];
            int cur = V(q.X, q.Y, col);
            Tri(c0, prev, cur);
            prev = cur;
        }
    }

    static readonly List<Vector2> ellBuf = new List<Vector2>();

    /// 椭圆轮廓点。圆就是 rx == ry
    public static List<Vector2> EllipsePts(float cx, float cy, float rx, float ry, int seg = 18) {
        ellBuf.Clear();
        for (int i = 0; i < seg; i++) {
            float a = Mathf.Pi * 2f * i / seg;
            ellBuf.Add(new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry));
        }
        return ellBuf;
    }

    /// 实心椭圆（含圆）
    public void Ellipse(float cx, float cy, float rx, float ry, Color col, int seg = 18) {
        PolyFan(cx, cy, EllipsePts(cx, cy, rx, ry, seg), col);
    }

    static readonly List<Vector2> starBuf = new List<Vector2>();

    /// 五角星轮廓点。内凹到 0.42 —— 再瘦读成海星，再胖读成花
    public static List<Vector2> StarPts(float cx, float cy, float r, float rot) {
        starBuf.Clear();
        for (int i = 0; i < 10; i++) {
            float a = rot - 1.5708f + i * 0.6283f;
            float rr = (i % 2) != 0 ? r * 0.42f : r;
            starBuf.Add(new Vector2(cx + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr));
        }
        return starBuf;
    }

    static readonly List<Vector2> heartBuf = new List<Vector2>();

    /* 爱心轮廓点 —— 网页版 fx.js 里 heartPath 那两段三次贝塞尔，逐控制点采样成
       折线。反正最后都要三角化，采 24 段在观众端那个尺寸上已经看不出是折线。

       ⚠ 别换成心形参数方程（x=16sin³t 那个）。两者形状差得比看上去多：参数方程
       的最宽点就是 r，而这两段贝塞尔的最宽点只有 0.78r、却高 1.67r —— 同一个 r
       换过去，爱心宽 28%、扁 20%。档 4 一次炸出三十颗，那个差别的直接后果是
       两张脸被糊死，而脸是这个玩法仅有的两个可读信息之一。

       顶点在 -r*1.02（控制点）而不是 -r：爱心的视觉重心明显偏下，按外接盒居中
       的话一堆爱心飘起来会整体显得往上飘了半个身位。 */
    public static List<Vector2> HeartPts(float cx, float cy, float r, int seg = 24) {
        heartBuf.Clear();
        int n = Mathf.Max(3, seg / 2);
        Cubic(cx, cy, r, 0f, 0.92f, -1.08f, 0.10f, -0.62f, -1.02f, 0f, -0.34f, n);
        Cubic(cx, cy, r, 0f, -0.34f, 0.62f, -1.02f, 1.08f, 0.10f, 0f, 0.92f, n);
        return heartBuf;
    }

    /// 三次贝塞尔采样，控制点按 r 归一化给。含起点不含终点，两段接得上
    static void Cubic(float cx, float cy, float r,
                      float x0, float y0, float x1, float y1,
                      float x2, float y2, float x3, float y3, int n) {
        for (int i = 0; i < n; i++) {
            float t = (float)i / n, u = 1f - t;
            float a = u * u * u, b = 3f * u * u * t, c = 3f * u * t * t, d = t * t * t;
            heartBuf.Add(new Vector2(cx + (a * x0 + b * x1 + c * x2 + d * x3) * r,
                                     cy + (a * y0 + b * y1 + c * y2 + d * y3) * r));
        }
    }

    /// 椭圆环（描边，不填充）。竖屏里是贴着地面看的冲击环，所以纵向压扁
    public void EllipseRing(float cx, float cy, float rx, float ry, float width, Color col, int seg = 30) {
        Vector2 prev = new Vector2(cx + rx, cy);
        for (int i = 1; i <= seg; i++) {
            float a = Mathf.Pi * 2f * i / seg;
            var cur = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
            Segment(prev, cur, width, col);
            prev = cur;
        }
    }

    readonly float[] rt = new float[4];
    readonly Color[] rc = new Color[4];
    static int[] ringA = new int[32], ringB = new int[32];

    /* 四档径向渐变，用同心环带做出来。中心那一档的 t 恒为 0。
       网页版所有 createRadialGradient（光斑、绒絮、弹道色晕）在这里全走它。
       那边必须先烘成离屏贴图，是因为 canvas 每帧重算渐变太贵、又没有 modulate
       可以一张贴图配多种颜色；Godot 这边顶点色本来就是逐像素线性插值，结果与
       渐变逐像素相同，而且所有粒子进同一批三角形、一次提交，比贴图还省。

       只有三档的渐变（绒絮）照样用它 —— 在中间补一个线性插值算得出的档，
       形状一模一样，省掉一个只差一档的重载。 */
    public void Radial(float cx, float cy, float rx, float ry,
                       Color c0, float t1, Color c1, float t2, Color c2, float t3, Color c3,
                       int seg = 20) {
        rt[0] = 0f; rc[0] = c0;
        rt[1] = t1; rc[1] = c1;
        rt[2] = t2; rc[2] = c2;
        rt[3] = t3; rc[3] = c3;
        if (ringA.Length < seg + 1) { ringA = new int[seg + 1]; ringB = new int[seg + 1]; }
        var prev = ringA; var cur = ringB;
        int center = V(cx, cy, rc[0]);
        for (int i = 0; i <= seg; i++) prev[i] = center;
        for (int k = 1; k < 4; k++) {
            float t = rt[k];
            for (int i = 0; i <= seg; i++) {
                float a = Mathf.Pi * 2f * i / seg;
                cur[i] = V(cx + Mathf.Cos(a) * rx * t, cy + Mathf.Sin(a) * ry * t, rc[k]);
            }
            for (int i = 0; i < seg; i++) {
                // 第一圈的"上一圈"整圈都是中心点，退化成扇形，别画零面积的三角形
                if (k == 1) Tri(prev[i], cur[i], cur[i + 1]);
                else { Tri(prev[i], prev[i + 1], cur[i + 1]); Tri(prev[i], cur[i + 1], cur[i]); }
            }
            (prev, cur) = (cur, prev);
        }
    }

    /* 把攒好的三角形提交给一个 canvas item。必须在 _Draw() 里调。
       count 的单位是三角形数，只提交前 nt/3 个 —— 数组尾部多出来的容量不会被
       任何索引引用，画不出来。 */
    public void Flush(Rid ci) {
        if (nt == 0) return;
        RenderingServer.CanvasItemAddTriangleArray(ci, ts, vs, cs, null, null, null, default, nt / 3);
    }
}
}
