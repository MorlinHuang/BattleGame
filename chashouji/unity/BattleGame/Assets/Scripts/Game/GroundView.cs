using UnityEngine;

namespace Chashouji {

/* 地面辉光：手机被拽向谁，谁脚下的地就烧起来，浓度 = 领先幅度。
   不用"线两侧分色"—— 拔河里绳结被拽过去不等于对面丢了地盘，那个画法在极端档
   会把颜色铺反。

   网页版是"矩形渐变 + 裁剪到地毯四边形"，这里换成把地毯本身切成网格、逐顶点
   算颜色：既省掉裁剪，也顺手把横向渐变和落点亮斑合成一层 —— 两层都是相加混合、
   又是同一个色相，把 alpha 相加即可。 */
public class GroundView {
    readonly Draw2D d = new Draw2D();
    readonly MeshObj obj;
    const int NX = 40, NY = 16;

    public GroundView(Transform parent, int order) {
        obj = Gfx.NewMesh("ground", parent, Gfx.NewAddMat(), order);
    }

    public void Rebuild(float bias) {
        d.Clear();
        float k = Mathf.Abs(bias);
        if (k < 0.02f) { d.Apply(obj.mesh); return; }

        var P = Director.P;
        Color col = bias > 0f ? Director.GREEN : Director.RED;
        float win = bias > 0f ? 0f : Director.W;            // 赢家所在的那一侧
        float fx = Director.FrontAt(P.rugBot - 90f) + Director.FX.jit;
        float fy = P.rugBot - 70f;

        System.Func<float, float, Color> colorAt = (x, y) => {
            float u = Mathf.Clamp01(Mathf.Abs(x - win) / Mathf.Max(1f, Director.W));
            float a = Mathf.Lerp(0.42f * k, 0.04f * k, u);
            float r = MathX.Hypot(x - fx, y - fy);
            if (r < 250f) a += (1f - r / 250f) * (0.30f + 0.28f * k);
            var c = col; c.a = a; return c;
        };

        for (int j = 0; j < NY; j++) {
            float t0 = (float)j / NY, t1 = (float)(j + 1) / NY;
            float y0 = Mathf.Lerp(P.rugTop, P.rugBot, t0), y1 = Mathf.Lerp(P.rugTop, P.rugBot, t1);
            float l0 = Mathf.Lerp(P.rugTL, P.rugBL, t0), r0 = Mathf.Lerp(P.rugTR, P.rugBR, t0);
            float l1 = Mathf.Lerp(P.rugTL, P.rugBL, t1), r1 = Mathf.Lerp(P.rugTR, P.rugBR, t1);
            for (int i = 0; i < NX; i++) {
                float s0 = (float)i / NX, s1 = (float)(i + 1) / NX;
                Vector2 a = new Vector2(Mathf.Lerp(l0, r0, s0), y0);
                Vector2 b = new Vector2(Mathf.Lerp(l0, r0, s1), y0);
                Vector2 c = new Vector2(Mathf.Lerp(l1, r1, s1), y1);
                Vector2 e = new Vector2(Mathf.Lerp(l1, r1, s0), y1);
                d.QuadPts(a, b, c, e, colorAt(a.x, a.y), colorAt(b.x, b.y), colorAt(c.x, c.y), colorAt(e.x, e.y));
            }
        }
        d.Apply(obj.mesh);
    }
}
}
