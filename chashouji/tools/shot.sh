#!/bin/bash
# v=时间戳 让 js 绕过共享缓存，图片仍走缓存
V=$(date +%s%N)
mkdir -p /tmp/shot
n=0
for a in "$@"; do
  n=$((n+1))
  timeout 240 google-chrome --headless=new --no-sandbox --disable-dev-shm-usage \
    --enable-unsafe-swiftshader --hide-scrollbars \
    --disk-cache-dir=/tmp/hlc --user-data-dir=/tmp/sh_$n \
    --window-size=960,1334 --virtual-time-budget=90000 \
    --screenshot=/tmp/shot/live_$n.png "http://127.0.0.1:40235/index.html?zoom=1&v=$V&$a" >/dev/null 2>&1
  echo "[$n] $a"
done
