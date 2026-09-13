using UnityEngine;

namespace Chashouji {

/* 调参面板，对应网页版页面下方那一排控件。用 IMGUI 画 —— 不引 uGUI，就不会有
   "预制体丢引用"这类问题，也方便出包后直接在真机上调。F1 收起。 */
public class DebugPanel : MonoBehaviour {
    bool show = true;
    Rect win = new Rect(12, 12, 396, 330);
    /// 自检截图时关掉，免得面板压着 HUD
    public static bool Muted;

    void Start() {
        // 顶上是血条和阵营名，面板挪到下方，别互相压着
        win = new Rect(12, Screen.height - 352f, 396, 330);
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.F1)) show = !show;
        if (Input.GetKeyDown(KeyCode.Space)) Director.S.auto = !Director.S.auto;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) Director.I.Nudge(60f * Time.deltaTime);
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) Director.I.Nudge(-60f * Time.deltaTime);
        /* 数字键 1~6 对应面板上那六件礼物，顺序与 FxTuning.GIFTS 一致。
           调手感的时候比伸手点按钮快得多。 */
        for (int i = 0; i < FxTuning.GIFTS.Length && i < 6; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Director.I.SendGift(FxTuning.GIFTS[i]);

        if (Input.GetKeyDown(KeyCode.Escape)) {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    void OnGUI() {
        if (Muted) return;
        if (!show) {
            GUI.Label(new Rect(12, 12, 300, 22), "F1 显示面板");
            return;
        }
        win = GUI.Window(0, win, DrawWin, "查手机 · 单一真源");
    }

    void DrawWin(int id) {
        var S = Director.S;
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("查岗党 +7", GUILayout.Width(96))) Director.I.Nudge(+7f);
        S.p = GUILayout.HorizontalSlider(S.p, 0f, 100f);
        if (GUILayout.Button("灭迹党 +7", GUILayout.Width(96))) Director.I.Nudge(-7f);
        GUILayout.EndHorizontal();

        /* 礼物按钮只负责发射：弹幕从己方屏幕外随机高度水平飞来，撞到对抗线才
           结算进度和特效。上面那两个 +7 是跳过飞行的直击，调特效时用。
           礼物本身（谁发的、多大、多快、命中炸什么）全在 FxTuning.GIFTS 里。 */
        var gifts = FxTuning.GIFTS;
        for (int row = 0; row < 2; row++) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(row == 0 ? "查岗党" : "灭迹党", GUILayout.Width(46));
            for (int i = row * 3; i < row * 3 + 3 && i < gifts.Length; i++)
                if (GUILayout.Button($"{i + 1} {gifts[i].key}")) Director.I.SendGift(gifts[i]);
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("对抗线", GUILayout.Width(46));
        Director.LineMode = GUILayout.SelectionGrid(
            Director.LineMode, new[] { "去掉", "发光柱", "地面+针", "只要针" }, 4);
        GUILayout.EndHorizontal();

        S.auto = GUILayout.Toggle(S.auto, "自动演示(空格)");

        var fv = Director.I.Frames;
        GUILayout.Label($"p={S.p:0.0}   对抗线x={Director.PhonePos().x:0}   {Director.I.Fps:0}fps");
        GUILayout.Label($"{Director.I.FrameInfo}   当前 f{fv.Shown:000}   弹幕{AmmoView.Count} 粒子{ParticleFx.Count}");
        GUILayout.Label("数值都在 FxTuning.cs：弹幕速度/转速/预警/拖尾在 Ammo 区，\n顿帧震动白闪在 Hit 区，礼物在 GIFTS 表。");
        GUI.DragWindow();
    }
}
}
