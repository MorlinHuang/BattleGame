#!/bin/bash
# 用法: runbench.sh "名字:&benchframes=1800&benchrate=300" ...
# 注意 URL 里绝不写死任何 bench 参数 —— 写死再拼 $2 会造成同名参数出现两次，
# 而 URLSearchParams.get 只取第一个，实验会静默地按错的参数跑。
V=$(date +%s%N)
run() {
  U="http://127.0.0.1:40235/index.html?zoom=1&v=$V&bench=1$2"
  DISPLAY=:1 google-chrome --user-data-dir=/tmp/bp_all --no-sandbox --disable-dev-shm-usage \
    --enable-unsafe-swiftshader --force-device-scale-factor=1 --no-first-run \
    --no-default-browser-check --disable-session-crashed-bubble \
    --window-size=1000,960 --window-position=0,0 --app="$U" >/tmp/bp_$1.log 2>&1 &
  for i in $(seq 1 90); do
    sleep 2
    if DISPLAY=:1 xdotool search --name "BENCHDONE" >/dev/null 2>&1; then echo "[$1] 完成于 $((i*2))s"; break; fi
  done
  sleep 1
  DISPLAY=:1 scrot -o /tmp/bench_$1.png
  pkill -f "bp_all"; sleep 3
}
for a in "$@"; do run "${a%%:*}" "${a#*:}"; done
