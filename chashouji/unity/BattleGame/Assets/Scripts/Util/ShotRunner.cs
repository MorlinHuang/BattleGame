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

    /* -shotfx <礼物序号> 时改走弹幕胶片模式：发一件礼物，然后每隔固定时间抓一
       格，把从出场到命中到炸开的整条过程按时间摊开。
       特效是瞬时的，单张截图验证不了任何东西 —— 顿帧生没生效、冲击波传没传出
       去、爆炸有没有迟到，都只有把同一次命中的前后若干毫秒并排摆着才看得出。
       与网页版的 ?ammostrip 一一对应，两边可以逐格比。 */
    int fxGift = -1;
    int fxN = 8;
    float fxMs = 85f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() {
        var args = System.Environment.GetCommandLineArgs();
        int at = System.Array.IndexOf(args, "-shot");
        if (at < 0) return;
        var go = new GameObject("ShotRunner");
        var sr = go.AddComponent<ShotRunner>();
        if (at + 1 < args.Length) sr.dir = args[at + 1];
        int fat = System.Array.IndexOf(args, "-shotfx");
        if (fat >= 0 && fat + 1 < args.Length && int.TryParse(args[fat + 1], out int gi)) sr.fxGift = gi;
        int nat = System.Array.IndexOf(args, "-shotfxn");
        if (nat >= 0 && nat + 1 < args.Length && int.TryParse(args[nat + 1], out int nn)) sr.fxN = nn;
        int mat = System.Array.IndexOf(args, "-shotfxms");
        if (mat >= 0 && mat + 1 < args.Length && float.TryParse(args[mat + 1], out float mm)) sr.fxMs = mm;

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

        if (fxGift >= 0) { yield return FxStrip(); yield break; }

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

    IEnumerator FxStrip() {
        /* 固定时间步。不设的话每格之间那次 PNG 编码要几十上百毫秒，Time.deltaTime
           会把这段真实耗时算进去，游戏一下推进过头 —— 抓出来的格子间隔就不是
           fxMs 而是"编码用了多久"，每次跑还都不一样。 */
        Time.captureDeltaTime = 1f / 60f;
        int step = Mathf.Max(1, Mathf.RoundToInt(fxMs / 1000f * 60f));
        var g = FxTuning.GIFTS[Mathf.Clamp(fxGift, 0, FxTuning.GIFTS.Length - 1)];

        Director.S.auto = false;
        Director.S.p = 50f;
        for (int k = 0; k < 150; k++) Director.Derive(1f / 60f);
        yield return null;

        Director.I.SendGift(g);
        GameLog.Line($"弹幕胶片：{g.key}  {fxN} 格 × {fxMs}ms（每格 {step} 帧）");

        float el = 0f;
        for (int i = 0; i < fxN; i++) {
            int adv = i == 0 ? 1 : step;     // 第一格只推一帧，命中瞬间本来就是空的
            for (int k = 0; k < adv; k++) yield return null;
            el += adv / 60f;

            yield return new WaitForEndOfFrame();
            /* 用 CaptureScreenshotAsTexture 而不是 CaptureScreenshot：后者异步落盘，
               得空转二十来帧等它写完，而那二十帧游戏还在跑，胶片的格间距就废了。 */
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string f = Path.Combine(dir, $"unity_fx{fxGift}_{i:00}_{Mathf.RoundToInt(el * 1000f)}ms.png");
            File.WriteAllBytes(f, tex.EncodeToPNG());
            Destroy(tex);
            GameLog.Line($"  +{Mathf.RoundToInt(el * 1000f)}ms  弹幕{AmmoView.Count} 粒子{ParticleFx.Count}");
        }
        Time.captureDeltaTime = 0f;
        GameLog.Line("弹幕胶片完成，退出");
        GameLog.Close();
        Application.Quit();
    }
}
}
