using Godot;

namespace Chashouji {

/* 玩法主循环。
   battle 算"手机该往哪走"，derive 算"算出来之后画面长什么样"。分成两个函数是
   因为它们回答的是两个问题，而把它们混在一起正是上一版最大的错误：礼物直接改
   了 S.p，等于把火力这一层整个删掉了。 */
public partial class Director : Node2D {
    BgView bg;
    GroundView ground;
    FrameView frames;
    FrontMarkView front;
    RulerView ruler;
    HudView hud;

    public override void _Ready() {
        /* 层序（ZIndex）。背景在最底，角色压着地面辉光，指针在最上面 ——
           它是这个玩法唯一永远不能被挡住的读数。 */
        bg     = new BgView();        bg.Setup(this, "bg", false, -100);
        ground = new GroundView(this, -20);
        frames = new FrameView();     frames.Init(this, 0);
        front  = new FrontMarkView(); front.Init(this, 20, 60);
        ruler  = new RulerView(this, 40);
        hud    = new HudView(this, 80);
        StartMatch();
    }

    public static void StartMatch() {
        S.p = 50f; S.fA = S.fB = 0f; S.budA = S.budB = 0f;
        S.debA = S.debB = S.debKA = S.debKB = 0f;
        S.clock = NUM.MATCH; S.phase = Phase.Play;
        S.edge = S.big = S.sudden = S.stand = 0f; S.standUsed = false; S.winner = 0;
        S.auto = false;
    }

    public override void _Process(double dtd) {
        float dt = (float)dtd;
        S.t += dt;
        Battle(dt);
        Derive(dt);

        float bias = (S.p - 50f) / 50f;
        ground.Rebuild(bias);
        frames.Rebuild(S.p, FX.actorX, FX.punch, FX.tint, FX.tintA);
        front.Rebuild(bias, S.line);
        ruler.Rebuild();
        hud.Rebuild(S.p);
    }

    // ---------- 数值层：算出 S.p ----------

    static void Battle(float dt) {
        if (S.phase != Phase.Play && S.phase != Phase.Sudden) return;

        // 减益倒计时。它作用在**注入**上（见 GiveGift），不作用在手机上
        if (S.debA > 0f && (S.debA -= dt) <= 0f) S.debKA = 0f;
        if (S.debB > 0f && (S.debB -= dt) <= 0f) S.debKB = 0f;
        if (S.stand > 0f) S.stand -= dt;

        /* 对冲：双方等量消耗，速度由火力**少**的一方决定。
           这一条是整个模型的关键，它自带一根橡皮筋 —— 劣势方火力少，所以流失慢、
           补起来划算；而优势方想维持差距就得一直喂。不必另外再加"劣势方 x1.5"
           那种补丁，橡皮筋是从机制里长出来的。 */
        float lo = Mathf.Min(S.fA, S.fB);
        float burn = NUM.BURN * lo;
        float useA = (burn + NUM.LOSS * S.fA) * dt;
        float useB = (burn + NUM.LOSS * S.fB) * dt;
        S.fA = Mathf.Max(0f, S.fA - useA);
        S.fB = Mathf.Max(0f, S.fB - useB);

        /* 打出去的火力就是屏幕上的弹幕。消耗多少就打多少发 —— 于是"对冲掉的那
           部分"和"穿过去的那部分"在画面上是分开的：前者在中线撞掉，后者才砸到
           人身上。观众刷了礼物手机没动时，屏幕上有答案：你的东西被对面在半空
           撞掉了。 */
        float denA = burn + NUM.LOSS * S.fA, denB = burn + NUM.LOSS * S.fB;
        S.budA += useA; S.budB += useB;
        EmitFire(+1, denA > 0f ? burn / denA : 0f);
        EmitFire(-1, denB > 0f ? burn / denB : 0f);

        // 只有净差值才动手机
        float diff = S.fA - S.fB;
        float dmg = (diff / 1000f) * NUM.DPS * dt;

        /* 濒死护盾：快被推到头时，火力越足越扛得住。刷礼物能直接保命，而且看得
           见：火力条长就是在替你挡。

           ⚠ 它**不能去扣火力存量**。试过那种写法，结果是一条正反馈：护盾吃掉劣
           势方的火力 → min 变小 → 对冲跟着变弱 → 优势方的火力不再被烧掉 → 差值
           反而越拉越大。实测净差冲到理论值（Δ注入/LOSS）的 2.4 倍，越接近终点崩
           得越快。根因是火力同时担着两个职责：它既是护盾的燃料，又是对冲的输入，
           扣一处动两处。所以护盾只能按**比例**减伤，不碰存量。 */
        if (dmg != 0f) {
            int losing = dmg > 0f ? -1 : +1;                  // 正在挨打的一方
            float room = losing > 0 ? S.p : 100f - S.p;       // 他离输还有多远
            if (room < NUM.SHIELD_AT) {
                float mine = losing > 0 ? S.fA : S.fB, his = losing > 0 ? S.fB : S.fA;
                float ratio = mine / (his + 1f);
                dmg *= 1f - Mathf.Min(NUM.SHIELD_MAX, ratio * 1.5f);
            }
        }
        float cap = NUM.MAXDPS * dt;
        if (dmg > cap) dmg = cap; else if (dmg < -cap) dmg = -cap;
        S.p = MathX.Clamp(S.p + dmg, 0f, 100f);

        /* 反击时刻：第一次被推到最后 8 个百分点时，劣势方注入翻倍两分钟，全局只
           触发一次。放大的是注入不是伤害 —— 在两层模型里，"更有力"只能是更多火力。 */
        if (!S.standUsed && (S.p < NUM.STAND_AT || S.p > 100f - NUM.STAND_AT)) {
            S.standUsed = true; S.stand = NUM.STAND;
        }

        // 绝杀：一直被压着就别耗了，给 30 秒最后的机会
        float lead = Mathf.Abs(S.p - 50f) * 2f;
        if (S.phase == Phase.Play) {
            S.big = lead >= NUM.SUDDEN_LEAD * 2f ? S.big + dt : 0f;
            if (S.big >= NUM.SUDDEN_WAIT) { S.phase = Phase.Sudden; S.sudden = NUM.SUDDEN; }
        } else if ((S.sudden -= dt) <= 0f) { Finish(S.p > 50f ? +1 : -1); return; }

        /* 推到端点还得按住三秒。没有这一条，一次爆发擦过端点就结束比赛，前十分钟
           全部作废 —— 观众读到的是"输得莫名其妙"，不是"输得精彩"。 */
        if (S.p >= 100f || S.p <= 0f) {
            S.edge += dt;
            if (S.edge >= NUM.EDGE_HOLD) { Finish(S.p >= 100f ? +1 : -1); return; }
        } else S.edge = 0f;

        if ((S.clock -= dt) <= 0f) Finish(S.p > 53f ? +1 : S.p < 47f ? -1 : 0);
    }

    static void Finish(int who) { S.phase = Phase.Over; S.winner = who; }

    /* 火力转成弹幕。clash 的那些飞到中线就互相撞掉，只有剩下的才砸到人身上 ——
       这是"对冲"唯一的可视化，没有它观众看不懂自己刷的东西去哪了。 */
    static void EmitFire(int side, float clashRatio) {
        int n = 0;
        if (side > 0) { while (S.budA >= NUM.SHOT && n < 3) { S.budA -= NUM.SHOT; n++; } }
        else          { while (S.budB >= NUM.SHOT && n < 3) { S.budB -= NUM.SHOT; n++; } }
        // 弹幕发射在 ammo.js 搬过来之后接上；数值层的消耗与节流已经是完整的
        _ = clashRatio; _ = n;
    }

    /// 送一件礼物。数值走火力，表现走弹幕 —— 两件事同一个入口，但不是同一层。
    public static void GiveGift(int side, string key) {
        if (!Shop.T.TryGetValue(key, out var it)) return;
        float deb = side > 0 ? S.debKA : S.debKB;
        int loser = S.p < 50f ? +1 : -1;                 // 谁正落后
        float boost = (S.stand > 0f && side == loser) ? 2f : 1f;
        float amt = it.push * (1f - deb) * boost;
        if (side > 0) S.fA += amt; else S.fB += amt;

        /* 高档礼物的第二维度：压制。光靠 push 拉开差距会逼出很难看的数值，而
           "让对方刷的每一件都打折"才是贵真正买到的东西。 */
        if (it.tier == 3) HexDebuff(-side, 0.30f, 5f);
        if (it.tier == 4) {
            HexDebuff(-side, 0.50f, 8f);
            // 直接削存量是唯一能瞬间改变差值的手段，也是翻盘的唯一来源
            if (side > 0) S.fB *= 0.5f; else S.fA *= 0.5f;
        }
    }

    static void HexDebuff(int side, float k, float sec) {
        if (side > 0) { S.debKA = Mathf.Max(S.debKA, k); S.debA = Mathf.Max(S.debA, sec); }
        else { S.debKB = Mathf.Max(S.debKB, k); S.debB = Mathf.Max(S.debB, sec); }
    }

    // ---------- 表现层：由 S.p 派生画面 ----------

    static readonly float[] nim = new float[K.ROWS];
    static readonly float[] next = new float[K.ROWS];

    static void Derive(float dt) {
        float bias = (S.p - 50f) / 50f;
        // p 大 = 查岗党(女方,在左)占优 = 手机被拽向左
        float target = K.MID - Mathf.Sign(bias) * Mathf.Pow(Mathf.Abs(bias), P.curve) * P.half;
        FX.phoneX += (target - FX.phoneX) * MathX.Approach(dt, 4.2f);

        FX.struggle = 1f - Mathf.Abs(bias) * 0.78f;       // 僵持度：五五开时最高
        FX.jit = Mathf.Sin(S.t * 47f) * 2.4f * FX.struggle;
        FX.phoneY = P.phoneY + Mathf.Sin(S.t * 9.3f) * 6f * FX.struggle - Mathf.Abs(bias) * 14f;

        /* 角色被推开又站回来：弹簧-阻尼，不是单纯衰减 —— 单纯衰减只有"飘回去"，
           看不出"被推动了"。挨一下给的是速度不是位移。 */
        FX.hitV += -FX.hitX * P.hitK * dt;
        FX.hitV *= Mathf.Pow(P.hitDamp, dt * 60f);
        FX.hitX += FX.hitV * dt;
        FX.actorX = (FX.phoneX - K.MID) * P.drag + FX.hitX;

        FX.punch *= Mathf.Pow(P.punchDecay, dt * 60f);
        if (FX.punch < 0.002f) FX.punch = 0f;
        FX.tintA *= Mathf.Pow(P.tintDecay, dt * 60f);
        if (FX.tintA < 0.004f) FX.tintA = 0f;

        /* 对抗线上的冲击波：命中那一行注入冲量，随后沿线上下传播并衰减。它与
           rowOff 分开演化、最后一起读 —— rowOff 管"谁在推"（慢、由 p 决定），
           冲量管"刚刚挨了一下"（快、由事件决定）。混在一个数组里的话，一次命中
           会被 shapeRate 的趋近吃掉大半，读不出撞击。 */
        var im = FX.rowImp;
        for (int r = 0; r < K.ROWS; r++) {
            float nb = ((r > 0 ? im[r - 1] : im[r]) + (r < K.ROWS - 1 ? im[r + 1] : im[r])) / 2f;
            nim[r] = (im[r] + (nb - im[r]) * MathX.Approach(dt, P.waveSpread)) * Mathf.Pow(P.waveDecay, dt * 60f);
        }
        for (int r = 0; r < K.ROWS; r++) im[r] = Mathf.Abs(nim[r]) < 0.05f ? 0f : nim[r];

        var o = FX.rowOff;
        for (int r = 0; r < K.ROWS; r++) {
            float d = (float)r / (K.ROWS - 1);
            float tilt = (0.5f - d) * 2f * bias * P.tilt;
            float wob = Mathf.Sin(S.t * 0.41f + r * 0.78f) * 0.62f + Mathf.Sin(S.t * 0.83f + r * 1.7f) * 0.31f;
            float desire = (tilt * 0.62f + wob * 0.42f) * P.bulge;
            float nb = ((r > 0 ? o[r - 1] : o[r]) + (r < K.ROWS - 1 ? o[r + 1] : o[r])) / 2f;
            float goal = (desire + P.linkW * nb) / (1f + P.linkW);
            next[r] = o[r] + (goal - o[r]) * MathX.Approach(dt, P.shapeRate);
        }
        for (int r = 0; r < K.ROWS; r++) o[r] = MathX.Clamp(next[r], -110f, 110f);

        float hot = 1f - Mathf.Abs(bias) * 0.42f;
        for (int r = 0; r < K.ROWS; r++) {
            float d = (float)r / (K.ROWS - 1);
            float g = Mathf.Exp(-Mathf.Pow((d - 0.36f) / 0.44f, 2f));
            FX.rowHeat[r] += (MathX.Clamp(g * 1.3f * hot, 0f, 1f) - FX.rowHeat[r]) * MathX.Approach(dt, 3.4f);
        }
    }

    // ---------- 几何派生：全画面唯一的横坐标来源 ----------

    /* Catmull-Rom 采样。逐行折线会让对抗线在行与行之间出现折角，而它是一条
       被两个人拽着的软线，折角读起来像铁丝。 */
    static float SampleRow(float[] arr, float y) {
        float d = MathX.Clamp((y - K.TOP) / (K.BOT - K.TOP), 0f, 1f) * (K.ROWS - 1);
        int i = (int)MathX.Clamp(Mathf.Floor(d), 0f, K.ROWS - 2);
        float f = d - i;
        float p0 = arr[Mathf.Max(0, i - 1)], p1 = arr[i], p2 = arr[i + 1], p3 = arr[Mathf.Min(K.ROWS - 1, i + 2)];
        return p1 + 0.5f * f * (p2 - p0 + f * (2f * p0 - 5f * p1 + 4f * p2 - p3 + f * (3f * (p1 - p2) + p3 - p0)));
    }

    /// 对抗线在高度 y 处的横坐标 —— 手机、光柱、刻度、地面分色全读这一个函数
    public static float FrontAt(float y) => FX.phoneX + SampleRow(FX.rowOff, y) + SampleRow(FX.rowImp, y);
    public static float HeatAt(float y) => MathX.Clamp(SampleRow(FX.rowHeat, y), 0f, 1f);
}
}
