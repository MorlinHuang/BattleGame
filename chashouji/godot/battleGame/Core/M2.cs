using Godot;

namespace Chashouji {

/* 2D 仿射：x' = a*x + c*y + e，与网页版 rig.js 里的 M 一一对应。
   整套玩法的几何都在"画布坐标系"里算：原点左上、y 轴向下、1 单位 = 1 像素。

   Unity 版到这里还要在落笔时把 y 取负（Unity 世界坐标 y 向上）；Godot 的 2D
   坐标系本来就是原点左上、y 向下，所以那一步整个没有了 —— 画布坐标就是 Godot
   坐标，移植过来的数值不用改一个，也不用记"哪一层已经翻过面了"。 */
public struct M2 {
    public float a, b, c, d, e, f;

    public static readonly M2 Id = new M2 { a = 1, b = 0, c = 0, d = 1, e = 0, f = 0 };

    // 先 n 后 m
    public static M2 Mul(M2 m, M2 n) => new M2 {
        a = m.a * n.a + m.c * n.b, b = m.b * n.a + m.d * n.b,
        c = m.a * n.c + m.c * n.d, d = m.b * n.c + m.d * n.d,
        e = m.a * n.e + m.c * n.f + m.e, f = m.b * n.e + m.d * n.f + m.f,
    };

    public static M2 Inv(M2 m) {
        float id = 1f / (m.a * m.d - m.b * m.c);
        return new M2 {
            a = m.d * id, b = -m.b * id, c = -m.c * id, d = m.a * id,
            e = (m.c * m.f - m.d * m.e) * id, f = (m.b * m.e - m.a * m.f) * id,
        };
    }

    public static M2 Trs(float x, float y, float rot, float sx = 1f, float sy = 1f) {
        float co = Mathf.Cos(rot), si = Mathf.Sin(rot);
        return new M2 { a = co * sx, b = si * sx, c = -si * sy, d = co * sy, e = x, f = y };
    }

    public Vector2 Apply(float x, float y) => new Vector2(a * x + c * y + e, b * x + d * y + f);
    public Vector2 Apply(Vector2 p) => Apply(p.X, p.Y);
}

public static class MathX {
    public static float Hypot(float x, float y) => Mathf.Sqrt(x * x + y * y);
    public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    /* 帧率无关的指数趋近。min(1,dt*k) 会让节奏随帧率漂移 —— 在 60 帧的机器上
       调好的手感，到 144 帧上会快出三分之一，而测试机往往是慢的那台。 */
    public static float Approach(float dt, float k) => 1f - Mathf.Exp(-k * dt);
    /// 网页版那些 [r,g,b] 字面量都是 0~255，这里统一从那个区间进来
    public static Color C255(float r, float g, float b, float a = 1f) =>
        new Color(r / 255f, g / 255f, b / 255f, a);
}
}
