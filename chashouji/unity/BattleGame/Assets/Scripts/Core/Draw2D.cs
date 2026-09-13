using System.Collections.Generic;
using UnityEngine;

namespace Chashouji {

/* 即时模式 2D 网格构造器：把网页版那些 ctx.fillRect / roundRect / 渐变 / 光晕
   逐个翻成带顶点色的三角形。所有坐标都在画布坐标系里给（原点左上、y 向下），
   落到 Unity 世界坐标时统一把 y 取负 —— 于是移植过来的数值不用改一个。
   渐变全部用顶点色插值，不生成贴图：画布的线性渐变本来就是线性插值，
   顶点色插值出来的结果与它逐像素相同。 */
public class Draw2D {
    readonly List<Vector3> vs = new List<Vector3>();
    readonly List<Color32> cs = new List<Color32>();
    readonly List<int> ts = new List<int>();
    M2 xf = M2.Id;
    bool xfOn = false;

    public int VertCount => vs.Count;

    public void Clear() { vs.Clear(); cs.Clear(); ts.Clear(); xf = M2.Id; xfOn = false; }

    /// 之后加进来的点都先过这个变换（手机要绕自己中心转、弹幕残影要缩一点）
    public void Push(float x, float y, float rot, float scale = 1f) {
        xf = M2.Trs(x, y, rot, scale, scale); xfOn = true;
    }
    public void Pop() { xf = M2.Id; xfOn = false; }

    int V(float x, float y, Color col) {
        if (xfOn) { var p = xf.Apply(x, y); x = p.x; y = p.y; }
        vs.Add(new Vector3(x, -y, 0f));
        cs.Add(col);
        return vs.Count - 1;
    }

    void Tri(int a, int b, int c) { ts.Add(a); ts.Add(b); ts.Add(c); }

    public void QuadPts(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
                        Color c0, Color c1, Color c2, Color c3) {
        int a = V(p0.x, p0.y, c0), b = V(p1.x, p1.y, c1);
        int c = V(p2.x, p2.y, c2), d = V(p3.x, p3.y, c3);
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
        int a = V(p0.x, p0.y, col), b = V(p1.x, p1.y, col), c = V(p2.x, p2.y, col);
        Tri(a, b, c);
    }

    /// 凸多边形，逐顶点上色（扇形三角化）
    public void Poly(IList<Vector2> pts, IList<Color> cols) {
        if (pts.Count < 3) return;
        int first = V(pts[0].x, pts[0].y, cols[0]);
        int prev = V(pts[1].x, pts[1].y, cols[1]);
        for (int i = 2; i < pts.Count; i++) {
            int cur = V(pts[i].x, pts[i].y, cols[i]);
            Tri(first, prev, cur);
            prev = cur;
        }
    }

    public void Poly(IList<Vector2> pts, Color col) {
        var cols = new Color[pts.Count];
        for (int i = 0; i < cols.Length; i++) cols[i] = col;
        Poly(pts, cols);
    }

    static readonly List<Vector2> roundBuf = new List<Vector2>();

    /// 圆角矩形轮廓点（每个角 4 段）
    public static List<Vector2> RoundRectPts(float x, float y, float w, float h, float r) {
        r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
        roundBuf.Clear();
        const int SEG = 4;
        var corners = new[] {
            new Vector3(x + w - r, y + r, -90f),   // 右上
            new Vector3(x + w - r, y + h - r, 0f), // 右下
            new Vector3(x + r, y + h - r, 90f),    // 左下
            new Vector3(x + r, y + r, 180f),       // 左上
        };
        foreach (var c in corners)
            for (int i = 0; i <= SEG; i++) {
                float a = (c.z + 90f * i / SEG) * Mathf.Deg2Rad;
                roundBuf.Add(new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r));
            }
        return roundBuf;
    }

    public void RoundRect(float x, float y, float w, float h, float r, Color col) {
        var pts = RoundRectPts(x, y, w, h, r);
        Poly(pts, col);
    }

    /// 圆角矩形 + 纵向渐变
    public void RoundRectV(float x, float y, float w, float h, float r, Color top, Color bot) {
        var pts = RoundRectPts(x, y, w, h, r);
        var cols = new Color[pts.Count];
        for (int i = 0; i < pts.Count; i++)
            cols[i] = Color.Lerp(top, bot, Mathf.Clamp01((pts[i].y - y) / Mathf.Max(1e-4f, h)));
        Poly(pts, cols);
    }

    /// 闭合折线描边
    public void StrokeClosed(IList<Vector2> pts, float width, Color col) {
        for (int i = 0; i < pts.Count; i++) Segment(pts[i], pts[(i + 1) % pts.Count], width, col);
    }

    public void Segment(Vector2 p0, Vector2 p1, float width, Color col) {
        Vector2 d = p1 - p0;
        float L = d.magnitude;
        if (L < 1e-4f) return;
        Vector2 nrm = new Vector2(-d.y, d.x) / L * (width * 0.5f);
        QuadPts(p0 - nrm, p1 - nrm, p1 + nrm, p0 + nrm, col, col, col, col);
    }

    /// 径向光晕：中心色 -> midT 处的中间色 -> 边缘透明，两圈三角带
    public void Glow(float cx, float cy, float r, Color inner, Color mid, float midT, int seg = 28) {
        Color outer = new Color(mid.r, mid.g, mid.b, 0f);
        int c0 = V(cx, cy, inner);
        var ringA = new int[seg + 1];
        var ringB = new int[seg + 1];
        for (int i = 0; i <= seg; i++) {
            float a = Mathf.PI * 2f * i / seg;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            ringA[i] = V(cx + ca * r * midT, cy + sa * r * midT, mid);
            ringB[i] = V(cx + ca * r, cy + sa * r, outer);
        }
        for (int i = 0; i < seg; i++) {
            Tri(c0, ringA[i], ringA[i + 1]);
            Tri(ringA[i], ringB[i], ringB[i + 1]);
            Tri(ringA[i], ringB[i + 1], ringA[i + 1]);
        }
    }

    /* 以给定圆心做扇形三角化。Poly 是从 pts[0] 出发扇形化的，只对凸多边形成立；
       五角星是凹的，必须从中心出发才不会画出错乱的三角形。 */
    public void PolyFan(float cx, float cy, IList<Vector2> pts, Color col) {
        if (pts.Count < 3) return;
        int c0 = V(cx, cy, col);
        int prev = V(pts[0].x, pts[0].y, col);
        for (int i = 1; i <= pts.Count; i++) {
            var q = pts[i % pts.Count];
            int cur = V(q.x, q.y, col);
            Tri(c0, prev, cur);
            prev = cur;
        }
    }

    static readonly List<Vector2> ellBuf = new List<Vector2>();

    /// 椭圆轮廓点。圆就是 rx == ry
    public static List<Vector2> EllipsePts(float cx, float cy, float rx, float ry, int seg = 18) {
        ellBuf.Clear();
        for (int i = 0; i < seg; i++) {
            float a = Mathf.PI * 2f * i / seg;
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

    /// 椭圆环（描边，不填充）。竖屏里是贴着地面看的冲击环，所以纵向压扁
    public void EllipseRing(float cx, float cy, float rx, float ry, float width, Color col, int seg = 30) {
        Vector2 prev = new Vector2(cx + rx, cy);
        for (int i = 1; i <= seg; i++) {
            float a = Mathf.PI * 2f * i / seg;
            var cur = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
            Segment(prev, cur, width, col);
            prev = cur;
        }
    }

    public void Apply(Mesh m) {
        m.Clear();
        if (vs.Count == 0) return;
        m.SetVertices(vs);
        m.SetColors(cs);
        m.SetTriangles(ts, 0, true);
    }
}
}
