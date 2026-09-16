---
name: chashouji
description: 《查手机》弹幕直播玩法的项目总览与导航——玩法定义、代码与素材在哪、怎么起服务、当前做到哪一步、还欠什么。任何涉及"查手机""查岗党/灭迹党""拽手机""BattleGame"的活先读这个，再按它指到对应的 chashouji-art / chashouji-web / chashouji-fx / chashouji-verify / chashouji-unity。新接手这个项目也从这里开始。
---

# 《查手机》项目总览

抖音直播间弹幕互动玩法。一对情侣各拽手机一端来回拉扯，**手机的位移本身就是
对抗线/进度条**；观众刷礼物 = 扔生活物品砸对方。

- **查岗党**（绿 `[126,217,87]`，女方，在左，`from=+1`）——要看手机
- **灭迹党**（红 `[255,72,72]`，男方，在右，`from=-1`）——要删记录

屏幕上**只有角色名与阵营名，永不出现"男队/女队"字样**；聊天内容全是日常短句，
一个露骨的词都没有。胜负绑在"手机被拽到哪一端"这件具体的事上。这三条是平台
红线要求，改任何文案都不能破。

## 在哪

| | 路径 |
|---|---|
| git 仓库根 | `/workspace/art`（远端 `git@github.com:MorlinHuang/BattleGame.git` main） |
| 网页版（**单一真源**） | `/workspace/art/chashouji/web/` |
| 角色帧成品 | `web/assets/frames/` 共 89 张 960×900 PNG（49MB） |
| v13 素材管线 | `/workspace/art/chashouji/v13/`（raw 入库，中间产物可重跑） |
| 概念图历史 | `/workspace/art/chashouji/v2/ … v9/` |
| Unity 工程 | `/workspace/art/chashouji/unity/BattleGame/`（容器副本）<br>用户本机 `D:\Hylyre\BattleGame`（真正出 exe 的地方） |
| 截图归档 | `/workspace/art/chashouji/shots/` |

## 怎么跑

网页版 **http://1.14.252.30:40235/index.html**（桌面容器 port-4 = 40235），
桌面容器 `desk-zx2`，SSH 别名 `kf-deployment`（用户 op），`DISPLAY=:1`，
桌面分辨率 1656×960。

```bash
# 增量部署
timeout 100 scp -q main.js fx.js ammo.js bubble.js index.html \
  kf-deployment:/home/op/chashouji/web/

# 推 GitHub（必须带这个环境变量）
export GIT_SSH_COMMAND="ssh -i /workspace/.sshkeys/id_github -o IdentitiesOnly=yes -o UserKnownHostsFile=/workspace/.sshkeys/known_hosts"
timeout 280 git push -q origin main
```

## 做到哪一步

网页版三层都在跑：预渲染关键帧角色 + 对抗线/刻度 + 弹幕/粒子/气泡。
礼物与数值体系已落地（`2a609ae`）：礼物注入**火力**，双方火力对冲，只有净差才拽手机；
数值九档跟平台礼物、表现五档。设计与实测见 `chashouji/docs/数值设计.md`。
上一版（`0a1e678` 自发光弹道、聊天气泡、顿帧不应期）**用户评价：60 分**。

Unity 版**落后网页版两个版本**——`0a1e678` 这批（自发光、气泡、顿帧不应期、
弹幕池满修复、粒子颜色串预生成、染色包围盒）尚未同步。

## 欠的东西（按价值排）

1. **六件飞行物还是代码画的几何剪影**（`ammo.js` 的 `ITEM` 表），抱枕在原尺寸下
   读作"粉色方块"。这是离 60 分最近的一段路。替换只要改 `ITEM` 和 `SILH` 两张表，
   飞行逻辑不动。
2. **对抗线与手机位置错位**：p=0 时线在 x=566、手机在 740，差 174px。
   修它要手工标 89 帧的手机坐标。
3. 缺的 12 档在 80~87 和 95~100，相邻帧跳变仍 47~68%。
4. 弹幕飞行高度约四分之一概率从脸部经过（`ammo.js` 里 `380 + Math.random()*520`）。
5. 手机屏幕内容烧死在帧里，没法动态显示聊天记录——这是选关键帧路线时接受的代价。
6. 团结引擎许可证 `StopDate 2026-09-09` 已过期，只能在服务器本机重新激活。

## 两条工作纪律（用户定的）

- **特效先在网页测试，用户说"这个特效没问题"之后才同步 Unity。**
- **用户会自己手动改 Unity 参数**，所以代码要有明确注释，工程根的
  《特效参数说明.md》每次同步都要更新。

## 去哪查

| 要干的事 | 看 |
|---|---|
| 角色帧、抠像、概念图、补帧 | `chashouji-art` |
| 改网页代码、加礼物、调数值 | `chashouji-web` |
| 特效、打击感、配色、气泡 | `chashouji-fx` |
| 看效果、截图、压测 | `chashouji-verify` |
| 同步 Unity、出 exe | `chashouji-unity` |
