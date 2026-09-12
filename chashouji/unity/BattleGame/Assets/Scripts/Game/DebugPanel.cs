using UnityEngine;

namespace Chashouji {

/* 调参面板，对应网页版页面下方那一排控件。用 IMGUI 画 —— 不引 uGUI，就不会有
   "预制体丢引用"这类问题，也方便出包后直接在真机上调。F1 收起。 */
public class DebugPanel : MonoBehaviour {
    bool show = true;
    Rect win = new Rect(12, 12, 330, 210);
    /// 自检截图时关掉，免得面板压着 HUD
    public static bool Muted;

    void Start() {
        // 顶上是血条和阵营名，面板挪到下方，别互相压着
        win = new Rect(12, Screen.height - 232f, 330, 210);
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.F1)) show = !show;
        if (Input.GetKeyDown(KeyCode.Space)) Director.S.auto = !Director.S.auto;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) Director.I.Nudge(60f * Time.deltaTime);
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) Director.I.Nudge(-60f * Time.deltaTime);
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

        S.auto = GUILayout.Toggle(S.auto, "自动演示(空格)");

        var fv = Director.I.Frames;
        GUILayout.Label($"p={S.p:0.0}   对抗线x={Director.PhonePos().x:0}   {Director.I.Fps:0}fps");
        GUILayout.Label($"{Director.I.FrameInfo}   当前 f{fv.Shown:000}");
        GUILayout.Label("角色是 0~100 每 1% 一张，28 档生图画的、其余光流插\n出来，按 p 取最近的一张硬切。");
        GUI.DragWindow();
    }
}
}
