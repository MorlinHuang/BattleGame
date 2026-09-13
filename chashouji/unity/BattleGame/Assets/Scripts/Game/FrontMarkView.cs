using UnityEngine;

namespace Chashouji {

/* FrontMarkView.cs —— 对抗线的替代画法，与网页版 ?line=2 / ?line=3 同构。
 * ============================================================================
 * 原来那根贯穿全屏的发光柱（LineView，Director.LineMode = 1）在这张底图上表现
 * 力很差，根因和白闪过曝是同一个：它走相加混合，而底图是明亮客厅 —— 浅绿墙
 * 本来就接近饱和，往上加光几乎不改变什么，只剩一团雾；暗色战场的玩法里同样的
 * 写法很炸。更要命的是它正好横在两个人中间，把抢手机的手和脸挡掉了，而那是这
 * 个玩法唯一值得看的东西。
 *
 * Director.LineMode 四档：
 *   0  全都不画。干净，但读不出战线**具体**压在哪一条竖线上
 *   1  原来那根发光柱（LineView）
 *   2  地面战线 + 顶部指针
 *   3  只要顶部指针 —— 默认，也是推荐值
 *
 * 地面战线横在两个人的腿中间，激烈的时候被挡掉大半，剩下的半截读起来像一根
 * 立在地上的杆子；顶部指针在画面最上方，既不挡人也永远看得见，而且补上了
 * "战线此刻在哪"这个读数 —— 手机位移就是这个玩法的进度条。
 * ============================================================================ */
public class FrontMarkView {
    readonly Draw2D dGround = new Draw2D();
    readonly Draw2D dMark = new Draw2D();
    MeshObj groundObj, markObj;

    public void Init(Transform parent, int orderGround, int orderMark) {
        groundObj = Gfx.NewMesh("front_ground", parent, Gfx.NewAlphaMat(), orderGround);
        markObj = Gfx.NewMesh("front_mark", parent, Gfx.NewAlphaMat(), orderMark);
    }

    public void Rebuild(float bias, int mode) {
        dGround.Clear(); dMark.Clear();
        groundObj.Visible = mode == 2;
        markObj.Visible = mode >= 2;
        if (mode == 2) BuildGround(bias);
        if (mode >= 2) BuildMark(bias);
        dGround.Apply(groundObj.mesh);
        dMark.Apply(markObj.mesh);
    }

    static Color Side(float bias, float neutral) =>
        Mathf.Abs(bias) < neutral ? Color.white : (bias > 0f ? Director.GREEN : Director.RED);

    /* 地面战线：深色带 + 势力色描边，靠轮廓而不是靠发光，明亮底图上反而更显眼。
       读数一点没少 —— 横坐标仍然是 FrontAt，和手机、刻度尺、地面辉光同一个源。 */
    void BuildGround(float bias) {
        var P = Director.P;
        Color col = Side(bias, 0.06f);
        const int N = 14;
        var xs = new float[N + 1];
        var ys = new float[N + 1];
        var ws = new float[N + 1];
        for (int i = 0; i <= N; i++) {
            float t = (float)i / N;
            float y = P.rugTop + (P.rugBot - P.rugTop) * t;
            ys[i] = y;
            xs[i] = Director.FrontAt(y) + Director.FX.jit;
            ws[i] = 8f + t * 20f;        // 下宽上窄，跟着地毯的透视走
        }
        var fill = new Color(0.086f, 0.071f, 0.102f, 0.70f);
        for (int i = 0; i < N; i++)
            dGround.QuadPts(
                new Vector2(xs[i] - ws[i], ys[i]), new Vector2(xs[i] + ws[i], ys[i]),
                new Vector2(xs[i + 1] + ws[i + 1], ys[i + 1]), new Vector2(xs[i + 1] - ws[i + 1], ys[i + 1]),
                fill, fill, fill, fill);
        /* 双描边：外圈暗、内圈势力色。单描边在地毯的绿和地板的米色上各有一段会
           掉对比，而这条带子从地毯一直压到地板边缘，横跨两种底色。 */
        var dark = new Color(0.047f, 0.039f, 0.063f, 0.55f);
        for (int pass = 0; pass < 2; pass++) {
            float lw = pass == 0 ? 7f : 3.5f;
            Color c = pass == 0 ? dark : new Color(col.r, col.g, col.b, 0.98f);
            for (int i = 0; i < N; i++) {
                dGround.Segment(new Vector2(xs[i] - ws[i], ys[i]), new Vector2(xs[i + 1] - ws[i + 1], ys[i + 1]), lw, c);
                dGround.Segment(new Vector2(xs[i] + ws[i], ys[i]), new Vector2(xs[i + 1] + ws[i + 1], ys[i + 1]), lw, c);
            }
        }
    }

    /// 战线在画面顶端的读数：一个不会被任何东西挡住的指针
    void BuildMark(float bias) {
        Color col = Side(bias, 0.06f);
        float x = Director.FrontAt(Director.TOP) + Director.FX.jit;
        float y = Director.TOP - 2f;

        dMark.Segment(new Vector2(x, y + 6f), new Vector2(x, y + 52f), 11f,
                      new Color(0.047f, 0.055f, 0.078f, 0.6f));
        dMark.Segment(new Vector2(x, y + 6f), new Vector2(x, y + 52f), 6f,
                      new Color(col.r, col.g, col.b, 0.98f));

        var tri = new[] {
            new Vector2(x, y + 16f), new Vector2(x - 21f, y - 16f), new Vector2(x + 21f, y - 16f) };
        dMark.Tri3(tri[0], tri[1], tri[2], new Color(col.r, col.g, col.b, 1f));
        dMark.StrokeClosed(tri, 3.5f, new Color(0.047f, 0.055f, 0.078f, 0.7f));
    }
}
}
