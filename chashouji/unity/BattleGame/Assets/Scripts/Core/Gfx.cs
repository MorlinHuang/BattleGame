using UnityEngine;

namespace Chashouji {

/// 一块能每帧重建的 2D 网格
public class MeshObj {
    public GameObject go;
    public Mesh mesh;
    public MeshRenderer mr;
    public MeshFilter mf;

    public void SetTexture(Texture2D t) { mr.sharedMaterial.mainTexture = t; }
    public bool Visible { get => mr.enabled; set => mr.enabled = value; }
}

public static class Gfx {
    public const string VERTEX_SHADER = "Chashouji/Vertex2D";

    static Shader vsh;

    public static Shader VertexShader {
        get { if (vsh == null) vsh = Shader.Find(VERTEX_SHADER); return vsh; }
    }

    /* 正常 alpha 叠加。shader 里把颜色预乘过 alpha 了，所以源因子取 One 而不是
       SrcAlpha —— 再乘一次等于把半透明的东西按 alpha 平方压暗。 */
    public static Material NewAlphaMat() {
        var m = new Material(VertexShader);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        return m;
    }

    /// 相加（对应网页版的 globalCompositeOperation = 'lighter'）
    public static Material NewAddMat() {
        var m = new Material(VertexShader);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        return m;
    }

    /* 画面层次全靠 sortingOrder 定，不动 renderQueue：字用的是动态字体的共享材质，
       一旦为了排序去实例化它，字体图集重建后实例还抓着旧贴图，字就整片消失。 */
    public static MeshObj NewMesh(string name, Transform parent, Material mat, int order) {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = new Mesh { name = name };
        /* 32 位索引。默认的 16 位上限是 65535 个顶点，平时够用，但粒子池满的时候
           不够：一个带描边的碎片是圆角矩形填充 20 个顶点加一圈描边 80 个，1200
           颗就是十二万。超了 Unity 会静默截断，画面上表现为"炸得最狠的时候有一
           半粒子不见了"，而且只在极端情况下复现。 */
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = mat;
        mr.sortingOrder = order;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        return new MeshObj { go = go, mesh = mesh, mr = mr, mf = mf };
    }
}
}
