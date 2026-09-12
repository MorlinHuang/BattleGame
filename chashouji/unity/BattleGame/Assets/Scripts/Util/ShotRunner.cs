using System.Collections;
using System.IO;
using UnityEngine;

namespace Chashouji {

/* 出包自检：带 -shot 启动时，依次把 p 定到几个档位各截一张图，然后退出。
   移植对不对，靠读代码是读不出来的 —— 得把三档画面摆出来跟网页版比。
   用法：BattleGame.exe -shot D:\...\shots -shotp 5,50,95 -screen-width 960 -screen-height 1334 */
public class ShotRunner : MonoBehaviour {

    string dir = "shots";
    float[] ps = { 5f, 50f, 95f };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() {
        var args = System.Environment.GetCommandLineArgs();
        int at = System.Array.IndexOf(args, "-shot");
        if (at < 0) return;
        var go = new GameObject("ShotRunner");
        var sr = go.AddComponent<ShotRunner>();
        if (at + 1 < args.Length) sr.dir = args[at + 1];
        int pat = System.Array.IndexOf(args, "-shotp");
        if (pat >= 0 && pat + 1 < args.Length) {
            var parts = args[pat + 1].Split(',');
            var list = new System.Collections.Generic.List<float>();
            foreach (var s in parts) if (float.TryParse(s, out float v)) list.Add(v);
            if (list.Count > 0) sr.ps = list.ToArray();
        }
        DontDestroyOnLoad(go);
    }

    IEnumerator Start() {
        Directory.CreateDirectory(dir);
        Application.targetFrameRate = 60;
        DebugPanel.Muted = true;
        yield return null; yield return null;      // 等 Director 把场子搭起来
        foreach (var p in ps) {
            Director.S.auto = false;
            Director.S.p = p;
            // 指数趋近要收敛到位才截图，否则拍到的是还在往目标滑的中间态
            for (int k = 0; k < 150; k++) Director.Derive(1f / 60f);
            yield return new WaitForEndOfFrame();
            yield return null;
            string f = Path.Combine(dir, $"unity_p{p:00}.png");
            ScreenCapture.CaptureScreenshot(f);
            GameLog.Line("截图 " + f);
            for (int k = 0; k < 20; k++) yield return null;   // CaptureScreenshot 是异步落盘
        }
        GameLog.Line("自检截图完成，退出");
        GameLog.Close();
        Application.Quit();
    }
}
}
