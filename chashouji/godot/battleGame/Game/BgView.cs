using Godot;

namespace Chashouji {

/// 背景照片，铺满整块画布。全程不动，所以只画一次。
public partial class BgView : Node2D {
    Texture2D tex;

    public override void _Ready() {
        tex = ResourceLoader.Exists("res://assets/bg.jpg") ? GD.Load<Texture2D>("res://assets/bg.jpg") : null;
        if (tex == null) GD.PushWarning("assets/bg.jpg 缺失，背景留空");
        QueueRedraw();
    }

    public override void _Draw() {
        if (tex == null) { DrawRect(new Rect2(0, 0, K.W, K.H), MathX.C255(24, 26, 32)); return; }
        DrawTextureRect(tex, new Rect2(0, 0, K.W, K.H), false);
    }
}
}
