using Godot;

namespace Chashouji {

/* 无人值守截图：跑 N 帧、存一张 png、退出。
   对应网页版 tools/shot.sh 那条链路 —— 改完能立刻拿到一张图看，而不是"你打开
   看看对不对"。命令行：
       Godot.exe --path <工程> -- --shot=D:\tmp\a.png --frames=6 --selftest */
public partial class Shot : Node {
    string path;
    int wait = 4;

    public override void _Ready() {
        foreach (var a in OS.GetCmdlineUserArgs()) {
            if (a.StartsWith("--shot=")) path = a.Substring(7);
            else if (a.StartsWith("--frames=")) wait = a.Substring(9).ToInt();
        }
        SetProcess(path != null);
    }

    public override void _Process(double dt) {
        if (--wait > 0) return;
        SetProcess(false);
        /* 必须等这一帧真的画完再抓，否则拿到的是上一帧甚至空白。
           FramePostDraw 是渲染线程画完后发的，比在 _Process 里直接抓可靠。 */
        RenderingServer.Singleton.Connect(RenderingServer.SignalName.FramePostDraw,
            Callable.From(Grab), (uint)ConnectFlags.OneShot);
    }

    void Grab() {
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print(err == Error.Ok ? $"[shot] {path} {img.GetWidth()}x{img.GetHeight()}"
                                 : $"[shot] 失败 {err}");
        GetTree().Quit();
    }
}
}
