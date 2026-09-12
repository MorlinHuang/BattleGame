using System.IO;
using UnityEngine;

namespace Chashouji {

/* 角色层：0~100 每 5% 一张预渲染帧，按 p 取最近的一张硬切。
 *
 * 从网格变形改走帧序列，是因为两个人抢同一部手机时，肩、肘、腕的相对关系每
 * 一档都不一样 —— 这种成对的姿态用一套骨骼去凑，永远是在"手够不到机身"和
 * "肘折过头"之间取舍，而画好的帧没有这个问题：谁跪下、谁后仰、头发甩向哪
 * 边，都是画里就定死的。
 *
 * 不做相邻帧的交叉淡化：两张画的是不同姿态而不是同一姿态的不同时刻，叠在一
 * 起就是两副骨架互相穿透的重影，越是姿态差得远的档位越糊。硬切虽然跳，但每
 * 一帧都是清清楚楚的一张画。
 */
public class FrameView {
    public const int STEP = 5, N = 21;

    readonly Texture2D[] tex = new Texture2D[N];
    MeshObj obj;

    public int Loaded { get; private set; }
    public int Shown { get; private set; }

    public void Init(Transform parent, int order) {
        for (int i = 0; i < N; i++) {
            tex[i] = LoadFrame(i * STEP);
            if (tex[i] != null) Loaded++;
        }
        obj = Gfx.NewMesh("actors", parent, Gfx.NewAlphaMat(), order);
    }

    static Texture2D LoadFrame(int p) {
        string path = Path.Combine(Application.streamingAssetsPath, "art", "frames", $"f{p:000}.png");
        if (!File.Exists(path)) { GameLog.Line("找不到帧 " + path); return null; }
        /* 不建 mipmap：这层永远按 1:1 贴满画布，多出来的 mip 链只是白占三成显存。 */
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!t.LoadImage(File.ReadAllBytes(path))) { GameLog.Line("解码失败 " + path); return null; }
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.Apply(false, true);      // 上传完就从内存里丢掉，21 张各 5MB 不必都留着
        return t;
    }

    public void Rebuild(float p, float offsetX) {
        int i = (int)MathX.Clamp(Mathf.Round(Mathf.Clamp(p, 0f, 100f) / STEP), 0, N - 1);
        Shown = i * STEP;

        obj.SetTexture(tex[i]);
        Quad(obj.mesh, offsetX);
        obj.Visible = tex[i] != null;
    }

    static void Quad(Mesh m, float x) {
        float W = Director.W, H = Director.H;
        m.Clear();
        m.vertices = new[] {
            new Vector3(x, 0, 0), new Vector3(x + W, 0, 0),
            new Vector3(x + W, -H, 0), new Vector3(x, -H, 0),
        };
        m.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
        var c = (Color32)Color.white;
        m.colors32 = new[] { c, c, c, c };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
    }
}
}
