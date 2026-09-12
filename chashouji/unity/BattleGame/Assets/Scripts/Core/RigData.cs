using UnityEngine;

namespace Chashouji {

/* 骨骼标定数据，与网页版 rigdata.js 逐字同步。坐标一律归一化 uv（0~1 相对图片
 * 宽高），换素材尺寸不用改数。几个反复踩的坑记在这里，改数前先读：
 *
 *   · 睡衣是七分袖，袖口不是手腕 —— 袖口到腕之间还有一截小臂；
 *   · 张开的手，指尖比手掌中心远得多。手骨的势力范围要罩住整只手，罩不住
 *     的话指尖抢不过覆盖全身的 root 骨，会被钉在原地撕成细针；可也不能靠
 *     一味加粗去罩 —— 两只手在图上只隔着约 80px，加粗到够得着自己指尖的
 *     半径，同时就把另一只手也吃了进去，那只手会被拽着一起走、扯成一团肉。
 *     正解是把骨末端放到张开的五指中间，再用 capT 把势力范围沿骨轴送出去。
 *
 * 只有靠中线那条手臂(armA)分三段做 IK，因为它要够到手机。另一条手臂不参与
 * 动作，就只给一根刚体骨整条跟着躯干走 —— 分段骨只会在每个关节处多出一道
 * 可能撕裂的接缝。
 *
 * core / radius：core 是"实心核"半径，按肢体自身的粗细量，核内一律满权重；
 *   core 到 radius 是过渡带，要落在背景上。过渡带压到肉上，同一条袖子里相邻
 *   两排顶点就会一个跟着手臂走、一个被 root 钉住，中间那条边被拉成拖影。
 * cap:[capH,capT] 势力范围沿骨轴两端的收口倍数，见 Skeleton.BoneField。肘、肩
 *   这些要弯折的接缝上，两根骨的势力范围必须大面积叠着：叠得窄，权重就在一
 *   格之内从五五开跳到独占，胳膊一折那一格就被剪开。
 * strength 不是"越大越黏"，它是跟 root 抢地盘的筹码：root 那根骨的实心核罩住
 *   整个人、权重恒为 1，乘上它的 strength 就是 0.50 —— 别的骨在某个顶点上只有
 *   把 field^3 × strength 抬过 0.50 才算真的"拥有"这块皮。所以一根骨的实际地盘
 *   远小于它的 radius；袖口、指尖这些离骨轴最远的肉要归自己管，要么把核放大
 *   到那里，要么把 strength 抬上去。
 * head 的 strength 要压过上臂：耳侧那缕垂发离上臂骨轴只有 32px，落在它的实心
 *   核里，纯按距离就会被手臂拽走盖住半张脸。谁该管头发是常识，不是几何。
 * from: 蒙皮范围沿骨轴的起点。只有 girl 的上臂需要 —— 她侧着身，肩关节离脸
 *   只有 55px，让上臂骨从 head 就开始吃肉，实心核会把脸罩进去。boy 的脸离肩
 *   有 122px，给他加 from 反而把 T恤袖口撕成不动和跟着转的两半。
 * rigid: 不继承父骨的轴向拉伸。手是刚体，前臂抻长时手指不该跟着变长。
 * armBSwing: 不抓手机那条手臂绕肩拧的固定角度（屏幕上顺时针为正）。立绘里两只
 *   手伸向同一处，不拧开就叠成一团分不出指头的肉。
 * grip: 机身被 armA 的手心握住的那个点，相对机身中心、在机身自己的坐标系里。
 *   取各自靠得近的那条侧缘再往外让出半只手掌、并一上一下错开：两只手掌各有
 *   约 70px 宽，挤在同一处谁也读不出来；取机身中心则整只手连五指一起横过
 *   屏幕；贴着侧缘取，整只手又会缩到机身背后看不见 —— 手心在机身外、指尖
 *   搭进机身里，才既攥得住又不挡屏幕。
 */
[System.Serializable]
public class BoneDef {
    public string name;
    public string parent;          // null = 根骨
    public float hu, hv, tu, tv;   // head / tail，归一化 uv
    public float core;             // <0 表示按 radius*0.62 取
    public float radius;
    public float strength;
    public float capH = 0.25f, capT = 0.25f;
    public float from = 0f;
    public bool rigid = false;

    public BoneDef(string name, string parent, float hu, float hv, float tu, float tv,
                   float core, float radius, float strength,
                   float capH, float capT, float from = 0f, bool rigid = false) {
        this.name = name; this.parent = parent;
        this.hu = hu; this.hv = hv; this.tu = tu; this.tv = tv;
        this.core = core; this.radius = radius; this.strength = strength;
        this.capH = capH; this.capT = capT; this.from = from; this.rigid = rigid;
    }
}

public class CharDef {
    public string img;         // StreamingAssets/art 下的文件名
    public float anchorX;      // 立绘横向锚点（站位 x 对齐到这里）
    public Vector2 gripA;      // 机身被手心握住的点，相对机身中心
    public float armBSwing;
    public BoneDef[] bones;
}

public static class RigData {

    public static readonly CharDef Girl = new CharDef {
        img = "girl.png",
        anchorX = 0.40f,
        gripA = new Vector2(-74f, -30f),   // 握住机身左缘偏上：她在左，手从左边伸过来
        armBSwing = 0.26f,                 // 另一只手压到机身下缘（屏幕上顺时针为正）
        bones = new[] {
            new BoneDef("root",      null,        0.330f, 0.520f, 0.330f, 0.990f, 0.500f, 0.780f, 0.50f, 3f,    3f),
            new BoneDef("torso",     "root",      0.330f, 0.500f, 0.420f, 0.165f, 0.150f, 0.250f, 1.05f, 0.8f,  0.25f),
            new BoneDef("head",      "torso",     0.420f, 0.155f, 0.420f, 0.020f, 0.130f, 0.190f, 4.50f, 0.3f,  0.4f),
            new BoneDef("armA_up",   "torso",     0.455f, 0.162f, 0.655f, 0.186f, 0.085f, 0.125f, 2.4f,  0.70f, 0.55f, 0.28f),
            new BoneDef("armA_fore", "armA_up",   0.655f, 0.186f, 0.800f, 0.190f, 0.075f, 0.115f, 2.4f,  0.55f, 0.25f),
            new BoneDef("armA_hand", "armA_fore", 0.800f, 0.190f, 0.955f, 0.140f, 0.075f, 0.105f, 3.5f,  0.10f, 1.00f, 0f, true),
            new BoneDef("armB",      "torso",     0.385f, 0.330f, 0.895f, 0.265f, 0.075f, 0.115f, 3.0f,  0.10f, 0.12f, 0f, true),
        },
    };

    public static readonly CharDef Boy = new CharDef {
        img = "boy.png",
        anchorX = 0.55f,
        gripA = new Vector2(76f, 30f),     // 握住机身右缘偏下 —— 与她上下错开，两只手不打架
        armBSwing = 0.30f,                 // 他的手臂朝左，顺时针是把另一只手抬到机身上缘
        bones = new[] {
            new BoneDef("root",      null,        0.560f, 0.480f, 0.560f, 0.990f, 0.500f, 0.780f, 0.50f, 3f,    3f),
            new BoneDef("torso",     "root",      0.550f, 0.450f, 0.500f, 0.160f, 0.150f, 0.250f, 1.05f, 0.8f,  0.25f),
            new BoneDef("head",      "torso",     0.500f, 0.150f, 0.500f, 0.020f, 0.130f, 0.190f, 4.50f, 0.3f,  0.4f),
            new BoneDef("armA_up",   "torso",     0.420f, 0.212f, 0.265f, 0.228f, 0.060f, 0.105f, 2.4f,  0.25f, 0.55f),
            new BoneDef("armA_fore", "armA_up",   0.265f, 0.228f, 0.140f, 0.227f, 0.050f, 0.095f, 2.4f,  0.55f, 0.25f),
            new BoneDef("armA_hand", "armA_fore", 0.140f, 0.227f, 0.015f, 0.200f, 0.075f, 0.115f, 3.5f,  0.10f, 0.35f, 0f, true),
            new BoneDef("armB",      "torso",     0.560f, 0.268f, 0.170f, 0.300f, 0.075f, 0.105f, 3.0f,  0.10f, 0.12f, 0f, true),
        },
    };
}
}
