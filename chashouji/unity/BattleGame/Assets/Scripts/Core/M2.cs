using UnityEngine;

namespace Chashouji {

/* 2D 仿射：x' = a*x + c*y + e，与网页版 rig.js 里的 M 一一对应。
   整套玩法的几何都在"画布坐标系"里算：原点左上、y 轴向下、1 单位 = 1 像素，
   跟网页版逐字一致；只在最后往 Unity 世界坐标落笔时把 y 取负。 */
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
    public Vector2 Apply(Vector2 p) => Apply(p.x, p.y);
}

public static class MathX {
    public static float Hypot(float x, float y) => Mathf.Sqrt(x * x + y * y);
    public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    // 帧率无关的指数趋近：min(1,dt*k) 会让节奏随帧率漂移
    public static float Approach(float dt, float k) => 1f - Mathf.Exp(-k * dt);
}
}
