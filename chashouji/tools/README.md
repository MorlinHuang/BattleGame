# 诊断脚本

都在桌面容器上跑（`ssh kf-deployment`，先 `scp` 到 `/tmp/`）。

| 脚本 | 干什么 |
|---|---|
| `sim.sh` | 纯数值快进，核对局长与净差。不渲染，两秒一局 |
| `shot.sh` | 真实对局截图，可快进到任意战况、指定时刻送礼物 |
| `runbench.sh` | GUI Chrome 真实计时压测 |

```bash
scp chashouji/tools/*.sh kf-deployment:/tmp/
ssh kf-deployment 'bash /tmp/sim.sh "simA=23&simB=11.5" "simA=230&simB=230&simT=800"'
ssh kf-deployment 'bash /tmp/shot.sh "live=1&liveA=230&liveB=200&livet=30&liveGift=drop&liveAt=28.2&liveStop=1"'
ssh kf-deployment 'bash /tmp/runbench.sh "real:&benchframes=1900&benchrate=300"'
```

## 两个坑，都踩过，都写进脚本里了

1. **共享磁盘缓存会把 js 一起缓存。** 帧素材 49MB，不共享缓存每次加载要两分钟；
   而共享缓存之后改完代码截出来的还是旧画面，**且没有任何报错**。
   解法是 URL 带 `?v=<时间戳>`，`index.html` 会把它透传到每个 js 的地址上，
   只让 js 失效、图片照旧走缓存。三个脚本都自动带 `v`。
2. **URL 里绝不写死 bench 参数再拼调用方的串。** 同名参数出现两次时
   `URLSearchParams.get` 只取第一个，实验会**静默地按错的参数跑** ——
   我按 1900 帧发的命令，实际跑的是写死的 420 帧，读数看上去完全正常。
