using System.IO;
using UnityEngine;

namespace Chashouji {

/* 角色层：0~100 每 5% 一张预渲染帧，按 p 取。
 *
 * 从网格变形改走帧序列，是因为两个人抢同一部手机时，肩、肘、腕的相对关系每
 * 一档都不一样 —— 这种成对的姿态用一套骨骼去凑，永远是在"手够不到机身"和
 * "肘折过头"之间取舍，而画好的帧没有这个问题：谁跪下、谁后仰、头发甩向哪
 * 边，都是画里就定死的。
 *
 * 相邻两帧交叉淡化：先把低档那张按不透明画满，再把高档那张按插值系数叠上去。
 * 两张都按系数画的话，重叠处的 alpha 会互相乘出一片半透明，人就成了能看见
 * 背景的影子。
 */
public class FrameView {
    public const int STEP = 5, N = 21;

    readonly Texture2D[] tex = new Texture2D[N];
    MeshObj lo, hi;

    public int Loaded { get; private set; }
    public int ShownLo { get; private set; }
    public float Blend { get; private set; }

    public void Init(Transform parent, int order) {
        for (int i = 0; i < N; i++) {
            tex[i] = LoadFrame(i * STEP);
            if (tex[i] != null) Loaded++;
        }
        lo = Gfx.NewMesh("actors_lo", parent, Gfx.NewAlphaMat(), order);
        hi = Gfx.NewMesh("actors_hi", parent, Gfx.NewAlphaMat(), order + 1);
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
        float f = Mathf.Clamp(p, 0f, 100f) / STEP;
        int i = (int)MathX.Clamp(Mathf.Floor(f), 0, N - 2);
        float t = f - i;
        ShownLo = i * STEP; Blend = t;

        lo.SetTexture(tex[i]);
        Quad(lo.mesh, offsetX, 1f);
        lo.Visible = tex[i] != null;

        hi.SetTexture(tex[i + 1]);
        Quad(hi.mesh, offsetX, t);
        hi.Visible = tex[i + 1] != null && t > 0.004f;
    }

    static void Quad(Mesh m, float x, float a) {
        float W = Director.W, H = Director.H;
        m.Clear();
        m.vertices = new[] {
            new Vector3(x, 0, 0), new Vector3(x + W, 0, 0),
            new Vector3(x + W, -H, 0), new Vector3(x, -H, 0),
        };
        m.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
        var c = (Color32)new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        m.colors32 = new[] { c, c, c, c };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
    }
}
}
