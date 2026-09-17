using Godot;

namespace Chashouji {

/* 对抗线的替代画法，与网页版 ?line=2 / ?line=3 同构。
 * ============================================================================
 * 原来那根贯穿全屏的发光柱在这张底图上表现力很差，根因和白闪过曝是同一个：
 * 它走相加混合，而底图是明亮客厅 —— 浅绿墙本来就接近饱和，往上加光几乎不改变
 * 什么，只剩一团雾；暗色战场的玩法里同样的写法很炸。更要命的是它正好横在两个
 * 人中间，把抢手机的手和脸挡掉了，而那是这个玩法唯一值得看的东西。
 *
 * S.line 四档：
 *   0  全都不画。干净，但读不出战线**具体**压在哪一条竖线上
 *   1  原来那根发光柱
 *   2  地面战线 + 顶部指针
 *   3  只要顶部指针 —— 默认，也是推荐值
 * ============================================================================ */
public class FrontMarkView {
    MeshLayer ground, mark;

    public void Init(Node parent, int zGround, int zMark) {
        ground = Gfx.NewLayer(parent, "front_ground", false, zGround);
        mark   = Gfx.NewLayer(parent, "front_mark", false, zMark);
    }

    public void Rebuild(float bias, int mode) {
        ground.D.Clear(); mark.D.Clear();
        ground.Visible = mode == 2;
        mark.Visible = mode >= 2;
        if (mode == 2) BuildGround(bias);
        if (mode >= 2) BuildMark(bias);
        ground.Commit(); mark.Commit();
    }

    static Color Side(float bias, float neutral) =>
        Mathf.Abs(bias) < neutral ? Colors.White : (bias > 0f ? K.GREEN : K.RED);

    /* 地面战线：深色带 + 势力色描边，靠轮廓而不是靠发光，明亮底图上反而更显眼。
       读数一点没少 —— 横坐标仍然是 FrontAt，和手机、刻度尺、地面辉光同一个源。 */
    void BuildGround(float bias) {
        var d = ground.D;
        Color col = Side(bias, 0.06f);
        const int N = 14;
        var xs = new float[N + 1]; var ys = new float[N + 1]; var ws = new float[N + 1];
        for (int i = 0; i <= N; i++) {
            float t = (float)i / N;
            float y = P.rug.top + (P.rug.bot - P.rug.top) * t;
            ys[i] = y;
            xs[i] = Director.FrontAt(y) + FX.jit;
            ws[i] = 8f + t * 20f;        // 下宽上窄，跟着地毯的透视走
        }
        var fill = new Color(0.086f, 0.071f, 0.102f, 0.70f);
        for (int i = 0; i < N; i++)
            d.QuadPts(
                new Vector2(xs[i] - ws[i], ys[i]), new Vector2(xs[i] + ws[i], ys[i]),
                new Vector2(xs[i + 1] + ws[i + 1], ys[i + 1]), new Vector2(xs[i + 1] - ws[i + 1], ys[i + 1]),
                fill, fill, fill, fill);
        /* 双描边：外圈暗、内圈势力色。单描边在地毯的绿和地板的米色上各有一段会
           掉对比，而这条带子从地毯一直压到地板边缘，横跨两种底色。 */
        var dark = new Color(0.047f, 0.039f, 0.063f, 0.55f);
        for (int pass = 0; pass < 2; pass++) {
            float lw = pass == 0 ? 7f : 3.5f;
            Color c = pass == 0 ? dark : new Color(col.R, col.G, col.B, 0.98f);
            for (int i = 0; i < N; i++) {
                d.Segment(new Vector2(xs[i] - ws[i], ys[i]), new Vector2(xs[i + 1] - ws[i + 1], ys[i + 1]), lw, c);
                d.Segment(new Vector2(xs[i] + ws[i], ys[i]), new Vector2(xs[i + 1] + ws[i + 1], ys[i + 1]), lw, c);
            }
        }
    }

    /// 战线在画面顶端的读数：一个不会被任何东西挡住的指针
    void BuildMark(float bias) {
        var d = mark.D;
        Color col = Side(bias, 0.06f);
        float x = Director.FrontAt(K.TOP) + FX.jit;
        float y = K.TOP - 2f;

        d.Segment(new Vector2(x, y + 6f), new Vector2(x, y + 52f), 11f, new Color(0.047f, 0.055f, 0.078f, 0.6f));
        d.Segment(new Vector2(x, y + 6f), new Vector2(x, y + 52f), 6f, new Color(col.R, col.G, col.B, 0.98f));

        var tri = new[] { new Vector2(x, y + 16f), new Vector2(x - 21f, y - 16f), new Vector2(x + 21f, y - 16f) };
        d.Tri3(tri[0], tri[1], tri[2], new Color(col.R, col.G, col.B, 1f));
        d.StrokeClosed(tri, 3.5f, new Color(0.047f, 0.055f, 0.078f, 0.7f));
    }
}
}
