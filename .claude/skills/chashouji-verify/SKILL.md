---
name: chashouji-verify
description: 《查手机》改完之后怎么看到效果、怎么拿到数——胶片截图、连点压测、差值分账，以及桌面容器 Chrome 的全部必带参数与陷阱。用于"看看效果""截个图""动画对不对""是不是卡了""压测一下""怎么验证这个改动"这类请求，以及任何要向用户交代改动结果的时刻。含"这台机器上哪些结论可信"。
---

# 《查手机》自检链路

我看不到动画，只能看静态图。这条链路把"动起来的东西"变成能读的图和数。
**改完特效不拍图就说"做好了"是不成立的**——这个项目里靠想象判断对错的每一次都错了。

## 开工前必须问清

1. **要验的是形态还是性能？** 两者工具和可信度完全不同（见最后一节）。
2. **要验的东西是随机出现的吗？** 是 → 先加强制指定内容的诊断参数，别碰运气。
3. **有对照组吗？** 性能类改动必须有改前改后两组数，否则说不清有没有用。

## 一、胶片：看形态

网页版自带的诊断参数（全表见 `chashouji-web`），每格强制指定内容：

```bash
# 弹道（四件礼物各拍一条）
"...?ammostrip=10&ammoms=85&ammogift=quilt&ammoy=620"
# 气泡（四种消息轮流）
"...?bubblestrip=8&bubblems=430&p=50"
# 粒子配方
"...?fxstrip=8&fxms=60&fxpower=3&fxrecipe=feather"
# 角色帧
"...?strip=21"
```

headless 截图：

```bash
google-chrome --headless=new --no-sandbox --disable-dev-shm-usage \
  --enable-unsafe-swiftshader --hide-scrollbars \
  --disk-cache-dir=/tmp/hlc --user-data-dir=/tmp/hl_$n \
  --window-size=4800,670 --virtual-time-budget=30000 \
  --screenshot=/tmp/shot/$n.png "$URL"
```

**串行跑多条共用同一个 `--user-data-dir` 会渲染成黑图**（像素 extrema 8~233，
不报任何错）。必须**独立 profile + 共享缓存**：`--user-data-dir=/tmp/hl_$n`
配 `--disk-cache-dir=/tmp/hlc`。共享缓存顺带把加载从 120s 降到 14~22s
（49MB 的帧素材走缓存）。

**胶片总时长要覆盖被测对象的周期。** 踩过：气泡从没在弹道胶片里出现过，
因为 `ammostrip` 总时长 850ms，短于气泡首条 1.2s 的间隔。

## 二、压测：看性能

**虚拟时间下测不了耗时**——`--virtual-time-budget` 会让 `performance.now()`
在同步块中不前进，所有计时读数为 0。真实计时必须用桌面容器的真 GUI Chrome：

```bash
run() {
  U="http://127.0.0.1:40235/index.html?zoom=1&bench=1&benchframes=420$2"
  DISPLAY=:1 google-chrome --user-data-dir=/tmp/bp_all \
    --no-sandbox --disable-dev-shm-usage --enable-unsafe-swiftshader \
    --force-device-scale-factor=1 --no-first-run --no-default-browser-check \
    --disable-session-crashed-bubble \
    --window-size=1000,960 --window-position=0,0 --app="$U" >/tmp/bp_$1.log 2>&1 &
  for i in $(seq 1 80); do
    sleep 2
    DISPLAY=:1 xdotool search --name "BENCHDONE" >/dev/null 2>&1 && { echo "[$1] $((i*2))s"; break; }
  done
  sleep 1; DISPLAY=:1 scrot -o /tmp/bench_$1.png; pkill -f "bp_all"; sleep 3
}
run fin110 "&benchrate=110"
```

三个必带参数缺一不可：
- `--no-sandbox --disable-dev-shm-usage --enable-unsafe-swiftshader`
  → 缺了报"追踪或断点陷阱（核心已转储）"
- **`--force-device-scale-factor=1`** → 缺了桌面 DPI 把 canvas 放大约 2.3 倍，
  读数被截断在屏幕外

**用标题当完成信号，不要按时间猜**：页面跑完置 `document.title = 'BENCHDONE'`，
外面 `xdotool search --name` 轮询。

## 三、压测要读的三个数

```
每帧总计 / 逻辑 / 底版层 / 角色层 / 特效层   （均值 + 峰值 + 最差帧序号）
p95 / 掉帧(>16.7ms)
世界被顿帧冻住 X/N = Y%        ← 最容易漏、也最常是真凶
粒子 均X 峰Y / 弹幕 均X 峰Y
```

**冻结帧占比在耗时曲线上完全看不出来**：那些帧的渲染一切正常，只是世界没动。
实测帧均值 2.98ms、掉帧率 0.2%，而 65% 的帧是冻住的——用户报的"明显卡顿"就是它。

**基线（改造前，110ms/件）**：均 2.98ms、p95 4.60、最差 39.20ms（第 0 帧预热）、
特效层 2.59ms 占 87%、冻结 65.0%。
**现在**：110ms/件 均 3.35ms、冻结 30.2%；300ms/件 均 1.75ms、冻结 17.1%。

## 四、差值法分账

想知道哪一层在拖后腿，把该层 draw 换成空函数再跑：

```
?benchoff=ammo | part | both
```

实测：弹幕绘制 0.90ms、粒子绘制 1.27ms，**但关掉两者最差帧仍是 37.8~40.5ms**
——一步就排除了"绘制是瓶颈"这个方向。没有这一步会在优化绘制上白花几小时。

前提是 `render()` 已拆成 `renderBg`/`renderActors`/`renderFx`，
合在一个函数里只能猜。

## 五、三个已经吃过亏的陷阱

1. **Bash 工具默认 timeout 120s**，不是命令里写的 `timeout 290`。长任务必须显式传
   工具的 `timeout` 参数，否则被移到后台——而这个平台上后台任务会随本轮对话结束
   被杀掉，等于白跑。
2. **shell 脚本里 URL 写死参数再拼 `$2` 会造成参数重复**，`URLSearchParams.get`
   只取第一个，导致整轮对照实验白跑。拼 URL 前检查有没有同名参数。
3. **诊断产物默认落在 `/tmp`，容器重启就没了。** 要留给用户复看的，
   `kf_deploy_fetch` 取回后归到 `/workspace/art/chashouji/shots/` 再入库。

## 六、这台机器上哪些结论可信

- ✅ **形态 / 编排可信**——姿势推进、构图、层次遮挡、元素出场顺序、
  配色在真实底图上的可见度
- ✅ **相对占比、冻结率可信**——分账比例、顿帧占空比不受渲染后端影响
- ❌ **绝对帧率 / 流畅度不可信**——桌面容器没有 GPU，走 SwiftShader 软渲染。
  这里量到的绝对耗时是机器的，不是用户设备的
- ⚠️ **用户实际观看设备（手机/PC 浏览器）的绝对帧时间从未测过**，交付时要说明

## 七、读图判据（别靠"看着还行"）

- **抠像**：贴亮黄底整张看，单点采样会漏
- **帧序列一致性**：逐帧量 alpha 质心 x/y 与面积，找"呼吸式胀缩"
- **该不该补帧**：两帧 alpha 各腐蚀 16px 再比——差异下降是边缘底噪（补帧无用），
  不降反升是真实肢体位移（补帧有效）
- **弹道**：拖尾要能在本体**之外**看得见。看不见就是 `reach` 不够，
  不是 alpha 不够
