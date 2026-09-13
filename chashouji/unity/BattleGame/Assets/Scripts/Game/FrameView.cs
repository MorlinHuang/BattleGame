using UnityEngine;

namespace Chashouji {

/* 角色层：0~100 每 1% 一张预渲染帧，按 p 取最近的一张硬切。
 *
 * 从网格变形改走帧序列，是因为两个人抢同一部手机时，肩、肘、腕的相对关系每
 * 一档都不一样 —— 这种成对的姿态用一套骨骼去凑，永远是在"手够不到机身"和
 * "肘折过头"之间取舍，而画好的帧没有这个问题：谁跪下、谁后仰、头发甩向哪
 * 边，都是画里就定死的。
 *
 * 不做相邻帧的交叉淡化：两张画的是不同姿态而不是同一姿态的不同时刻，叠在一
 * 起就是两副骨架互相穿透的重影，越是姿态差得远的档位越糊。
 *
 * 101 档里有 29 张是生图画的关键档，其余由 v13/interp_frames.py 用光流从相邻
 * 关键档插出来。姿态跨度太大的几个区间插不出干净的中间帧，那里还缺着几档，
 * Init 里把缺档映射到最近的邻居 —— 画面照常，只是那一段的跳变还是原来的大小。
 *
 * 帧只覆盖人物所在的那条横带（FRAME_TOP 往下 FRAME_H 高），不是整块画布：
 * 画布上下加起来四百多行全是透明像素，白占三成显存；而且 DXT 压缩要求边长是
 * 4 的倍数，1334 不是，Unity 只能退回未压缩的 RGBA32，93 张就是 454MB。
 */
public class FrameView {
    public const int N = 101;
    /* 与 v13/export_unity.py 里的同名常量必须一致 —— 那边决定人物画在横带的
       哪个位置，这边决定横带贴回画布的哪一段。 */
    const int FRAME_TOP = 308, FRAME_H = 900;

    readonly Texture2D[] tex = new Texture2D[N];
    readonly int[] near = new int[N];
    MeshObj obj;
    /* 命中染色单独一层，走相加混合。角色是预渲染帧、做不了受击变形，打击反馈
       只能来自贴图之外：这一层用同一张帧当遮罩，所以只会盖在已经画出的角色像
       素上，不会糊到背景 —— 等价于网页版的 source-atop。
       （网页版那边是往角色上叠一层半透明纯色，这边是按角色自身颜色加亮 15%，
       亮部加得多暗部加得少，读起来更像"被光扫了一下"。） */
    MeshObj tintObj;

    public int Loaded { get; private set; }
    public int Shown { get; private set; }

    public void Init(Transform parent, int order) {
        /* 走 Resources 而不是 StreamingAssets：这样 Unity 在导入时就把 PNG 压成
           GPU 能直接吃的格式，运行时既不用解码也不占四倍显存。 */
        for (int p = 0; p < N; p++) {
            tex[p] = Resources.Load<Texture2D>($"frames/f{p:000}");
            if (tex[p] != null) Loaded++;
        }
        for (int p = 0; p < N; p++) {
            near[p] = -1;
            for (int d = 0; d < N; d++) {
                if (p - d >= 0 && tex[p - d] != null) { near[p] = p - d; break; }
                if (p + d < N && tex[p + d] != null) { near[p] = p + d; break; }
            }
        }
        if (Loaded == 0) GameLog.Line("Resources/frames 下一张帧都没有");
        obj = Gfx.NewMesh("actors", parent, Gfx.NewAlphaMat(), order);
        tintObj = Gfx.NewMesh("actors_tint", parent, Gfx.NewAddMat(), order + 1);
        tintObj.Visible = false;
    }

    /* punch 是缩放脉冲，tint/tintA 是命中染色，两者都由 Director 从 FX 读来。
       缩放以脚底为锚：人挨了一下会"胀"一下，但脚不离地。 */
    public void Rebuild(float p, float offsetX, float punch, Color tint, float tintA) {
        int i = near[(int)MathX.Clamp(Mathf.Round(Mathf.Clamp(p, 0f, 100f)), 0, N - 1)];
        Shown = i;
        obj.Visible = i >= 0;
        tintObj.Visible = i >= 0 && tintA > 0.004f;
        if (i < 0) return;

        obj.SetTexture(tex[i]);
        Quad(obj.mesh, offsetX, punch, Color.white);
        if (tintObj.Visible) {
            tintObj.SetTexture(tex[i]);
            Quad(tintObj.mesh, offsetX, punch, new Color(tint.r, tint.g, tint.b, tintA));
        }
    }

    static void Quad(Mesh m, float x, float punch, Color col) {
        float k = 1f + punch;
        float w = Director.W * k, h = FRAME_H * k;
        float foot = FRAME_TOP + FRAME_H;          // 脚底那条线，缩放的锚
        float x0 = x + (Director.W - w) * 0.5f;
        float top = -(foot - h), bot = -foot;
        m.Clear();
        m.vertices = new[] {
            new Vector3(x0, top, 0), new Vector3(x0 + w, top, 0),
            new Vector3(x0 + w, bot, 0), new Vector3(x0, bot, 0),
        };
        m.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
        var c = (Color32)col;
        m.colors32 = new[] { c, c, c, c };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
    }
}
}
