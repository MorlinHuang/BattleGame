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
 * 101 档里有 28 张是生图画的关键档，其余由 v13/interp_frames.py 用光流从相邻
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
    }

    public void Rebuild(float p, float offsetX) {
        int i = near[(int)MathX.Clamp(Mathf.Round(Mathf.Clamp(p, 0f, 100f)), 0, N - 1)];
        Shown = i;
        obj.Visible = i >= 0;
        if (i < 0) return;

        obj.SetTexture(tex[i]);
        Quad(obj.mesh, offsetX);
    }

    static void Quad(Mesh m, float x) {
        float W = Director.W, top = -FRAME_TOP, bot = -(FRAME_TOP + FRAME_H);
        m.Clear();
        m.vertices = new[] {
            new Vector3(x, top, 0), new Vector3(x + W, top, 0),
            new Vector3(x + W, bot, 0), new Vector3(x, bot, 0),
        };
        m.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
        var c = (Color32)Color.white;
        m.colors32 = new[] { c, c, c, c };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
    }
}
}
