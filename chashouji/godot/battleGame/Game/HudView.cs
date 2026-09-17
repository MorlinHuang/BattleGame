using Godot;

namespace Chashouji {

/* 顶部两条血条 + 火力条 + 阵营名 + 百分比 + 时钟。
   数值只有 S.p 一个来源，HUD 与画面不可能对不上。 */
public partial class HudView {
    readonly MeshLayer bars;
    readonly TextLayer text;

    public HudView(Node parent, int z) {
        bars = Gfx.NewLayer(parent, "hud_bars", false, z);
        text = new TextLayer();
        text.Setup(parent, "hud_text", false, z + 5);
    }

    struct Bar { public float x, w; public Color c; public float v; public int dir; }

    public void Rebuild(float p) {
        var d = bars.D;
        var list = new[] {
            new Bar { x = 92f,  w = 276f, c = K.GREEN, v = p,        dir = 1 },
            new Bar { x = 572f, w = 296f, c = K.RED,   v = 100f - p, dir = -1 },
        };
        d.Clear();
        foreach (var b in list) {
            d.Rect(b.x, 74f, b.w, 31f, MathX.C255(8, 10, 13, .92f));
            float fw = b.w * b.v / 100f;
            float gx = b.dir > 0 ? b.x : b.x + b.w - fw;
            // 顶到底压暗 62%，和网页版的 createLinearGradient 逐像素等价
            var bot = new Color(Mathf.Floor(b.c.R * 255f * .62f) / 255f,
                                Mathf.Floor(b.c.G * 255f * .62f) / 255f,
                                Mathf.Floor(b.c.B * 255f * .62f) / 255f, 1f);
            d.RectV(gx, 74f, fw, 31f, b.c, bot);
            d.Rect(gx, 74f, fw, 9f, MathX.C255(255, 255, 255, .30f));
        }

        if (S.phase != Phase.Idle) {
            /* 火力条 —— 第二个属性必须看得见。观众刷了礼物、手机没动，屏幕上要是
               没有任何交代，他会认为这游戏是假的。这两条细带就是那个交代：它们
               一起涨说明双方在对拼（手机自然不动），一条比另一条长出来的那截才
               是战况。开方是为了让小额也看得出动静 —— 线性的话几百点火力在几千
               的量程里几乎不动一根头发。 */
            void FireBar(float x, float w, int dir, float f, Color c) {
                float k = Mathf.Min(1f, Mathf.Sqrt(f / 3000f));
                d.Rect(x, 109f, w, 10f, MathX.C255(8, 10, 13, .55f));
                float fw = w * k, gx = dir > 0 ? x : x + w - fw;
                d.Rect(gx, 109f, fw, 10f, new Color(c.R, c.G, c.B, .86f));
            }
            FireBar(92f, 276f, 1, S.fA, K.GREEN);
            FireBar(572f, 296f, -1, S.fB, K.RED);
        }
        bars.Commit();
        text.p = p;
        text.QueueRedraw();
    }

    /* 文字层。
       字体走系统字体：Godot 内置的 Open Sans 没有汉字，"查岗党/灭迹党"会整行
       变成方框。SystemFont 按家族名向操作系统要，与 Unity 版
       Font.CreateDynamicFontFromOSFont 的做法一致。 */
    public partial class TextLayer : Node2D {
        public float p;
        Font f23, f26, f27;

        static Font Sys(int weight) {
            var s = new SystemFont();
            s.FontNames = new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun", "Arial" };
            s.FontWeight = weight;
            return s;
        }

        public override void _Ready() { f23 = Sys(700); f26 = Sys(700); f27 = Sys(700); }

        /* 网页版用 textBaseline='middle'，Godot 的 DrawString 落笔在基线上。
           把中线换算成基线：基线 = 中线 + 上伸部 - 行高的一半。 */
        void Mid(Font fo, float x, float y, string s, int size, HorizontalAlignment al,
                 float w, Color col, float outline) {
            float by = y + fo.GetAscent(size) - fo.GetHeight(size) * 0.5f;
            var pos = new Vector2(al == HorizontalAlignment.Right ? x - w : al == HorizontalAlignment.Center ? x - w / 2f : x, by);
            if (outline > 0f)
                DrawStringOutline(fo, pos, s, al, w, size, (int)outline, MathX.C255(0, 0, 0, .75f));
            DrawString(fo, pos, s, al, w, size, col);
        }

        public override void _Draw() {
            const float WBOX = 300f;
            Mid(f23, 100f, 90f, Mathf.Round(p).ToString("0") + "%", 23, HorizontalAlignment.Left, WBOX, Colors.White, 4);
            Mid(f23, 860f, 90f, Mathf.Round(100f - p).ToString("0") + "%", 23, HorizontalAlignment.Right, WBOX, Colors.White, 4);
            Mid(f26, 92f, 42f, "查岗党", 26, HorizontalAlignment.Left, WBOX, Colors.White, 5);
            Mid(f26, 868f, 42f, "灭迹党", 26, HorizontalAlignment.Right, WBOX, Colors.White, 5);

            if (S.phase == Phase.Idle) return;
            float mm = Mathf.Max(0f, S.clock);
            string tip = $"{(int)(mm / 60f)}:{((int)mm % 60):00}";
            Color col = Colors.White;
            if (S.phase == Phase.Sudden) { tip = $"绝杀 {S.sudden:0}"; col = MathX.C255(255, 90, 90); }
            else if (S.phase == Phase.Over) {
                tip = S.winner > 0 ? "查岗党胜" : S.winner < 0 ? "灭迹党胜" : "平局";
                col = MathX.C255(255, 212, 90);
            } else if (S.stand > 0f) { tip = $"反击 {S.stand:0}"; col = MathX.C255(255, 212, 90); }
            Mid(f27, 480f, 42f, tip, 27, HorizontalAlignment.Center, WBOX, col, 5);
        }
    }
}
}
