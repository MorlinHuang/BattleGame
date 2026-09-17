using Godot;

namespace Chashouji {

/* 角色层：0~100 每 1% 一张预渲染帧，按 p 取最近的一张硬切。
 *
 * 从网格变形改走帧序列，是因为两个人抢同一部手机时，肩、肘、腕的相对关系每一
 * 档都不一样 —— 这种成对的姿态用一套骨骼去凑，永远是在"手够不到机身"和"肘折
 * 过头"之间取舍，而画好的帧没有这个问题。
 *
 * 不做相邻帧的交叉淡化：两张画的是不同姿态而不是同一姿态的不同时刻，叠在一起
 * 就是两副骨架互相穿透的重影。
 *
 * 101 档里有 29 张是生图画的关键档，其余由光流从相邻关键档插出来。姿态跨度太大
 * 的几个区间插不出干净的中间帧，那里还缺着几档，Init 里把缺档映射到最近的邻居。
 *
 * 帧只覆盖人物所在的那条横带（FRAME_TOP 往下 FRAME_H 高），不是整块画布：画布
 * 上下加起来四百多行全是透明像素，白占三成显存。 */
public partial class FrameView : Node2D {
    public const int N = 101;

    readonly Texture2D[] tex = new Texture2D[N];
    readonly int[] near = new int[N];
    /* 命中染色单独一层，走相加混合。角色是预渲染帧、做不了受击变形，打击反馈只能
       来自贴图之外：这一层用同一张帧当遮罩，所以只会盖在已经画出的角色像素上，
       不会糊到背景 —— 等价于网页版的 source-atop。 */
    TintLayer tint;

    public int Loaded { get; private set; }
    public int Shown { get; private set; }

    float ox, punch, tintA;
    Color tintC = Colors.White;

    public void Init(Node parent, int z) {
        for (int p = 0; p < N; p++) {
            string path = $"res://assets/frames/f{p:000}.png";
            if (ResourceLoader.Exists(path)) { tex[p] = GD.Load<Texture2D>(path); Loaded++; }
        }
        /* 缺档映射到最近的邻居 —— 画面照常，只是那一段的跳变还是原来的大小。
           不做插值合成：见文件头，两张不同姿态叠起来是重影不是中间帧。 */
        for (int p = 0; p < N; p++) {
            near[p] = -1;
            for (int d = 0; d < N; d++) {
                if (p - d >= 0 && tex[p - d] != null) { near[p] = p - d; break; }
                if (p + d < N && tex[p + d] != null) { near[p] = p + d; break; }
            }
        }
        if (Loaded == 0) GD.PushWarning("assets/frames 下一张帧都没有");
        this.Setup(parent, "actors", false, z);
        tint = new TintLayer { Owner2 = this };
        tint.Setup(parent, "actors_tint", true, z + 1);
    }

    public void Rebuild(float p, float offsetX, float pu, Color tc, float ta) {
        int i = near[(int)MathX.Clamp(Mathf.Round(MathX.Clamp(p, 0f, 100f)), 0, N - 1)];
        Shown = i;
        Visible = i >= 0;
        ox = offsetX; punch = pu; tintC = tc; tintA = ta;
        tint.Visible = i >= 0 && ta > 0.004f;
        if (i >= 0) { QueueRedraw(); if (tint.Visible) tint.QueueRedraw(); }
    }

    /* 缩放以脚底为锚：人挨了一下会"胀"一下，但脚不离地。 */
    Rect2 QuadRect() {
        float k = 1f + punch;
        float w = K.W * k, h = K.FRAME_H * k;
        float foot = K.FRAME_TOP + K.FRAME_H;          // 脚底那条线，缩放的锚
        return new Rect2(ox + (K.W - w) * 0.5f, foot - h, w, h);
    }

    public override void _Draw() {
        if (Shown < 0 || tex[Shown] == null) return;
        DrawTextureRect(tex[Shown], QuadRect(), false);
    }

    /// 染色层：同一张帧、同一个矩形，只是整体乘上染色并走相加混合
    public partial class TintLayer : Node2D {
        public FrameView Owner2;
        public override void _Draw() {
            var o = Owner2;
            if (o == null || o.Shown < 0 || o.tex[o.Shown] == null) return;
            DrawTextureRect(o.tex[o.Shown], o.QuadRect(), false,
                            new Color(o.tintC.R, o.tintC.G, o.tintC.B, o.tintA));
        }
    }
}
}
