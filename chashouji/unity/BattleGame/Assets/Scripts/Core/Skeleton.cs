using System.Collections.Generic;
using UnityEngine;

namespace Chashouji {

public class Bone {
    public string name;
    public int idx, parent = -1;
    public float hx, hy, tx, ty, len, restAng;
    public float R, R0, capH, capT, from, strength;
    public bool rigid;
    public M2 restWorld, restWorldInv, restLocal, poseWorld, skin;
    public float restLocalAng, poseAng, delta, scale = 1f;
    public float sx, sy, parAng;   // IK 用：肩点在当前姿势下的落点
}

public class Skeleton {
    public readonly List<Bone> bones = new List<Bone>();
    public readonly float imgW, imgH;

    public Skeleton(BoneDef[] defs, float imgW, float imgH) {
        this.imgW = imgW; this.imgH = imgH;
        for (int i = 0; i < defs.Length; i++) {
            var d = defs[i];
            var b = new Bone {
                name = d.name, idx = i,
                hx = d.hu * imgW, hy = d.hv * imgH,
                tx = d.tu * imgW, ty = d.tv * imgH,
                R = d.radius * imgW,
                R0 = (d.core >= 0f ? d.core : d.radius * 0.62f) * imgW,
                capH = d.capH, capT = d.capT, from = d.from,
                strength = d.strength, rigid = d.rigid,
            };
            b.len = MathX.Hypot(b.tx - b.hx, b.ty - b.hy);
            b.restAng = Mathf.Atan2(b.ty - b.hy, b.tx - b.hx);
            b.parent = -1;
            if (d.parent != null)
                for (int k = 0; k < defs.Length; k++) if (defs[k].name == d.parent) { b.parent = k; break; }
            bones.Add(b);
        }
        foreach (var b in bones) {
            b.restWorld = M2.Trs(b.hx, b.hy, b.restAng);
            b.restWorldInv = M2.Inv(b.restWorld);
            var p = b.parent >= 0 ? bones[b.parent] : null;
            b.restLocal = p != null ? M2.Mul(p.restWorldInv, b.restWorld) : b.restWorld;
            b.restLocalAng = b.restAng - (p != null ? p.restAng : 0f);
        }
    }

    /* 顶点对某根骨的归属度 0~1。
       骨的势力范围是一个"实心核 + 过渡带"的胶囊：核半径 core 对应肢体本身的
       粗细，核内一律满权重；从 core 到 radius 才线性衰减到 0。这一层是必须的 ——
       若从骨轴就开始衰减，肢体边缘天然落在陡降区，同一条袖子里相邻两排顶点
       一个跟着手臂走、一个被 root 钉住，边就被拉成拖影；而一味加大半径去救它，
       手臂骨又会伸手够到头发和脸上去。过渡带该落在背景上，不该落在肉上。 */
    public static float BoneField(float px, float py, Bone b) {
        float vx = b.tx - b.hx, vy = b.ty - b.hy, L = vx * vx + vy * vy;
        float t = L > 0f ? ((px - b.hx) * vx + (py - b.hy) * vy) / L : 0f;
        float tc = t < 0f ? 0f : (t > 1f ? 1f : t);
        float d = MathX.Hypot(px - (b.hx + vx * tc), py - (b.hy + vy * tc));
        /* 沿轴收口是一个独立的衰减因子，不能拿它去缩整个胶囊：按比例缩半径的话，
           实心核的边界会横着扫过肢体，同一排相邻两个顶点，一个还在核里权重满格、
           一个已经掉出核外几乎归零 —— 肘和肩这种要弯折的接缝，就在那一格上被
           剪开。乘成因子之后，权重沿骨轴一格一格匀着让给下一根骨。 */
        float over = t < b.from ? (b.from - t) / b.capH : (t > 1f ? (t - 1f) / b.capT : 0f);
        float k = 1f - Mathf.Min(1f, over);
        if (k <= 0f || d >= b.R) return 0f;
        return k * (d <= b.R0 ? 1f : (b.R - d) / (b.R - b.R0));
    }

    public void Reset() { foreach (var b in bones) { b.delta = 0f; b.scale = 1f; } }

    public Bone ByName(string n) {
        foreach (var b in bones) if (b.name == n) return b;
        return null;
    }

    /* 解算所有骨的 pose，须按父先子后的顺序（bones 数组本身即拓扑序） */
    public void Solve() {
        foreach (var b in bones) {
            var p = b.parent >= 0 ? bones[b.parent] : null;
            var local = M2.Mul(b.restLocal, M2.Trs(0f, 0f, b.delta, b.scale, 1f));
            var w = p != null ? M2.Mul(p.poseWorld, local) : local;
            b.poseAng = (p != null ? p.poseAng : 0f) + b.restLocalAng + b.delta;
            /* 刚体骨只继承父骨的落点与朝向，不继承父骨为够到目标而做的轴向拉伸。
               手是刚体：前臂抻长时手指没有理由跟着变长。 */
            b.poseWorld = b.rigid ? M2.Trs(w.e, w.f, b.poseAng) : w;
            b.skin = M2.Mul(b.poseWorld, b.restWorldInv);
        }
    }

    /* 骨起点在当前姿势下的落点。手该沿哪个方向伸出去，得先知道肩落在哪。 */
    public Vector2 HeadOf(string name) {
        var b = ByName(name);
        var p = b.parent >= 0 ? bones[b.parent] : null;
        if (p == null) return new Vector2(b.hx, b.hy);
        var w = M2.Mul(p.poseWorld, b.restLocal);
        return new Vector2(w.e, w.f);
    }

    /* 两段 IK：让 fore 骨的末端落到 (tx,ty)。在 up 的父骨已解算后调用。
       够不到时沿臂轴等比拉伸（上限 maxStretch），mesh 变形的便宜要占。 */
    public void IK(string upName, string foreName, float tx, float ty, float bend,
                   float maxStretch = 1.15f, float minReach = 0.58f) {
        var up = ByName(upName); var fore = ByName(foreName);
        var par = up.parent >= 0 ? bones[up.parent] : null;
        var sh = HeadOf(upName);                      // 肩点随父骨走
        up.sx = sh.x; up.sy = sh.y;
        up.parAng = par != null ? par.poseAng : 0f;

        float L1 = up.len, L2 = fore.len;
        float dx = tx - up.sx, dy = ty - up.sy;
        float d = MathX.Hypot(dx, dy);
        float st = 1f;
        if (d > L1 + L2) { st = Mathf.Min(maxStretch, d / (L1 + L2)); L1 *= st; L2 *= st; }
        d = Mathf.Min(d, L1 + L2 - 1e-3f);
        d = Mathf.Max(d, (L1 + L2) * minReach);       // 折得比这更狠，网格就撕成面条了

        float baseAng = Mathf.Atan2(dy, dx);
        float cosA = (d * d + L1 * L1 - L2 * L2) / (2f * d * L1);
        float A = Mathf.Acos(Mathf.Max(-1f, Mathf.Min(1f, cosA)));
        float angUp = baseAng + bend * A;
        float ex = up.sx + L1 * Mathf.Cos(angUp), ey = up.sy + L1 * Mathf.Sin(angUp);
        float angFore = Mathf.Atan2(ty - ey, tx - ex);

        up.delta = angUp - up.parAng - up.restLocalAng;
        up.scale = st;
        fore.delta = angFore - angUp - fore.restLocalAng;
        fore.scale = st;
    }
}
}
