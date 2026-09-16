#!/bin/bash
run() {
  U="http://127.0.0.1:40235/index.html?sim=1&$1"
  timeout 200 google-chrome --headless=new --no-sandbox --disable-dev-shm-usage \
    --enable-unsafe-swiftshader --disk-cache-dir=/tmp/hlc --user-data-dir=/tmp/sim_$$ \
    --virtual-time-budget=60000 --dump-dom "$U" 2>/dev/null \
    | grep -o 'id="msg">[^<]*' | sed 's/id="msg">//'
}
for a in "$@"; do echo "[$a]"; run "$a"; echo; done
