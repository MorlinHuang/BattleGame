namespace Chashouji {

/* FxTuning.cs —— 特效与弹幕的全部可调数值，集中在这一个文件里。
 * ============================================================================
 * 想改什么，看这张表：
 *
 *   弹幕飞多快 / 转多快 / 速度差多大 / 预警多久 / 拖尾多长
 *       → 本文件 Ammo 区
 *   某件礼物是谁扔的、多大、走哪种样式、推多少进度、命中炸出什么
 *       → 本文件 GIFTS 表（每件礼物一行）
 *   物品长什么样（发卡、抱枕、外卖箱的画法）
 *       → AmmoView.cs 的 ITEM 区，一件一个方法，纯几何绘制
 *   命中炸出什么粒子（羽毛 / 星星 / 碎屑 / 通用撞击）
 *       → Recipes.cs，一个配方一个方法
 *   顿帧多久、屏幕震多狠、白闪多亮、角色被推多远
 *       → 本文件 Hit 区
 *   粒子本身怎么画（六种形态的绘制）
 *       → ParticleFx.cs，一般不需要动
 *
 * 改数值不用改任何别的文件。网页版是这套东西的单一真源，参数名与
 * web/ammo.js、web/fx.js、web/main.js 一一对应，两边对着调。
 * ============================================================================ */
public static class FxTuning {

    // ======================== Hit 区：挨一下之后发生什么 ========================

    /* 顿帧：命中瞬间把游戏逻辑冻住多少秒。打击感有一半来自这个。
       给太长会让画面像卡顿而不是定格，大礼物 0.11 已经接近上限。
       注意冻的只是逻辑（角色姿态、对抗线、进度、弹幕），粒子照真实时间走 ——
       火花正是这一下的可视化，跟着一起冻的话爆炸会迟到一百毫秒。 */
    public const float HitStopSmall = 0.035f;   // 点赞级
    public const float HitStopMid   = 0.07f;    // 普通礼物
    public const float HitStopBig   = 0.11f;    // 大礼物

    /* 全屏白闪的强度 0~1。
       ⚠ 这个值的上限是底图亮度定的，不是美术偏好：底图是明亮客厅（浅绿墙、
       米色地板），0.3 往上人物就开始糊成一片白。暗色底图的玩法能给到 0.5。 */
    public const float FlashSmall = 0.03f;
    public const float FlashMid   = 0.10f;
    public const float FlashBig   = 0.22f;

    /// 屏幕震动的注入量（像素），会按礼物档位再乘一个倍率
    public const float ShakePerHit = 7f;
    /// 震动每帧的留存率，越大晃得越久。0.86 约等于四分之一秒收住
    public const float ShakeDecay = 0.86f;
    /// 震动幅度上限，防止连续命中把画面晃散
    public const float ShakeMax = 30f;
    /// 白闪每帧的留存率
    public const float FlashDecay = 0.80f;

    /* 命中档位对应的整体强度倍率。五样东西（粒子、冲量、位移、震动、顿帧）
       共用它 —— 分开调的话小礼物会震得比大礼物还狠，而观众读到的"这一下有
       多重"正是它们的合力。 */
    public const float PowerScaleSmall = 0.55f;
    public const float PowerScaleMid   = 1.0f;
    public const float PowerScaleBig   = 1.7f;

    /* 对抗线上的冲击波。它与常规形变（rowOff）分开演化、最后一起读：
       rowOff 管"谁在推"（慢、由进度决定），冲量管"刚刚挨了一下"（快、由事件
       决定）。混进一个数组的话，一次命中会被 shapeRate 的趋近吃掉大半。 */
    public const float WaveSpread = 7.0f;    // 冲量向相邻行传播的速率
    public const float WaveDecay  = 0.945f;  // 每帧留存；再高线会晃到一秒开外
    public const float WaveInject = 40f;     // 命中那一行注入的冲量
    public const float WaveInjectSide = 22f; // 相邻两行各注入多少

    /* 角色被推开又站回来：弹簧-阻尼，不是单纯衰减 —— 单纯衰减只有"飘回去"，
       看不出"被推动了"。挨一下给的是速度不是位移。 */
    public const float HitPush = 320f;   // 一次命中给角色的横向速度
    public const float HitK    = 620f;   // 回中的弹力，越大回得越快越硬
    public const float HitDamp = 0.90f;  // 横向速度每帧的阻尼

    /* 缩放脉冲：角色是预渲染帧、做不了受击变形，所以打击反馈只能来自贴图
       之外。以脚底为锚缩放，人挨了一下会"胀"一下但脚不离地。 */
    public const float PunchAmount = 0.045f;  // 每点强度对应的缩放量
    public const float PunchDecay  = 0.88f;

    /* 命中染色。只是"挨了一下"的提示，不是照明 —— 超过 0.2 角色的线稿和睡衣
       花纹就被洗掉了，而那正是这个玩法唯一能看的东西。 */
    public const float TintAmount = 0.15f;
    public const float TintDecay  = 0.82f;

    // ======================== Ammo 区：弹幕怎么飞 ========================

    /* 三种样式的基准速度，像素/秒。屏幕外到对抗线大约六百像素，所以
       1400 ≈ 0.43 秒到，900 ≈ 0.67 秒，850+加速 ≈ 0.53 秒。

       ⚠ 重投不要为了"显得重"而调慢。620 的时候光飞行就要一秒，加上预警一共
       一秒二，中间那大半秒是匀速滑行、画面上什么也没发生，观众注意力会飘走。
       分量感靠体量、预警和落地那一下，不靠拖时间。 */
    public const float SpeedVolley = 1400f;   // 连珠：一串小件
    public const float SpeedSingle = 900f;    // 单投：单件中等
    public const float SpeedHeavy  = 850f;    // 重投：大件，且是唯一加速的

    /* 重投的加速度，像素/秒²。只有重投加速：匀速的大件在屏幕上就是一路平移，
       拖沓的根子在匀速而不在速度值不够；单纯调快又会丢掉"看清它是什么"的
       那一段。加速两头都要 —— 前段慢得认得出，后段砸得狠。
       设 0 就退回匀速。 */
    public const float AccHeavy = 900f;

    /* 每一发在基准速度上再抖动的幅度（±比例）。整批同速看着像传送带上排好的
       货，而连珠本来该读成"抓一把撒过去"—— 有的先到有的后到，命中的节奏才是
       碎的。重投抖得最少：它有预警，观众在等那一下，节奏不该飘。 */
    public const float JitterVolley = 0.34f;
    public const float JitterSingle = 0.26f;
    public const float JitterHeavy  = 0.10f;

    /* 自转速度上限（弧度/秒，实际取 ±一半的随机值，所以一批里有顺时针也有
       逆时针）。跟着体量走：小东西翻得快，大件转太快就看不出是什么了。 */
    public const float SpinVolley = 26f;
    public const float SpinSingle = 13f;
    public const float SpinHeavy  = 4.5f;

    /// 连珠每颗之间的间隔（秒）。调小 = 更密的"哒哒哒"
    public const float VolleyGap = 0.07f;
    /// 连珠每颗在基准高度上下的散布范围（像素）
    public const float VolleySpreadY = 170f;

    /* 重投的预警时长（秒）：边缘先闪三重箭头，之后物体才冲进来。
       大礼物的价值一半在"全场都看见有人刷了大的"—— 不预警的话一个大件突然
       出现，观众只来得及看到它已经砸上了。 */
    public const float WarnTime = 0.25f;
    /// 重投飞行途中持续注入的震动量，稳态约 1px：不是震动，是压迫感
    public const float HeavyFlyShake = 0.15f;

    /* 发射高度的随机范围（画布坐标，y 向下）。默认覆盖躯干那一段 ——
       全高度随机的话会有打在脚底下和头顶外的。
       约四分之一的概率会从脸部高度经过、短暂挡住人物；想避开就把
       AmmoYMin 调到 620 以下或 AmmoYMax 调到 460 以上分成两段。 */
    public const float AmmoYMin = 380f;
    public const float AmmoYMax = 900f;
    /// 硬边界，连珠散开之后也不许越过
    public const float AmmoYClampMin = 300f;
    public const float AmmoYClampMax = 960f;

    /* 拖尾。画的是这个东西**真正走过**的地方：每发记 8 帧轨迹，拖尾带从第 7
       帧前的位置收拢到本体，残影把最近 4 帧各画一遍。所以拖尾长度不是写死
       的，是这一发实际飞过的距离 —— 速度一抖动就看得出谁快谁慢。 */
    public const int   TrailFrames   = 8;     // 轨迹环形缓冲的长度，必须是 2 的幂
    public const int   TrailBandBack = 7;     // 拖尾带拉到几帧前
    public const int   GhostCount    = 4;     // 残影个数
    public const float TrailBandAlpha = 0.30f;
    public const float GhostAlphaBase = 0.42f; // 第 k 个残影的透明度 = 这个 - k*Step
    public const float GhostAlphaStep = 0.075f;
    public const float GhostShrink    = 0.045f; // 每个残影缩小的比例

    /// 同屏弹幕上限
    public const int AmmoMax = 48;

    // ======================== 礼物表 ========================

    /* 一件礼物 = 谁发的 + 哪种样式 + 什么物品 + 多大 + 命中落哪个配方 + 推多少
       进度。加新礼物就在 GIFTS 里添一行，别的文件都不用动。

       from   +1 = 查岗党（女方，在左，从左往右飞）
              -1 = 灭迹党（男方，在右，从右往左飞）
       style  Volley 连珠 / Single 单投 / Heavy 重投
       item   物品画法的名字，对应 AmmoView.ITEM 里的一个分支
       r      半宽（像素）。画布宽 960，所以 56 的抱枕占屏宽约 12%
       n      连珠的颗数，其它样式忽略
       power  1 点赞级 / 2 普通礼物 / 3 大礼物，决定顿帧白闪震动的档位
       recipe 命中时炸出什么，对应 Recipes.cs 里的一个方法
       gain   命中推多少进度。连珠是每颗都推，八颗合计还不到一次单投 ——
              刷得越久推得越多，但单发永远比不过真金白银的礼物。 */
    public enum Style { Volley, Single, Heavy }
    public enum Item { Hairpin, Seed, Pillow, Gamepad, Quilt, Box }
    public enum Recipe { Thud, Feather, Star, Debris }

    public struct Gift {
        public string key;      // 面板上的按钮名
        public int from;
        public Style style;
        public Item item;
        public float r;
        public int n;
        public int power;
        public Recipe recipe;
        public float gain;
    }

    public static readonly Gift[] GIFTS = {
        // 查岗党（女方，在左）
        new Gift { key = "发卡×8",   from = +1, style = Style.Volley, item = Item.Hairpin, r = 22f, n = 8, power = 1, recipe = Recipe.Star,    gain = 0.8f  },
        new Gift { key = "抱枕",     from = +1, style = Style.Single, item = Item.Pillow,  r = 56f,        power = 2, recipe = Recipe.Feather, gain = 7f    },
        new Gift { key = "整条被子", from = +1, style = Style.Heavy,  item = Item.Quilt,   r = 78f,        power = 3, recipe = Recipe.Feather, gain = 18f   },
        // 灭迹党（男方，在右）
        new Gift { key = "瓜子×8",   from = -1, style = Style.Volley, item = Item.Seed,    r = 21f, n = 8, power = 1, recipe = Recipe.Star,    gain = 0.8f  },
        new Gift { key = "游戏手柄", from = -1, style = Style.Single, item = Item.Gamepad, r = 52f,        power = 2, recipe = Recipe.Debris,  gain = 7f    },
        new Gift { key = "外卖箱",   from = -1, style = Style.Heavy,  item = Item.Box,     r = 74f,        power = 3, recipe = Recipe.Debris,  gain = 18f   },
    };

    // ---------- 按档位取值的小工具，正文里就不用到处写三元了 ----------

    public static float PowerScale(int power) =>
        power <= 1 ? PowerScaleSmall : (power == 2 ? PowerScaleMid : PowerScaleBig);

    public static float HitStop(int power) =>
        power <= 1 ? HitStopSmall : (power == 2 ? HitStopMid : HitStopBig);

    public static float Flash(int power) =>
        power <= 1 ? FlashSmall : (power == 2 ? FlashMid : FlashBig);

    public static float Speed(Style s) =>
        s == Style.Volley ? SpeedVolley : (s == Style.Single ? SpeedSingle : SpeedHeavy);

    public static float Acc(Style s) => s == Style.Heavy ? AccHeavy : 0f;

    public static float Jitter(Style s) =>
        s == Style.Volley ? JitterVolley : (s == Style.Single ? JitterSingle : JitterHeavy);

    public static float Spin(Style s) =>
        s == Style.Volley ? SpinVolley : (s == Style.Single ? SpinSingle : SpinHeavy);
}
}
