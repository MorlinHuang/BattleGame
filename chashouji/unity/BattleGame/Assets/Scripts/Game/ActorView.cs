using UnityEngine;

namespace Chashouji {

/* 一个角色：立绘 + 骨架 + 每帧 CPU 蒙皮出来的网格。
   网页版是在顶点着色器里做 LBS，这里放到 CPU 上做 —— 两个角色合计约一万顶点，
   每帧几十微秒的量级，换来的是不用为了传骨矩阵再造一套 shader 约定。 */
public class ActorView {
    public CharDef def;
    public int side;            // -1 在左(girl)，+1 在右(boy)
    public Skeleton sk;
    public RigMesh rm;
    public bool hidden;

    MeshObj obj, wire;
    Material mat;
    Vector3[] verts;
    Color32[] wireCols;
    int bendA;

    /* 手骨上"握住东西"的位置（0=腕，1=指尖）。东西是攥在手心里的，既不是顶在
       腕上，也不是挑在指尖上 —— 握点落在手心，指尖才会自然搭过机身另一侧。 */
    public const float PALM = 0.46f;
    const int COLS = 60, ROWS_MESH = 88;

    public void Init(CharDef d, Texture2D tex, int side, Transform parent, int order) {
        this.def = d; this.side = side;
        sk = new Skeleton(d.bones, tex.width, tex.height);
        rm = MeshRig.Build(tex, sk, COLS, ROWS_MESH);

        mat = Gfx.NewCharMat(tex);
        obj = Gfx.NewMesh((side < 0 ? "girl" : "boy"), parent, mat, order);
        verts = new Vector3[rm.verts];
        obj.mesh.vertices = verts;
        obj.mesh.uv = rm.uv;
        obj.mesh.triangles = rm.tris;
        obj.mesh.bounds = new Bounds(new Vector3(480f, -667f, 0f), new Vector3(4000f, 4000f, 10f));

        // 网格线视图：同一批顶点，换成线拓扑
        var wmat = Gfx.NewAlphaMat();
        wire = Gfx.NewMesh((side < 0 ? "girl" : "boy") + "_wire", parent, wmat, order + 100);
        var li = new int[rm.tris.Length * 2];
        for (int i = 0, o = 0; i < rm.tris.Length; i += 3) {
            li[o++] = rm.tris[i];     li[o++] = rm.tris[i + 1];
            li[o++] = rm.tris[i + 1]; li[o++] = rm.tris[i + 2];
            li[o++] = rm.tris[i + 2]; li[o++] = rm.tris[i];
        }
        wireCols = new Color32[rm.verts];
        for (int i = 0; i < wireCols.Length; i++) wireCols[i] = new Color(0.1f, 1f, 0.4f, 0.35f);
        wire.mesh.vertices = verts;
        wire.mesh.colors32 = wireCols;
        wire.mesh.SetIndices(li, MeshTopology.Lines, 0);
        wire.mesh.bounds = obj.mesh.bounds;
        wire.Visible = false;

        // 肘往哪边弯：照立绘静止姿势的叉积定，免得 IK 把胳膊反关节折过去
        var u = sk.ByName("armA_up"); var f = sk.ByName("armA_fore");
        bendA = ((u.tx - u.hx) * (f.ty - f.hy) - (u.ty - u.hy) * (f.tx - f.hx)) >= 0f ? 1 : -1;
    }

    public float Scale => (side < 0 ? Director.P.girlH : Director.P.boyH) / sk.imgH;

    public M2 Model {
        get {
            float s = Scale;
            float x = side < 0 ? Director.FX.girlX : Director.FX.boyX;
            return M2.Mul(M2.Trs(x, Director.P.footY, 0f, s, s),
                          M2.Trs(-def.anchorX * sk.imgW, -sk.imgH, 0f));
        }
    }

    /* 摆姿势：躯干倾角由劣势程度决定；只有靠中线那只手(armA)用 IK 握住手机，
       一人握机身一端，上下错开 —— 两只手掌各约 70px 宽，都去抓同一处，必然叠成
       一团谁也读不出；外侧那只手离手机太远，硬拽过去就是把袖子拉成面条，所以它
       整条作为刚体跟着躯干走，一点都不变形。

       IK 的末端是腕，但真正该落到机身上的是手心。直接把腕钉在机身上，整只手连同
       张开的五指就一路盖过屏幕（手骨有大半根前臂那么长）—— 屏幕是这个玩法的题材
       层主角，盖住了玩法本身就没了；而且腕要够到那么远，胳膊只能绷成一条直线还得
       再拉长，肘和袖口跟着一起抻变形。所以标定的 grip 是"机身被握住的那个点"，腕
       沿肩→握点方向后退 PALM 段手骨倒推出来，手骨再单独转向握点 —— 手是刚体，
       不跟着前臂的朝向乱指。 */
    public void Pose(float bias) {
        var inv = M2.Inv(Model);
        sk.Reset();
        float ph = side < 0 ? 0f : 1.7f;
        float lean = -bias * Director.P.lean
                   + Mathf.Sin(Director.S.t * 11f + ph) * 0.012f * Director.FX.struggle;
        sk.ByName("torso").delta = lean;
        sk.ByName("head").delta = -lean * 0.55f;
        /* 另一条手臂整条作为刚体跟着躯干走，只绕肩拧一个固定的角度：立绘里两只手
           本来就伸向同一处，不错开就叠成一团分不出指头的肉。 */
        sk.ByName("armB").delta = def.armBSwing;
        sk.Solve();

        Vector2 pp = Director.PhonePos();
        float ro = Director.FX.phoneRot;
        float co = Mathf.Cos(ro), si = Mathf.Sin(ro);
        float ox = def.gripA.x, oy = def.gripA.y;
        Vector2 g = inv.Apply(pp.x + ox * co - oy * si, pp.y + ox * si + oy * co);
        Vector2 sh = sk.HeadOf("armA_up");
        var hand = sk.ByName("armA_hand"); var fore = sk.ByName("armA_fore");
        float dx = g.x - sh.x, dy = g.y - sh.y;
        float d = MathX.Hypot(dx, dy); if (d <= 0f) d = 1f;
        float back = hand.len * PALM;
        sk.IK("armA_up", "armA_fore", g.x - dx / d * back, g.y - dy / d * back, bendA);
        sk.Solve();

        Vector2 w = sk.HeadOf("armA_hand");
        /* 手腕最多别扭到这个角度。手是刚体，硬拧过头，腕口那一圈顶点一半跟着手转、
           一半还归前臂，接缝就被剪开了 —— 手心差几个像素没人看得出来，腕上豁一道
           口子一眼就看得见。 */
        float turn = Mathf.Atan2(g.y - w.y, g.x - w.x) - fore.poseAng - hand.restLocalAng;
        hand.delta = MathX.Clamp(Mathf.Atan2(Mathf.Sin(turn), Mathf.Cos(turn)), -0.30f, 0.30f);
        sk.Solve();
    }

    /// 蒙皮：顶点 = Σ 权重 × 骨的 skin 矩阵，再乘模型矩阵，最后落到世界坐标（y 取负）
    public void Skin() {
        var mdl = Model;
        var bs = sk.bones;
        for (int v = 0; v < rm.verts; v++) {
            float x = rm.rest[v].x, y = rm.rest[v].y;
            float ax = 0f, ay = 0f;
            for (int k = 0; k < 4; k++) {
                float wgt = rm.wt[v * 4 + k];
                if (wgt == 0f) continue;
                var m = bs[rm.bone[v * 4 + k]].skin;
                ax += wgt * (m.a * x + m.c * y + m.e);
                ay += wgt * (m.b * x + m.d * y + m.f);
            }
            float wx = mdl.a * ax + mdl.c * ay + mdl.e;
            float wy = mdl.b * ax + mdl.d * ay + mdl.f;
            verts[v] = new Vector3(wx, -wy, 0f);
        }
        obj.mesh.vertices = verts;
        if (wire.Visible) wire.mesh.vertices = verts;
    }

    /// tint 的 RGB 沿用网页版 0~255 的写法
    public void SetTint(float r, float g, float b, float a) {
        mat.SetVector("_Tint", new Vector4(r / 255f, g / 255f, b / 255f, a));
    }

    public bool Visible { get => obj.Visible; set => obj.Visible = value; }
    public bool WireVisible {
        get => wire.Visible;
        set { wire.Visible = value; if (value) wire.mesh.vertices = verts; }
    }
}
}
