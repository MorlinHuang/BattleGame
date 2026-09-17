using Godot;

namespace Chashouji {

/* 根节点。命令行决定跑哪条路：
     --selftest   画笔自检图（验证 Draw2D 的每个图元）
     其余         正常玩法
   与网页版的 ?sim / ?live / ?bench 是同一个思路：诊断路径必须能被单独进入，
   否则"改完看不看得出对不对"要靠肉眼盯整局。 */
public partial class Main : Node2D {
    public override void _Ready() {
        bool selftest = false;
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--selftest") selftest = true;

        AddChild(new Shot());

        if (selftest) { AddChild(new SelfTest()); return; }

        AddChild(new Director());
    }
}
}
