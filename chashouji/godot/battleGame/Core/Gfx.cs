using Godot;

namespace Chashouji {

/* 一层能每帧重建的 2D 几何。
   Unity 版这里是 GameObject + MeshFilter + MeshRenderer + Material + 自定义
   shader 五件套；Godot 里就是一个 Node2D 加两个属性，因为 CanvasItem 本来
   就是干这个的。 */
public partial class MeshLayer : Node2D {
    public readonly Draw2D D = new Draw2D();

    public override void _Draw() { D.Flush(GetCanvasItem()); }

    /// 重建完调一次。Godot 的 _Draw 不是每帧自动跑的，不调就是画面冻住而逻辑在跑
    public void Commit() => QueueRedraw();
}

public static class Gfx {
    /* 混合模式。
       Unity 版为了配合贴图采样在 shader 里把颜色预乘了 alpha，于是源因子取
       One 而不是 SrcAlpha。Godot 这两个模式对**纯顶点色**几何与它逐像素等价：
         Mix = SrcAlpha/OneMinusSrcAlpha → src.rgb*a + dst*(1-a)
         预乘 + One/OneMinusSrcAlpha    → (src.rgb*a)*1 + dst*(1-a)   一样
         Add = SrcAlpha/One             → src.rgb*a + dst
         预乘 + One/One                 → (src.rgb*a)*1 + dst         一样
       所以 Vertex2D.shader 整个不需要了 —— 少一个"运行时才按名字取、打包后
       静默失效"的东西（Unity 版踩过这个坑）。 */
    static CanvasItemMaterial mix, add;

    public static CanvasItemMaterial MixMat =>
        mix ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mix };

    /// 相加（对应网页版的 globalCompositeOperation = 'lighter'）
    public static CanvasItemMaterial AddMat =>
        add ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

    /* 画面层次全靠 ZIndex 定。Godot 的 ZIndex 取值范围是 ±4096，而且是**同一
       个 CanvasLayer 内**的排序，跨层要用 CanvasLayer.layer —— 这套画面全在
       一层里，用 ZIndex 就够。 */
    public static MeshLayer NewLayer(Node parent, string name, bool additive, int z) {
        var n = new MeshLayer { Name = name, ZIndex = z, Material = additive ? AddMat : MixMat };
        parent.AddChild(n);
        return n;
    }

    /// 给已有的 CanvasItem 挂混合模式与层序，贴图层用
    public static T Setup<T>(this T n, Node parent, string name, bool additive, int z)
            where T : CanvasItem {
        n.Name = name; n.ZIndex = z;
        n.Material = additive ? AddMat : MixMat;
        parent.AddChild(n);
        return n;
    }
}
}
