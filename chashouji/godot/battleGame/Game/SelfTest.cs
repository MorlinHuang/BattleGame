using Godot;

namespace Chashouji {

/* 画笔自检：把 Draw2D 的每个图元画一遍，一张图看全。
   几何层错了，后面所有东西都是错的，所以它必须能被单独验证 —— 这跟网页版
   的 ?fxstrip / ?linestrip 是同一个用意。 */
public partial class SelfTest : Node2D {
    public override void _Ready() {
        var bg = Gfx.NewLayer(this, "bg", false, -10);
        bg.D.Rect(0, 0, K.W, K.H, MathX.C255(24, 26, 32));
        bg.Commit();

        var L = Gfx.NewLayer(this, "mix", false, 0);
        var A = Gfx.NewLayer(this, "add", true, 1);
        var d = L.D; var a = A.D;

        // 1. Rect / RectH / RectV —— 纯色与两向渐变
        d.Rect(40, 40, 200, 90, K.GREEN);
        d.RectH(260, 40, 200, 90, K.GREEN, K.RED);
        d.RectV(480, 40, 200, 90, K.RED, MathX.C255(255, 72, 72, 0f));

        // 2. RoundRect / RoundRectV —— 圆角在短边收成半圆是对的
        d.RoundRect(40, 160, 200, 90, 22, MathX.C255(255, 214, 226));
        d.RoundRectV(260, 160, 200, 90, 22, K.EMBER, K.INK);
        d.RoundRect(480, 160, 90, 90, 45, MathX.C255(126, 217, 87, .8f));

        // 3. Tri3 / StrokeClosed —— 指针那一套
        var tri = new[] { new Vector2(120, 290), new Vector2(40, 380), new Vector2(200, 380) };
        d.Tri3(tri[0], tri[1], tri[2], K.RED);
        d.StrokeClosed(tri, 4, MathX.C255(12, 14, 18, .8f));

        // 4. Segment —— 单段粗线，端点不封口是对的
        for (int i = 0; i < 5; i++)
            d.Segment(new Vector2(260 + i * 44, 290), new Vector2(300 + i * 44, 380),
                      3 + i * 3, MathX.C255(255, 255, 255, .9f));

        // 5. Ellipse / EllipseRing —— 冲击环是压扁的
        d.Ellipse(560, 335, 60, 60, MathX.C255(246, 226, 198));
        d.EllipseRing(760, 335, 90, 34, 6, MathX.C255(255, 156, 38, .95f));

        // 6. PolyFan + StarPts —— 凹多边形必须从中心扇形化
        for (int i = 0; i < 4; i++)
            d.PolyFan(90 + i * 120, 470, Draw2D.StarPts(90 + i * 120, 470, 46, i * 0.4f),
                      MathX.C255(255, 214, 96));

        // 7. PolyFan + HeartPts —— 同上，且视觉重心偏下
        for (int i = 0; i < 4; i++)
            d.PolyFan(90 + i * 120, 590, Draw2D.HeartPts(90 + i * 120, 590, 44),
                      i % 2 == 0 ? MathX.C255(255, 92, 130) : MathX.C255(198, 40, 78));

        // 8. Glow —— 相加混合层，中心亮、边缘透明
        a.Glow(200, 760, 150, MathX.C255(255, 244, 220, .85f), MathX.C255(255, 244, 220, .30f), .45f);
        a.Glow(480, 760, 150, MathX.C255(126, 217, 87, .85f), MathX.C255(126, 217, 87, .30f), .45f);
        a.Glow(760, 760, 150, MathX.C255(255, 72, 72, .85f), MathX.C255(255, 72, 72, .30f), .45f);

        /* 9. Push/Pop 变换 —— 一排绕自己中心转的方块。
              验的是变换只影响 Push 之后加进来的点。 */
        for (int i = 0; i < 8; i++) {
            d.Push(90 + i * 110, 940, i * 0.31f, 1f - i * 0.05f);
            d.RoundRect(-40, -40, 80, 80, 14, MathX.C255(120, 200, 255, .9f));
            d.Pop();
        }
        // Pop 之后这一条必须是水平的；歪了就说明 Pop 没生效
        d.Rect(40, 1010, 880, 6, MathX.C255(255, 255, 255, .7f));

        /* 10. 混合模式对照：同一块半透明白，Mix 层压在底色上应该变灰白，
               Add 层应该更亮。两块看起来一样 = 混合模式没生效。 */
        d.Rect(40, 1060, 200, 80, MathX.C255(255, 255, 255, .5f));
        a.Rect(260, 1060, 200, 80, MathX.C255(255, 255, 255, .5f));
        // 底下垫一条有颜色的，才看得出叠加的差别
        bg.D.Rect(40, 1100, 420, 40, MathX.C255(40, 120, 90));
        bg.Commit();

        // 11. 大批量 —— 逼 Draw2D 扩容，验证扩容后索引仍然正确
        for (int i = 0; i < 600; i++) {
            float x = 40 + (i % 60) * 15f, y = 1170 + (i / 60) * 10f;
            d.Rect(x, y, 11, 7, MathX.C255(200 - i % 120, 120 + i % 100, 90, .85f));
        }

        L.Commit(); A.Commit();
        GD.Print($"[selftest] mix 顶点 {d.VertCount} · add 顶点 {a.VertCount}");
    }
}
}
