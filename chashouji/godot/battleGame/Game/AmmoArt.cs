using System.Collections.Generic;
using Godot;

namespace Chashouji {

/* 飞行物的画法 —— 逐字取自网页版 web/ammo.js 的 ITEM / SILH / AURA / TAIL。
 *
 * 都在原点画，调用方已经 Push 过变换了，r 是半宽。
 * 每一件都是"填充 + 深色粗描边"：底图是明亮客厅，实体靠轮廓而不是靠亮度
 * 才看得见 —— 跟角色睡衣有线稿是同一个道理。
 *
 * 物品目前是代码画的：几何剪影 + 粗描边 + 高对比色。飞行物在画面上只有
 * 60~160px 而且高速移动，这个精度足够验证节奏 —— 而节奏是这个功能的全部。
 * 换成生图贴图只改这里；玫瑰花束已经换成 3D 转盘（见 Sprites）。 */
public static class AmmoArt {
    static readonly Color INK = MathX.C255(52, 40, 36);
    static readonly List<Vector2> buf = new List<Vector2>();

    static Color C(float r, float g, float b, float a = 1f) => MathX.C255(r, g, b, a);

    /// 二次贝塞尔采样成折线，追加到 o（不含起点）
    static void QuadTo(List<Vector2> o, Vector2 p0, Vector2 c, Vector2 p1, int seg) {
        for (int i = 1; i <= seg; i++) {
            float t = (float)i / seg, u = 1f - t;
            o.Add(new Vector2(u * u * p0.X + 2f * u * t * c.X + t * t * p1.X,
                              u * u * p0.Y + 2f * u * t * c.Y + t * t * p1.Y));
        }
    }

    static void RoundBox(Draw2D d, float x, float y, float w, float h, float r, Color fill, float lw, Color line) {
        d.RoundRect(x, y, w, h, r, fill);
        if (lw > 0f) d.StrokeClosed(Draw2D.RoundRectPts(x, y, w, h, r), lw, line);
    }

    static void Disc(Draw2D d, float cx, float cy, float r, Color fill, float lw, Color line, int seg = 18) {
        d.Ellipse(cx, cy, r, r, fill, seg);
        if (lw > 0f) d.StrokeClosed(Draw2D.EllipsePts(cx, cy, r, r, seg), lw, line);
    }

    // ---------- 本体画法 ----------

    public static void Draw(Draw2D d, string item, float r) {
        switch (item) {
            case "hairpin": Hairpin(d, r); break;
            case "seed":    Seed(d, r);    break;
            case "pillow":  Pillow(d, r);  break;
            case "gamepad": Gamepad(d, r); break;
            case "bouquet": Bouquet(d, r); break;
            case "milktea": Milktea(d, r); break;
            case "ringbox": Ringbox(d, r); break;
            case "photo":   Photo(d, r);   break;
            case "quilt":   Quilt(d, r);   break;
            case "box":     Box(d, r);     break;
        }
    }

    // 发卡：一根粉色小棒加一颗珠子，连珠用
    static void Hairpin(Draw2D d, float r) {
        RoundBox(d, -r, -r * 0.3f, r * 2f, r * 0.6f, r * 0.3f, C(255, 143, 184), r * 0.26f, INK);
        Disc(d, -r * 0.62f, 0f, r * 0.46f, C(255, 217, 232), r * 0.22f, INK);
    }

    // 瓜子壳：一颗水滴，深棕。男方的连珠，嗑瓜子看戏顺手就弹过去了
    static void Seed(Draw2D d, float r) {
        SeedPts(r);
        d.PolyFan(0f, 0f, buf, C(107, 81, 54));
        d.StrokeClosed(buf, r * 0.24f, INK);
        d.Segment(new Vector2(r * 0.5f, 0f), new Vector2(-r * 0.62f, 0f), r * 0.16f, C(255, 240, 220, 0.5f));
    }

    static void SeedPts(float r) {
        buf.Clear();
        buf.Add(new Vector2(r, 0f));
        QuadTo(buf, new Vector2(r, 0f), new Vector2(0f, r * 0.66f), new Vector2(-r, 0f), 8);
        QuadTo(buf, new Vector2(-r, 0f), new Vector2(0f, -r * 0.66f), new Vector2(r, 0f), 8);
        buf.RemoveAt(buf.Count - 1);          // 末点与首点重合，闭合折线不需要它
    }

    // 抱枕：圆角方 + 缝线 + 两点兔耳暗示，跟沙发上那只兔抱枕呼应
    static void Pillow(Draw2D d, float r) {
        RoundBox(d, -r, -r * 0.84f, r * 2f, r * 1.68f, r * 0.4f, C(253, 242, 246), r * 0.12f, INK);
        d.StrokeClosed(Draw2D.RoundRectPts(-r * 0.78f, -r * 0.64f, r * 1.56f, r * 1.28f, r * 0.3f),
                       r * 0.055f, C(255, 160, 195, 0.85f));
        for (int s = -1; s <= 1; s += 2)
            d.Ellipse(s * r * 0.26f, -r * 0.16f, r * 0.1f, r * 0.26f, C(255, 179, 205), 14);
    }

    // 游戏手柄：深色机身 + 一个摇杆一个按键，小但好认
    static void Gamepad(Draw2D d, float r) {
        RoundBox(d, -r, -r * 0.48f, r * 2f, r * 0.96f, r * 0.44f, C(61, 68, 80), r * 0.12f, INK);
        Disc(d, -r * 0.46f, 0f, r * 0.22f, C(143, 227, 255), r * 0.08f, INK, 14);
        Disc(d, r * 0.44f, -r * 0.08f, r * 0.16f, C(255, 122, 122), r * 0.07f, INK, 12);
    }

    /* 玫瑰花束（档 3）。剪影必须是**放射状**的 —— 它对面那件是奶茶杯（梯形），
       两件飞在半空时观众只看得到纯色轮廓，一个发散一个收拢才分得开。
       这也是它换掉棉被的原因：棉被 1:0.70 和外卖箱 1:0.72 在剪影层上是
       两个一模一样的大方块。 */
    static readonly float[,] HEADS = {
        { 0f, -0.72f }, { -0.60f, -0.42f }, { 0.60f, -0.42f },
        { -0.72f, 0.10f }, { 0.72f, 0.10f }, { -0.28f, -0.06f }, { 0.30f, -0.10f },
    };
    static readonly float[] LEAF_A = { -2.5f, -0.65f, 3.55f, 1.75f };

    static void Bouquet(Draw2D d, float r) {
        // 包装纸：底下那个倒锥
        WrapPts(r);
        d.Poly(buf, C(247, 227, 207));
        d.StrokeClosed(buf, r * 0.09f, INK);
        // 叶子先铺一层，压在花底下
        foreach (float a in LEAF_A) {
            d.Push(Mathf.Cos(a) * r * 0.62f, Mathf.Sin(a) * r * 0.62f - r * 0.18f, a);
            d.Ellipse(0f, 0f, r * 0.34f, r * 0.13f, C(95, 143, 87), 14);
            d.Pop();
        }
        // 七朵花头按放射排开，这就是那个"发散"的轮廓
        for (int i = 0; i < 7; i++) {
            float hx = HEADS[i, 0] * r, hy = HEADS[i, 1] * r, hr = r * (i < 3 ? 0.32f : 0.27f);
            Disc(d, hx, hy, hr, i % 3 == 1 ? C(255, 95, 134) : C(230, 58, 98), r * 0.075f, INK, 16);
            // 花心那一圈：没有它七个圆读成七颗球
            d.StrokeClosed(Draw2D.EllipsePts(hx, hy, hr * 0.44f, hr * 0.44f, 12),
                           r * 0.05f, C(255, 196, 214, 0.9f));
        }
    }

    static void WrapPts(float r) {
        buf.Clear();
        buf.Add(new Vector2(-r * 0.30f, r * 0.20f));
        buf.Add(new Vector2(r * 0.30f, r * 0.20f));
        buf.Add(new Vector2(r * 0.16f, r * 0.95f));
        buf.Add(new Vector2(-r * 0.16f, r * 0.95f));
    }

    /* 奶茶（档 3）。上宽下窄的杯子 + 一根斜插的吸管，轮廓收拢，跟对面的
       花束正好相反。珍珠不画在杯里 —— 飞行时那么小根本看不见，它们留到
       命中时才作为粒子出场。 */
    static void Milktea(Draw2D d, float r) {
        CupPts(r);
        d.Poly(buf, C(232, 201, 160));
        d.StrokeClosed(buf, r * 0.085f, INK);
        /* 杯底那层珍珠：一条深色带子，比画一堆小圆更容易读。
           网页版是"剪裁到杯形再铺满色"，剪裁区域本身就是这个梯形，所以这里
           直接画那个梯形 —— 同一块像素，少一次离屏合成。 */
        buf.Clear();
        buf.Add(new Vector2(-r * 0.46f, r * 0.36f));
        buf.Add(new Vector2(r * 0.46f, r * 0.36f));
        buf.Add(new Vector2(r * 0.40f, r * 0.96f));
        buf.Add(new Vector2(-r * 0.40f, r * 0.96f));
        d.Poly(buf, C(74, 51, 36));
        // 封膜
        RoundBox(d, -r * 0.62f, -r * 1.00f, r * 1.24f, r * 0.22f, r * 0.07f,
                 C(255, 244, 228), r * 0.075f, INK);
        // 吸管：斜的，它是这件东西的识别点
        d.Push(r * 0.10f, -r * 0.30f, -0.34f);
        RoundBox(d, -r * 0.09f, -r * 0.95f, r * 0.18f, r * 1.5f, r * 0.09f,
                 C(255, 143, 184), r * 0.07f, INK);
        d.Pop();
    }

    static void CupPts(float r) {
        buf.Clear();
        buf.Add(new Vector2(-r * 0.56f, -r * 0.86f));
        buf.Add(new Vector2(r * 0.56f, -r * 0.86f));
        buf.Add(new Vector2(r * 0.40f, r * 0.96f));
        buf.Add(new Vector2(-r * 0.40f, r * 0.96f));
    }

    /* 求婚戒指盒（档 4）。开着盖飞过来 —— 合着的盒子只是个方块，跟档 2 的
       抱枕撞轮廓；掀开的盖子给了它一个别人没有的折角。
       它买的不是"更大的方块"，是命中那一刻绽开的东西。 */
    static void Ringbox(Draw2D d, float r) {
        /* 掀开的盖子，向后仰。仰角和位移都要收着给：第一版 -0.42 配 -0.46
           的位移，盖子跟盒身之间开了一道缝，230px 宽的东西飞过去读成两个
           分开的粉方块，而不是一个开着的盒子。 */
        d.Push(0f, -r * 0.38f, -0.30f);
        RoundBox(d, -r * 0.78f, -r * 0.62f, r * 1.56f, r * 0.66f, r * 0.1f,
                 C(193, 65, 107), r * 0.075f, INK);
        d.RoundRect(-r * 0.64f, -r * 0.50f, r * 1.28f, r * 0.42f, r * 0.08f, C(255, 230, 239));
        d.Pop();
        // 盒身
        RoundBox(d, -r * 0.80f, -r * 0.10f, r * 1.60f, r * 0.86f, r * 0.12f,
                 C(212, 82, 125), r * 0.08f, INK);
        // 绒布槽
        d.Ellipse(0f, -r * 0.06f, r * 0.50f, r * 0.13f, C(143, 45, 79), 16);
        /* 戒指：环 + 一颗钻，钻用菱形，圆的会读成又一颗珠子。
           尺寸比"真实比例"大一圈 —— 它是整件东西的识别点，按真比例画出来
           在飞行中只是一个小金点，观众读到的就只剩"一个粉盒子"了。 */
        d.StrokeClosed(Draw2D.EllipsePts(0f, -r * 0.16f, r * 0.30f, r * 0.30f, 16),
                       r * 0.13f, C(255, 211, 90));
        d.StrokeClosed(Draw2D.EllipsePts(0f, -r * 0.16f, r * 0.30f, r * 0.30f, 16),
                       r * 0.035f, C(122, 82, 20, 0.85f));
        buf.Clear();
        buf.Add(new Vector2(0f, -r * 0.74f));
        buf.Add(new Vector2(r * 0.25f, -r * 0.46f));
        buf.Add(new Vector2(0f, -r * 0.20f));
        buf.Add(new Vector2(-r * 0.25f, -r * 0.46f));
        d.Poly(buf, C(234, 247, 255));
        d.StrokeClosed(buf, r * 0.07f, INK);
    }

    /* 合照相框（档 4）。竖着的框 + 里面两个挨在一起的小人影。
       竖长比 1:1.3，跟戒指盒的 1:0.8 差 1.6 倍，剪影分得开。 */
    static void Photo(Draw2D d, float r) {
        RoundBox(d, -r * 0.68f, -r * 0.90f, r * 1.36f, r * 1.80f, r * 0.08f,
                 C(184, 134, 79), r * 0.08f, INK);
        RoundBox(d, -r * 0.52f, -r * 0.74f, r * 1.04f, r * 1.48f, 0f,
                 C(253, 246, 234), r * 0.05f, INK);
        // 照片里的两个人：肩线挨着，不画脸 —— 这个尺寸画脸只会糊成两个点
        var skin = C(125, 147, 181);
        for (int k = 0; k < 2; k++) {
            float dx = (k == 0 ? -0.22f : 0.22f) * r;
            d.Ellipse(dx, r * 0.02f, r * 0.17f, r * 0.17f, skin, 14);
            // 上半椭圆 = 肩。canvas 那边是 ellipse(..., π, 2π)，y 向下所以取的是上半
            buf.Clear();
            for (int i = 0; i <= 12; i++) {
                float a = Mathf.Pi + Mathf.Pi * i / 12f;
                buf.Add(new Vector2(dx + Mathf.Cos(a) * r * 0.28f, r * 0.52f + Mathf.Sin(a) * r * 0.30f));
            }
            d.PolyFan(dx, r * 0.52f, buf, skin);
        }
        /* 右上角一颗小爱心，把"这是合照"点破。网页版那两段贝塞尔的外接宽度是
           26 个单位、再 scale(r*0.013)，折合半宽 r*0.169 —— HeartPts 的半宽就是
           它的 r 参数，所以直接给这个数。 */
        d.PolyFan(r * 0.34f, -r * 0.46f,
                  Draw2D.HeartPts(r * 0.34f, -r * 0.46f, r * 0.169f, 16), C(255, 95, 134));
    }

    /* ↓ 以下两件是**备选池**，当前未启用（档 3 已换成花束/奶茶）。
       留着不是死代码：换回来只需改 Shop.ItemL / ItemR 一行。
       弃用原因见 docs/礼物设计.md 判据二 —— 两件剪影只差 1.03 倍。 */

    // 整条被子：最大的一件，格纹让它在翻滚时读得出体积
    static void Quilt(Draw2D d, float r) {
        RoundBox(d, -r, -r * 0.7f, r * 2f, r * 1.4f, r * 0.18f, C(255, 230, 238), r * 0.085f, INK);
        var line = C(255, 146, 183, 0.8f);
        foreach (float t in new[] { -0.34f, 0.34f })
            d.Segment(new Vector2(t * r * 2f, -r * 0.7f), new Vector2(t * r * 2f, r * 0.7f), r * 0.05f, line);
        foreach (float t in new[] { -0.3f, 0.3f })
            d.Segment(new Vector2(-r, t * r * 1.4f), new Vector2(r, t * r * 1.4f), r * 0.05f, line);
        // 翻出来的一角，不然大色块读成一块板子
        buf.Clear();
        buf.Add(new Vector2(r, -r * 0.7f));
        buf.Add(new Vector2(r * 0.5f, -r * 0.7f));
        buf.Add(new Vector2(r, -r * 0.2f));
        d.Poly(buf, C(255, 248, 251));
        d.StrokeClosed(buf, r * 0.07f, INK);
    }

    // 外卖箱：牛皮纸色 + 胶带十字，一眼是个箱子
    static void Box(Draw2D d, float r) {
        RoundBox(d, -r, -r * 0.72f, r * 2f, r * 1.44f, r * 0.1f, C(207, 151, 96), r * 0.085f, INK);
        RoundBox(d, -r, -r * 0.72f, r * 2f, r * 0.4f, r * 0.1f, C(184, 127, 75), r * 0.07f, INK);
        var tape = C(246, 238, 222, 0.9f);
        d.Segment(new Vector2(0f, -r * 0.72f), new Vector2(0f, r * 0.72f), r * 0.13f, tape);
        d.Segment(new Vector2(-r, r * 0.1f), new Vector2(r, r * 0.1f), r * 0.13f, tape);
    }

    // ---------- 外轮廓剪影（残影专用） ----------

    /* 残影是速度残像，照着本体一笔一笔画满缝线、按键、格纹，既贵（四个残影
       就是四遍完整物品）又脏（细节在高速移动里糊成噪点）。它只需要形状。

       与网页版的一处实现差别：那边整件剪影是**一条 path 一次 fill**，子形状
       重叠处不会加深；这里是逐块提交三角形，半透明的两块叠起来会更深一层。
       所以凡是子形状明显互相压着的，这里改画成**相切而不相交**的版本 ——
       发卡就是这么调的（棒从珠子的右边缘起画，两者正好切在 -0.16r）。
       花束那七个花头必然互相交叠，但它走的是 3D 转盘贴图，矢量剪影只在缺
       素材时才退回来用。 */
    public static void Silh(Draw2D d, string item, float r, Color col) {
        switch (item) {
            case "hairpin":
                d.RoundRect(-r * 0.16f, -r * 0.3f, r * 1.16f, r * 0.6f, r * 0.3f, col);
                d.Ellipse(-r * 0.62f, 0f, r * 0.46f, r * 0.46f, col, 18);
                break;
            case "seed":
                SeedPts(r); d.PolyFan(0f, 0f, buf, col);
                break;
            case "pillow":
                d.RoundRect(-r, -r * 0.84f, r * 2f, r * 1.68f, r * 0.4f, col);
                break;
            case "gamepad":
                d.RoundRect(-r, -r * 0.48f, r * 2f, r * 0.96f, r * 0.44f, col);
                break;
            case "bouquet":
                // 外接的是七个花头，画成一圈交叠的圆 —— 放射轮廓就是它的识别点
                WrapPts(r); d.Poly(buf, col);
                for (int i = 0; i < 7; i++)
                    d.Ellipse(HEADS[i, 0] * r, HEADS[i, 1] * r, r * (i < 3 ? 0.32f : 0.27f),
                              r * (i < 3 ? 0.32f : 0.27f), col, 14);
                break;
            case "milktea":
                CupPts(r); d.Poly(buf, col);
                d.Rect(-r * 0.62f, -r * 1.00f, r * 1.24f, r * 0.22f, col);
                break;
            case "ringbox":
                d.RoundRect(-r * 0.80f, -r * 0.10f, r * 1.60f, r * 0.86f, r * 0.12f, col);
                // 掀开的盖子单独补一块，斜的 —— 剪影上那个折角全靠它
                d.Push(0f, -r * 0.46f, -0.42f);
                d.RoundRect(-r * 0.78f, -r * 0.62f, r * 1.56f, r * 0.66f, r * 0.1f, col);
                d.Pop();
                break;
            case "photo":
                d.RoundRect(-r * 0.68f, -r * 0.90f, r * 1.36f, r * 1.80f, r * 0.08f, col);
                break;
            case "quilt":
                d.RoundRect(-r, -r * 0.7f, r * 2f, r * 1.4f, r * 0.18f, col);
                break;
            case "box":
                d.RoundRect(-r, -r * 0.72f, r * 2f, r * 1.44f, r * 0.1f, col);
                break;
        }
    }

    // ---------- 自发光与拖尾的颜色 ----------

    /* 这里的"发光"不是加光。明亮客厅底图上相加混合是加不上去的 —— 浅绿墙
       本来就接近饱和，任何颜色加上去都只是趋向白，红色叠浅绿直接变成纯白。
       所以走的是**色晕**：普通混合的半透明高饱和色，比本体大一圈、边缘柔化。
       它靠色相从背景里跳出来，跟粒子那套"发光的一律高饱和暖橙"是同一条规矩。

       取每件物品主色的高饱和版，顺带把阵营也读出来：查岗党偏粉紫，灭迹党偏
       琥珀与青。观众看一眼弹道的颜色就知道这一发是谁打的。 */
    public static Color AuraOf(string item) => item switch {
        "hairpin" => MathX.C255(255, 64, 156),
        "pillow"  => MathX.C255(255, 92, 164),
        "quilt"   => MathX.C255(255, 76, 148),
        "seed"    => MathX.C255(255, 148, 48),
        "gamepad" => MathX.C255(64, 206, 255),
        "box"     => MathX.C255(255, 136, 40),
        "bouquet" => MathX.C255(255, 48, 110),
        "ringbox" => MathX.C255(255, 186, 56),
        "milktea" => MathX.C255(255, 158, 72),
        "photo"   => MathX.C255(255, 206, 140),
        _         => MathX.C255(255, 140, 60),
    };

    /* 拖尾带的颜色：每件物品自己主色的暗调，不是统一的黑。统一用深色的话，
       拖在浅粉抱枕后面读起来像一团影子或者污渍 —— 那是"另一个东西"，而拖尾
       应该是它自己甩出来的。 */
    public static Color TailOf(string item) => item switch {
        "hairpin" => MathX.C255(176, 64, 112),
        "seed"    => MathX.C255(58, 42, 26),
        "pillow"  => MathX.C255(196, 116, 150),
        "gamepad" => MathX.C255(38, 46, 58),
        "quilt"   => MathX.C255(198, 112, 148),
        "box"     => MathX.C255(126, 82, 48),
        "bouquet" => MathX.C255(150, 42, 72),
        "milktea" => MathX.C255(132, 94, 58),
        "ringbox" => MathX.C255(150, 58, 92),
        "photo"   => MathX.C255(120, 88, 54),
        _         => MathX.C255(52, 40, 36),
    };
}

/* 3D 渲出来的转盘序列。
   物品有两种画法：AmmoArt 里的矢量函数（画得出但转起来光影跟着一起转，物理上
   是错的），和这里的贴图序列（每个角度都是 Blender 渲的，光影各自正确）。
   有贴图就用贴图，没有就退回矢量。

   cell 是单格边长，n 是转盘帧数，scale 补偿裁剪留白：图集是按**所有角度的
   并集**裁的，单帧物体填不满一格。 */
public static class Sprites {
    public class Sheet {
        public Texture2D img, silh;
        public int n, cols, cell;
        public float scale;
        public string src;
    }

    static readonly Dictionary<string, Sheet> T = new Dictionary<string, Sheet> {
        ["bouquet"] = new Sheet { src = "res://assets/items/rose_atlas.png",
                                  n = 36, cols = 6, cell = 268, scale = 1.34f },
    };

    public static int Loaded { get; private set; }

    /// 有贴图就返回，没有返回 null —— 调用方据此退回矢量画法
    public static Sheet Of(string item) =>
        item != null && T.TryGetValue(item, out var s) && s.img != null ? s : null;

    /// off 对应网页版 ?nosprite=1：强制退回矢量画法，用来和转盘版并排对比
    public static void Load(bool off) {
        if (off) return;
        foreach (var kv in T) {
            var sh = kv.Value;
            if (!ResourceLoader.Exists(sh.src)) continue;
            var tex = GD.Load<Texture2D>(sh.src);
            if (tex == null) continue;
            sh.img = tex;
            Loaded++;

            /* 残影层要的是**单色剪影**，而贴图是彩色的。一次性烘一张染好色的
               副本，运行时直接贴 —— 跟网页版 loadSprites 里那段 source-in 是
               同一件事：把 RGB 全部换成拖尾色，只留 alpha。 */
            var im = tex.GetImage();
            if (im == null) { GD.PushWarning($"[sprite] {sh.src} 取不到像素，残影退回不画"); continue; }
            im.Convert(Image.Format.Rgba8);
            var data = im.GetData();
            var c = AmmoArt.TailOf(kv.Key);
            byte r = (byte)Mathf.Round(c.R * 255f), g = (byte)Mathf.Round(c.G * 255f), b = (byte)Mathf.Round(c.B * 255f);
            for (int i = 0; i + 3 < data.Length; i += 4) { data[i] = r; data[i + 1] = g; data[i + 2] = b; }
            sh.silh = ImageTexture.CreateFromImage(
                Image.CreateFromData(im.GetWidth(), im.GetHeight(), false, Image.Format.Rgba8, data));
        }
    }

    /// 当前朝向对应转盘的哪一格
    public static int CellOf(Sheet s, float rot) {
        int k = (int)Mathf.Floor(rot / 6.2832f * s.n) % s.n;
        if (k < 0) k += s.n;
        return k;
    }

    /// 图集里第 c 格的像素矩形
    public static Rect2 Src(Sheet s, int c) =>
        new Rect2((c % s.cols) * s.cell, (c / s.cols) * s.cell, s.cell, s.cell);
}
}
