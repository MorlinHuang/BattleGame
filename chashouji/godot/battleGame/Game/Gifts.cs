using System.Collections.Generic;
using Godot;

namespace Chashouji {

/* 礼物表。逐字取自网页版 main.js 的 SHOP / ITEM_OF / GIFT。 */
public struct ShopItem {
    public int tier; public float push; public string name;
}

public struct GiftDef {
    public int from;           // +1 查岗党（女方，在左） / -1 灭迹党（男方，在右）
    public string style;       // volley 连珠 / single 单投 / heavy 重投
    public string item;        // 物品画法的名字
    public float r;            // 半宽（像素）
    public int n;              // 连珠的颗数，其它样式忽略
    public float spin;         // 贴图转盘专用的转速（rad/s）；矢量物品为 0
    public int power;          // 1 点赞级 / 2 普通 / 3 大礼物 / 4 顶档
    public string recipe;      // 命中炸出什么
    public float push;         // 被直接发射时的默认注入量（诊断胶片用）
}

public static class Shop {
    public static readonly Dictionary<string, ShopItem> T = new() {
        ["like"]    = new ShopItem { tier = 0, push = 0.01f, name = "点赞" },
        ["six"]     = new ShopItem { tier = 0, push = 0.06f, name = "666" },
        ["wand"]    = new ShopItem { tier = 1, push = 1f,    name = "仙女棒" },
        ["chest"]   = new ShopItem { tier = 1, push = 10f,   name = "技能宝箱" },
        ["mirror"]  = new ShopItem { tier = 2, push = 20f,   name = "魔法镜" },
        ["battery"] = new ShopItem { tier = 2, push = 110f,  name = "能量电池" },
        ["boom"]    = new ShopItem { tier = 3, push = 230f,  name = "爱的爆炸" },
        ["mic"]     = new ShopItem { tier = 3, push = 360f,  name = "派对话筒" },
        ["drop"]    = new ShopItem { tier = 4, push = 600f,  name = "神秘空投" },
    };

    /* tier → 该阵营飞出去的是什么。
       物品方案：**档 1~2 客厅现场 + 档 3~4 甜蜜反击**（见 docs/礼物设计.md 第六节）。
       这不是折中，是两段要的东西本来就不同：档 1~2 一局出现上百次，它的好处恰恰
       是不够特别；档 3~4 一局只有几次，要的是"我没见过"—— 而这个题材里观众最没
       见过的，就是吵到最后砸过来的是一束花。 */
    public static readonly string[] ItemL = { null, "hairpin", "pillow", "bouquet", "ringbox" };
    public static readonly string[] ItemR = { null, "seed", "gamepad", "milktea", "photo" };

    /* 三种样式的差别是节奏与体量，不是物品：
         volley 连珠  一串小件快速飞来，每颗单独命中 —— 也是常规火力用的那一种
         single 单投  单件中等速度，看得清是什么东西
         heavy  重投  先预警再慢慢压过来
       观众不需要认出飞过来的是什么，光看节奏就知道这一发有多重。 */
    public static readonly Dictionary<string, GiftDef> G = new() {
        // 查岗党（女方，在左，from=+1）
        ["hairpin"] = new GiftDef { from = +1, style = "volley", item = "hairpin", r = 22, n = 8, power = 1, recipe = "star",    push = 1 },
        ["pillow"]  = new GiftDef { from = +1, style = "single", item = "pillow",  r = 56,        power = 2, recipe = "feather", push = 20 },
        ["bouquet"] = new GiftDef { from = +1, style = "heavy",  item = "bouquet", r = 76, spin = 14, power = 3, recipe = "petal", push = 230 },
        /* 档 4 的 r 看着不大，是因为 exec 会再乘 1.8：64→115、68→122，占屏宽的
           24% 与 25%。飞行体积负责预告"这一下很重"，兑现在命中那一刻的绽放里
           —— 所以本体不必再大，大的是绽开的东西。 */
        ["ringbox"] = new GiftDef { from = +1, style = "heavy",  item = "ringbox", r = 64,        power = 4, recipe = "bloom",   push = 600 },
        // 灭迹党（男方，在右，from=-1）
        ["seed"]    = new GiftDef { from = -1, style = "volley", item = "seed",    r = 21, n = 8, power = 1, recipe = "star",    push = 1 },
        ["gamepad"] = new GiftDef { from = -1, style = "single", item = "gamepad", r = 52,        power = 2, recipe = "debris",  push = 20 },
        ["milktea"] = new GiftDef { from = -1, style = "heavy",  item = "milktea", r = 72,        power = 3, recipe = "splash",  push = 230 },
        ["photo"]   = new GiftDef { from = -1, style = "heavy",  item = "photo",   r = 68,        power = 4, recipe = "memory",  push = 600 },
    };
}
}
