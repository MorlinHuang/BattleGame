using System;
using System.Collections.Generic;
using Godot;

namespace Chashouji {

/* 调试台面板。
   网页版这一层是 index.html 里的 DOM 按钮，浏览器白送；Godot 没有 DOM，得自己
   画、自己判命中。所以这里不是逐字移植而是重写，但**行为**逐条对齐网页版：
   idle 下 ±7 直接摆进度、点任意礼物自动开局、开局后进度只能由火力差推出来。

   面板画在游戏画布内（960x1334 那套坐标），而不是用 Godot 的 Control/Theme ——
   复用已有的 Draw2D 画笔，不必为一个调试台引入主题、锚点、布局容器一整套体系。
   代价是它占画面，所以 F1 能整体关掉：最终产品是 OBS 捕获窗口，调试台绝不能
   出现在直播画面里。命令行 --nopanel 则是一开始就不显示，给截图用。 */
public partial class PanelView : Node2D {
    /* 布局。面板贴画布底部，起点压在刻度尺（y≈1144）之下 —— 调试台可以挡地毯，
       但不能挡任何一个读数，否则"看着面板调参数"会调出错的结论。 */
    const float TOP = 1156f, STAT_Y = 1170f;
    const float R1 = 1184f, R2 = 1234f, R3 = 1284f, BH = 44f;
    const float WBOX = 600f;

    struct Btn {
        public float x, y, w, h;
        public string label;
        public Color tone;          // 描边与文字的基色，同时也是阵营色
        public Action act;
        public Func<string> dyn;    // 标签随状态变的按钮用它，null 就用 label
    }

    readonly List<Btn> btns = new();
    MeshLayer box;
    LabelLayer text;
    int hover = -1, down = -1;
    float downT = -9f;
    /* 叫 Showing 而不是 Show：CanvasItem 自己有个 Show() 方法，同名字段会把它
       藏掉（CS0108），将来谁写 panel.Show() 会得到一条看不懂的错误。 */
    public bool Showing = true;

    /* 命令行 --click=x,y（画布坐标）与 --key=<Key枚举名>：等几帧后注入一次
       点击 / 按键。对应网页版的 ?live=…&liveGift=… —— 交互链路必须能在无人值守
       的截图里走一遍，否则"按钮到底点不点得动"只能靠人坐在那儿点。 */
    /* 最近一次输入事件，显示在状态行尾巴上。对应网页版 index.html 里的 #msg。
       不是纯调试装饰：点了没反应时，它能直接区分"事件没到"（这里一片空白）
       和"事件到了但没命中"（这里有坐标）—— 否则只能靠猜。 */
    static string lastMsg = "";
    static float lastMsgT = -9f;
    static void Say(string m) { lastMsg = m; lastMsgT = S.t; }

    Vector2? clickAt;
    string keyName;
    /* 等 30 帧再注入，不是 6 帧。canvas transform 在最初几帧里还是单位阵，
       stretch 尚未生效 —— 那时换算出来的坐标是错的，而且错得很隐蔽：正变换给
       单位阵、逆变换却已经是缩放阵，两边对不上。我就是被这个矛盾带偏过一次。 */
    int injectWait = 30;
    bool diag;

    static readonly Color NEUTRAL = MathX.C255(180, 190, 205);
    static readonly Color GOLD = MathX.C255(255, 212, 90);

    public void Init(Node parent, int z) {
        parent.AddChild(this);
        ZIndex = z;
        box = Gfx.NewLayer(this, "panel_box", false, 0);
        text = new LabelLayer { Owner2 = this };
        text.Setup(this, "panel_text", false, 5);
        Build();

        foreach (var a in OS.GetCmdlineUserArgs()) {
            if (a.StartsWith("--click=")) {
                var xy = a.Substring(8).Split(',');
                if (xy.Length == 2) { clickAt = new Vector2(xy[0].ToFloat(), xy[1].ToFloat()); diag = true; }
                GD.Print($"[click] 解析 \"{a}\" → {(clickAt.HasValue ? clickAt.Value.ToString() : "失败")}");
            } else if (a.StartsWith("--key=")) {
                keyName = a.Substring(6); diag = true;
                GD.Print($"[key] 解析 \"{a}\" → {keyName}");
            }
        }
    }

    /* 五个礼物对应数值九档里的 0/1/2/3/4 档，与网页版 index.html 的 data-shop
       逐字一致。两行分别是查岗党与灭迹党 —— 同一件礼物两边都能送，这样才能
       测出"双方对刷时手机纹丝不动"那个核心手感。 */
    void Build() {
        string[] keys = { "like", "wand", "mirror", "boom", "drop" };
        string[] tags = { "点赞⓪", "仙女棒①", "魔法镜②", "爱的爆炸③", "神秘空投④" };
        float bx = 96f, bw = (K.W - bx - 12f - 4f * 6f) / 5f;
        for (int i = 0; i < 5; i++) {
            string k = keys[i];
            float x = bx + i * (bw + 6f);
            btns.Add(new Btn { x = x, y = R1, w = bw, h = BH, label = tags[i],
                               tone = K.GREEN, act = () => Gift(+1, k) });
            btns.Add(new Btn { x = x, y = R2, w = bw, h = BH, label = tags[i],
                               tone = K.RED,   act = () => Gift(-1, k) });
        }

        float cw = (K.W - 24f - 4f * 6f) / 5f;
        float Cx(int i) => 12f + i * (cw + 6f);
        btns.Add(new Btn { x = Cx(0), y = R3, w = cw, h = BH, tone = GOLD,
                           dyn = () => S.phase == Phase.Idle ? "开始对局" : "回到调试台",
                           act = ToggleMatch });
        btns.Add(new Btn { x = Cx(1), y = R3, w = cw, h = BH, label = "查岗 +7",
                           tone = K.GREEN, act = () => Nudge(+7f) });
        btns.Add(new Btn { x = Cx(2), y = R3, w = cw, h = BH, label = "灭迹 +7",
                           tone = K.RED,   act = () => Nudge(-7f) });
        btns.Add(new Btn { x = Cx(3), y = R3, w = cw, h = BH, tone = NEUTRAL,
                           dyn = () => "对抗线 " + S.line,
                           act = () => { S.line = (S.line + 1) % 4; Say("对抗线档位 " + S.line); } });
        btns.Add(new Btn { x = Cx(4), y = R3, w = cw, h = BH, tone = NEUTRAL,
                           dyn = () => S.auto ? "自动 开" : "自动 关",
                           act = () => { S.auto = !S.auto; Say(S.auto ? "自动演示 开" : "自动演示 关"); } });
    }

    // ---------- 动作。三个都与网页版的 onclick 一一对应 ----------

    static void Gift(int side, string key) {
        if (S.phase == Phase.Idle) Director.StartMatch();   // 点礼物自动开局
        Director.GiveGift(side, key);
        var it = Shop.T[key];
        /* 把注入量说出来。低档礼物（点赞 0.01、仙女棒 1）在火力条上根本看不出
           动静 —— 火力条是开方的，1 点火力只占 1.8%，肉眼就是没动。没有这行字，
           点了低档礼物会被读成"按钮坏了"。 */
        Say($"{(side > 0 ? "查岗党" : "灭迹党")} 送出 {it.name}  火力 +{it.push:0.##}"
          + $"  →  {S.fA:0}:{S.fB:0}");
    }

    static void ToggleMatch() {
        if (S.phase == Phase.Idle) { Director.StartMatch(); Say("开局"); }
        else { Director.BackToIdle(); Say("回到调试台"); }
    }

    /* 只有 idle 下才准直接摆进度。这条守卫是网页版 nudge() 里就有的：开局之后
       进度只能由火力差推出来，否则"这件礼物到底推了多少"永远说不清 —— 手动
       摆过一次 p，后面所有读数都不能用了。 */
    static void Nudge(float d) {
        if (S.phase != Phase.Idle) { Say("对局中不能手动摆进度，先『回到调试台』"); return; }
        S.auto = false;
        S.p = MathX.Clamp(S.p + d, 0f, 100f);
        Say($"进度 → {S.p:0.0}");
    }

    // ---------- 输入 ----------

    /* 键盘和鼠标都给。键盘不是图省事：连点压测要的是稳定节奏，手点不出来；
       而且键盘在 --nopanel（面板关掉）时照样能用，OBS 捕获时不必为了送一件
       礼物把调试台调出来。 */
    public override void _UnhandledInput(InputEvent e) {
        if (e is InputEventKey k && k.Pressed && !k.Echo) {
            if (diag) GD.Print($"[key] 收到 {k.Keycode}");
            switch (k.Keycode) {
                case Key.F1:    Showing = !Showing; return;
                case Key.Key1:  Gift(+1, "like");   return;
                case Key.Key2:  Gift(+1, "wand");   return;
                case Key.Key3:  Gift(+1, "mirror"); return;
                case Key.Key4:  Gift(+1, "boom");   return;
                case Key.Key5:  Gift(+1, "drop");   return;
                case Key.Q:     Gift(-1, "like");   return;
                case Key.W:     Gift(-1, "wand");   return;
                case Key.E:     Gift(-1, "mirror"); return;
                case Key.R:     Gift(-1, "boom");   return;
                case Key.T:     Gift(-1, "drop");   return;
                case Key.Space: ToggleMatch();      return;
                case Key.Left:  Nudge(-7f);         return;
                case Key.Right: Nudge(+7f);         return;
                case Key.L:     S.line = (S.line + 1) % 4; Say("对抗线档位 " + S.line); return;
                case Key.A:     S.auto = !S.auto; Say(S.auto ? "自动演示 开" : "自动演示 关"); return;
            }
            return;
        }
        if (!Showing) return;
        if (e is InputEventMouseButton m && m.Pressed && m.ButtonIndex == MouseButton.Left) {
            /* 事件带的是 **viewport 坐标**，要过 canvas transform 的逆才是画布坐标。
               stretch=canvas_items 下 viewport 的实际尺寸就是窗口尺寸（540x750），
               960x1334 那套画布坐标是被 canvas transform 缩放后画进去的 —— 截图
               出来是 539x750 而不是 960x1334，就是这件事的直接证据。

               ⚠ 别被"窗口按 1:1 开时不转也能命中"骗了：那时逆变换恰好是单位阵，
               转不转都对。一缩放就全偏，而缩放才是默认（window_width_override=540）。
               这一条只能用真实鼠标验证，--click 注入验不出来 —— 注入的事件是我
               自己按同一个变换算出来的，两边一起错也一起对。 */
            /* 事件带的 position 已经是画布坐标，直接用。
               viewport 在分发之前会对它做一次 GetScreenTransform() 的逆变换
               （实测：注入 865 收到 1538.85 = 865/0.5615），所以 960x1334 这套
               坐标是现成的。别再套 canvas transform —— 那个管的是 Camera2D 一类
               画布内变换，没有相机时是单位阵，套了不报错也不起作用，只会让人
               以为这里已经处理过缩放了。 */
            var q = m.Position;
            int i = HitAt(q);
            if (diag) GD.Print($"[click] 画布({q.X:0},{q.Y:0}) → 按钮 {i}");
            if (i >= 0) { btns[i].act(); down = i; downT = S.t; }
            else Say($"点击({q.X:0},{q.Y:0}) 没落在任何按钮上");
        }
    }

    int HitAt(Vector2 q) {
        for (int i = 0; i < btns.Count; i++) {
            var b = btns[i];
            if (q.X >= b.x && q.X <= b.x + b.w && q.Y >= b.y && q.Y <= b.y + b.h) return i;
        }
        return -1;
    }

    // ---------- 每帧重建 ----------

    public void Rebuild() {
        /* 诊断注入。等几帧再发：Director 刚建完的头一两帧里 viewport 的变换还
           没稳定，这时候换算出来的窗口坐标是错的。 */
        if ((clickAt.HasValue || keyName != null) && --injectWait <= 0) {
            if (keyName != null) {
                if (System.Enum.TryParse<Key>(keyName, true, out var kc)) {
                    GD.Print($"[key] 注入 {kc}");
                    Input.ParseInputEvent(new InputEventKey { Keycode = kc, Pressed = true });
                } else GD.Print($"[key] 无法识别的键名 \"{keyName}\"");
                keyName = null;
            }
            if (clickAt.HasValue) {
                /* 画布坐标 → 窗口坐标。ParseInputEvent 把 position 原样送进输入
                   管线，之后 viewport 照样会对它做 screenXf 的逆 —— 所以这里得先
                   正着乘一次，两下抵消，接收端拿到的才是画布坐标。 */
                var ev = new InputEventMouseButton {
                    ButtonIndex = MouseButton.Left, Pressed = true,
                    Position = GetViewport().GetScreenTransform() * clickAt.Value
                };
                GD.Print($"[click] 注入 画布({clickAt.Value.X:0},{clickAt.Value.Y:0})"
                       + $" → 窗口({ev.Position.X:0},{ev.Position.Y:0})");
                clickAt = null;
                Input.ParseInputEvent(ev);
            }
        }
        Visible = Showing;
        if (!Showing) return;
        hover = HitAt(GetGlobalMousePosition());

        var d = box.D;
        d.Clear();
        /* 上边缘给一道渐变而不是硬边。硬边在截图里会被读成画面构图的一部分，
           而这东西根本不属于画面。 */
        d.RectV(0f, TOP - 8f, K.W, 8f, MathX.C255(10, 12, 16, 0f), MathX.C255(10, 12, 16, .88f));
        d.Rect(0f, TOP, K.W, K.H - TOP, MathX.C255(10, 12, 16, .88f));

        for (int i = 0; i < btns.Count; i++) {
            var b = btns[i];
            bool hot = i == hover;
            bool hit = i == down && S.t - downT < 0.12f;   // 按下的闪一下，确认点到了
            float a = hit ? .34f : hot ? .20f : .10f;
            d.RoundRect(b.x, b.y, b.w, b.h, 8f, new Color(b.tone.R, b.tone.G, b.tone.B, a));
            d.StrokeClosed(Draw2D.RoundRectPts(b.x, b.y, b.w, b.h, 8f), hit ? 2.4f : 1.4f,
                           new Color(b.tone.R, b.tone.G, b.tone.B, hot || hit ? .95f : .55f));
        }
        box.Commit();
        text.QueueRedraw();
    }

    /* 对应网页版底部那行 #stat。idle 与 play 显示的东西不一样 —— 调试台关心
       "现在摆在哪一格"，对局关心"两边火力差多少"。 */
    string Stat() {
        // 事件回显留 6 秒，够看清刚才那一下到底有没有到、到了落在哪
        string tail = (lastMsg != "" && S.t - lastMsgT < 6f) ? "   ◀ " + lastMsg : "";
        return Head() + tail;
    }

    string Head() {
        if (S.phase == Phase.Idle)
            return $"调试台  p={S.p:0.0}  对抗线x={Director.FrontAt(FX.phoneY):0}"
                 + $"  粒子{Particles.Count} 弹幕{Ammo.Count}  {Engine.GetFramesPerSecond()}fps"
                 + "   ·  [1-5]查岗党送礼  [Q-T]灭迹党  [空格]开局  [←→]±7  [L]对抗线  [F1]隐藏面板";
        float mm = Mathf.Max(0f, S.clock);
        string s = $"p={S.p:0.0}  火力 {S.fA:0}:{S.fB:0}  净差{S.fA - S.fB:0}  "
                 + $"{(int)(mm / 60f)}:{((int)mm % 60):00}";
        if (S.phase == Phase.Sudden) s += $"  绝杀{S.sudden:0}";
        if (S.stand > 0f) s += $"  反击{S.stand:0}";
        if (S.phase == Phase.Over)
            s += "  " + (S.winner > 0 ? "查岗党胜" : S.winner < 0 ? "灭迹党胜" : "平局");
        return s + $"   粒子{Particles.Count} 弹幕{Ammo.Count}  {Engine.GetFramesPerSecond()}fps";
    }

    /* 文字层。与 HudView 同样的理由走 SystemFont：Godot 内置字体没有汉字，
       "查岗党"会整行变成方框。 */
    public partial class LabelLayer : Node2D {
        public PanelView Owner2;
        Font bold, reg;

        static Font Sys(int weight) {
            var s = new SystemFont();
            s.FontNames = new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun", "Arial" };
            s.FontWeight = weight;
            return s;
        }

        public override void _Ready() { bold = Sys(700); reg = Sys(400); }

        // 中线换基线，与 HudView 里那套一致
        void Mid(Font fo, float cx, float cy, string s, int size, Color col, float w = WBOX) {
            float by = cy + fo.GetAscent(size) - fo.GetHeight(size) * 0.5f;
            DrawString(fo, new Vector2(cx - w / 2f, by), s, HorizontalAlignment.Center, w, size, col);
        }

        public override void _Draw() {
            var o = Owner2;
            if (o == null) return;
            Mid(bold, 51f, R1 + BH / 2f, "查岗党", 18, K.GREEN);
            Mid(bold, 51f, R2 + BH / 2f, "灭迹党", 18, K.RED);
            for (int i = 0; i < o.btns.Count; i++) {
                var b = o.btns[i];
                // 阵营色掺白，让它在半透明底板上读得出来又不跟血条抢眼
                var c = b.tone.Lerp(Colors.White, .55f);
                Mid(bold, b.x + b.w / 2f, b.y + b.h / 2f, b.dyn != null ? b.dyn() : b.label, 20, c);
            }
            Mid(reg, K.W / 2f, STAT_Y, o.Stat(), 16, MathX.C255(150, 160, 175), 940f);
        }
    }
}
}
