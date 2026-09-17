using Godot;

namespace Chashouji {

/* 几何常量与参数表 —— 全部逐字取自网页版 web/main.js（单一真源）。
   改数值不用改别的文件；两边对着调。 */
public static class K {
    public const float W = 960f, H = 1334f;
    public const float TOP = 128f, BOT = 1232f, MID = 480f;
    // 帧纹理只覆盖人物那条横带，不是整块画布
    public const int FRAME_TOP = 308, FRAME_W = 960, FRAME_H = 900;
    public const int ROWS = 15;

    public static readonly Color GREEN = MathX.C255(126, 217, 87);
    public static readonly Color RED   = MathX.C255(255, 72, 72);
    public static readonly Color INK   = MathX.C255(58, 44, 38);
    public static readonly Color EMBER = MathX.C255(255, 156, 38);
}

public struct Rug { public float top, bot, tl, tr, bl, br; }

/// 手感参数。对应 main.js 的 P
public static class P {
    public const float curve = 1.55f;   // 进度→位移的非线性，中段慢、末段快
    /* 关键帧本身已经把"谁被拖过去"画进姿态里了，half/drag 管的是在此之上整组
       人物平移多少：中段那几档姿态差别很小，全靠这段平移把"手机正在被拽走"
       读出来；两头则相反 —— 姿态已经够夸张，再平移就该出画了。 */
    public const float half = 108f;     // 对抗线最大偏移
    public const float drag = 0.58f;    // 角色整体跟随对抗线的比例
    public const float tilt = 1.55f, bulge = 46f, linkW = 0.80f, shapeRate = 2.6f;
    public const float phoneY = 560f;   // 对抗线上"手机所在高度"，刻度与辉光的锚

    public static readonly Rug rug = new Rug { top = 738f, bot = 1128f, tl = 88f, tr = 872f, bl = 28f, br = 912f };

    /* 挨一下之后的反应。冲击沿对抗线传播、角色被推开又弹回，两件事各有一套
       参数：线是软的（传得快、留得久），人是硬的（推得动、马上站回来）。 */
    public const float waveSpread = 7.0f;
    public const float waveDecay = 0.945f;
    public const float hitK = 620f;
    public const float hitDamp = 0.90f;
    public const float punchDecay = 0.88f;
    public const float tintDecay = 0.82f;
}

/* 数值参数表 —— 整局的手感全在这十来个数上。
   模型是**两层**的：礼物注入的是"火力"，双方火力互相对冲，**只有净差值**才把
   手机往一边拽。两边火力相等时刷得再凶手机也不动 —— 那正是拔河该有的样子。 */
public static class NUM {
    public const float BURN = 0.05f;   // 对冲系数：双方等量消耗，由火力少的一方定速
    public const float LOSS = 0.012f;  // 自然流失：势头会过去。时间常数 83 秒
    public const float DPS = 0.08f;    // 每 1000 点火力差，每秒把手机推动几个百分点
    public const float SHOT = 9f;      // 每消耗这么多火力打出一发弹幕
    public const float MATCH = 720f;   // 单局 12 分钟
    public const float EDGE_HOLD = 3f; // 推到端点还要按住这么久才算赢
    public const float SUDDEN_LEAD = 35f;
    public const float SUDDEN_WAIT = 60f;
    public const float SUDDEN = 30f;
    public const float STAND_AT = 8f;
    public const float STAND = 120f;
    public const float SHIELD_AT = 10f;
    public const float SHIELD_MAX = 0.75f;
    /* 手机的最大速度。净差是没有上限的 —— 大哥一秒注入 600 而对面只有 80 时，
       净差能到四万，折合 3.5 个百分点每秒，十四秒就从中点推到底。那不叫碾压，
       那叫没有过程。0.85 表示最快也要一分钟才能从中点推到端点。 */
    public const float MAXDPS = 0.85f;
}
}
