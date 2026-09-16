---
name: chashouji-web
description: 《查手机》网页版的代码结构与改法——文件分工、几何常量、参数表 P、状态 S、派生 FX、礼物表 GIFT、URL 诊断参数、部署命令。用于"改网页""加一件礼物""调数值""对抗线位置不对""帧和线对不上""加个诊断参数""部署上去"这类请求。网页版是这个项目的单一真源，Unity 逐字对照搬。
---

# 《查手机》网页版

`/workspace/art/chashouji/web/`，部署在 **http://1.14.252.30:40235/index.html**。
**这里是单一真源**：所有数值和手感在这里调定，Unity 只做呈现。

## 开工前必须问清

1. **改的是数值还是结构？** 数值一律进 `P` 表或 `GIFT`/`RECIPE` 表，不要散落到
   函数里——用户会自己手动改参数，找不到就等于没有。
2. **改完要不要同步 Unity？** 默认**不同步**，等用户说"这个特效没问题"。
3. **怎么验证？** 见 `chashouji-verify`。改完至少 `node --check` 过一遍。

## 文件分工

```
index.html   画布 + 礼物按钮 + 调试面板 + script 引入顺序 + 说明文字
main.js      常量 / P / S / FX / derive / impact / RECIPE / GIFT / 各层绘制 / 主循环 / URL 分支
fx.js        Particles —— 粒子、屏幕震动、全屏闪、顿帧
ammo.js      Ammo —— 飞行物与弹道（色晕/拖尾/残影/本体）
bubble.js    Bubble —— 手机上方的聊天气泡
```

引入顺序 `fx → ammo → bubble → main`，改 index.html 时别打乱。

## 几何常量（`main.js` 顶部，改动牵一发动全身）

```js
const W = 960, H = 1334;
const TOP = 128, BOT = 1232, MID = 480;          // 对抗线的上下端与中位
const FRAME_TOP = 308, FRAME_W = 960, FRAME_H = 900;  // 帧纹理只覆盖人物那条横带
const ROWS = 15;                                  // 对抗线按 15 行采样
const GREEN = [126, 217, 87], RED = [255, 72, 72];
```

实测位置：手机 y≈650，两人脸 y≈520~610，**墙面空白区 y=150~470**，HUD 血条 y=74~105。
要往画面上加东西先量底版，不要拍脑袋（做法见 `chashouji-fx` 的气泡一节）。

## 三张表：改数值只改这里

**`P` —— 参数表**（几何 + 手感）：`curve` 进度→位移的非线性 / `half` 对抗线最大偏移 /
`drag` 角色整体跟随比例 / `phoneY=560` 手机高度锚 / `rug` 地毯四角 /
`waveSpread` `waveDecay` 冲量传播 / `hitK` `hitDamp` 角色被推开的弹力与阻尼 /
`punchDecay` `tintDecay` 脉冲与染色衰减。

**`GIFT` —— 礼物表**（加/改礼物只动这张）：

```js
hairpin: { from:+1, style:'volley', item:'hairpin', r:22, n:8, power:1, recipe:'star',    push:1   }
pillow:  { from:+1, style:'single', item:'pillow',  r:56,      power:2, recipe:'feather', push:20  }
bouquet: { from:+1, style:'heavy',  item:'bouquet', r:76,      power:3, recipe:'petal',   push:230 }
ringbox: { from:+1, style:'heavy',  item:'ringbox', r:64,      power:4, recipe:'bloom',   push:600 }
seed:    { from:-1, style:'volley', item:'seed',    r:21, n:8, power:1, recipe:'star',    push:1   }
gamepad: { from:-1, style:'single', item:'gamepad', r:52,      power:2, recipe:'debris',  push:20  }
milktea: { from:-1, style:'heavy',  item:'milktea', r:72,      power:3, recipe:'splash',  push:230 }
photo:   { from:-1, style:'heavy',  item:'photo',   r:68,      power:4, recipe:'memory',  push:600 }
```

`from`：+1 查岗党（左）/ -1 灭迹党（右）。三种 `style` 的速度在 `ammo.js` 的
`SPEED = { volley:1400, single:900, heavy:850 }`。档 4 另外由 `exec` 把 `r` 乘 1.8、
速度减半——它买的是一段没人打断的时间，飞快就把这段时间还回去了。

`quilt` / `box`（棉被、外卖箱）留在 `ammo.js` 的 `ITEM`/`SILH` 里作**备选池**，
当前未启用；换回档 3 只需改 `ITEM_OF` 一行。弃用原因：两件剪影只差 1.03 倍。

**`RECIPE` —— 特效配方表**：`thud` / `feather` / `star` / `debris` /
`petal` / `splash` / `bloom` / `memory` 八种，礼物只引用配方名，不各写各的。

**同档两件的剪影长宽比至少要差 1.4 倍。** 飞在半空观众只看得到纯色剪影（`SILH` 层），
形状太像就会出现"屏幕上在对撞，但我不知道谁占上风"。挑物品时先算这个比值。

加一件新礼物要动的全部地方：`GIFT` 一行 + `ammo.js` 的 `ITEM`/`SILH`/`AURA`/`TAIL`
各一行 + `index.html` 一个按钮。**引擎本体一行都不该改。**
换某一档飞什么东西：只改 `main.js` 的 `ITEM_OF` 一行，数值表 `SHOP` 一个字都不用动。

## 状态与派生：渲染只读不写

```js
const S = { p: 50, t: 0, auto: true, line: 3 };   // p 是唯一的真实状态
const FX = { phoneX, phoneY, rowOff[], rowHeat[], rowImp[], struggle, actorX, jit,
             hitX, hitV, punch, tint, tintA };     // 全部由 derive(dt) 算出
```

`derive(dt)` 每帧从 `S.p` 算出这一帧所有位置与强度，绘制函数只读 `FX`。
**不要在绘制里改状态**——那是"帧和线对不上"这类 bug 的来源。

对抗线在任意高度的横坐标：
```js
const frontAt = (y) => FX.phoneX + sampleRow(FX.rowOff, y) + sampleRow(FX.rowImp, y);
const phonePos = () => [frontAt(FX.phoneY) + FX.jit, FX.phoneY];
```
`sampleRow` 是 15 行之间的 Catmull-Rom 插值。**弹幕命中的是对抗线在它自己那个
高度上的横坐标**，所以不同高度飞来的弹幕会让线在不同位置抖——这是设计，不是 bug。

## 帧率无关插值（三个项目里各踩过一次）

```js
const approach = (dt, k) => 1 - Math.exp(-k * dt);
```
**绝对不要写 `Math.min(1, dt*k)`**——它让节奏随帧率漂移，而低帧率测试机会掩盖
高帧率才暴露的缺陷。`proto/index.html` 里还剩两处没改，是已知待办。

## 渲染分层

```
renderBg      底版照片 960×1334
renderActors  角色帧（按 p 取最近一张硬切）+ 染色
renderFx      对抗线/指针 → 刻度尺 → 气泡 → 弹幕 → 粒子 → HUD → 全屏闪
```

**拆成三段是为了能分层计时**（`?bench` 靠它分账），合在一个函数里只能猜哪层慢。

气泡在弹幕**之下**：它贴在后面那堵墙上，弹幕是前景。

## URL 诊断参数（全表）

| 参数 | 作用 |
|---|---|
| `?p=<0-100>` | 强制进度（自动关掉 auto） |
| `?auto=0` | 停掉自动推进 |
| `?zoom=1` | 画布按原始宽度显示（截图用） |
| `?line=0..3` | 对抗线样式：0 全无 / 1 原发光柱 / 2 地面战线+指针 / 3 只要指针 |
| `?strip=N` | N 档角色帧并排成胶片（2~21） |
| `?fxstrip=N&fxms=M&fxpower=1..3&fxrecipe=thud\|feather\|star\|debris` | 粒子配方胶片 |
| `?ammostrip=N&ammoms=M&ammogift=<礼物名>&ammoy=<高度>` | 弹道胶片 |
| `?bubblestrip=N&bubblems=M` | 气泡胶片，四种消息轮流强制推 |
| `?linestrip=1` | 对抗线胶片 |
| `?bench=1&benchframes=N&benchrate=M` | 连点压测 |
| `?benchoff=ammo\|part\|both` | 关掉某层做差值分账 |

**新加诊断模式时照这个套路**：每格强制指定内容，不要碰运气等随机——
气泡按一两秒随机冒，混在别的胶片里拍不到语音条和撤回提示。

## 数值层（两层模型，2026-09-16 加）

**礼物不直接改进度。** `S.p` 是双方火力净差积分出来的：

```js
FA += push;                              // 礼物注入火力（giveGift）
const burn = NUM.BURN * Math.min(FA, FB); // 对冲，由火力少的一方定速
FA -= (burn + NUM.LOSS * FA) * dt;        // FB 同
S.p += (FA - FB) / 1000 * NUM.DPS * dt;   // 只有净差才动手机
```

稳态有个干净的性质：**净差 = 两边注入速度差 ÷ `NUM.LOSS`**（对冲项在两边相同，
推导时直接消掉）。所以手机快慢只取决于"刷礼物的速度差"，与总量无关。
调局长就调 `NUM.DPS`，调差值量级就调 `NUM.LOSS`，两个旋钮互不干扰。

`battle(dt)` 算 `S.p`，`derive(dt)` 由 `S.p` 算画面。**别把它们混在一起** ——
上一版最大的错误就是让礼物直接改了 `S.p`，等于把火力这一层整个删掉。

三条不能动的规矩：
- `onHit` 只演出、**不改进度**。让命中再推一次等于同一份伤害算两遍，
  还会破坏"两边都刷时手机不动"这条最要紧的手感。
- **濒死护盾只能按比例减伤，不能扣火力存量。** 扣存量会构成正反馈：
  护盾吃火力 → `min` 变小 → 对冲变弱 → 优势方火力不再被烧 → 差值反而扩大。
- **净差要有上限**（`NUM.MAXDPS`）。极端投入下净差能到四万，十四秒推完全程。

礼物是**两张表**：`SHOP`（九件平台礼物，管数值）× `GIFT`（八件物品，管表现），
用 `tier` 和 `ITEM_OF` 连接。加礼物改 `SHOP` 一行。
物品方案是**档 1~2 客厅现场 + 档 3~4 甜蜜反击**（发卡瓜子/抱枕手柄 → 玫瑰奶茶 →
戒指盒相框），依据与备选池见 `docs/礼物设计.md`。

## 新增诊断参数

| 参数 | 作用 |
|---|---|
| `?sim=1&simA=&simB=&simT=` | 纯数值快进，不渲染。两秒跑完一局，调参不用看画面 |
| `?live=1&liveA=&liveB=&livet=` | 真实对局，按给定注入率快进到指定秒数 |
| `&liveGift=drop&liveAt=&liveSide=` | 指定时刻送一件礼物 |
| `&liveFreeze=1` | 冻住进度，火力与弹幕照跑 |
| `&liveStop=1` | 冻住整个世界（截处决这种只存在零点几秒的东西） |
| `&v=<任意值>` | 绕过 js 的磁盘缓存，见 chashouji-verify |

## 部署

```bash
node --check main.js && node --check ammo.js && node --check fx.js && node --check bubble.js
timeout 100 scp -q main.js fx.js ammo.js bubble.js index.html kf-deployment:/home/op/chashouji/web/
```
桌面容器上 `python3 -m http.server 40235` 常驻在 `/home/op/chashouji/web`。

## 改完别忘

- `index.html` 底部那段**说明文字要跟着改**——它是给用户看的，代码改了文字没改
  就是错的（已发生过：改成按距离回溯 16 帧后，说明里还写着"记了八帧轨迹"）。
- 参数有改动 → Unity 的《特效参数说明.md》下次同步时一起更新。
