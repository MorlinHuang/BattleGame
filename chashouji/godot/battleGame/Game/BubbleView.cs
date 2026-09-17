using System.Collections.Generic;
using Godot;

namespace Chashouji {

/* 手机上方的聊天气泡 —— 逐字取自网页版 web/bubble.js。
 *
 * 这个玩法争的是一部手机，而手机里到底有什么，此前画面上一个字都没交代。
 * 观众看到的是两个人在拔河，看不到他们**为什么**拔河 —— 气泡补的就是这个。
 * 消息一条条冒出来，争夺的理由才立得住，而且它每隔一两秒就给观众一个新的
 * 阅读目标，这是直播画面最缺的东西。
 *
 * 位置是按底版量出来的：手机在 y≈650，两个人的脸在 520~610，而 y=150~470
 * 那一整片（沙发上沿到血条之间的墙）完全空着。所以气泡从手机正上方升起，
 * 穿过两人中间那条缝，最后停在墙面上 —— 不停在手机边上，是因为那里全是手
 * 和脸，而那是这个玩法唯一值得看的东西。对抗线当初被改掉也是同一个理由。
 *
 * 四种消息各有各的作用，凑在一起就是"这部手机为什么不能给你看"：
 *   text   日常的一来一往，给密度
 *   hot    暧昧的那几句，给动机
 *   sys    撤回、正在输入 —— 什么都没说，但最让人往下想
 *   voice  一条没听过的语音，同上
 * 全程不出现任何露骨的词，观众自己脑补的永远比写出来的狠。
 */
public class Msg {
    public string t, s;
    public int w;
    public float cw = -1f;      // 文字宽度，量过一次就缓存
    public Msg(string t, string s, int w) { this.t = t; this.s = s; this.w = w; }
}

public class Bub {
    public Msg m;
    public float w, h;
    public int st;              // 被后来的消息顶上去了几格
    public float x, y, rise, age, life;
}

public static class Bubble {
    const int MAX = 3;          // 同屏上限。再多就成了刷屏，单条反而没人读
    /* 飘多高、让多少，是被上下两头夹出来的：
         下边界 —— 出生点在手机上方 44px（y≈516），那里正好是两人中间的沙发，
                  不挡脸；再低就贴到抢手机的那两双手上了
       上边界 —— 血条在 y=74~105，堆到第四条时不能顶进去
       所以 RISE + (MAX-1)*STACK 必须 ≤ 341，最高那条的顶边才停在 150 左右。 */
    const float RISE = 140f;    // 从出生点往上飘多远（减速趋近，不是匀速）
    /* 让位的距离必须**大于**气泡自己的高度，否则两条上下贴死，读起来是一坨。
       大气泡高 64，所以 76 留出 12px 的缝。同屏上限跟着从 4 压到 3。 */
    public const float STACK = 76f;
    const float LIFE = 4.6f;
    public const float FADE = 0.9f;   // 末尾这段时间淡出

    public const int SIZE = 34, SIZE_S = 26;

    public static readonly List<Bub> Act = new List<Bub>();
    static float gap = 1.2f;

    /* 消息池。权重就是出现频率 —— 日常最多，暧昧的那几句要稀，稀才有分量；
       系统提示和语音条各占一小份，它们是"留白"，多了就不灵了。 */
    static readonly Msg[] MSG = {
        new Msg("text", "在吗", 3),
        new Msg("text", "睡了没", 3),
        new Msg("text", "怎么不回我", 3),
        new Msg("text", "洗澡去了", 3),
        new Msg("text", "刚开完会", 2),
        new Msg("text", "这么晚还没睡？", 2),
        new Msg("text", "明天有空吗", 2),
        new Msg("text", "记得吃饭", 2),
        new Msg("text", "那我等你", 2),
        new Msg("hot",  "我好想你", 2),
        new Msg("hot",  "今天梦到你了", 1),
        new Msg("hot",  "还是你懂我", 1),
        new Msg("hot",  "什么时候见一面", 1),
        new Msg("hot",  "别被发现了", 1),
        new Msg("sys",  "对方撤回了一条消息", 2),
        new Msg("sys",  "对方正在输入…", 2),
        new Msg("sys",  "3 条未读消息", 1),
        new Msg("voice", "7″", 1),
        new Msg("voice", "12″", 1),
        new Msg("voice", "23″", 1),
    };

    static int totalW = -1;
    static Msg Pick() {
        if (totalW < 0) { totalW = 0; foreach (var m in MSG) totalW += m.w; }
        float r = Particles.Rnd() * totalW;
        foreach (var m in MSG) { r -= m.w; if (r <= 0f) return m; }
        return MSG[0];
    }

    /* 三种底色。白的是日常，粉的是那几句，灰的是系统提示。
       都带深色描边 —— 明亮客厅底图上，浅色块不描边就糊进墙里，跟碎片、跟角色
       线稿是同一条规矩。 */
    public struct Skin { public Color bg, line, fg; public float lw; }

    public static Skin SkinOf(string t) => t switch {
        "hot" => new Skin { bg = MathX.C255(255, 226, 238), line = MathX.C255(206, 64, 116, 0.95f),
                            fg = MathX.C255(150, 33, 79), lw = 3.4f },
        "sys" => new Skin { bg = MathX.C255(226, 226, 232, 0.94f), line = MathX.C255(96, 94, 106, 0.7f),
                            fg = MathX.C255(94, 92, 104), lw = 2.2f },
        _     => new Skin { bg = MathX.C255(251, 251, 253), line = MathX.C255(46, 40, 52, 0.88f),
                            fg = MathX.C255(44, 40, 48), lw = 3.0f },
    };

    /* 文字宽度得测，不能估。中文和数字宽度差很多，估出来的气泡不是撑爆就是
       留一大块空白。测一次就够，结果缓存在消息上。
       Measure 由视图层在字体就绪后注入 —— 字体是 Godot 资源，静态数据层不该
       自己去持有它。 */
    public static System.Func<string, int, float> Measure;

    /* 推一条新消息。旧的整体往上让一格 —— 读起来就是聊天记录在往上滚，而不是
       几个气泡各飘各的。 */
    public static void Push(string forceType) {
        if (Measure == null) return;
        Msg m;
        if (forceType != null) {
            var sub = new List<Msg>();
            foreach (var x in MSG) if (x.t == forceType) sub.Add(x);
            m = sub.Count > 0 ? sub[(int)(Particles.Rnd() * sub.Count) % sub.Count] : MSG[0];
        } else m = Pick();

        bool small = m.t == "sys";
        if (m.cw < 0f) m.cw = Measure(m.s, small ? SIZE_S : SIZE);
        float pad = small ? 22f : 30f;
        float w = m.cw + pad * 2f + (m.t == "voice" ? 58f : 0f);   // 语音条左边还要放喇叭
        float h = small ? 50f : 64f;

        foreach (var b in Act) b.st++;
        if (Act.Count >= MAX) Act.RemoveAt(0);

        Act.Add(new Bub {
            m = m, w = w, h = h, st = 0,
            x = Director.FrontAt(FX.phoneY) + FX.jit, y = FX.phoneY - 44f,
            rise = 0f, age = 0f, life = LIFE,
        });
    }

    public static void Update(float dt, float struggle) {
        /* 越僵持发得越急。五五开的时候两个人谁也拽不动，画面上信息量最少 ——
           正好让手机替他们说话；而一边倒的时候胜负本身已经够看了。 */
        gap -= dt;
        if (gap <= 0f) {
            Push(null);
            gap = (1.5f + Particles.Rnd() * 1.3f) * (1.35f - struggle * 0.5f);
        }
        for (int i = Act.Count - 1; i >= 0; i--) {
            var b = Act[i];
            b.age += dt; b.life -= dt;
            if (b.life <= 0f) { Act.RemoveAt(i); continue; }
            // 减速上升：冒出来那一下快，之后慢慢停住。匀速飘会读成"气球"
            b.rise += (RISE - b.rise) * MathX.Approach(dt, 2.4f);
        }
    }

    public static void Clear() { Act.Clear(); gap = 1.2f; }

    /* 弹入：0.6 → 冲过 1 一点点 → 收回 1。消息是"跳"出来的，线性放大读起来
       像是慢慢显影，那是另一种情绪。 */
    public static float ScaleOf(Bub b) {
        float e = Mathf.Min(1f, b.age / 0.16f);
        return e < 1f ? 0.62f + 0.52f * e - 0.14f * e * e : 1f;
    }

    public static float AlphaOf(Bub b) =>
        Mathf.Min(1f, b.life / FADE) * Mathf.Min(1f, b.age / 0.07f);

    public static float TopOf(Bub b) => b.y - b.rise - b.st * STACK;
}

/* 气泡的两块画布：底与描边走顶点色批，文字走 DrawString。 */
public partial class BubbleView : Node2D {
    MeshLayer box;
    TextLayer text;
    static Font font;

    static Color A(Color c, float a) => new Color(c.R, c.G, c.B, c.A * a);

    public void Init(Node parent, int z) {
        parent.AddChild(this);
        ZIndex = z;
        box = Gfx.NewLayer(this, "bubble_box", false, 0);
        text = new TextLayer();
        text.Setup(this, "bubble_text", false, 2);

        var f = new SystemFont();
        f.FontNames = new[] { "Microsoft YaHei", "微软雅黑", "PingFang SC", "SimHei", "黑体", "Arial" };
        f.FontWeight = 600;
        font = f;
        Bubble.Measure = (s, size) => font.GetStringSize(s, HorizontalAlignment.Left, -1f, size).X;
    }

    public void Rebuild() {
        var d = box.D;
        d.Clear();
        foreach (var b in Bubble.Act) {
            float alpha = Bubble.AlphaOf(b);
            if (alpha <= 0.01f) continue;
            var sk = Bubble.SkinOf(b.m.t);
            bool small = b.m.t == "sys";
            float w2 = b.w / 2f, h2 = b.h / 2f;

            d.Push(b.x, Bubble.TopOf(b), 0f, Bubble.ScaleOf(b));

            /* 尾巴只给最新的那一条。上面几条已经是"记录"了，都拖着尾巴的话，读起来
               是每一条从下面那条里冒出来的 —— 而它们其实都来自同一部手机。
               先画尾巴，再让气泡本体盖住它的上边，两个形状才连得成一个。 */
            if (!small && b.st == 0) {
                var t0 = new Vector2(-16f, h2 - 2f);
                var t1 = new Vector2(-2f, h2 + 17f);
                var t2 = new Vector2(10f, h2 - 2f);
                d.Tri3(t0, t1, t2, A(sk.bg, alpha));
                var lc = A(sk.line, alpha);
                d.Segment(t0, t1, sk.lw, lc); d.Segment(t1, t2, sk.lw, lc); d.Segment(t2, t0, sk.lw, lc);
            }

            float rr = small ? h2 : 20f;
            d.RoundRect(-w2, -h2, b.w, b.h, rr, A(sk.bg, alpha));
            d.StrokeClosed(Draw2D.RoundRectPts(-w2, -h2, b.w, b.h, rr), sk.lw, A(sk.line, alpha));

            if (b.m.t == "voice") {
                /* 语音条：三根高低不一的竖线 + 时长。观众听不到里面是什么，而这正是
                   它比任何一句台词都有劲的地方。 */
                float bx = -w2 + 30f;
                for (int k = 0; k < 3; k++) {
                    float hh = 10f + k * 8f;
                    d.Rect(bx + k * 12f, -hh / 2f, 6f, hh, A(sk.fg, alpha));
                }
            }
            d.Pop();
        }
        box.Commit();
        text.QueueRedraw();
    }

    public partial class TextLayer : Node2D {
        public override void _Draw() {
            if (font == null) return;
            foreach (var b in Bubble.Act) {
                float alpha = Bubble.AlphaOf(b);
                if (alpha <= 0.01f) continue;
                var sk = Bubble.SkinOf(b.m.t);
                bool small = b.m.t == "sys";
                int size = small ? Bubble.SIZE_S : Bubble.SIZE;
                float sc = Bubble.ScaleOf(b);
                // 网页版是 textBaseline='middle' 再往下挪 1px，这里换算成基线位置
                float by = 1f + font.GetAscent(size) - font.GetHeight(size) * 0.5f;
                var col = A(sk.fg, alpha);

                DrawSetTransform(new Vector2(b.x, Bubble.TopOf(b)), 0f, new Vector2(sc, sc));
                if (b.m.t == "voice") {
                    float bx = -b.w / 2f + 30f;
                    DrawString(font, new Vector2(bx + 52f, by), b.m.s,
                               HorizontalAlignment.Left, -1f, Bubble.SIZE, col);
                } else {
                    DrawString(font, new Vector2(-b.w / 2f, by), b.m.s,
                               HorizontalAlignment.Center, b.w, size, col);
                }
            }
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }
}
}
