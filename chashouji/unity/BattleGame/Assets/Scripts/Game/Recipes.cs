using UnityEngine;
using R = Chashouji.FxTuning.Recipe;

namespace Chashouji {

/* Recipes.cs —— 一次命中炸出什么，与网页版 web/main.js 的 RECIPE 表同构。
 * ============================================================================
 * 想改"抱枕砸中之后飘什么"就改这里；想改"抱枕飞多快多大"去 FxTuning.cs；
 * 想改"抱枕长什么样"去 AmmoView.cs 的 ITEM 区。
 *
 * 形态（Dot/Spark/Ring/Chip/Star/Soft）是通用的，换题材皮不用动 ParticleFx.cs。
 *
 * 颜色有一条硬规矩，是这张底图逼出来的：客厅是浅绿墙 + 米色地板，**白色在这
 * 上面几乎加不亮**。所以发光的一律用高饱和暖橙（靠色相跳出来而不是靠亮度），
 * 实体的一律深色 + 描边（靠轮廓跳出来）。早先整套用奶白和浅米，在实拍里基本
 * 看不见。换到暗色底图的玩法上要反过来调。
 *
 * 参数里的 s 是强度倍率（点赞级 0.55 / 普通 1.0 / 大礼物 1.7），
 * side 是被打的一侧：+1 打向查岗党(左)，-1 打向灭迹党(右)，粒子朝反方向溅。
 * ============================================================================ */
public static class Recipes {

    static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

    /// 描边色，取角色线稿那个暖黑
    public static readonly Color INK = C(58, 44, 38);
    /// 发光基色，高饱和暖橙
    public static readonly Color EMBER = C(255, 156, 38);

    /// 命中时角色泛的那一下颜色
    public static Color TintOf(R r) {
        switch (r) {
            case R.Feather: return C(255, 238, 240);
            case R.Star:    return C(255, 242, 196);
            case R.Debris:  return C(226, 238, 255);
            default:        return C(255, 224, 186);
        }
    }

    public static void Burst(R r, float x, float y, int side, float s) {
        switch (r) {
            case R.Feather: Feather(x, y, side, s); break;
            case R.Star:    Star(x, y, side, s);    break;
            case R.Debris:  Debris(x, y, side, s);  break;
            default:        Thud(x, y, side, s);    break;
        }
    }

    static float Rnd => Random.value;

    // ---------------------------------------------------------------- 通用撞击
    /* 任何还没单独配方的东西都落到这上面。 */
    static void Thud(float x, float y, int side, float s) {
        ParticleFx.Spawn(new ParticleFx.Req {
            kind = ParticleFx.Kind.Dot, x = x, y = y, r = 16f * s, r1 = 70f * s,
            life = 0.20f, rgb = C(255, 196, 110), a = 0.9f });
        ParticleFx.Spawn(new ParticleFx.Req {
            kind = ParticleFx.Kind.Ring, x = x, y = y, r = 10f * s, r1 = 120f * s,
            life = 0.38f, rgb = EMBER, lw = 6f * s });
        // 第二道环晚 70ms 出场，读起来是"砰—砰"两下而不是一下
        ParticleFx.SpawnLater(0.07f, new ParticleFx.Req {
            kind = ParticleFx.Kind.Ring, x = x, y = y, r = 8f * s, r1 = 180f * s,
            life = 0.44f, rgb = C(255, 132, 54), lw = 4f * s });

        /* 火花给足数量。画布 960x1334，二三十个粒子铺开就只剩零星几点，读不出
           "炸开"—— 这里的密度感是靠数量堆的，不是靠单颗更亮。 */
        int nSpark = Mathf.RoundToInt(26f * s);
        for (int i = 0; i < nSpark; i++) {
            float a = (Rnd - 0.5f) * 2.2f;
            float sp = (240f + Rnd * 560f) * s;
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Spark, x = x, y = y,
                vx = -side * Mathf.Cos(a) * sp, vy = Mathf.Sin(a) * sp - 110f,
                g = 980f, drag = 0.985f, life = 0.24f + Rnd * 0.3f,
                rgb = (i % 4) != 0 ? EMBER : C(255, 238, 150),
                lw = 1.6f + Rnd * 2.4f * s });
        }
        int nSoft = Mathf.RoundToInt(14f * s);
        for (int i = 0; i < nSoft; i++) {
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Soft,
                x = x + (Rnd - 0.5f) * 60f * s, y = y + (Rnd - 0.3f) * 40f,
                vx = -side * (40f + Rnd * 150f) * s, vy = -20f - Rnd * 80f,
                g = 90f, drag = 0.94f, r = 10f * s, r1 = (46f + Rnd * 34f) * s,
                life = 0.7f + Rnd * 0.7f, rgb = C(150, 128, 110), a = 0.30f });
        }
        // 翻滚的小片：撞击总要崩下点什么，没有它只有光，像是凭空亮了一下
        int nChip = Mathf.RoundToInt(9f * s);
        for (int i = 0; i < nChip; i++) {
            float a = (Rnd - 0.5f) * 2.4f;
            float sp = (170f + Rnd * 330f) * s;
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Chip, x = x, y = y,
                vx = -side * Mathf.Cos(a) * sp, vy = Mathf.Sin(a) * sp - 200f,
                g = 780f, drag = 0.99f, life = 0.7f + Rnd * 0.6f,
                w = 6f + Rnd * 8f * s, h = 4f + Rnd * 6f * s,
                rot = Rnd * 6.28f, vrot = (Rnd - 0.5f) * 16f,
                rgb = C(122, 96, 78), edge = INK, hasEdge = true, lw = 1.6f, a = 0.95f });
        }
    }

    // ------------------------------------------------------------------ 枕头砸脸
    /* 全场唯一一个不出火花的配方 —— 枕头砸下去的是一团闷响和漫天绒毛，给它配
       火花就成了爆炸。冲击感全靠数量和滞空：羽毛重力只有常规的八分之一、阻力
       极大，所以命中半秒之后画面里还在飘，而火花那时候早没了。 */
    static void Feather(float x, float y, int side, float s) {
        ParticleFx.Spawn(new ParticleFx.Req {
            kind = ParticleFx.Kind.Dot, x = x, y = y, r = 22f * s, r1 = 72f * s,
            life = 0.20f, rgb = C(255, 206, 198), a = 0.40f });

        /* 绒絮：贴着撞击点炸开的那一蓬，用来糊住撞击瞬间。别给多 —— 它是浅色
           的，在浅色沙发前面堆厚了就是一团白雾，把羽毛的形状全吃掉。 */
        int nSoft = Mathf.RoundToInt(11f * s);
        for (int i = 0; i < nSoft; i++) {
            float a = Rnd * 6.283f;
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Soft, x = x, y = y,
                vx = Mathf.Cos(a) * (70f + Rnd * 240f) * s,
                vy = Mathf.Sin(a) * (60f + Rnd * 180f) * s - 90f,
                g = 60f, drag = 0.92f, r = 14f * s, r1 = (48f + Rnd * 38f) * s,
                life = 0.5f + Rnd * 0.6f, rgb = C(214, 200, 206), a = 0.26f });
        }

        /* 羽毛本体：sway 左右摆，慢慢打着旋往下落。
           尺寸和数量是按"直播画面上看得见"定的，不是按真羽毛定的 —— 一根真羽毛
           在 960 宽的画布上只有十几像素，观众端再缩一半就是几个像素的白点，等于
           没有。阻力也不能给真实值：0.958 每帧意味着三分之一秒后羽毛就地停住，
           全堆在命中点上，看着像一摊泡沫而不是炸开的枕头。 */
        int nF = Mathf.RoundToInt(30f * s);
        for (int i = 0; i < nF; i++) {
            float a = (Rnd - 0.5f) * 2.8f;
            float sp = (200f + Rnd * 560f) * s;
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Chip,
                x = x + (Rnd - 0.5f) * 60f, y = y + (Rnd - 0.5f) * 80f,
                vx = -side * Mathf.Cos(a) * sp, vy = Mathf.Sin(a) * sp - 170f,
                g = 150f, drag = 0.988f, sway = 40f + Rnd * 55f,
                life = 1.5f + Rnd * 1.4f,
                w = 20f + Rnd * 17f * s, h = 12f + Rnd * 9f,
                rot = Rnd * 6.28f, vrot = (Rnd - 0.5f) * 5.0f,
                rgb = (i % 5) != 0 ? C(252, 250, 250) : C(250, 232, 236),
                edge = C(138, 118, 122), hasEdge = true, lw = 1.9f });
        }
    }

    // -------------------------------------------------------------------- 打懵了
    /* 头顶转圈的星星。这是全套里最"卡通"的一个，也是最省事的一个 —— 星星本身
       就是观众对"挨了一下"的默认图示，不需要任何解释。
       spin 让它绕着命中点公转，不是原地飞散：飞散读成爆炸，公转才读成眩晕。 */
    static void Star(float x, float y, int side, float s) {
        ParticleFx.Spawn(new ParticleFx.Req {
            kind = ParticleFx.Kind.Dot, x = x, y = y, r = 14f * s, r1 = 74f * s,
            life = 0.18f, rgb = C(255, 214, 96), a = 0.85f });
        ParticleFx.Spawn(new ParticleFx.Req {
            kind = ParticleFx.Kind.Ring, x = x, y = y, r = 8f * s, r1 = 108f * s,
            life = 0.32f, rgb = C(255, 196, 72), lw = 5f * s });

        // 公转的大星星：数量少，每颗都要看得清，所以描边给足
        int nBig = Mathf.RoundToInt(7f * s);
        for (int i = 0; i < nBig; i++) {
            float a = Rnd * 6.283f;
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Star,
                x = x - side * 20f, y = y - 40f - Rnd * 60f,
                vx = -side * (20f + Rnd * 90f), vy = -60f - Rnd * 90f,
                g = 180f, drag = 0.95f, spin = 26f + Rnd * 34f,
                r = (13f + Rnd * 11f) * s, r1 = 3f,
                life = 0.7f + Rnd * 0.6f,
                rot = a, vrot = (Rnd - 0.5f) * 7f,
                rgb = C(255, 208, 56), edge = C(126, 74, 18), hasEdge = true, lw = 2.4f });
        }
        // 小星星飞散，补密度
        int nSmall = Mathf.RoundToInt(11f * s);
        for (int i = 0; i < nSmall; i++) {
            float a = (Rnd - 0.5f) * 2.8f;
            float sp = (200f + Rnd * 440f) * s;
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Star, x = x, y = y,
                vx = -side * Mathf.Cos(a) * sp, vy = Mathf.Sin(a) * sp - 180f,
                g = 640f, drag = 0.982f, r = (6f + Rnd * 6f) * s, r1 = 2f,
                life = 0.5f + Rnd * 0.45f,
                rot = a, vrot = (Rnd - 0.5f) * 14f,
                rgb = C(255, 226, 120), edge = C(150, 96, 24), hasEdge = true, lw = 1.6f });
        }
        int nSpark = Mathf.RoundToInt(14f * s);
        for (int i = 0; i < nSpark; i++) {
            float a = (Rnd - 0.5f) * 2.4f;
            float sp = (260f + Rnd * 480f) * s;
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Spark, x = x, y = y,
                vx = -side * Mathf.Cos(a) * sp, vy = Mathf.Sin(a) * sp - 120f,
                g = 900f, drag = 0.985f, life = 0.2f + Rnd * 0.22f,
                rgb = C(255, 198, 70), lw = 1.4f + Rnd * 2f * s });
        }
    }

    // ---------------------------------------------------------------- 硬东西砸碎
    /* 遥控器、马克杯那一类。碎片一律深色 —— 这是三个配方里唯一能在米色地板上
       自带对比的，所以它不描边也认得出，描边只是为了和另外两个配方看起来是同
       一套东西。 */
    static void Debris(float x, float y, int side, float s) {
        ParticleFx.Spawn(new ParticleFx.Req {
            kind = ParticleFx.Kind.Dot, x = x, y = y, r = 18f * s, r1 = 82f * s,
            life = 0.16f, rgb = C(255, 236, 190), a = 0.95f });
        ParticleFx.Spawn(new ParticleFx.Req {
            kind = ParticleFx.Kind.Ring, x = x, y = y, r = 12f * s, r1 = 150f * s,
            life = 0.30f, rgb = C(255, 176, 60), lw = 7f * s });

        // 碎片：重、快、弹不起来，落地就停 —— 和羽毛正好是两个极端
        int nChip = Mathf.RoundToInt(24f * s);
        for (int i = 0; i < nChip; i++) {
            float a = (Rnd - 0.5f) * 2.5f;
            float sp = (300f + Rnd * 620f) * s;
            bool dark = (i % 3) == 0;
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Chip, x = x, y = y,
                vx = -side * Mathf.Cos(a) * sp, vy = Mathf.Sin(a) * sp - 300f,
                g = 1450f, drag = 0.995f, life = 0.55f + Rnd * 0.5f,
                w = 5f + Rnd * 12f * s, h = 4f + Rnd * 8f * s,
                rot = Rnd * 6.28f, vrot = (Rnd - 0.5f) * 22f,
                rgb = dark ? C(48, 54, 64) : C(96, 106, 120),
                edge = INK, hasEdge = true, lw = 1.5f });
        }
        int nSpark = Mathf.RoundToInt(30f * s);
        for (int i = 0; i < nSpark; i++) {
            float a = (Rnd - 0.5f) * 2.0f;
            float sp = (320f + Rnd * 700f) * s;
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Spark, x = x, y = y,
                vx = -side * Mathf.Cos(a) * sp, vy = Mathf.Sin(a) * sp - 140f,
                g = 1020f, drag = 0.984f, life = 0.18f + Rnd * 0.26f,
                rgb = (i % 3) != 0 ? EMBER : C(255, 244, 176),
                lw = 1.4f + Rnd * 2.6f * s });
        }
        // 一小撮灰，落在碎片后面，撞击点不至于干干净净
        int nSoft = Mathf.RoundToInt(9f * s);
        for (int i = 0; i < nSoft; i++) {
            ParticleFx.Spawn(new ParticleFx.Req {
                kind = ParticleFx.Kind.Soft,
                x = x + (Rnd - 0.5f) * 70f * s, y = y + (Rnd - 0.2f) * 50f,
                vx = -side * (50f + Rnd * 170f) * s, vy = -30f - Rnd * 70f,
                g = 70f, drag = 0.93f, r = 12f * s, r1 = (50f + Rnd * 40f) * s,
                life = 0.6f + Rnd * 0.6f, rgb = C(138, 132, 128), a = 0.34f });
        }
    }
}
}
