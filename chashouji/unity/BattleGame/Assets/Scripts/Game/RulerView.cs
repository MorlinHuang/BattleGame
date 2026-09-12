using UnityEngine;

namespace Chashouji {

/* 地毯前缘的固定标尺 + 跟着对抗线滑的指针 —— 刻度不动、指针动，才看得出
   "推进了多少"；刻度跟着线一起动，等于没有参照物。 */
public class RulerView {
    readonly Draw2D d = new Draw2D();
    readonly MeshObj obj;

    static Color C(float r, float g, float b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);

    public RulerView(Transform parent, int order) {
        obj = Gfx.NewMesh("ruler", parent, Gfx.NewAlphaMat(), order);
    }

    public void Rebuild() {
        var P = Director.P;
        float y = P.rugBot + 16f, L = P.rugBL + 26f, R = P.rugBR - 26f;
        d.Clear();
        d.Rect(L - 8f, y - 4f, R - L + 16f, 9f, C(10, 12, 16, .34f));
        for (int i = 0; i <= 10; i++) {
            float x = L + (R - L) * i / 10f;
            bool big = i == 5;
            float w = big ? 5f : 3f, h = big ? 16f : 10f;
            d.Rect(x - w / 2f, y - h / 2f, w, h, C(255, 255, 255, big ? .95f : .62f));
        }
        float fx = MathX.Clamp(Director.FrontAt(P.rugBot) + Director.FX.jit, L, R);
        float b = (Director.S.p - 50f) / 50f;
        Color col = Mathf.Abs(b) < 0.06f ? C(255, 255, 255, .95f)
                  : (b > 0f ? new Color(Director.GREEN.r, Director.GREEN.g, Director.GREEN.b, .95f)
                            : new Color(Director.RED.r, Director.RED.g, Director.RED.b, .95f));
        var pts = new[] {
            new Vector2(fx, y - 12f), new Vector2(fx - 11f, y - 28f), new Vector2(fx + 11f, y - 28f),
        };
        d.Tri3(pts[0], pts[1], pts[2], col);
        d.StrokeClosed(pts, 2f, C(0, 0, 0, .5f));
        d.Apply(obj.mesh);
    }
}
}
