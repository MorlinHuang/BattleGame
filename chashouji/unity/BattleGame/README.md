# 查手机 · Unity 版（BattleGame）

男女对抗弹幕直播玩法《查手机》。这份工程是网页版（`/workspace/art/chashouji/web`，
线上 http://1.14.252.30:40235/index.html ）的 Unity 移植，玩法逻辑、骨骼标定、
权重规则、数值全部逐条同步，不是重写。

## 打开与运行

* 编辑器：`D:\Hylyre\UnityEditor\2022.3.50f1c1\Editor\Unity.exe`
* 双击 `run_editor.bat` 打开（编辑器日志会落到 `log\editor.log`）
* 第一次打开后执行菜单 **查手机 → 整备工程（色彩空间 + shader + 场景）**，
  它做三件事：色彩空间设为 Gamma、把两个自写 shader 加进 Always Included Shaders、
  生成 `Assets/Scenes/Main.unity` 并填进 Build Settings。
* 之后直接 Play。场景里其实只有一个 `Director`，整台戏由它在 `Awake` 里用代码搭
  起来 —— 场景文件里没有引用，也就不会出现"引用丢了、Play 一下白屏"。
* 出包：双击 `build_windows.bat`，产物在 `Build\BattleGame.exe`，日志在 `log\build.log`。

## 自检截图

出包后带 `-shot` 启动，会依次把 p 定到几档各截一张图然后自己退出 —— 移植对不对
读代码是读不出来的，得把画面摆出来跟网页版比：

```
Build\BattleGame.exe -shot D:\Hylyre\BattleGame\shots -shotp 5,50,95 ^
  -screen-width 720 -screen-height 1000 -screen-fullscreen 0
```

截图落在 `shots\`，运行日志落在 `log\runtime_*.log`。

## 操作

| 键 | 作用 |
|---|---|
| 滑杆 / ← → 或 A D | 拨动进度 p（0~100） |
| 空格 | 自动演示开关 |
| W | 网格线 |
| B | 骨骼线 |
| F1 | 收起/展开面板 |
| Esc | 退出 |

## 结构

```
Assets/Scripts/Core/     与玩法无关的底座
  M2.cs          2D 仿射矩阵（画布坐标系：原点左上、y 向下、1 单位 = 1 像素）
  RigData.cs     骨骼标定数据（归一化 uv），与网页版 rigdata.js 逐字同步
  Skeleton.cs    骨架：权重场 BoneField、正解 Solve、两段 IK
  MeshRig.cs     从立绘生成网格与权重（覆盖率剔除、跨臂剔除、权重突变剔除）
  Draw2D.cs      即时模式 2D 网格构造器（矩形/圆角矩形/渐变/描边/光晕）
  Gfx.cs         材质与网格对象工厂，画面层次靠 sortingOrder
  Label.cs       画布坐标系里的一行字（系统动态字体，带描边）
Assets/Scripts/Game/
  Director.cs    单一真源：S.p → FX.phoneX → 手机/双手 IK/对抗线/刻度/地面/HUD
  ActorView.cs   角色：骨架 + 每帧 CPU 蒙皮 + 握点 IK
  LineView.cs    对抗线三条带 + 落点光晕（相加混合）
  GroundView.cs  地面辉光（地毯切成网格逐顶点上色）
  PhoneView.cs   手机机身与屏幕（画在角色层之上，屏幕永不被手挡住）
  RulerView.cs   固定刻度 + 滑动指针
  HudView.cs     两条血条 + 阵营名 + 百分比
  BonesView.cs   骨骼调试视图
  DebugPanel.cs  IMGUI 调参面板
Assets/Scripts/Util/GameLog.cs   运行日志 -> log\runtime_*.log
Assets/Shaders/  Vertex2D（顶点色 2D）、CharTint（角色压暗）
Assets/StreamingAssets/art/      bg.jpg / girl.png / boy.png
log/             运行、编辑器、出包日志都在这里
```

## 已验证

* `-batchmode` 编译零错误；出包 `Build\BattleGame.exe`（68MB）成功。
* 出包后跑自检：p=5 / 50 / 95 三档画面与网页版一致 —— 双手握住机身侧缘、屏幕不
  被挡、对抗线与刻度指针跟着 p 走、劣势方压暗、地面辉光按领先幅度铺开。
* 网格 7706 三角形 / 4402 顶点（两人合计），与网页版同一套剔除规则算出来的。
* 日志：`log\verify.log`（编译校验）、`log\build.log`（出包）、
  `log\runtime_*.log`（运行）、`log\editor.log`（用 run_editor.bat 开编辑器时）。

## 移植时必须知道的几件事

1. **坐标系**：所有玩法数值都在"画布坐标系"里算（960×1334，原点左上，y 向下），
   跟网页版逐字一致；只在最后落到 Unity 世界坐标时把 y 取负。因此三角形绕序整体
   翻了个面，两个 shader 都是 `Cull Off`。

2. **权重是运行时刷的，不用 Sprite Editor**。骨骼数据是 `RigData.cs` 里的归一化
   uv，网格和权重在 `MeshRig.Build` 里按"顶点到骨轴的距离"算出来 —— 于是标定数据
   与运行时永远一致，也不需要在 GUI 里手工刷权重。

3. **root 的 strength 是所有骨的地盘门槛**。root 的实心核罩住整个人、权重恒为 1，
   乘上 strength 就是 0.50；别的骨只有把 `field^3 × strength` 抬过 0.50 才算真的
   拥有那块皮。所以一根骨的实际地盘远小于它的 radius —— 袖口、指尖归不到自己名下
   时，要放大 core 或抬 strength，加 radius 只会把隔壁那只手一起吃进来。

4. **手是握住机身侧缘的，不是盖在机身上**。IK 的末端是腕，但该落到机身上的是手心：
   腕由握点沿"肩→握点"方向后退 `PALM × 手骨长` 倒推，手骨再单独转向握点，腕的转角
   有 ±0.3rad 上限。站位与手机高度是配着这个握法调出来的，改一个就要重排另一个。

5. **色彩空间**：网页版是画布 2D，颜色按 sRGB 直接写。工程应设为 Gamma；若留在
   Linear，shader 里已做 sRGB→线性换算，观感接近但不逐像素相同。
