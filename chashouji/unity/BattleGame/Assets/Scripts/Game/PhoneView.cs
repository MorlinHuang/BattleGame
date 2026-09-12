using UnityEngine;

namespace Chashouji {

/* 手机：机身画在角色层之上 —— 手是张开的五指，怎么摆都会压掉一片屏幕，而屏幕上
   的聊天记录正是这个玩法要给人看的东西。手落在机身两侧、指尖伸进机身轮廓里被挡
   住，读起来就是"从后面攥住了它"。 */
public class PhoneView {
    readonly Draw2D d = new Draw2D();
    readonly MeshObj obj;

    static Color C(float r, float g, float b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);

    // [靠右?, 宽度比例]
    static readonly float[,] ROWS = { { 0, .62f }, { 1, .46f }, { 0, .74f }, { 1, .38f }, { 0, .54f }, { 1, .60f }, { 0, .50f } };

    public PhoneView(Transform parent, int order) {
        obj = Gfx.NewMesh("phone", parent, Gfx.NewAlphaMat(), order);
    }

    public void Rebuild() {
        var P = Director.P;
        var pp = Director.PhonePos();
        d.Clear();
        d.Push(pp.x, pp.y, Director.FX.phoneRot);

        float w = P.phoneW, h = P.phoneH, r = 9f;

        // 投影：网页版是 shadowBlur 14 / offsetY 4，这里用三层逐渐扩散的半透明圆角矩形凑
        for (int i = 3; i >= 1; i--) {
            float g = i * 4f;
            d.RoundRect(-w / 2f - g, -h / 2f - g + 4f, w + g * 2f, h + g * 2f, r + g, C(0, 0, 0, 0.10f));
        }

        d.RoundRect(-w / 2f, -h / 2f, w, h, r, C(28, 30, 36, 1f));                   // 机身 #1c1e24
        d.StrokeClosed(Draw2D.RoundRectPts(-w / 2f, -h / 2f, w, h, r), 3f, C(250, 250, 250, 1f));

        d.RoundRectV(-w / 2f + 5f, -h / 2f + 9f, w - 10f, h - 18f, 4f,               // 屏幕
                     C(150, 210, 255, .95f), C(90, 150, 230, .85f));

        int n = ROWS.GetLength(0);
        float iw = w - 20f, top = -h / 2f + 20f, lh = (h - 40f) / n;
        for (int i = 0; i < n; i++) {
            bool right = ROWS[i, 0] > 0.5f;
            float bw = iw * ROWS[i, 1];
            float bx = right ? w / 2f - 10f - bw : -w / 2f + 10f;
            d.RoundRect(bx, top + i * lh, bw, lh * .68f, 4f,
                        right ? C(120, 220, 120, .92f) : C(252, 252, 252, .92f));
        }

        d.Pop();
        d.Apply(obj.mesh);
    }
}
}
