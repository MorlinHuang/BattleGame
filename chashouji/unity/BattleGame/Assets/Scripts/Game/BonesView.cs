using UnityEngine;

namespace Chashouji {

/* 标定模式：画骨骼线与关节点。骨在道具上而肉没跟过去，一眼就能分清是权重问题
   还是 IK 问题 —— 光看画面只知道"哪儿歪了"，不知道"是谁把它拽歪的"。 */
public class BonesView {
    readonly Draw2D d = new Draw2D();
    readonly MeshObj obj;

    static Color C(float r, float g, float b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);

    public BonesView(Transform parent, int order) {
        obj = Gfx.NewMesh("bones", parent, Gfx.NewAlphaMat(), order);
    }

    public void Rebuild(ActorView[] actors) {
        d.Clear();
        if (actors == null) { d.Apply(obj.mesh); return; }
        foreach (var A in actors) {
            var mdl = A.Model;
            foreach (var b in A.sk.bones) {
                if (b.name == "root") continue;
                Vector2 h = mdl.Apply(b.skin.Apply(b.hx, b.hy));
                Vector2 t = mdl.Apply(b.skin.Apply(b.tx, b.ty));
                d.Segment(h, t, 3f, C(255, 210, 40, .9f));
                foreach (var pt in new[] { h, t }) {
                    d.Poly(Circle(pt, 7f), C(20, 20, 20, .85f));
                    d.Poly(Circle(pt, 4.5f), C(255, 210, 40, 1f));
                }
            }
        }
        d.Apply(obj.mesh);
    }

    static Vector2[] Circle(Vector2 c, float r) {
        var pts = new Vector2[14];
        for (int i = 0; i < pts.Length; i++) {
            float a = Mathf.PI * 2f * i / pts.Length;
            pts[i] = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
        }
        return pts;
    }
}
}
