using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Chashouji {

/* 运行日志。按要求一律落在工程根目录的 log\ 下：
   编辑器里  <工程>\log\runtime_*.log
   出包后    <exe 同级>\log\runtime_*.log
   同时接管 Unity 的 Debug/异常输出，崩了也留得下现场。 */
public static class GameLog {
    static StreamWriter sw;
    static bool inited;

    /* 日志一律落在工程根的 log\ 下。编辑器里 dataPath 的上一级就是工程根；出包后
       dataPath 是 <exe>_Data，上一级是 Build\，还要再往上找一层才是工程根 ——
       判据是"这一级下面有 Assets 或 log"。找不到就退回 exe 同级，不至于写不出来。 */
    public static string Root {
        get {
            string dir = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            for (int i = 0; i < 3; i++) {
                if (Directory.Exists(Path.Combine(dir, "Assets")) || Directory.Exists(Path.Combine(dir, "log"))) break;
                var up = Directory.GetParent(dir);
                if (up == null) break;
                dir = up.FullName;
            }
            return Path.Combine(dir, "log");
        }
    }

    public static void Init() {
        if (inited) return;
        inited = true;
        try {
            Directory.CreateDirectory(Root);
            string f = Path.Combine(Root, "runtime_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            sw = new StreamWriter(f, false, new UTF8Encoding(true)) { AutoFlush = true };   // 带 BOM，记事本和 PowerShell 才不乱码
            Application.logMessageReceived += OnLog;
            Line($"=== 查手机 Unity 版启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            Line($"Unity {Application.unityVersion} / {SystemInfo.operatingSystem} / {SystemInfo.graphicsDeviceType}");
            Line($"屏幕 {Screen.width}x{Screen.height}");
            Line("日志目录 " + Root);
        } catch (Exception e) {
            Debug.LogWarning("日志文件打不开：" + e.Message);
        }
    }

    static void OnLog(string msg, string stack, LogType type) {
        if (type == LogType.Log) return;                      // Debug.Log 已经由 Line 写过
        Write($"[{type}] {msg}\n{stack}");
    }

    public static void Line(string s) { Debug.Log(s); Write(s); }

    static void Write(string s) {
        try { sw?.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {s}"); } catch { }
    }

    public static void Close() {
        try { Line("=== 结束 ==="); Application.logMessageReceived -= OnLog; sw?.Dispose(); } catch { }
        sw = null; inited = false;
    }
}
}
