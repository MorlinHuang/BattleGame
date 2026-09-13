using UnityEngine;

namespace Chashouji {

/* 对抗线：三条带 + 底部落点光晕，全部相加混合（网页版的 globalCompositeOperation
   = 'lighter'）。横向渐变不用贴图，直接按停靠点切成几段用顶点色插值 —— 画布的
   线性渐变本来就是线性插值。 */
public class LineView {
    readonly Draw2D d = new Draw2D();
    readonly MeshObj obj;
    /* 整体强度。这条线要建两遍：一遍排在角色之下当背景光柱，一遍以更低的强度
       排在角色之上 —— 两个人正好在中间抢东西，只画在下面的话对抗线全程被两具
       身体挡死，而它是这个玩法唯一的战况读数。 */
    readonly float k;

    struct Stop { public float t; public Color c; public Stop(float t, Color c) { this.t = t; this.c = c; } }

    static Color C(float r, float g, float b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);

    static readonly Stop[] BAND = {
        new Stop(0f,   C(255, 255, 255, 0f)),
        new Stop(.42f, C(255, 250, 235, .9f)),
        new Stop(.5f,  C(255, 255, 255, 1f)),
        new Stop(.58f, C(255, 250, 235, .9f)),
        new Stop(1f,   C(255, 255, 255, 0f)),
    };
    static readonly Stop[] GL = {
        new Stop(0f,   C(126, 217, 87, 0f)),
        new Stop(.55f, C(126, 217, 87, .55f)),
        new Stop(1f,   C(126, 217, 87, 1f)),
    };
    static readonly Stop[] RR = {
        new Stop(0f,   C(255, 72, 72, 1f)),
        new Stop(.45f, C(255, 72, 72, .55f)),
        new Stop(1f,   C(255, 72, 72, 0f)),
    };

    /// 只在 Director.LineMode==1 时显示；其余模式这一层整个藏起来
    public bool Visible { get => obj.Visible; set => obj.Visible = value; }

    public LineView(Transform parent, int order, float strength = 1f) {
        obj = Gfx.NewMesh("line", parent, Gfx.NewAddMat(), order);
        k = strength;
    }

    void GradQuad(float x, float y, float w, float h, Stop[] stops, float alpha) {
        for (int i = 0; i + 1 < stops.Length; i++) {
            var s0 = stops[i]; var s1 = stops[i + 1];
            var c0 = s0.c; c0.a *= alpha;
            var c1 = s1.c; c1.a *= alpha;
            d.RectH(x + w * s0.t, y, w * (s1.t - s0.t), h, c0, c1);
        }
    }

    // anchor: -1 贴左（右缘落在线上）, +1 贴右, 0 居中
    void Ribbon(Stop[] stops, float top, float bot, int slices,
                System.Func<float, int, float> widthAt,
                System.Func<float, int, float> alphaAt, int anchor) {
        float h = (bot - top) / slices;
        for (int i = 0; i < slices; i++) {
            float y0 = top + h * i, dd = (i + .5f) / slices;
            float a = alphaAt(dd, i), w = widthAt(dd, i);
            if (a <= .004f || w <= .5f) continue;
            float x = Director.FrontAt(y0 + h / 2f) + Director.FX.jit;
            float x0 = anchor < 0 ? x - w : (anchor > 0 ? x : x - w / 2f);
            GradQuad(x0, y0, w, h + 1f, stops, a);
        }
    }

    public void Rebuild() {
        float t = Director.S.t;
        float pulse = .76f + Mathf.Sin(t * 13f) * .14f + Mathf.Sin(t * 29f) * .08f;
        System.Func<float, float> depth = x => .42f + x * .58f;
        System.Func<float, float> yAt = x => Director.TOP + (Director.BOT - Director.TOP) * x;
        System.Func<float, float> fade = x => Mathf.Min(1f, x * 7f) * Mathf.Min(1f, (1f - x) * 9f);

        d.Clear();

        System.Func<float, int, float> sideW = (x, i) => (92f + x * 150f) * (.55f + Director.HeatAt(yAt(x)) * .45f);
        System.Func<float, int, float> sideA = (x, i) =>
            pulse * depth(x) * (.35f + Director.HeatAt(yAt(x)) * .65f) * fade(x) * .38f * k;

        Ribbon(GL, Director.TOP, Director.BOT, 26, sideW, sideA, -1);
        Ribbon(RR, Director.TOP, Director.BOT, 26, sideW, sideA, +1);
        Ribbon(BAND, Director.TOP, Director.BOT, 56,
            (x, i) => (16f + x * 40f) * (.86f + Mathf.Sin(t * 21f + i * .4f) * .14f) * (.42f + Director.HeatAt(yAt(x)) * .85f),
            (x, i) => pulse * depth(x) * (.3f + Director.HeatAt(yAt(x)) * .8f) * fade(x) * 0.95f * k, 0);

        float ga = Director.HeatAt(Director.BOT) * .3f * k;
        if (ga > .004f) {
            float gx = Director.FrontAt(Director.BOT - 40f) + Director.FX.jit;
            d.Glow(gx, Director.BOT - 72f, 96f, C(255, 244, 220, .85f * ga), C(255, 244, 220, .3f * ga), .45f);
        }
        d.Apply(obj.mesh);
    }
}
}
