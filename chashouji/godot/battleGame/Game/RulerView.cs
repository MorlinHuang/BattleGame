using Godot;

namespace Chashouji {

/* 地毯前缘的固定标尺 + 跟着对抗线滑的指针 —— 刻度不动、指针动，才看得出
   "推进了多少"；刻度跟着线一起动，等于没有参照物。 */
public class RulerView {
    readonly MeshLayer L;

    public RulerView(Node parent, int z) { L = Gfx.NewLayer(parent, "ruler", false, z); }

    public void Rebuild() {
        var d = L.D;
        float y = P.rug.bot + 16f, Lx = P.rug.bl + 26f, Rx = P.rug.br - 26f;
        d.Clear();
        d.Rect(Lx - 8f, y - 4f, Rx - Lx + 16f, 9f, MathX.C255(10, 12, 16, .34f));
        for (int i = 0; i <= 10; i++) {
            float x = Lx + (Rx - Lx) * i / 10f;
            bool big = i == 5;
            float w = big ? 5f : 3f, h = big ? 16f : 10f;
            d.Rect(x - w / 2f, y - h / 2f, w, h, MathX.C255(255, 255, 255, big ? .95f : .62f));
        }
        float fx = MathX.Clamp(Director.FrontAt(P.rug.bot) + FX.jit, Lx, Rx);
        float b = (S.p - 50f) / 50f;
        Color col = Mathf.Abs(b) < 0.06f ? MathX.C255(255, 255, 255, .95f)
                  : (b > 0f ? new Color(K.GREEN.R, K.GREEN.G, K.GREEN.B, .95f)
                            : new Color(K.RED.R, K.RED.G, K.RED.B, .95f));
        var pts = new[] {
            new Vector2(fx, y - 12f), new Vector2(fx - 11f, y - 28f), new Vector2(fx + 11f, y - 28f),
        };
        d.Tri3(pts[0], pts[1], pts[2], col);
        d.StrokeClosed(pts, 2f, MathX.C255(0, 0, 0, .5f));
        L.Commit();
    }
}
}
