using System;
using System.Collections.Generic;
using Godot;

namespace Chashouji {

/* 配方表与命中链路 —— 逐字取自网页版 web/main.js 的 RECIPE 与 impact()。
 *
 * 一件礼物炸出什么，只在这里定义。形态（Dot/Spark/Ring/Chip/Star/Soft/Heart/Card）
 * 是通用的，换题材皮不用动 ParticleFx.cs。
 *
 * 颜色有一条硬规矩，是这张底图逼出来的：客厅是浅绿墙 + 米色地板，**白色在
 * 这上面几乎加不亮**。所以发光的那几种一律用高饱和暖橙 —— 它靠色相跳出来
 * 而不是靠亮度；实体那几种一律深色 + 描边 —— 它靠轮廓跳出来。上一版整套
 * 用的奶白和浅米，在胶片上基本看不见。
 *
 * thud 是通用撞击，任何还没单独配方的东西都落到它上面。 */
public struct Recipe {
    public Color tint;
    /// (x, y, side, s) —— side 是"打向哪一侧"，s 是分量系数
    public Action<float, float, int, float> burst;
}

public static class RECIPE {
    static float Rnd() => Particles.Rnd();
    static int Round(float v) => (int)Mathf.Round(v);

    public static readonly Dictionary<string, Recipe> T = new Dictionary<string, Recipe> {
        ["thud"]    = new Recipe { tint = MathX.C255(255, 224, 186), burst = Thud },
        ["feather"] = new Recipe { tint = MathX.C255(255, 238, 240), burst = Feather },
        ["star"]    = new Recipe { tint = MathX.C255(255, 242, 196), burst = Star },
        ["debris"]  = new Recipe { tint = MathX.C255(226, 238, 255), burst = Debris },
        ["petal"]   = new Recipe { tint = MathX.C255(255, 214, 226), burst = Petal },
        ["splash"]  = new Recipe { tint = MathX.C255(246, 226, 198), burst = Splash },
        ["bloom"]   = new Recipe { tint = MathX.C255(255, 232, 214), burst = Bloom },
        ["memory"]  = new Recipe { tint = MathX.C255(252, 240, 222), burst = Memory },
    };

    public static Recipe Get(string key) =>
        key != null && T.TryGetValue(key, out var r) ? r : T["thud"];

    // ---------- thud：通用撞击 ----------

    static void Thud(float x, float y, int side, float s) {
        var p = Particles.Spawn(PKind.Dot, x, y, 0.20f);
        p.r = 16f * s; p.r1 = 70f * s; p.Rgb(255, 196, 110); p.a = 0.9f;

        p = Particles.Spawn(PKind.Ring, x, y, 0.38f);
        p.r = 10f * s; p.r1 = 120f * s; p.Rgb(K.EMBER); p.lw = 6f * s;

        // 第二道环晚 70ms 出场，读起来是"砰—砰"两下而不是一下
        p = Particles.Spawn(PKind.Ring, x, y, 0.44f);
        p.r = 8f * s; p.r1 = 180f * s; p.Rgb(255, 132, 54); p.lw = 4f * s; p.delay = 0.07f;

        /* 火花给足数量。画布 960x1334，二三十个粒子铺开就只剩零星几点，
           读不出"炸开" —— 这里的密度感是靠数量堆的，不是靠单颗更亮。 */
        for (int i = 0; i < Round(26f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.2f;
            float sp = (240f + Rnd() * 560f) * s;
            p = Particles.Spawn(PKind.Spark, x, y, 0.24f + Rnd() * 0.3f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 110f;
            p.g = 980f; p.drag = 0.985f;
            if (i % 4 != 0) p.Rgb(K.EMBER); else p.Rgb(255, 238, 150);
            p.lw = 1.6f + Rnd() * 2.4f * s;
        }
        for (int i = 0; i < Round(14f * s); i++) {
            p = Particles.Spawn(PKind.Soft, x + (Rnd() - 0.5f) * 60f * s, y + (Rnd() - 0.3f) * 40f,
                                0.7f + Rnd() * 0.7f);
            p.vx = -side * (40f + Rnd() * 150f) * s; p.vy = -20f - Rnd() * 80f;
            p.g = 90f; p.drag = 0.94f; p.r = 10f * s; p.r1 = (46f + Rnd() * 34f) * s;
            p.Rgb(150, 128, 110); p.a = 0.30f;
        }
        // 翻滚的小片：撞击总要崩下点什么，没有它只有光，像是凭空亮了一下
        for (int i = 0; i < Round(9f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.4f;
            float sp = (170f + Rnd() * 330f) * s;
            p = Particles.Spawn(PKind.Chip, x, y, 0.7f + Rnd() * 0.6f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 200f;
            p.g = 780f; p.drag = 0.99f;
            p.w = 6f + Rnd() * 8f * s; p.h = 4f + Rnd() * 6f * s;
            p.rot = Rnd() * 6.28f; p.vrot = (Rnd() - 0.5f) * 16f;
            p.Rgb(122, 96, 78); p.Edge(K.INK); p.lw = 1.6f; p.a = 0.95f;
        }
    }

    /* 枕头砸脸。全场唯一一个不出火花的配方 —— 枕头砸下去的是一团闷响和漫天
       绒毛，给它配火花就成了爆炸。冲击感全靠数量和滞空：羽毛重力只有常规的
       八分之一、阻力极大，所以命中半秒之后画面里还在飘，而火花那时候早没了。 */
    static void Feather(float x, float y, int side, float s) {
        var p = Particles.Spawn(PKind.Dot, x, y, 0.20f);
        p.r = 22f * s; p.r1 = 72f * s; p.Rgb(255, 206, 198); p.a = 0.40f;

        // 绒絮：贴着撞击点炸开的那一蓬，用来糊住撞击瞬间。别给多 —— 它是浅色
        // 的，在浅色沙发前面堆厚了就是一团白雾，把羽毛的形状全吃掉。
        for (int i = 0; i < Round(11f * s); i++) {
            float a = Rnd() * 6.283f;
            p = Particles.Spawn(PKind.Soft, x, y, 0.5f + Rnd() * 0.6f);
            p.vx = Mathf.Cos(a) * (70f + Rnd() * 240f) * s;
            p.vy = Mathf.Sin(a) * (60f + Rnd() * 180f) * s - 90f;
            p.g = 60f; p.drag = 0.92f; p.r = 14f * s; p.r1 = (48f + Rnd() * 38f) * s;
            p.Rgb(214, 200, 206); p.a = 0.26f;
        }
        /* 羽毛本体：sway 左右摆，慢慢打着旋往下落。
           尺寸和数量是按"直播画面上看得见"定的，不是按真羽毛定的 —— 一根真
           羽毛在 960 宽的画布上只有十几像素，观众端再缩一半就是几个像素的白
           点，等于没有。阻力也不能给真实值：0.958 每帧意味着三分之一秒后羽毛
           就地停住，全堆在命中点上，看着像一摊泡沫而不是炸开的枕头。 */
        for (int i = 0; i < Round(30f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.8f;
            float sp = (200f + Rnd() * 560f) * s;
            p = Particles.Spawn(PKind.Chip, x + (Rnd() - 0.5f) * 60f, y + (Rnd() - 0.5f) * 80f,
                                1.5f + Rnd() * 1.4f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 170f;
            p.g = 150f; p.drag = 0.988f; p.sway = 40f + Rnd() * 55f;
            p.w = 20f + Rnd() * 17f * s; p.h = 12f + Rnd() * 9f;
            p.rot = Rnd() * 6.28f; p.vrot = (Rnd() - 0.5f) * 5.0f;
            if (i % 5 != 0) p.Rgb(252, 250, 250); else p.Rgb(250, 232, 236);
            p.Edge(138, 118, 122); p.lw = 1.9f; p.a = 1f;
        }
    }

    /* 打懵了：头顶转圈的星星。这是全套里最"卡通"的一个，也是最省事的一个 ——
       星星本身就是观众对"挨了一下"的默认图示，不需要任何解释。
       spin 让它绕着命中点公转，不是原地飞散：飞散读成爆炸，公转才读成眩晕。 */
    static void Star(float x, float y, int side, float s) {
        var p = Particles.Spawn(PKind.Dot, x, y, 0.18f);
        p.r = 14f * s; p.r1 = 74f * s; p.Rgb(255, 214, 96); p.a = 0.85f;

        p = Particles.Spawn(PKind.Ring, x, y, 0.32f);
        p.r = 8f * s; p.r1 = 108f * s; p.Rgb(255, 196, 72); p.lw = 5f * s;

        // 公转的大星星：数量少，每颗都要看得清，所以描边给足
        for (int i = 0; i < Round(7f * s); i++) {
            float a = Rnd() * 6.283f;
            p = Particles.Spawn(PKind.Star, x - side * 20f, y - 40f - Rnd() * 60f, 0.7f + Rnd() * 0.6f);
            p.vx = -side * (20f + Rnd() * 90f); p.vy = -60f - Rnd() * 90f;
            p.g = 180f; p.drag = 0.95f; p.spin = 26f + Rnd() * 34f;
            p.r = (13f + Rnd() * 11f) * s; p.r1 = 3f;
            p.rot = a; p.vrot = (Rnd() - 0.5f) * 7f;
            p.Rgb(255, 208, 56); p.Edge(126, 74, 18); p.lw = 2.4f; p.a = 1f;
        }
        // 小星星飞散，补密度
        for (int i = 0; i < Round(11f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.8f;
            float sp = (200f + Rnd() * 440f) * s;
            p = Particles.Spawn(PKind.Star, x, y, 0.5f + Rnd() * 0.45f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 180f;
            p.g = 640f; p.drag = 0.982f; p.r = (6f + Rnd() * 6f) * s; p.r1 = 2f;
            p.rot = a; p.vrot = (Rnd() - 0.5f) * 14f;
            p.Rgb(255, 226, 120); p.Edge(150, 96, 24); p.lw = 1.6f; p.a = 1f;
        }
        for (int i = 0; i < Round(14f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.4f;
            float sp = (260f + Rnd() * 480f) * s;
            p = Particles.Spawn(PKind.Spark, x, y, 0.2f + Rnd() * 0.22f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 120f;
            p.g = 900f; p.drag = 0.985f;
            p.Rgb(255, 198, 70); p.lw = 1.4f + Rnd() * 2f * s;
        }
    }

    /* 硬东西砸碎（遥控器、马克杯）。碎片一律深色 —— 这是三个配方里唯一能在
       米色地板上自带对比的，所以它不描边也认得出，描边只是为了和另外两个
       配方看起来是同一套东西。 */
    static void Debris(float x, float y, int side, float s) {
        var p = Particles.Spawn(PKind.Dot, x, y, 0.16f);
        p.r = 18f * s; p.r1 = 82f * s; p.Rgb(255, 236, 190); p.a = 0.95f;

        p = Particles.Spawn(PKind.Ring, x, y, 0.30f);
        p.r = 12f * s; p.r1 = 150f * s; p.Rgb(255, 176, 60); p.lw = 7f * s;

        // 碎片：重、快、弹不起来，落地就停 —— 和羽毛正好是两个极端
        for (int i = 0; i < Round(24f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.5f;
            float sp = (300f + Rnd() * 620f) * s;
            bool dark = i % 3 == 0;
            p = Particles.Spawn(PKind.Chip, x, y, 0.55f + Rnd() * 0.5f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 300f;
            p.g = 1450f; p.drag = 0.995f;
            p.w = 5f + Rnd() * 12f * s; p.h = 4f + Rnd() * 8f * s;
            p.rot = Rnd() * 6.28f; p.vrot = (Rnd() - 0.5f) * 22f;
            if (dark) p.Rgb(48, 54, 64); else p.Rgb(96, 106, 120);
            p.Edge(K.INK); p.lw = 1.5f; p.a = 1f;
        }
        for (int i = 0; i < Round(30f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.0f;
            float sp = (320f + Rnd() * 700f) * s;
            p = Particles.Spawn(PKind.Spark, x, y, 0.18f + Rnd() * 0.26f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 140f;
            p.g = 1020f; p.drag = 0.984f;
            if (i % 3 != 0) p.Rgb(K.EMBER); else p.Rgb(255, 244, 176);
            p.lw = 1.4f + Rnd() * 2.6f * s;
        }
        // 一小撮灰，落在碎片后面，撞击点不至于干干净净
        for (int i = 0; i < Round(9f * s); i++) {
            p = Particles.Spawn(PKind.Soft, x + (Rnd() - 0.5f) * 70f * s, y + (Rnd() - 0.2f) * 50f,
                                0.6f + Rnd() * 0.6f);
            p.vx = -side * (50f + Rnd() * 170f) * s; p.vy = -30f - Rnd() * 70f;
            p.g = 70f; p.drag = 0.93f; p.r = 12f * s; p.r1 = (50f + Rnd() * 40f) * s;
            p.Rgb(138, 132, 128); p.a = 0.34f;
        }
    }

    /* 玫瑰花束炸开（档 3 左）。结构照抄 feather —— 枕头和花束在物理上是同一
       件事：一团轻的东西散开、长时间滞空。差别只在颜色和形状。
       这也是为什么它值得换掉棉被：白羽毛在这张浅色底图上本来就偏淡，全靠
       数量才看得见；深玫红的花瓣自带对比，同样的数量亮一倍。 */
    static void Petal(float x, float y, int side, float s) {
        var p = Particles.Spawn(PKind.Dot, x, y, 0.22f);
        p.r = 20f * s; p.r1 = 78f * s; p.Rgb(255, 138, 172); p.a = 0.55f;

        p = Particles.Spawn(PKind.Ring, x, y, 0.34f);
        p.r = 10f * s; p.r1 = 128f * s; p.Rgb(255, 96, 140); p.lw = 5f * s;

        // 花瓣：sway 让它们打着旋往下飘，重力只有碎片的十分之一
        for (int i = 0; i < Round(32f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.9f;
            float sp = (210f + Rnd() * 580f) * s;
            bool deep = i % 3 == 0;
            p = Particles.Spawn(PKind.Chip, x + (Rnd() - 0.5f) * 60f, y + (Rnd() - 0.5f) * 80f,
                                1.4f + Rnd() * 1.4f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 180f;
            p.g = 140f; p.drag = 0.987f; p.sway = 44f + Rnd() * 60f;
            /* 长宽比压到 1:0.7 左右。第一版是 1:0.45，翻滚时被 cos 压扁，
               一屏读成几十根粉色胶囊而不是花瓣。 */
            p.w = 19f + Rnd() * 14f * s; p.h = 15f + Rnd() * 10f * s;
            p.rot = Rnd() * 6.28f; p.vrot = (Rnd() - 0.5f) * 3.2f;
            if (deep) p.Rgb(198, 40, 78); else p.Rgb(255, 92, 130);
            p.Edge(122, 30, 58); p.lw = 1.8f; p.a = 1f;
        }
        // 一点金粉。花束里那层包装纸的反光，也把粉色压不住的地方提亮
        for (int i = 0; i < Round(12f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.6f;
            float sp = (240f + Rnd() * 460f) * s;
            p = Particles.Spawn(PKind.Spark, x, y, 0.22f + Rnd() * 0.26f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 150f;
            p.g = 820f; p.drag = 0.983f;
            p.Rgb(255, 216, 132); p.lw = 1.4f + Rnd() * 2f * s;
        }
    }

    /* 奶茶泼一身（档 3 右）。和花瓣正好相反：液体是**重**的，落地就停，
       所以走 debris 的物理参数而不是 feather 的 —— 一杯奶茶泼出去要是像羽毛
       那样飘半秒，读起来就成了雾。 */
    static void Splash(float x, float y, int side, float s) {
        var p = Particles.Spawn(PKind.Dot, x, y, 0.18f);
        p.r = 18f * s; p.r1 = 86f * s; p.Rgb(236, 202, 158); p.a = 0.8f;

        p = Particles.Spawn(PKind.Ring, x, y, 0.30f);
        p.r = 12f * s; p.r1 = 142f * s; p.Rgb(214, 158, 96); p.lw = 6f * s;

        /* 奶茶渍：**糊住不动**。drag 给到 0.86，冲出去三分之一秒就停在原地，
           然后一直挂在被泼的那个人身上 —— 这是这个配方唯一在做的事。
           颜色必须是饱和焦糖不能是淡奶油色：底图是浅绿墙加米色地板，淡色的浆
           泼上去等于没泼。第一版就是栽在这儿，整个配方几乎是隐形的。 */
        for (int i = 0; i < Round(15f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.4f;
            p = Particles.Spawn(PKind.Soft, x, y, 1.2f + Rnd() * 0.8f);
            p.vx = -side * Mathf.Cos(a) * (110f + Rnd() * 330f) * s;
            p.vy = Mathf.Sin(a) * (80f + Rnd() * 230f) * s - 110f;
            p.g = 180f; p.drag = 0.86f; p.r = 14f * s; p.r1 = (36f + Rnd() * 30f) * s;
            p.Rgb(176, 120, 64); p.a = 0.5f;
        }
        /* 珍珠：深褐、够大、弹得开。它们是全套里对比最强的一组 —— 深色在浅底
           上本来就跳，所以奶茶的可见度主要靠它们扛，浆只负责"湿了一片"。
           尺寸从 7 提到 13 起步：小于十几像素在观众端缩一半就成了灰点。 */
        for (int i = 0; i < Round(20f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.7f;
            float sp = (240f + Rnd() * 520f) * s;
            float dd = 13f + Rnd() * 9f * s;
            p = Particles.Spawn(PKind.Chip, x, y, 1.1f + Rnd() * 0.8f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 300f;
            p.g = 1180f; p.drag = 0.992f; p.sway = 12f + Rnd() * 20f;
            p.w = dd; p.h = dd;
            p.rot = Rnd() * 6.28f; p.vrot = (Rnd() - 0.5f) * 18f;
            if (i % 4 != 0) p.Rgb(52, 34, 22); else p.Rgb(96, 62, 38);
            p.Edge(K.INK); p.lw = 1.8f; p.a = 1f;
        }
        // 糖浆丝
        for (int i = 0; i < Round(14f * s); i++) {
            float a = (Rnd() - 0.5f) * 2.1f;
            float sp = (300f + Rnd() * 620f) * s;
            p = Particles.Spawn(PKind.Spark, x, y, 0.16f + Rnd() * 0.24f);
            p.vx = -side * Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 160f;
            p.g = 960f; p.drag = 0.985f;
            if (i % 3 != 0) p.Rgb(232, 190, 128); else p.Rgb(255, 240, 208);
            p.lw = 1.5f + Rnd() * 2.2f * s;
        }
    }

    /* 求婚戒指盒绽放（档 4 左）。**绽放式**：不朝被打的一侧溅，而是从命中点
       向四周全向炸开 —— 这是它跟前面所有配方唯一的结构差别，也是"绽放"和
       "溅射"的分界。独占那 0.8 秒里画面重心必须在被砸的那个人身上，全向才
       能把他整个圈住。
       爱心给负重力：往上飘。这是全套里唯一一个不往下掉的配方。 */
    static void Bloom(float x, float y, int side, float s) {
        var p = Particles.Spawn(PKind.Dot, x, y, 0.30f);
        p.r = 30f * s; p.r1 = 150f * s; p.Rgb(255, 226, 150); p.a = 0.95f;

        p = Particles.Spawn(PKind.Ring, x, y, 0.40f);
        p.r = 14f * s; p.r1 = 210f * s; p.Rgb(255, 206, 92); p.lw = 8f * s;

        // 三道环依次荡开，"绽"的那一下就是它
        p = Particles.Spawn(PKind.Ring, x, y, 0.50f);
        p.r = 10f * s; p.r1 = 300f * s; p.Rgb(255, 170, 88); p.lw = 5f * s; p.delay = 0.08f;

        p = Particles.Spawn(PKind.Ring, x, y, 0.58f);
        p.r = 8f * s; p.r1 = 380f * s; p.Rgb(255, 140, 170); p.lw = 3.5f * s; p.delay = 0.18f;

        /* 爱心：先向外冲开一圈（高初速 + 大阻力，0.2 秒内就减速停住），再靠
           负重力慢慢飘起来。两段连起来读就是"绽开、然后升上去"。
           半径拿 s 放大之后很容易失控：第一版 (15+16)*2.8 得到 42~87 的半径，
           一颗爱心就有 190px 宽、占屏宽五分之一，四十颗直接把两张脸糊死。
           档 4 确实该铺满屏，但**脸是这个玩法仅有的两个可读信息之一**，而
           爱心要在画面上待一秒半到两秒半，不是一闪而过。密度靠数量，不靠
           单颗更大。 */
        for (int i = 0; i < Round(11f * s); i++) {
            float a = Rnd() * 6.283f;
            float sp = (300f + Rnd() * 420f) * s;
            p = Particles.Spawn(PKind.Heart, x, y, 1.5f + Rnd() * 1.1f);
            p.vx = Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp;
            p.g = -46f; p.drag = 0.90f; p.sway = 26f + Rnd() * 34f;
            p.r = (9f + Rnd() * 9f) * s; p.r1 = 4f;
            p.rot = (Rnd() - 0.5f) * 0.7f; p.vrot = (Rnd() - 0.5f) * 2.4f;
            if (i % 3 != 0) p.Rgb(255, 92, 130); else p.Rgb(255, 150, 180);
            p.Edge(150, 34, 70); p.lw = 2.2f; p.a = 1f;
        }
        // 金色星光，绕着命中点公转 —— 借 star 配方那套"眩晕"的读法
        for (int i = 0; i < Round(10f * s); i++) {
            float a = Rnd() * 6.283f;
            p = Particles.Spawn(PKind.Star, x, y, 0.8f + Rnd() * 0.7f);
            p.vx = Mathf.Cos(a) * (60f + Rnd() * 170f);
            p.vy = Mathf.Sin(a) * (60f + Rnd() * 150f) - 70f;
            p.g = 150f; p.drag = 0.94f; p.spin = 30f + Rnd() * 40f;
            p.r = (11f + Rnd() * 12f) * s; p.r1 = 3f;
            p.rot = a; p.vrot = (Rnd() - 0.5f) * 7f;
            p.Rgb(255, 214, 74); p.Edge(140, 84, 20); p.lw = 2.4f; p.a = 1f;
        }
        // 金粉全向铺满
        for (int i = 0; i < Round(26f * s); i++) {
            float a = Rnd() * 6.283f;
            float sp = (280f + Rnd() * 700f) * s;
            p = Particles.Spawn(PKind.Spark, x, y, 0.24f + Rnd() * 0.34f);
            p.vx = Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp;
            p.g = 700f; p.drag = 0.982f;
            if (i % 4 != 0) p.Rgb(255, 214, 120); else p.Rgb(255, 248, 214);
            p.lw = 1.6f + Rnd() * 2.6f * s;
        }
        _ = side;   // 绽放是全向的，不朝哪一侧溅 —— 这正是它跟前面几个配方的分界
    }

    /* 合照相框绽放（档 4 右）。同样全向，但落点相反：爱心往上飘，照片往下落。
       一张张翻着往下掉的照片比爆炸更有分量 —— 它占的是**时间**不是亮度，
       生命期给到 2.6 秒，独占窗口结束之后它还在飘，这段余韵才是档 4 的味道。 */
    static void Memory(float x, float y, int side, float s) {
        var p = Particles.Spawn(PKind.Dot, x, y, 0.28f);
        p.r = 28f * s; p.r1 = 146f * s; p.Rgb(255, 236, 200); p.a = 0.9f;

        p = Particles.Spawn(PKind.Ring, x, y, 0.38f);
        p.r = 14f * s; p.r1 = 206f * s; p.Rgb(236, 196, 140); p.lw = 8f * s;

        p = Particles.Spawn(PKind.Ring, x, y, 0.54f);
        p.r = 10f * s; p.r1 = 320f * s; p.Rgb(206, 168, 120); p.lw = 4f * s; p.delay = 0.12f;

        /* 照片。走 Card 而不是 Chip —— 理由见 ParticleFx.cs 里 Card 那段：Chip
           带描边时永远是胶囊，照片必须是矩形。
           配色回到这张底图的老规矩上：**相纸保持浅色，对比靠那圈粗深边**。
           中间试过把整张压成复古棕来"让它看得见"，那是绕开规律不是用它 ——
           看得见了，但不再像照片。
           初速比第一版降了三成、阻力加大：原来冲得太猛，一秒后全飞出屏幕，
           留在画面里的反而是空的。慢落的余韵才是档 4 买到的东西。 */
        for (int i = 0; i < Round(20f * s); i++) {
            float a = Rnd() * 6.283f;
            float sp = (130f + Rnd() * 330f) * s;
            bool old = i % 4 == 0;
            p = Particles.Spawn(PKind.Card, x, y, 2.0f + Rnd() * 0.8f);
            p.vx = Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp - 150f;
            p.g = 190f; p.drag = 0.976f; p.sway = 62f + Rnd() * 70f;
            p.w = 30f + Rnd() * 17f * s; p.h = 24f + Rnd() * 13f * s;
            p.rot = (Rnd() - 0.5f) * 1.5f; p.vrot = (Rnd() - 0.5f) * 1.1f;
            if (old) p.Rgb(242, 226, 198); else p.Rgb(252, 247, 238);
            p.Edge(74, 52, 34); p.lw = 3.6f; p.a = 1f;
        }
        // 少量爱心，把这一下和戒指盒认作同一档
        for (int i = 0; i < Round(7f * s); i++) {
            float a = Rnd() * 6.283f;
            p = Particles.Spawn(PKind.Heart, x, y, 1.4f + Rnd() * 1.0f);
            p.vx = Mathf.Cos(a) * (200f + Rnd() * 300f) * s;
            p.vy = Mathf.Sin(a) * (200f + Rnd() * 260f) * s;
            p.g = -40f; p.drag = 0.90f; p.sway = 24f + Rnd() * 30f;
            p.r = (8f + Rnd() * 8f) * s; p.r1 = 3f;
            p.rot = (Rnd() - 0.5f) * 0.6f; p.vrot = (Rnd() - 0.5f) * 2.2f;
            p.Rgb(255, 122, 152); p.Edge(150, 48, 82); p.lw = 2.0f; p.a = 1f;
        }
        for (int i = 0; i < Round(16f * s); i++) {
            float a = Rnd() * 6.283f;
            float sp = (240f + Rnd() * 560f) * s;
            p = Particles.Spawn(PKind.Spark, x, y, 0.2f + Rnd() * 0.3f);
            p.vx = Mathf.Cos(a) * sp; p.vy = Mathf.Sin(a) * sp;
            p.g = 760f; p.drag = 0.983f;
            if (i % 3 != 0) p.Rgb(255, 226, 168); else p.Rgb(255, 248, 226);
            p.lw = 1.5f + Rnd() * 2.4f * s;
        }
        _ = side;
    }

    // ---------- 命中 ----------

    /* side: +1 打向查岗党(左/女方)，-1 打向灭迹党(右/男方)
       power: 1 点赞级  2 普通礼物  3 大礼物  4 顶档 */
    public static void Impact(int side, float y, int power, string recipeKey) {
        var r = Get(recipeKey);
        float s = power >= 4 ? 2.8f : power == 3 ? 1.7f : power == 2 ? 1.0f : 0.55f;
        float x = Director.FrontAt(y);

        // 冲量注入命中高度那一行，方向朝被打的一侧
        float d = MathX.Clamp((y - K.TOP) / (K.BOT - K.TOP), 0f, 1f) * (K.ROWS - 1);
        int i0 = (int)MathX.Clamp(Mathf.Floor(d), 0f, K.ROWS - 1);
        FX.rowImp[i0] += -side * 40f * s;
        if (i0 > 0) FX.rowImp[i0 - 1] += -side * 22f * s;
        if (i0 < K.ROWS - 1) FX.rowImp[i0 + 1] += -side * 22f * s;

        FX.hitV += -side * 320f * s;
        FX.punch = Mathf.Max(FX.punch, 0.045f * s);

        /* 染色只是"挨了一下"的提示，不是照明。超过 0.21 角色的线稿和睡衣花纹就被
           洗掉了，而那正是这个玩法唯一能看的东西 —— 所以它有一个**可读性天花板**，
           档 3 起就顶在那儿，档 4 不会更红。写成 min(天花板, …) 而不是 min(1.4, s)，
           是因为后者看起来像"按分量缩放"，实际从档 3 就封死了，读代码会被骗一次。
           档 4 强在独占那 0.8 秒，不在染得更狠。 */
        const float TINT_MAX = 0.21f;
        FX.tint = r.tint;
        FX.tintA = Mathf.Max(FX.tintA, Mathf.Min(TINT_MAX, 0.15f * s));

        Particles.AddShake(7f * s);
        Particles.AddFlash(power >= 4 ? 0.34f : power >= 3 ? 0.22f : power >= 2 ? 0.10f : 0.03f);
        /* 点赞级不顿帧。连珠一串八颗，每颗都冻 35ms 的话，这串"哒哒哒"就被拆成
           八次停顿 —— 而它的表现力全在快。顿帧留给看得出分量的那两档，在那里它
           才是"全世界停下来看这一击"，而不是一段接一段的停摆。
           档 4 是"全世界停下来看这一击"：冻 320ms，配合 AmmoView 里的独占窗口，
           这段时间别的礼物只排队不落地。它买的不是更大的数字，是一段没人打断的
           时间。 */
        Particles.HitStop(power >= 4 ? 0.32f : power >= 3 ? 0.11f : power >= 2 ? 0.07f : 0f);

        r.burst(x, y, side, s);
    }
}
}
