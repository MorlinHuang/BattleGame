using UnityEngine;

namespace Chashouji {

/* 骨骼标定数据，与网页版 rigdata.js 逐字同步。坐标一律归一化 uv（0~1 相对图片
 * 宽高），换素材尺寸不用改数。几个反复踩的坑记在这里，改数前先读：
 *
 * 关节位置不能靠眼睛在图上点 —— 目测的肩点曾经偏进脖子 65px，上臂骨的实心
 * 核直接罩住下巴，手一摆半张脸跟着走。正确的做法是扫描：沿手臂方向逐列取
 * 不透明像素的最长连续段，得到条带的中线与宽度剖面，
 *   · 中线 = 骨轴该压的位置（骨轴必须在肢体中线上，偏上一点下缘就掉出核外）；
 *   · 宽度骤降处 = 腕（睡衣是七分袖，袖口不是手腕，袖口到腕之间还有一截小臂；
 *     宽度从 80 掉到 30 的那一列才是腕）；
 *   · 宽度重新涨起来并随后碎成细条 = 手掌与张开的五指。
 * 睡衣与皮肤靠蓝绿通道分：粉睡衣 B>G，皮肤 G>B，比按亮度或 R-G 判干净得多。
 *
 * 两条手臂都分段。靠中线那条(armA)分三段是因为它要做 IK 够到手机；另一条
 * (armB)不参与动作，本来一根刚体骨就够，但立绘里它的肘是下垂的，一根直骨
 * 的轴线无论怎么摆都会离袖子中线 30~40px，袖子下缘掉出实心核被 root 钉住，
 * 于是同一条袖子上半跟着手走、下半不动，从中间撕开。分两段后每段各自压在
 * 自己那截的中线上。
 *
 * core / radius：core 是"实心核"半径，按肢体自身的粗细量，核内一律满权重；
 *   core 到 radius 是过渡带，要落在背景上。过渡带压到肉上，同一条袖子里相邻
 *   两排顶点就会一个跟着手臂走、一个被 root 钉住，中间那条边被拉成拖影。
 * cap:[capH,capT] 势力范围沿骨轴两端的收口倍数，见 Skeleton.BoneField。肘、肩
 *   这些要弯折的接缝上，两根骨的势力范围必须大面积叠着：收口不只是把范围
 *   截短，它同时按比例缩小实心核 —— 顶点没动，核缩过去了，它就掉进过渡带，
 *   权重在相邻两格之间从五五开跳到独占，胳膊一折那一格就被剪开。所以接缝
 *   两侧的 cap 要给到 0.9 以上，让核在整个重叠区里都还罩得住肉。手骨的 capT
 *   要给到 1.0 往上 —— 张开的手，指尖比骨末端还远出大半根手骨，靠沿轴延伸
 *   够到它；横向加粗够不得，两只手在图上只隔约 80px，粗到够着自己指尖就把
 *   隔壁一起吃了。
 * strength 不是"越大越黏"，它是跟 root 抢地盘的筹码：root 那根骨的实心核罩住
 *   整个人、权重恒为 1，乘上它的 strength 就是 0.50 —— 别的骨在某个顶点上只有
 *   把 field^3 × strength 抬过 0.50 才算真的"拥有"这块皮。所以一根骨的实际地盘
 *   远小于它的 radius；袖口、指尖这些离骨轴最远的肉要归自己管，要么把核放大
 *   到那里，要么把 strength 抬上去。
 * head 的 radius 是按头发量的，不是按头：girl 的头发垂到腰，发梢离头骨轴
 *   130px，半径只按头颅给的话那一片就在 head / torso / root 三方之间僵持，
 *   谁也不占优，躯干一倾发梢就被剪开。strength 也要压过上臂：耳侧那缕垂发
 *   离上臂骨轴只有 32px，落在它的实心核里，纯按距离就会被手臂拽走盖住半
 *   张脸。谁该管头发是常识，不是几何。
 * from: 蒙皮范围沿骨轴的起点。只有 girl 的上臂需要 —— 她侧着身，肩关节离脸
 *   只有 112px，而肩那截肉本就属于躯干；让上臂骨从 head 就开始吃肉，锁骨和
 *   脖子会分到三成权重跟着手臂走。boy 的上臂朝外，capH 收紧就够。
 * rigid: 不继承父骨的轴向拉伸。手是刚体，前臂抻长时手指不该跟着变长。
 * palm: 手心在手骨上的位置（0=腕，1=骨末端）。骨末端标在张开的五指中间，
 *   手心则在腕和它之间。IK 拿它把腕从握点往回倒推，倒推短了整只手陷进机身
 *   里，倒推长了手悬在机身外面。站位也要按"握点再退这一段"算，否则会系统
 *   性地偏近。
 * armBSwing: (上臂, 前臂) 两段各自绕自己的 head 拧的固定角度（屏幕上顺时针
 *   为正）。立绘里两只手伸向同一处，不拧开就叠成一团分不出指头的肉。
 * grip: 机身被 armA 的手心握住的那个点，相对机身中心、在机身自己的坐标系里。
 *   手机画在角色层之下，手是实打实压在机身上的，所以这个点直接决定屏幕被
 *   盖掉多少：取在侧缘外一点点，手心落在机身外、只有指尖那 30px 压进来，
 *   两人再一上一中错开，屏幕中间那条竖带就全程露着。
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
    public float palm;         // 手心在手骨上的位置（0=腕，1=骨末端）
    public Vector2 armBSwing;  // (上臂, 前臂) 各自的固定拧角
    public BoneDef[] bones;
}

public static class RigData {

    public static readonly CharDef Girl = new CharDef {
        img = "girl.png",
        anchorX = 0.40f,
        gripA = new Vector2(-78f, -20f),   // 攥住机身左上：只压住一角，屏幕中间那条竖带全程露着
        palm = 0.66f,
        armBSwing = new Vector2(0.55f, 0.15f),  // 把另一只手甩到机身左下角外（顺时针为正）
        bones = new[] {
            new BoneDef("root",       null,         0.330f, 0.520f, 0.330f, 0.990f, 0.500f, 0.780f, 0.50f, 3f,    3f),
            new BoneDef("torso",      "root",       0.330f, 0.500f, 0.420f, 0.165f, 0.150f, 0.250f, 1.05f, 0.8f,  0.25f),
            new BoneDef("head",       "torso",      0.420f, 0.155f, 0.420f, 0.020f, 0.130f, 0.260f, 4.50f, 0.3f,  0.4f),
            new BoneDef("armA_up",    "torso",      0.524f, 0.191f, 0.689f, 0.182f, 0.085f, 0.125f, 2.4f,  1.20f, 0.90f, 0.28f),
            new BoneDef("armA_fore",  "armA_up",    0.689f, 0.182f, 0.842f, 0.166f, 0.075f, 0.115f, 2.4f,  0.90f, 0.25f),
            new BoneDef("armA_hand",  "armA_fore",  0.842f, 0.166f, 0.961f, 0.133f, 0.075f, 0.105f, 3.5f,  0.10f, 1.00f, 0f, true),
            new BoneDef("armB_up",    "torso",      0.526f, 0.311f, 0.691f, 0.317f, 0.075f, 0.115f, 3.0f,  0.35f, 0.55f, 0f, true),
            new BoneDef("armB_fore",  "armB_up",    0.691f, 0.317f, 0.862f, 0.272f, 0.075f, 0.115f, 3.0f,  0.55f, 1.00f, 0f, true),
        },
    };

    public static readonly CharDef Boy = new CharDef {
        img = "boy.png",
        anchorX = 0.55f,
        gripA = new Vector2(80f, 20f),     // 攥住机身右中 —— 与她错开半个手掌
        palm = 0.59f,
        armBSwing = new Vector2(-0.45f, -0.15f), // 逆时针把另一只手压到机身右下角外
        bones = new[] {
            new BoneDef("root",       null,         0.560f, 0.480f, 0.560f, 0.990f, 0.500f, 0.780f, 0.50f, 3f,    3f),
            new BoneDef("torso",      "root",       0.550f, 0.450f, 0.500f, 0.160f, 0.150f, 0.250f, 1.05f, 0.8f,  0.25f),
            new BoneDef("head",       "torso",      0.500f, 0.150f, 0.500f, 0.020f, 0.130f, 0.260f, 4.50f, 0.3f,  0.4f),
            new BoneDef("armA_up",    "torso",      0.388f, 0.204f, 0.263f, 0.225f, 0.060f, 0.105f, 2.4f,  0.45f, 0.90f),
            new BoneDef("armA_fore",  "armA_up",    0.263f, 0.225f, 0.147f, 0.224f, 0.050f, 0.095f, 2.4f,  0.90f, 0.25f),
            new BoneDef("armA_hand",  "armA_fore",  0.147f, 0.224f, 0.040f, 0.201f, 0.075f, 0.115f, 3.5f,  0.10f, 0.80f, 0f, true),
            new BoneDef("armB_up",    "torso",      0.559f, 0.288f, 0.480f, 0.321f, 0.075f, 0.115f, 3.0f,  0.35f, 0.55f, 0f, true),
            new BoneDef("armB_fore",  "armB_up",    0.480f, 0.321f, 0.263f, 0.299f, 0.075f, 0.115f, 3.0f,  0.55f, 1.00f, 0f, true),
        },
    };
}
}
