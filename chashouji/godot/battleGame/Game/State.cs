using Godot;

namespace Chashouji {

/* 全场唯一状态是 S.p（查岗党进度 0~100）。它派生出 FX.phoneX，对抗线、刻度尺、
   地面分色、HUD、角色取哪一帧，全部读它。画面上没有第二个战况来源，所以
   "对抗线对不上画面"在构造上不可能发生。 */
public enum Phase { Idle, Play, Sudden, Over }

public static class S {
    public static float p = 50f, t = 0f;
    public static bool auto = true;
    public static int line = 3;        // 0 全无 / 1 原发光柱 / 2 地面战线+指针 / 3 只要指针
    public static float fA = 0f, fB = 0f;          // 火力：A=查岗党(左) B=灭迹党(右)
    public static float budA = 0f, budB = 0f;      // 发射预算：攒够一发就打一发
    public static float debA = 0f, debB = 0f, debKA = 0f, debKB = 0f;  // 注入减益：剩余秒数与折扣
    public static float clock = 0f;
    public static Phase phase = Phase.Idle;        // Idle 不跑数值（诊断与老演示模式）
    public static float edge = 0f, big = 0f, sudden = 0f;
    public static float stand = 0f;
    public static bool standUsed = false;
    public static int winner = 0;
}

public static class FX {
    public static float phoneX = K.MID, phoneY = P.phoneY;
    public static readonly float[] rowOff = new float[K.ROWS];
    public static readonly float[] rowHeat = new float[K.ROWS];
    public static readonly float[] rowImp = new float[K.ROWS];   // 冲击波，独立于常规形变
    public static float struggle = 1f, actorX = 0f, jit = 0f;
    public static float hitX = 0f, hitV = 0f;      // 角色被推开的位移与速度
    public static float punch = 0f;                // 缩放脉冲
    public static Color tint = Colors.White;
    public static float tintA = 0f;
}
}
