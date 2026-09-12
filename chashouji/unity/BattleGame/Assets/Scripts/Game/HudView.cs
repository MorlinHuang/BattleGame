using UnityEngine;

namespace Chashouji {

/* 顶部两条血条 + 阵营名 + 百分比。数值只有 S.p 一个来源，HUD 与画面不可能对不上。 */
public class HudView {
    readonly Draw2D d = new Draw2D();
    readonly MeshObj obj;
    readonly Label pctL, pctR, nameL, nameR;

    static Color C(float r, float g, float b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);

    public HudView(Transform parent, int order) {
        obj = Gfx.NewMesh("hud", parent, Gfx.NewAlphaMat(), order);
        var f23 = Label.LoadCJKFont(46);
        var f26 = Label.LoadCJKFont(52);
        var white = Color.white; var stroke = C(0, 0, 0, .75f);
        pctL  = new Label(parent, f23, 23f, TextAnchor.MiddleLeft,  order + 5, white, stroke, 2f);
        pctR  = new Label(parent, f23, 23f, TextAnchor.MiddleRight, order + 5, white, stroke, 2f);
        nameL = new Label(parent, f26, 26f, TextAnchor.MiddleLeft,  order + 5, white, stroke, 2.5f);
        nameR = new Label(parent, f26, 26f, TextAnchor.MiddleRight, order + 5, white, stroke, 2.5f);
        pctL.SetPos(100f, 90f); pctR.SetPos(860f, 90f);
        nameL.SetPos(92f, 42f); nameR.SetPos(868f, 42f);
        nameL.SetText("查岗党"); nameR.SetText("灭迹党");
    }

    struct Bar { public float x, w; public Color c; public float v; public int dir; }

    public void Rebuild(float p) {
        var bars = new[] {
            new Bar { x = 92f,  w = 276f, c = Director.GREEN, v = p,          dir = 1 },
            new Bar { x = 572f, w = 296f, c = Director.RED,   v = 100f - p,   dir = -1 },
        };
        d.Clear();
        foreach (var b in bars) {
            d.Rect(b.x, 74f, b.w, 31f, C(8, 10, 13, .92f));
            float fw = b.w * b.v / 100f;
            float gx = b.dir > 0 ? b.x : b.x + b.w - fw;
            var top = b.c;
            var bot = new Color(Mathf.Floor(b.c.r * 255f * .62f) / 255f,
                                Mathf.Floor(b.c.g * 255f * .62f) / 255f,
                                Mathf.Floor(b.c.b * 255f * .62f) / 255f, 1f);
            d.RectV(gx, 74f, fw, 31f, top, bot);
            d.Rect(gx, 74f, fw, 9f, C(255, 255, 255, .30f));
        }
        d.Apply(obj.mesh);
        pctL.SetText(Mathf.Round(p).ToString("0") + "%");
        pctR.SetText(Mathf.Round(100f - p).ToString("0") + "%");
    }
}
}
