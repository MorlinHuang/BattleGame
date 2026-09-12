/* 《查手机》网页版 —— 单一真源驱动。
 *
 * 全场唯一状态是 S.p（查岗党进度 0~100）。它派生出 FX.phoneX，对抗线、
 * 刻度尺、地面分色、HUD、角色取哪一帧，全部读它。画面上没有第二个战况
 * 来源，所以"对抗线对不上画面"在构造上不可能发生。
 */
const W = 960, H = 1334;
const TOP = 128, BOT = 1232, MID = 480;
const FRAME_TOP = 308, FRAME_W = 960, FRAME_H = 900;  // 帧纹理只覆盖人物那条横带
const ROWS = 15;
const GREEN = [126, 217, 87], RED = [255, 72, 72];

const P = {
  curve: 1.55,     // 进度→位移的非线性，中段慢、末段快
  /* 关键帧本身已经把"谁被拖过去"画进姿态里了，half/drag 管的是在此之上
     整组人物平移多少：中段那几档姿态差别很小，全靠这段平移把"手机正在被
     拽走"读出来；两头则相反 —— 姿态已经够夸张，再平移就该出画了。 */
  half: 108,       // 对抗线最大偏移
  drag: 0.58,      // 角色整体跟随对抗线的比例
  tilt: 1.55, bulge: 46, linkW: 0.80, shapeRate: 2.6,
  phoneY: 560,     // 对抗线上"手机所在高度"，刻度与辉光的锚
  rug: { top: 738, bot: 1128, tl: 88, tr: 872, bl: 28, br: 912 },  // 底版里地毯四角
};

const S = { p: 50, t: 0, auto: true };
const FX = {
  phoneX: MID, phoneY: P.phoneY,
  rowOff: new Array(ROWS).fill(0), rowHeat: new Array(ROWS).fill(0),
  struggle: 1, actorX: 0, jit: 0,
};

const clamp = (v, a, b) => v < a ? a : v > b ? b : v;
const rgba = (c, a) => `rgba(${c[0]},${c[1]},${c[2]},${a})`;
// 帧率无关的指数趋近：min(1,dt*k) 会让节奏随帧率漂移
const approach = (dt, k) => 1 - Math.exp(-k * dt);

/* ---------- 单一真源 ---------- */
function derive(dt) {
  const bias = (S.p - 50) / 50;
  // p 大 = 查岗党(女方,在左)占优 = 手机被拽向左
  const target = MID - Math.sign(bias) * Math.pow(Math.abs(bias), P.curve) * P.half;
  FX.phoneX += (target - FX.phoneX) * approach(dt, 4.2);

  FX.struggle = 1 - Math.abs(bias) * 0.78;          // 僵持度：五五开时最高
  FX.jit = Math.sin(S.t * 47) * 2.4 * FX.struggle;
  FX.phoneY = P.phoneY + Math.sin(S.t * 9.3) * 6 * FX.struggle - Math.abs(bias) * 14;

  FX.actorX = (FX.phoneX - MID) * P.drag;

  const o = FX.rowOff, next = new Array(ROWS);
  for (let r = 0; r < ROWS; r++) {
    const d = r / (ROWS - 1);
    const tilt = (0.5 - d) * 2 * bias * P.tilt;
    const wob = Math.sin(S.t * 0.41 + r * 0.78) * 0.62 + Math.sin(S.t * 0.83 + r * 1.7) * 0.31;
    const desire = (tilt * 0.62 + wob * 0.42) * P.bulge;
    const nb = ((r > 0 ? o[r - 1] : o[r]) + (r < ROWS - 1 ? o[r + 1] : o[r])) / 2;
    const goal = (desire + P.linkW * nb) / (1 + P.linkW);
    next[r] = o[r] + (goal - o[r]) * approach(dt, P.shapeRate);
  }
  for (let r = 0; r < ROWS; r++) o[r] = clamp(next[r], -110, 110);

  const hot = 1 - Math.abs(bias) * 0.42;
  for (let r = 0; r < ROWS; r++) {
    const d = r / (ROWS - 1);
    const g = Math.exp(-Math.pow((d - 0.36) / 0.44, 2));
    FX.rowHeat[r] += (clamp(g * 1.3 * hot, 0, 1) - FX.rowHeat[r]) * approach(dt, 3.4);
  }
}

function sampleRow(arr, y) {
  const d = clamp((y - TOP) / (BOT - TOP), 0, 1) * (ROWS - 1);
  const i = clamp(Math.floor(d), 0, ROWS - 2), f = d - i;
  const p0 = arr[Math.max(0, i - 1)], p1 = arr[i], p2 = arr[i + 1], p3 = arr[Math.min(ROWS - 1, i + 2)];
  return p1 + 0.5 * f * (p2 - p0 + f * (2 * p0 - 5 * p1 + 4 * p2 - p3 + f * (3 * (p1 - p2) + p3 - p0)));
}
// 对抗线在高度 y 处的横坐标 —— 手机、光柱、刻度、地面分色全读这一个函数
const frontAt = (y) => FX.phoneX + sampleRow(FX.rowOff, y);
const heatAt = (y) => clamp(sampleRow(FX.rowHeat, y), 0, 1);
const phonePos = () => [frontAt(FX.phoneY) + FX.jit, FX.phoneY];

/* ---------- 角色：预渲染关键帧 ---------- */
/* 0~100 每 1% 一张。其中 29 张是生图画的关键档，其余由 interp_frames.py 用
   光流从相邻关键档插出来。从网格变形改走帧序列，是因为两个人抢同一部
   手机时，肩、肘、腕的相对关系每一档都不一样 —— 这种成对的姿态用一套骨骼
   去凑，永远是在"手够不到机身"和"肘折过头"之间取舍。

   不做相邻帧的交叉淡化：两张画的是不同姿态而不是同一姿态的不同时刻，叠在
   一起就是两副骨架互相穿透的重影，越是姿态差得远的档位越糊。硬切虽然跳，
   但每一帧都是清清楚楚的一张画。 */
class FrameSeq {
  /* imgs 是 0~100 共 101 项，缺的那几档是 null —— 姿态跨度太大的区间光流插
     不出干净的中间帧（会长出两个红发夹），只能等生图补上。缺档先映射到最近
     的邻居，画面照常，只是那里的跳变还是原来的大小。 */
  constructor(imgs) {
    this.imgs = imgs;
    this.map = imgs.map((im, i) => {
      if (im) return i;
      let best = -1, bd = 1e9;
      imgs.forEach((o, j) => { const d = Math.abs(j - i); if (o && d < bd) { bd = d; best = j; } });
      return best;
    });
  }

  draw(ctx, p, offsetX) {
    const i = this.map[clamp(Math.round(clamp(p, 0, 100)), 0, 100)];
    ctx.drawImage(this.imgs[i], offsetX, FRAME_TOP, FRAME_W, FRAME_H);
    this.shown = i;
  }
}

/* ---------- 2D 绘制（背景层 / 特效层） ---------- */
function tex(g2, stops) {
  const c = document.createElement('canvas'); c.width = 64; c.height = 1;
  const g = c.getContext('2d'), grad = g.createLinearGradient(0, 0, 64, 0);
  stops.forEach(s => grad.addColorStop(s[0], s[1]));
  g.fillStyle = grad; g.fillRect(0, 0, 64, 1); return c;
}
function glowTex(c) {
  const s = 192, cv = document.createElement('canvas'); cv.width = cv.height = s;
  const g = cv.getContext('2d'), gr = g.createRadialGradient(s / 2, s / 2, 0, s / 2, s / 2, s / 2);
  gr.addColorStop(0, rgba(c, .85)); gr.addColorStop(.45, rgba(c, .3)); gr.addColorStop(1, rgba(c, 0));
  g.fillStyle = gr; g.fillRect(0, 0, s, s); return cv;
}
let TX, GLOW;
function initTex() {
  TX = {
    band: tex(0, [[0, 'rgba(255,255,255,0)'], [.42, 'rgba(255,250,235,.9)'], [.5, 'rgba(255,255,255,1)'],
      [.58, 'rgba(255,250,235,.9)'], [1, 'rgba(255,255,255,0)']]),
    gL: tex(0, [[0, rgba(GREEN, 0)], [.55, rgba(GREEN, .55)], [1, rgba(GREEN, 1)]]),
    rR: tex(0, [[0, rgba(RED, 1)], [.45, rgba(RED, .55)], [1, rgba(RED, 0)]]),
  };
  GLOW = glowTex([255, 244, 220]);
}

function ribbon(ctx, t, top, bot, slices, widthAt, alphaAt, anchor) {
  const h = (bot - top) / slices;
  for (let i = 0; i < slices; i++) {
    const y0 = top + h * i, d = (i + .5) / slices;
    const a = alphaAt(d, i), w = widthAt(d, i);
    if (a <= .004 || w <= .5) continue;
    const x = frontAt(y0 + h / 2) + FX.jit;
    ctx.globalAlpha = a;
    ctx.drawImage(t, anchor < 0 ? x - w : anchor > 0 ? x : x - w / 2, y0, w, h + 1);
  }
}

/* k 是整体强度。这条线要画两遍：一遍在角色之下当背景光柱，一遍以更低的
   强度叠在角色之上 —— 两个人正好在中间抢东西，只画在下面的话对抗线全程
   被两具身体挡死，而它是这个玩法唯一的战况读数。 */
function drawLine(ctx, k = 1) {
  const pulse = .76 + Math.sin(S.t * 13) * .14 + Math.sin(S.t * 29) * .08;
  const depth = d => .42 + d * .58;
  const yAt = d => TOP + (BOT - TOP) * d;
  const fade = d => Math.min(1, d * 7) * Math.min(1, (1 - d) * 9);
  ctx.save(); ctx.globalCompositeOperation = 'lighter';
  const sideW = d => (92 + d * 150) * (.55 + heatAt(yAt(d)) * .45);
  const sideA = d => pulse * depth(d) * (.35 + heatAt(yAt(d)) * .65) * fade(d) * .38 * k;
  ribbon(ctx, TX.gL, TOP, BOT, 26, sideW, sideA, -1);
  ribbon(ctx, TX.rR, TOP, BOT, 26, sideW, sideA, +1);
  ribbon(ctx, TX.band, TOP, BOT, 56,
    (d, i) => (16 + d * 40) * (.86 + Math.sin(S.t * 21 + i * .4) * .14) * (.42 + heatAt(yAt(d)) * .85),
    d => pulse * depth(d) * (.3 + heatAt(yAt(d)) * .8) * fade(d) * 0.95 * k, 0);
  ctx.globalAlpha = heatAt(BOT) * .3 * k;
  ctx.drawImage(GLOW, frontAt(BOT - 40) + FX.jit - 96, BOT - 168, 192, 192);
  ctx.restore(); ctx.globalAlpha = 1;
}

/* 地面辉光：手机被拽向谁，谁脚下的地就烧起来，浓度 = 领先幅度。
   不用"线两侧分色"——拔河里绳结被拽过去不等于对面丢了地盘，
   那个画法在极端档会把颜色铺反。 */
function drawGround(ctx, bias) {
  const R = P.rug, span = R.bot - R.top, k = Math.abs(bias);
  if (k < 0.02) return;
  const col = bias > 0 ? GREEN : RED;
  ctx.save();
  ctx.beginPath();
  ctx.moveTo(R.tl, R.top); ctx.lineTo(R.tr, R.top);
  ctx.lineTo(R.br, R.bot); ctx.lineTo(R.bl, R.bot); ctx.closePath();
  ctx.clip();
  ctx.globalCompositeOperation = 'lighter';
  const win = bias > 0 ? 0 : W;                       // 赢家所在的那一侧
  const g = ctx.createLinearGradient(win, 0, W - win, 0);
  g.addColorStop(0, rgba(col, 0.42 * k));
  g.addColorStop(1, rgba(col, 0.04 * k));
  ctx.fillStyle = g; ctx.fillRect(0, R.top, W, span);
  // 手机正下方的落点亮斑：战线此刻具体压在地上的哪个位置
  const fx = frontAt(R.bot - 90) + FX.jit;
  const rg = ctx.createRadialGradient(fx, R.bot - 70, 0, fx, R.bot - 70, 250);
  rg.addColorStop(0, rgba(col, 0.30 + 0.28 * k));
  rg.addColorStop(1, rgba(col, 0));
  ctx.fillStyle = rg; ctx.fillRect(0, R.top, W, span);
  ctx.restore();
}

/* 地毯前缘的固定标尺 + 跟着对抗线滑的指针 —— 刻度不动、指针动，
   才看得出"推进了多少"；原来刻度跟着线一起动，等于没有参照物。 */
function drawRuler(ctx) {
  const R = P.rug, y = R.bot + 16, L = R.bl + 26, Rr = R.br - 26;
  ctx.save();
  ctx.fillStyle = 'rgba(10,12,16,.34)';
  ctx.fillRect(L - 8, y - 4, Rr - L + 16, 9);
  for (let i = 0; i <= 10; i++) {
    const x = L + (Rr - L) * i / 10, big = i === 5, w = big ? 5 : 3, h = big ? 16 : 10;
    ctx.fillStyle = big ? 'rgba(255,255,255,.95)' : 'rgba(255,255,255,.62)';
    ctx.fillRect(x - w / 2, y - h / 2, w, h);
  }
  const fx = clamp(frontAt(R.bot) + FX.jit, L, Rr);
  const b = (S.p - 50) / 50;
  const col = Math.abs(b) < 0.06 ? [255, 255, 255] : (b > 0 ? GREEN : RED);
  ctx.fillStyle = rgba(col, .95);
  ctx.beginPath();
  ctx.moveTo(fx, y - 12); ctx.lineTo(fx - 11, y - 28); ctx.lineTo(fx + 11, y - 28);
  ctx.closePath(); ctx.fill();
  ctx.strokeStyle = 'rgba(0,0,0,.5)'; ctx.lineWidth = 2; ctx.stroke();
  ctx.restore();
}

function drawHUD(ctx, p) {
  const bars = [{ x: 92, w: 276, c: GREEN, v: p, dir: 1 }, { x: 572, w: 296, c: RED, v: 100 - p, dir: -1 }];
  ctx.save();
  for (const b of bars) {
    ctx.fillStyle = 'rgba(8,10,13,.92)'; ctx.fillRect(b.x, 74, b.w, 31);
    const fw = b.w * b.v / 100, gx = b.dir > 0 ? b.x : b.x + b.w - fw;
    const g = ctx.createLinearGradient(0, 74, 0, 105);
    g.addColorStop(0, rgba(b.c, 1)); g.addColorStop(1, rgba(b.c.map(v => v * .62 | 0), 1));
    ctx.fillStyle = g; ctx.fillRect(gx, 74, fw, 31);
    ctx.fillStyle = 'rgba(255,255,255,.30)'; ctx.fillRect(gx, 74, fw, 9);
  }
  ctx.font = 'bold 23px ui-monospace,Menlo,monospace'; ctx.textBaseline = 'middle';
  ctx.lineWidth = 4; ctx.strokeStyle = 'rgba(0,0,0,.75)';
  ctx.textAlign = 'left'; ctx.strokeText(p.toFixed(0) + '%', 100, 90); ctx.fillStyle = '#fff'; ctx.fillText(p.toFixed(0) + '%', 100, 90);
  ctx.textAlign = 'right'; ctx.strokeText((100 - p).toFixed(0) + '%', 860, 90); ctx.fillStyle = '#fff'; ctx.fillText((100 - p).toFixed(0) + '%', 860, 90);
  ctx.font = 'bold 26px system-ui,"PingFang SC","Microsoft YaHei",sans-serif';
  ctx.textAlign = 'left'; ctx.lineWidth = 5;
  ctx.strokeText('查岗党', 92, 42); ctx.fillText('查岗党', 92, 42);
  ctx.textAlign = 'right';
  ctx.strokeText('灭迹党', 868, 42); ctx.fillText('灭迹党', 868, 42);
  ctx.restore();
}

/* ---------- 启动 ---------- */
const load = (src) => new Promise((ok, no) => { const i = new Image(); i.onload = () => ok(i); i.onerror = no; i.src = src; });

(async function boot() {
  const cvBg = document.getElementById('bg'), cvCh = document.getElementById('ch'), cvFx = document.getElementById('fx');
  const bctx = cvBg.getContext('2d'), cctx = cvCh.getContext('2d'), fctx = cvFx.getContext('2d');
  initTex();

  const bg = await load('assets/bg.jpg');
  /* 每 1% 一张。缺的档位解码失败是预期内的，取 null 交给 FrameSeq 映射到邻居。 */
  const frames = await Promise.all(
    Array.from({ length: 101 }, (_, p) =>
      load(`assets/frames/f${String(p).padStart(3, '0')}.png`).catch(() => null)));
  const seq = new FrameSeq(frames);
  document.getElementById('msg').textContent =
    `${frames.filter(Boolean).length}/101 档 · 每 1%`;

  const Q = new URLSearchParams(location.search);
  if (Q.has('p')) { S.p = clamp(+Q.get('p'), 0, 100); S.auto = false; }
  if (Q.get('auto') === '0') S.auto = false;
  // ?zoom=1 用画布原生尺寸铺开，截图时才看得清脸和手的实际画法
  if (Q.get('zoom') === '1') document.getElementById('stage').style.width = W + 'px';
  document.getElementById('pv').value = S.p;
  document.getElementById('auto').checked = S.auto;
  for (let i = 0; i < 90; i++) derive(1 / 60);   // 预热，让指数趋近收敛到位

  function render() {
    const bias = (S.p - 50) / 50;
    bctx.clearRect(0, 0, W, H);
    bctx.drawImage(bg, 0, 0, W, H);
    drawGround(bctx, bias);
    drawLine(bctx);

    cctx.clearRect(0, 0, W, H);
    seq.draw(cctx, S.p, FX.actorX);

    fctx.clearRect(0, 0, W, H);
    drawLine(fctx, 0.42);
    drawRuler(fctx);
    drawHUD(fctx, S.p);
  }

  /* ?strip=N 出一条连帧胶片：一次看清 N 个档位之间过不过得去。
     动画在静止截图里看不出问题，只有把相邻档位并排摆着才看得出哪一格在跳。 */
  if (Q.has('strip')) {
    const n = clamp(+Q.get('strip') | 0, 2, 21), sc = 0.5;
    const out = document.createElement('canvas');
    out.width = n * W * sc; out.height = H * sc;
    const o = out.getContext('2d');
    o.fillStyle = '#0c0e12'; o.fillRect(0, 0, out.width, out.height);
    for (let i = 0; i < n; i++) {
      S.p = i * 100 / (n - 1); S.auto = false;
      FX.phoneX = MID; FX.rowOff.fill(0); FX.rowHeat.fill(0); S.t = 3.0;
      for (let k = 0; k < 150; k++) derive(1 / 60);
      render();
      const dx = i * W * sc;
      for (const c of [cvBg, cvCh, cvFx]) o.drawImage(c, dx, 0, W * sc, H * sc);
      o.fillStyle = 'rgba(0,0,0,.66)'; o.fillRect(dx, 0, 86, 26);
      o.fillStyle = '#fff'; o.font = '600 15px system-ui';
      o.fillText(`p=${S.p.toFixed(0)}`, dx + 8, 18);
    }
    const stage = document.getElementById('stage');
    stage.style.width = out.width + 'px';
    stage.style.aspectRatio = `${out.width}/${out.height}`;
    stage.innerHTML = '';
    out.style.cssText = 'position:absolute;inset:0;width:100%;height:100%';
    stage.appendChild(out);
    return;
  }

  let last = performance.now(), fps = 0, fr = 0, acc = 0, dir = 1;
  function frame(now) {
    const dt = Math.min(.05, (now - last) / 1000); last = now;
    S.t += dt; fr++; acc += dt;
    if (acc >= .5) { fps = fr / acc; fr = 0; acc = 0; }

    if (S.auto) {
      S.p += dir * dt * 9 * (0.35 + Math.abs(Math.sin(S.t * .27)) * 1.5);
      if (S.p > 97) { S.p = 97; dir = -1; } if (S.p < 3) { S.p = 3; dir = 1; }
      document.getElementById('pv').value = S.p;
    }
    derive(dt);
    render();
    document.getElementById('stat').textContent =
      `p=${S.p.toFixed(1)}  对抗线x=${phonePos()[0].toFixed(0)}  f${String(seq.shown).padStart(3, '0')}  ${fps.toFixed(0)}fps`;
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);

  const pv = document.getElementById('pv');
  pv.oninput = () => { S.p = +pv.value; S.auto = false; document.getElementById('auto').checked = false; };
  document.getElementById('auto').onchange = e => S.auto = e.target.checked;
  const nudge = d => { S.auto = false; document.getElementById('auto').checked = false;
                       S.p = clamp(S.p + d, 0, 100); pv.value = S.p; };
  document.getElementById('hitL').onclick = () => nudge(+7);
  document.getElementById('hitR').onclick = () => nudge(-7);
})();
