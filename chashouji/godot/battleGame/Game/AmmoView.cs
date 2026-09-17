using System.Collections.Generic;
using Godot;

namespace Chashouji {

/* 礼物弹幕：飞过去，撞到对抗线才算数 —— 逐字取自网页版 web/ammo.js。
 *
 * 分两层，跟 ParticleFx 的"形态 vs 题材"是同一个分法：
 *   样式（volley / single / heavy）—— 三种，管发射节奏与体量
 *   物品（发卡 / 抱枕 / 手柄 …）—— 可以无限加，只是皮
 * 观众不需要认出飞过来的是什么，光看节奏就知道这一发有多重。加新礼物只往
 * Shop.G 表里添一行，这个文件不用动；除非要加新物品的画法（那在 AmmoArt）。
 *
 * 命中判定用的是 Director.FrontAt(y) —— 对抗线在弹幕**自己那个高度**上的真实
 * 横坐标，不是中点。所以不同高度飞来的弹幕会在不同的行注入冲量，那条线才活
 * 得起来。 */

public struct LaunchOpt {
    public bool one;      // 常规火力：一发就是一发，不走连珠那一串
    public bool gift;     // 礼物触发的，独占期间排队而不是丢掉
    public bool exec;     // 处决（档 4）
    public bool clash;    // 飞到中线就被对面撞掉
}

/* 一发在飞的弹幕。不叫 Shot —— Util/Shot.cs 里那个 Shot 是截图工具。 */
public class Bolt {
    public GiftDef g;
    public string item;
    public float r, y, x, vx, ax, rot, vrot;
    public int from;
    public bool exec, clash;
    public float clashOff;
    /* 轨迹环形缓冲：拖尾画的是这个东西**真正走过**的地方。原先那几条速度线
       是固定画在本体后方的，跟实际路径无关，所以飞得快飞得慢看上去一个样；
       记下真实轨迹之后，拖尾长度自己就跟速度挂上钩了。 */
    public float[] hx, hr;
    public int hi;
    public float x0;      // 起点，用来算"飞到哪儿了"
    public float near;    // 0 刚出场 → 1 贴上对抗线，色晕靠它烧起来
}

public static class Ammo {
    const int MAX = 48;
    /* 轨迹环形缓冲的长度。原来是 8 帧，看着够，实际不够：被子 850px/s，八帧
       只往回退了 113 像素，而被子本身就有 156 像素宽 —— 拖尾比物体还短，整条
       全叠在本体底下，画了等于没画。 */
    const int TRAIL = 16, TMASK = 15;

    static readonly List<Bolt> act = new List<Bolt>(MAX);
    static readonly Stack<Bolt> pool = new Stack<Bolt>();

    struct Qi { public float t; public GiftDef g; public float y; public bool exec, clash; }
    struct Wn { public int from; public float y, t, max; }
    struct Pd { public GiftDef g; public float y; public LaunchOpt o; }

    static readonly List<Qi> queue = new List<Qi>();    // 待发射：连珠的后续几颗、重投的预警期
    static readonly List<Wn> warns = new List<Wn>();    // 预警箭头
    /* 独占窗口。档 4 落地后的这一段时间里，别的东西不许出现在屏幕上 ——
       这是最强的一种表现手段，而且不需要任何新的粒子技术：观众看到的是
       "全世界让开，只有这一下"。礼物级的排队补发，常规火力直接丢掉
       （它只是火力的表现，数值那边早就算过了，少画几发不影响战况）。 */
    static float lockT;
    static readonly List<Pd> pending = new List<Pd>();

    static float Rnd() => Particles.Rnd();
    public static int Count => act.Count + queue.Count;

    /* 重投给到 850 而不是更慢。直觉上"大件就该飞得慢"，但屏幕外到对抗线只有
       六百来像素，620 的时候光飞行就要一秒，加上预警一共一秒二 —— 中间那大半
       秒是匀速滑行，画面上什么也没发生，观众的注意力会飘走。分量感是靠体量、
       预警和落地那一下给的，不是靠拖时间。 */
    static float SpeedOf(string st) => st == "volley" ? 1400f : st == "single" ? 900f : 850f;
    /* 重投是唯一加速的。匀速的大件在屏幕上就是一路平移，中间大半秒什么也没
       发生 —— 拖沓的根子在匀速，不在速度值不够；单纯调快又会丢掉"看清它是
       什么"的那一段。加速两头都要：前段慢得认得出，后段砸得狠，总时长还短了
       三分之一。 */
    static float AccOf(string st) => st == "heavy" ? 900f : 0f;
    /* 每一发在基准速度上再抖一下。整批同速看着像传送带上排好的货，而连珠本来
       该读成"抓一把撒过去" —— 有的先到有的后到，命中的节奏才是碎的。
       重投抖得最少：它有预警，观众在等那一下，节奏不该飘。 */
    static float JitOf(string st) => st == "volley" ? 0.34f : st == "single" ? 0.26f : 0.10f;

    static float ClampY(float y) => y < 300f ? 300f : y > 960f ? 960f : y;

    // ---------- 发射 ----------

    /* g 是 Shop.G 表里的一行。fixedY 传 NaN 表示随机高度；给定值只用于诊断
       胶片 —— 随机高度会让每次截出来的图不一样，没法比。 */
    public static void Launch(GiftDef g, float fixedY, LaunchOpt o) {
        if (lockT > 0f && !o.exec) {
            if (o.gift && pending.Count < 6) pending.Add(new Pd { g = g, y = fixedY, o = o });
            return;
        }
        bool hasY = !float.IsNaN(fixedY);
        float y0 = hasY ? fixedY : 380f + Rnd() * 520f;

        if (o.exec) {
            /* 处决：预警拉长到半秒多，让全场先看见它要来；体积按 1.8 倍压过来。
               这是"倾倒式"演出的简化实现 —— 等美术到位再换成真正的倾泻，
               飞行逻辑一行都不用改。 */
            warns.Add(new Wn { from = g.from, y = y0, t = 0.55f, max = 0.55f });
            queue.Add(new Qi { t = 0.55f, g = g, y = y0, exec = true });
            return;
        }
        /* 常规火力：一发就是一发，不走连珠那一串。
           高度要避开两个人的脸（520~610）—— 火力弹幕是连绵不断的，糊在脸上的话
           整局都看不清表情，而表情是这个玩法仅有的两个可读信息之一。礼物弹幕是
           孤立事件，遮一下无妨；常态的那一路不行。 */
        if (o.one) {
            float yy = hasY ? fixedY
                     : (Rnd() < 0.45f ? 366f + Rnd() * 140f      // 脸以上：沙发靠背那一带
                                      : 636f + Rnd() * 300f);    // 脸以下：手和腿那一带
            queue.Add(new Qi { t = 0f, g = g, y = ClampY(yy), clash = o.clash });
            return;
        }
        if (g.style == "volley") {
            // 连珠：排成一串，间隔 70ms。每一颗单独判定、单独触发一次小命中，
            // 读起来是"哒哒哒"一串轻击而不是一下
            int n = g.n > 0 ? g.n : 8;
            for (int i = 0; i < n; i++)
                queue.Add(new Qi { t = i * 0.07f, g = g, y = ClampY(y0 + (Rnd() - 0.5f) * 170f) });
        } else if (g.style == "heavy") {
            /* 重投先预警再发射。大礼物的价值一半在"全场都看见有人刷了大的" ——
               冲进来之前得先让观众知道它要来，否则一个大件突然出现在画面里，
               观众只来得及看到它已经砸上了。 */
            warns.Add(new Wn { from = g.from, y = y0, t = 0.25f, max = 0.25f });
            queue.Add(new Qi { t = 0.25f, g = g, y = y0 });
        } else {
            queue.Add(new Qi { t = 0f, g = g, y = y0 });
        }
    }

    static void Fire(Qi q) {
        /* 池满了就回收最老的那一发，而不是把新的丢掉。原来是直接 return ——
           观众刷了礼物，屏幕上却什么也没飞出来，这是连点时最糟糕的一种反馈。
           act 按发射顺序排，队头那一发飞得最久、离对抗线最近，让它提前退场的
           代价远小于让刚刷的这一件凭空消失。 */
        if (act.Count >= MAX) { pool.Push(act[0]); act.RemoveAt(0); }
        var g = q.g;
        float sp = SpeedOf(g.style);
        var p = pool.Count > 0 ? pool.Pop() : new Bolt();
        p.g = g; p.item = g.item; p.r = q.exec ? g.r * 1.8f : g.r; p.y = q.y;
        p.from = g.from;
        p.exec = q.exec;
        p.clash = q.clash;
        // 对撞点落在自己这一侧一点，错开一些 —— 全撞在同一条线上会读成一堵墙
        p.clashOff = 20f + Rnd() * 90f;
        p.x = g.from > 0 ? -g.r - 40f : K.W + g.r + 40f;
        p.vx = g.from * sp * (1f + (Rnd() - 0.5f) * 2f * JitOf(g.style));
        p.ax = AccOf(g.style);
        /* 处决压过来的速度只有一半。它买的是"一段没人打断的时间" —— 嗖一下飞
           过去就把这段时间还回去了。慢，才有"全场都看着它过来"。

           ⚠ 网页版这里还有一句 `p.vrot *= 0.5`，但它写在 vrot 被赋值**之前**，
           乘的是上一次复用这个对象时残留的值，随即被下面整个覆盖 —— 那句是
           死代码，"处决转得慢一半"这个意图在网页版上从来没生效过。这里照
           **实际行为**搬（不减半），两边才是一致的；要让它生效得先改网页版。 */
        if (p.exec) { p.vx *= 0.5f; p.ax *= 0.35f; }
        p.rot = Rnd() * 6.283f;
        /* 转速。矢量物品转的是一张平面图，快了只会晃眼；贴图物品转的是真的转盘，
           **必须在飞行途中转够一圈以上**，观众才看得出它是个有厚度的东西。
           重投飞完全程约 0.65 秒，给 14 rad/s 差不多是一圈半。
           符号仍随机：顺着翻和倒着翻都是合理的姿势。 */
        p.vrot = g.spin != 0f
            ? (Rnd() < 0.5f ? -1f : 1f) * g.spin * (0.85f + Rnd() * 0.3f)
            : (Rnd() - 0.5f) * (g.style == "volley" ? 26f : g.style == "single" ? 13f : 4.5f);
        if (p.hx == null) { p.hx = new float[TRAIL]; p.hr = new float[TRAIL]; }
        for (int i = 0; i < TRAIL; i++) { p.hx[i] = p.x; p.hr[i] = p.rot; }
        p.hi = 0;
        p.x0 = p.x;
        p.near = 0f;
        act.Add(p);
    }

    // ---------- 推进 ----------

    public static void Update(float dt) {
        if (lockT > 0f) {
            lockT -= dt;
            if (lockT <= 0f) {
                lockT = 0f;
                while (pending.Count > 0) {
                    var pd = pending[0]; pending.RemoveAt(0);
                    Launch(pd.g, pd.y, pd.o);
                }
            }
        }
        for (int i = queue.Count - 1; i >= 0; i--) {
            var q = queue[i];
            q.t -= dt;
            if (q.t <= 0f) { queue.RemoveAt(i); Fire(q); }
            else queue[i] = q;
        }
        for (int i = warns.Count - 1; i >= 0; i--) {
            var w = warns[i];
            w.t -= dt;
            if (w.t <= 0f) warns.RemoveAt(i); else warns[i] = w;
        }
        for (int i = act.Count - 1; i >= 0; i--) {
            var p = act[i];
            if (p.ax != 0f) p.vx += p.from * p.ax * dt;
            p.x += p.vx * dt;
            p.rot += p.vrot * dt;
            /* 重投在途中让画面一直轻轻发抖。每帧加的这一点点会被 0.86/帧 的衰减
               吃掉，稳态就在 1px 上下 —— 不是震动，是压迫感。 */
            if (p.g.style == "heavy") Particles.AddShake(0.15f);

            p.hi = (p.hi + 1) & TMASK;
            p.hx[p.hi] = p.x; p.hr[p.hi] = p.rot;

            float fx = Director.FrontAt(p.y);
            /* 走完了全程的多少。色晕靠它在命中前一路烧起来 —— 观众在撞上之前就
               知道这一发要到了，而这正是弹幕能制造期待的唯一窗口。 */
            float span = fx - p.x0;
            p.near = span == 0f ? 1f : MathX.Clamp01((p.x - p.x0) / span);

            if (p.clash) {
                // 对冲掉的那些飞不到人身上，在中线前撞掉
                float cx = fx - p.from * p.clashOff;
                if (p.from > 0 ? p.x >= cx : p.x <= cx) {
                    act.RemoveAt(i); pool.Push(p);
                    /* 对冲掉的那些在中线互相撞掉：粒子照爆，但不推角色、不染色、
                       不顿帧。它要回答的问题只有一个 —— "我刷了礼物怎么手机没动"。
                       答案就在画面上：你的东西被对面在半空撞掉了。 */
                    RECIPE.Get(p.g.recipe).burst(cx, p.y, p.from, 0.42f);
                    Particles.AddShake(0.6f);
                    continue;
                }
            }
            if (p.from > 0 ? p.x >= fx : p.x <= fx) {
                act.RemoveAt(i); pool.Push(p);
                /* 独占窗口从**命中那一刻**才开始。原来写在 Launch 里，可处决要飞
                   一秒半才落地 —— 等它真砸上的时候窗口早过期了，而飞行途中反倒
                   把常规火力全丢光，画面空成一片。"全世界停下来看这一击"说的是
                   这一击落地之后。 */
                if (p.exec) lockT = 0.8f;
                /* 命中只负责演出，**不改进度**。进度是双方火力净差积分出来的（见
                   Director.Battle）—— 让命中再推一次，等于同一份伤害算两遍，而且
                   会把"两边都在刷时手机不动"这条最要紧的手感破坏掉。弹幕是火力
                   的表现形式，不是伤害的来源。 */
                RECIPE.Impact(-p.from, p.y, p.exec ? 4 : p.g.power, p.g.recipe);
            } else if (p.x < -400f || p.x > K.W + 400f) {
                // 对抗线被推到极端位置时弹幕可能追不上，别让它永远飞下去
                act.RemoveAt(i); pool.Push(p);
            }
        }
    }

    public static void Clear() {
        while (act.Count > 0) { pool.Push(act[act.Count - 1]); act.RemoveAt(act.Count - 1); }
        queue.Clear(); warns.Clear(); pending.Clear(); lockT = 0f;
    }

    // ---------- 绘制 ----------

    /* 一发弹幕"拖多长"。
       取尺寸和速度里更大的那个：大件按自己的身长拖（否则拖尾埋在本体底下），
       快件按速度拖（一颗小发卡飞得再快，只按身长拖也读不出快）。两条各自都
       会在另一头失效，所以是 max 而不是二选一。 */
    static float ReachOf(Bolt p) => Mathf.Max(p.r * 2.6f, Mathf.Abs(p.vx) * 0.09f);

    /* 沿轨迹往回找距离本体 dist 像素的那一点。
       残影和拖尾都按**距离**回溯，不按帧数 —— 按帧数的话同样是四个残影，
       发卡（1400px/s、半宽 22）能拖出四个身长，被子（850px/s、半宽 78）四帧
       只退 57 像素，全糊在本体上。眼睛读的是拖了多长，那是距离不是时间。 */
    static int BackAt(Bolt p, float dist) {
        for (int k = 1; k < TRAIL; k++) {
            int idx = (p.hi + TRAIL - k) & TMASK;
            if (Mathf.Abs(p.x - p.hx[idx]) >= dist) return idx;
        }
        return (p.hi + 1) & TMASK;
    }

    static Color A(Color c, float a) => new Color(c.R, c.G, c.B, a);

    /// 预警箭头：贴着发射方的屏幕边缘，闪两下
    public static void DrawWarns(Draw2D d) {
        foreach (var w in warns) {
            float k = w.t / w.max;
            float blink = 0.5f + Mathf.Abs(Mathf.Sin(k * 18f)) * 0.5f;
            float x = w.from > 0 ? 58f : K.W - 58f;
            var col = w.from > 0 ? K.GREEN : K.RED;
            // 越接近发射越大，读起来是"它正在逼近"
            float sc = 1.1f + (1f - k) * 0.7f;
            d.Push(x, w.y, 0f, w.from * sc, sc);
            // 三重箭头，越靠后越淡：一个静止的三角读不出方向，一串才读得出"来了"
            for (int j = 0; j < 3; j++) {
                float al = blink * (1f - j * 0.3f);
                float ox = -j * 26f;
                var a0 = new Vector2(ox + 26f, 0f);
                var a1 = new Vector2(ox - 12f, -25f);
                var a2 = new Vector2(ox - 12f, 25f);
                d.Tri3(a0, a1, a2, A(col, al));
                var line = MathX.C255(20, 16, 22, 0.8f * al);
                d.Segment(a0, a1, 4.5f, line);
                d.Segment(a1, a2, 4.5f, line);
                d.Segment(a2, a0, 4.5f, line);
            }
            d.Pop();
        }
    }

    /// 色晕、拖尾、亮芯、矢量残影与矢量本体 —— 全部走顶点色批
    public static void DrawMesh(Draw2D d) {
        for (int i = 0; i < act.Count; i++) {
            var p = act[i];
            var au = AmmoArt.AuraOf(p.item);
            var tl = AmmoArt.TailOf(p.item);
            /* 这一发拖多长。色晕、拖尾、残影全按它摊开，所以三层永远是对齐的 ——
               分别写死各自的长度，快件和大件总有一头对不上。 */
            float reach = ReachOf(p);
            float tail = p.hx[BackAt(p, reach)];

            /* ① 弹道色晕：沿真实轨迹摆三团，越靠后越小越淡。
               光要铺满整条路径 —— 只挂在本体上的话弹道本身是暗的，读出来是"一个
               发光的东西在移动"，而不是"它带着一道光在走"。
               只比本体大半圈：色晕一大就把物品洗白了。它该是物体边缘的一圈光，
               不是一团雾。越接近对抗线越浓：命中前的最后一段自己会烧起来。 */
            float gk = 0.34f + p.near * 0.40f;
            for (int k = 2; k >= 0; k--) {
                int idx = k != 0 ? BackAt(p, reach * k * 0.33f) : p.hi;
                float f = 1f - k * 0.19f;
                float al = gk * f * f;
                float gw = p.r * 1.55f * f, gh = p.r * 1.18f * f;
                d.Radial(p.hx[idx], p.y, gw, gh,
                         A(au, 0.92f * al), 0.34f, A(au, 0.56f * al),
                         0.68f, A(au, 0.18f * al), 1f, A(au, 0f), 16);
            }

            /* ② 拖尾带：从轨迹上 reach 那么远的一点收拢到本体。用暗调而不是亮色
               —— 明亮客厅底图上浅色线几乎看不见，跟粒子配色是同一条规矩：靠轮廓
               不靠亮度。长度是这一发**实际飞过**的距离，所以速度抖动一上来就看得
               出谁快谁慢。 */
            var tc = A(tl, 0.30f);
            d.QuadPts(new Vector2(tail, p.y - p.r * 0.06f),
                      new Vector2(p.x, p.y - p.r * 0.5f),
                      new Vector2(p.x, p.y + p.r * 0.5f),
                      new Vector2(tail, p.y + p.r * 0.06f), tc, tc, tc, tc);

            /* ③ 弹道亮芯：压在暗拖尾中间的一条细楔子。两条都要 —— 只有暗的读成
               一道划痕，只有亮的在浅底图上又浮不起来；暗的给实体感，亮的给能量感。 */
            d.Tri3(new Vector2(tail, p.y),
                   new Vector2(p.x, p.y - p.r * 0.26f),
                   new Vector2(p.x, p.y + p.r * 0.26f), A(au, 0.48f));

            /* ④ 残影：在它身后 reach 的四分之一、二分之一…处，把**外轮廓**再画
               一遍，越远越淡越小。只画轮廓不画内部细节 —— 缝线、按键、格纹在高速
               移动里糊成噪点，而且四个残影就是四遍完整物品，那是这个文件里最贵的
               一笔开销。

               用拖尾那个暗调，不用色晕色。残影是**物体的形状**，跟色晕、亮芯不是
               一回事 —— 三层全用同一个高饱和色的话，它们会融成一条实色带，形状
               就没了。发光的靠色相，实体的靠轮廓，这条规矩在这里同样成立。

               间距按距离均分，所以残影之间永远接得上：间距大于物体宽度的话读出来
               是"一串独立的小东西"，而不是"一个东西拖出来的影"。 */
            if (Sprites.Of(p.item) != null) continue;      // 贴图那一路在 DrawSprites 里
            for (int k = 4; k >= 1; k--) {
                int idx = BackAt(p, reach * k / 4f);
                float al = 0.40f - k * 0.068f;
                float sc = 1f - k * 0.05f;
                d.Push(p.hx[idx], p.y, p.hr[idx], sc);
                AmmoArt.Silh(d, p.item, p.r, A(tl, al));
                d.Pop();
            }

            d.Push(p.x, p.y, p.rot);
            AmmoArt.Draw(d, p.item, p.r);
            d.Pop();
        }
    }

    /// 转盘贴图那一路：残影与本体
    public static void DrawSprites(CanvasItem ci) {
        for (int i = 0; i < act.Count; i++) {
            var p = act[i];
            var sh = Sprites.Of(p.item);
            if (sh == null) continue;
            float reach = ReachOf(p);

            /* 残影数量和浓度分两套。矢量物品的剪影是**刻意简化过的**轮廓（花束就是
               七个圆），四个叠起来仍读作"影子"；贴图剪影带着全部细节（每片花瓣、
               叶子、包装纸），同样画四个、同样的浓度，糊出来是一大团暗红，比本体
               还抢眼，读成"另一个物体"而不是它的轨迹。
               贴图本身转盘就带足了运动信息，两个淡影够了。 */
            if (sh.silh != null)
                for (int k = 2; k >= 1; k--) {
                    int idx = BackAt(p, reach * k / 2f);
                    float al = (0.40f - k * 0.068f) * 0.5f;
                    float sc = 1f - k * 0.05f;
                    /* 残影取**那一刻的朝向**对应的格子，不是当前朝向 —— 用当前朝向
                       的话两个残影会是同一个姿势，读成"复制粘贴"而不是"它飞过来的
                       轨迹"。这跟 hr[] 记录历史旋转角是同一个用意。 */
                    float dd = p.r * sh.scale * 2f * sc;
                    ci.DrawTextureRectRegion(sh.silh,
                        new Rect2(p.hx[idx] - dd / 2f, p.y - dd / 2f, dd, dd),
                        Sprites.Src(sh, Sprites.CellOf(sh, p.hr[idx])),
                        new Color(1f, 1f, 1f, al));
                }

            /* 贴图本体：**不转画布**。转盘序列里每一格自己就是那个角度渲好的，
               再叠一次 2D 旋转就成了"又翻又转"，而且光影会跟着一起转，那正是
               换 3D 要解决的问题。 */
            float d0 = p.r * sh.scale * 2f;
            ci.DrawTextureRectRegion(sh.img,
                new Rect2(p.x - d0 / 2f, p.y - d0 / 2f, d0, d0),
                Sprites.Src(sh, Sprites.CellOf(sh, p.rot)));
        }
    }
}

/* 弹幕的两块画布。
   矢量那一路走顶点色批（一次提交），贴图那一路只能逐张贴 —— 所以分成两层，
   贴图层压在矢量层上面。当前只有玫瑰花束是贴图，跨物品的前后遮挡关系无所谓：
   它们各飞各的高度。 */
public partial class AmmoView : Node2D {
    MeshLayer mesh;
    SpriteLayer spr;

    public void Init(Node parent, int z, bool noSprite) {
        parent.AddChild(this);
        ZIndex = z;
        mesh = Gfx.NewLayer(this, "ammo_mesh", false, 0);
        spr = new SpriteLayer();
        spr.Setup(this, "ammo_spr", false, 1);
        Sprites.Load(noSprite);
    }

    public void Rebuild() {
        var d = mesh.D;
        d.Clear();
        Ammo.DrawWarns(d);
        Ammo.DrawMesh(d);
        mesh.Commit();
        spr.QueueRedraw();
    }

    public partial class SpriteLayer : Node2D {
        public override void _Draw() => Ammo.DrawSprites(this);
    }
}
}
