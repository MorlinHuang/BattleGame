---
name: chashouji-unity
description: 把《查手机》网页版的改动同步到 Unity 工程并出 exe。用于"同步到 Unity""移植""出个 exe""Unity 里效果不对""编辑器正常但打包后没了""校验编译"这类请求。网页版是单一真源，这里只做呈现层的逐字对照搬运。含真实文件对应表与四类已踩过的坑。
---

# 《查手机》Unity 移植

工程：`/workspace/art/chashouji/unity/BattleGame/`（容器副本）/
`D:\Hylyre\BattleGame`（用户本机，真正出 exe 的地方）。Unity **2022.3.50f1c1**（团结引擎）。

## 铁律

**网页版是单一真源，C# 逐字对照搬，不另起炉灶。** 网页原型是调参和验证的地方，
Unity 只做呈现；两边各存一份标定数据必然漂移。

**同步时机由用户定**：特效先在网页测试，**用户说"这个特效没问题"之后才同步**。
不要提前搬——网页还在改的东西搬过去就是两份都要改。

## 开工前必须问清

1. **网页版这一版用户认可了吗？** 没认可就不搬。
2. **这次搬哪几个文件？** 先列出网页侧改了什么，一一对应。
3. **动的是用户本机还是容器副本？** 动用户电脑要用 `mcp__kf-device__*`，
   且只在请求明确涉及他电脑时。

## 文件对应表（实际清单，不是设想）

| 网页 | Unity |
|---|---|
| `main.js` 的 `P` / `GIFT` / 常量 | `Assets/Scripts/Game/FxTuning.cs`（**集中，供用户手改**） |
| `main.js` 的 `RECIPE` | `Game/Recipes.cs` |
| `main.js` 主循环与 `derive` | `Game/Director.cs` |
| `main.js` 的角色帧绘制 | `Game/FrameView.cs` |
| `main.js` 的 `drawLine` / `drawFrontMark` / `drawRuler` / `drawGround` / `drawHUD` | `Game/LineView.cs` / `FrontMarkView.cs` / `RulerView.cs` / `GroundView.cs` / `HudView.cs` |
| `fx.js` | `Game/ParticleFx.cs` |
| `ammo.js` | `Game/AmmoView.cs` |
| `bubble.js` | `Game/BubbleView.cs`（**尚未建，本批要新增**） |
| canvas 2D 画笔 | `Core/Draw2D.cs` / `Gfx.cs` / `Label.cs` + `Shaders/Vertex2D.shader` |
| 2D 仿射 | `Core/M2.cs` |
| 调试面板 | `Game/DebugPanel.cs` |
| — | `Util/GameLog.cs`（日志）/ `Util/ShotRunner.cs`（截图）/ `Editor/BuildTool.cs`（构建） |

## 当前待同步的一批（`0a1e678`）

- `FxTuning.cs`：顿帧不应期比例、`AURA` 色晕色表、`TRAIL=16`、`reach` 系数
- `ParticleFx.cs`：`HitStop` 加 `cool`/`lvl` 不应期；粒子颜色与贴图改在 spawn 预生成
- `AmmoView.cs`：色晕贴图、`SILH` 轮廓表、按距离回溯 `backAt`、池满回收最老
- **新建 `BubbleView.cs`**
- `Director.cs`：接入 Bubble；点赞级不顿帧；染色只填角色矩形
- 《特效参数说明.md》：更新到这批

## 四类已踩过的坑

### 1. 运行时才引用的资源会被剥离

自定义 shader、系统字体字形这类**只在运行时按名字取**的东西，编辑器里一切正常，
**打成 exe 后静默失效**（不报错，就是没了）。必须放进 Resources /
Always Included Shaders，或在场景里留一个引用。

### 2. 纹理：尺寸和路径决定内存差六倍

**DXT 压缩要求边长是 4 的倍数。** 画布高 1334 不是，只能退回 RGBA32——
93 张帧就是 **454MB**。裁成 **960×900** 的人物横带、并从 `StreamingAssets` 改走
**`Assets/Resources/frames/`**（编辑器导入时就压好，运行时不用解码也不占四倍显存）
之后是 **77MB**。引擎侧按 `FRAME_TOP = 308` 贴回画布。

### 3. 编辑器占着工程时，用 Roslyn 独立编译校验

**源文件列表必须扫全 `Assets`，不能只扫 `Assets\Scripts`。** 漏掉 `Assets\Editor`
时，删运行时符号（如 `Gfx.CHAR_SHADER`）不会被发现，**退出码 0 也是假的**，
用户切回编辑器立刻 CS0117。

引用只需 `Editor\Data\Managed\UnityEngine\*.dll`（87 个）+ netstandard——
这个目录里本来就含全部 `UnityEditor.*Module.dll`。**不要**再另加
`Managed\UnityEditor.dll` 或 `Managed\UnityEditor.CoreModule.dll`，
会报 `CS0433 类型"MenuItem"同时存在于…`。

### 4. 仓库

`.gitignore` 已补 Unity 生成物规则（`Library/` `Temp/` `obj/` `*.csproj` 等）——
**Library 能到 GB 级**，且 Unity 打开工程会自动重建，不要入库。

## 交付约定（用户明确要求）

- **log 单独放在 `D:\Hylyre\BattleGame\log`**
- **工程根的《特效参数说明.md》每次同步都要更新**。它现有七节：
  想改什么去哪个文件 / 三种弹幕样式 / 礼物表 `FxTuning.GIFTS` /
  两条不好违反的规矩 / 运行时操作 / 与网页版的对应 / 已知的取舍。
  用户了解 Unity 脚本和插件、会自己手动改参数，**代码要有明确注释**。
- 团结引擎许可证 `StopDate 2026-09-09` 已过期，只能在服务器本机用账号密码重新激活。

## 同步自检

- [ ] 网页侧这一版用户认可了？
- [ ] 改动的每个网页文件都有对应 C# 文件改到？
- [ ] 数值是逐字搬的，没有"顺手调一下"？
- [ ] 新建的 shader / 字体被 Resources 或 Always Included 覆盖了？
- [ ] 纹理边长 4 的倍数、走 Resources？
- [ ] Roslyn 校验扫的是 `Assets` 全目录？
- [ ] 《特效参数说明.md》更新了？
