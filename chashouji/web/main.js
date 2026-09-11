/* 《查手机》网页版 —— 单一真源驱动。
 *
 * 全场唯一状态是 S.p（查岗党进度 0~100）。它派生出 FX.phoneX，
 * 手机、双方的手（IK 目标）、对抗线、刻度尺、地面分色、HUD 全部读它。
 * 画面上没有第二个战况来源，所以"对抗线对不上画面"在构造上不可能发生。
 */
const W = 960, H = 1334;
const TOP = 128, BOT = 1232, MID = 480;
const ROWS = 15;
const GREEN = [126, 217, 87], RED = [255, 72, 72];

const P = {
  curve: 1.55,     // 进度→位移的非线性，中段慢、末段快
  half: 160,       // 手机最大偏移（再大，极端档角色会被拖出画）
  drag: 0.58,      // 角色跟随手机的比例（拔河：赢方后退、输方被拖，间距不变）
  tilt: 1.55, bulge: 46, linkW: 0.80, shapeRate: 2.6,
  lean: 0.24,      // 躯干最大倾角(rad)
  phoneY: 566, phoneW: 78, phoneH: 150,
  girlX: 270, boyX: 676, footY: 1125,
  rug: { top: 738, bot: 1128, tl: 88, tr: 872, bl: 28, br: 912 },  // 底版里地毯四角
  girlH: 828, boyH: 838,
};

const S = { p: 50, t: 0, auto: true };
const FX = {
  phoneX: MID, phoneY: P.phoneY, phoneRot: 0,
  rowOff: new Array(ROWS).fill(0), rowHeat: new Array(ROWS).fill(0),
  struggle: 1, girlX: P.girlX, boyX: P.boyX, jit: 0,
};
const DBG = { wire: false, bones: false, weights: false, calib: false };

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
  FX.phoneRot = bias * 0.20 + Math.sin(S.t * 23) * 0.045 * FX.struggle;
  FX.phoneY = P.phoneY + Math.sin(S.t * 9.3) * 6 * FX.struggle - Math.abs(bias) * 14;

  FX.girlX = P.girlX + (FX.phoneX - MID) * P.drag;
  FX.boyX = P.boyX + (FX.phoneX - MID) * P.drag;

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

/* ---------- 角色 ---------- */
class Actor {
  constructor(def, gl, img, ren, side) {
    this.def = def; this.side = side;      // side: -1 在左(girl), +1 在右(boy)
    this.sk = new Skeleton(def.bones, img.width, img.height);
    this.mesh = buildMesh(img, this.sk, 46, 68);
    this.gpu = ren.upload(this.mesh, img);
    this.ren = ren;
    this.bend = {};
    for (const a of ['armA', 'armB']) {
      const u = this.sk.byName(a + '_up'), f = this.sk.byName(a + '_fore');
      const cr = (u.tx - u.hx) * (f.ty - f.hy) - (u.ty - u.hy) * (f.tx - f.hx);
      this.bend[a] = cr >= 0 ? 1 : -1;
    }
  }
  rebuild(img) {
    this.sk = new Skeleton(this.def.bones, img.width, img.height);
    this.mesh = buildMesh(img, this.sk, 46, 68);
    this.gpu = this.ren.upload(this.mesh, img);
  }
  get scale() { return (this.side < 0 ? P.girlH : P.boyH) / this.sk.imgH; }
  get model() {
    const s = this.scale, x = this.side < 0 ? FX.girlX : FX.boyX;
    return M.mul(M.trs(x, P.footY, 0, s, s), M.trs(-this.def.anchorX * this.sk.imgW, -this.sk.imgH, 0));
  }
  /* 摆姿势：躯干倾角由劣势程度决定；
     两只手抓机身的上下两处，落点写在标定数据里按角色分别给 ——
     两人臂长和站位不同，共用一组偏移必然有一边够不到或折过头。 */
  pose(bias) {
    const sk = this.sk, inv = M.inv(this.model);
    sk.reset();
    const lean = -bias * P.lean + Math.sin(S.t * 11 + (this.side < 0 ? 0 : 1.7)) * 0.012 * FX.struggle;
    sk.byName('torso').delta = lean;
    sk.byName('head').delta = -lean * 0.55;
    sk.solve();

    const [px, py] = phonePos(), ro = FX.phoneRot;
    const co = Math.cos(ro), si = Math.sin(ro);
    const grip = ([ox, oy]) => M.apply(inv, px + ox * co - oy * si, py + ox * si + oy * co);
    const G = this.def.grip;
    sk.ik('armA_up', 'armA_fore', ...grip(G.A), this.bend.armA);
    sk.ik('armB_up', 'armB_fore', ...grip(G.B), this.bend.armB);
    sk.solve();
  }
  draw(tint) { this.ren.draw(this.gpu, this.sk, this.model, tint, false); if (DBG.wire) this.ren.draw(this.gpu, this.sk, this.model, tint, true); }
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

function drawLine(ctx) {
  const pulse = .76 + Math.sin(S.t * 13) * .14 + Math.sin(S.t * 29) * .08;
  const depth = d => .42 + d * .58;
  const yAt = d => TOP + (BOT - TOP) * d;
  const fade = d => Math.min(1, d * 7) * Math.min(1, (1 - d) * 9);
  ctx.save(); ctx.globalCompositeOperation = 'lighter';
  const sideW = d => (92 + d * 150) * (.55 + heatAt(yAt(d)) * .45);
  const sideA = d => pulse * depth(d) * (.35 + heatAt(yAt(d)) * .65) * fade(d) * .38;
  ribbon(ctx, TX.gL, TOP, BOT, 26, sideW, sideA, -1);
  ribbon(ctx, TX.rR, TOP, BOT, 26, sideW, sideA, +1);
  ribbon(ctx, TX.band, TOP, BOT, 56,
    (d, i) => (16 + d * 40) * (.86 + Math.sin(S.t * 21 + i * .4) * .14) * (.42 + heatAt(yAt(d)) * .85),
    d => pulse * depth(d) * (.3 + heatAt(yAt(d)) * .8) * fade(d) * 0.95, 0);
  ctx.globalAlpha = heatAt(BOT) * .3;
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

function drawPhone(ctx) {
  const [x, y] = phonePos();
  ctx.save(); ctx.translate(x, y); ctx.rotate(FX.phoneRot);
  ctx.shadowColor = 'rgba(0,0,0,.45)'; ctx.shadowBlur = 14; ctx.shadowOffsetY = 4;
  ctx.fillStyle = '#1c1e24'; ctx.strokeStyle = '#fafafa'; ctx.lineWidth = 3;
  const w = P.phoneW, h = P.phoneH, r = 9;
  ctx.beginPath(); ctx.roundRect(-w / 2, -h / 2, w, h, r); ctx.fill(); ctx.stroke();
  ctx.shadowBlur = 0;
  const g = ctx.createLinearGradient(0, -h / 2, 0, h / 2);
  g.addColorStop(0, 'rgba(150,210,255,.95)'); g.addColorStop(1, 'rgba(90,150,230,.85)');
  ctx.fillStyle = g; ctx.beginPath(); ctx.roundRect(-w / 2 + 5, -h / 2 + 9, w - 10, h - 18, 4); ctx.fill();
  const rows = [[0, .62], [1, .46], [0, .74], [1, .38], [0, .54], [1, .60]];
  const iw = w - 16, top = -h / 2 + 16;
  rows.forEach(([right, r], i) => {
    const bw = iw * r, bx = right ? w / 2 - 8 - bw : -w / 2 + 8;
    ctx.fillStyle = right ? 'rgba(120,220,120,.92)' : 'rgba(252,252,252,.92)';
    ctx.beginPath(); ctx.roundRect(bx, top + i * 18, bw, 13, 4); ctx.fill();
  });
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

/* 标定模式：画骨骼线与关节点，可直接拖 */
function drawBones(ctx, actors) {
  ctx.save();
  for (const A of actors) {
    const mdl = A.model;
    for (const b of A.sk.bones) {
      if (b.name === 'root' && !DBG.calib) continue;
      const [hx, hy] = M.apply(mdl, ...M.apply(b.skin, b.hx, b.hy));
      const [tx, ty] = M.apply(mdl, ...M.apply(b.skin, b.tx, b.ty));
      ctx.strokeStyle = 'rgba(255,210,40,.9)'; ctx.lineWidth = 3;
      ctx.beginPath(); ctx.moveTo(hx, hy); ctx.lineTo(tx, ty); ctx.stroke();
      for (const [x, y] of [[hx, hy], [tx, ty]]) {
        ctx.fillStyle = 'rgba(20,20,20,.85)'; ctx.beginPath(); ctx.arc(x, y, 7, 0, 7); ctx.fill();
        ctx.fillStyle = '#ffd228'; ctx.beginPath(); ctx.arc(x, y, 4.5, 0, 7); ctx.fill();
      }
    }
  }
  ctx.restore();
}

/* ---------- 启动 ---------- */
const load = (src) => new Promise((ok, no) => { const i = new Image(); i.onload = () => ok(i); i.onerror = no; i.src = src; });

(async function boot() {
  const cvBg = document.getElementById('bg'), cvCh = document.getElementById('ch'), cvFx = document.getElementById('fx');
  const bctx = cvBg.getContext('2d'), fctx = cvFx.getContext('2d');
  const gl = cvCh.getContext('webgl2', { alpha: true, premultipliedAlpha: false, antialias: true, preserveDrawingBuffer: true });
  if (!gl) { document.getElementById('msg').textContent = '浏览器不支持 WebGL2'; return; }
  initTex();

  const [bg, gimg, bimg] = await Promise.all([load('assets/bg.jpg'), load(RIG.girl.img), load(RIG.boy.img)]);
  const ren = new Renderer(gl);
  const girl = new Actor(RIG.girl, gl, gimg, ren, -1);
  const boy = new Actor(RIG.boy, gl, bimg, ren, +1);
  const actors = [girl, boy];
  const IMGS = { girl: gimg, boy: bimg };
  document.getElementById('msg').textContent =
    `网格 ${girl.mesh.tris + boy.mesh.tris} 三角形 / ${girl.mesh.verts + boy.mesh.verts} 顶点`;

  // URL 参数便于定档截图核对：?p=20&auto=0
  const Q = new URLSearchParams(location.search);
  if (Q.has('p')) { S.p = clamp(+Q.get('p'), 0, 100); S.auto = false; }
  if (Q.get('auto') === '0') S.auto = false;
  document.getElementById('pv').value = S.p;
  document.getElementById('auto').checked = S.auto;
  if (Q.get('bones') === '1') { DBG.bones = true; document.getElementById('bones').checked = true; }
  if (Q.get('wire') === '1') { DBG.wire = true; document.getElementById('wire').checked = true; }
  for (let i = 0; i < 90; i++) derive(1 / 60);   // 预热，让指数趋近收敛到位

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
      `p=${S.p.toFixed(1)}  phoneX=${phonePos()[0].toFixed(0)}  ${fps.toFixed(0)}fps`;
    requestAnimationFrame(frame);
  }

  function render() {
    const bias = (S.p - 50) / 50;
    bctx.clearRect(0, 0, W, H);
    bctx.drawImage(bg, 0, 0, W, H);
    drawGround(bctx, bias);
    drawLine(bctx);
    drawPhone(bctx);          // 画在角色层之下，双手才压得住机身

    gl.viewport(0, 0, W, H);
    gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT);
    for (const A of actors) A.pose(bias);
    // 劣势方压暗，不叠色相 —— 叠红会把黑发染成棕色，人物形象就变了
    const SHADE = [26, 24, 38];
    girl.draw([...SHADE, clamp(-bias, 0, 1) * .32]);
    boy.draw([...SHADE, clamp(bias, 0, 1) * .32]);

    fctx.clearRect(0, 0, W, H);
    drawRuler(fctx);
    drawHUD(fctx, S.p);
    if (DBG.bones) drawBones(fctx, actors);
  }

  /* 胶片模式 ?strip=N：把 N 个连续档位并排渲成一条，用来核对
     "变化是不是连续的几何运动"。相邻两格之间没有任何淡入淡出，
     每一格都是同一套素材被骨骼摆出来的真实姿势。 */
  if (Q.has('strip')) {
    const N = clamp(+Q.get("strip") || 7, 2, 12);
    const SX = 0, SY = 190, SW = 960, SH = 1010, DW = 270, DH = 284;
    const out = document.createElement('canvas');
    out.width = DW * N; out.height = DH + 26;
    const o = out.getContext('2d');
    o.fillStyle = '#0c0e12'; o.fillRect(0, 0, out.width, out.height);
    for (let i = 0; i < N; i++) {
      S.p = 5 + 90 * i / (N - 1);
      S.t = 3.0;                                  // 固定相位，抖动不干扰对比
      FX.phoneX = MID; FX.rowOff.fill(0); FX.rowHeat.fill(0);
      for (let k = 0; k < 150; k++) derive(1 / 60);
      render();
      for (const c of [cvBg, cvCh, cvFx]) o.drawImage(c, SX, SY, SW, SH, i * DW, 0, DW, DH);
      o.fillStyle = '#e8ecf2';
      o.font = 'bold 15px ui-monospace,Menlo,monospace';
      o.textAlign = 'center';
      o.fillText('p=' + S.p.toFixed(0), i * DW + DW / 2, DH + 19);
    }
    document.getElementById('stage').replaceWith(out);
    out.style.cssText = 'width:100%;max-width:1900px;display:block';
    document.querySelector('.panel').style.display = 'none';
    document.title = 'strip ready';
    return;
  }

  requestAnimationFrame(frame);

  /* --- 控件 --- */
  const $ = id => document.getElementById(id);
  $('pv').addEventListener('input', e => { S.p = +e.target.value; S.auto = false; $('auto').checked = false; });
  $('auto').addEventListener('change', e => S.auto = e.target.checked);
  for (const k of ['wire', 'bones']) $(k).addEventListener('change', e => DBG[k] = e.target.checked);
  $('hitL').onclick = () => { S.auto = false; $('auto').checked = false; S.p = clamp(S.p + 7, 0, 100); $('pv').value = S.p; };
  $('hitR').onclick = () => { S.auto = false; $('auto').checked = false; S.p = clamp(S.p - 7, 0, 100); $('pv').value = S.p; };

  /* --- 标定：拖关节 --- */
  let drag = null;
  const toCanvas = e => {
    const r = cvFx.getBoundingClientRect();
    return [(e.clientX - r.left) / r.width * W, (e.clientY - r.top) / r.height * H];
  };
  cvFx.addEventListener('pointerdown', e => {
    if (!DBG.bones) return;
    const [mx, my] = toCanvas(e);
    let best = null, bd = 16;
    for (const A of actors) {
      const mdl = A.model;
      A.sk.bones.forEach((b, bi) => {
        for (const end of ['head', 'tail']) {
          const rx = end === 'head' ? b.hx : b.tx, ry = end === 'head' ? b.hy : b.ty;
          const [x, y] = M.apply(mdl, ...M.apply(b.skin, rx, ry));
          const d = Math.hypot(x - mx, y - my);
          if (d < bd) { bd = d; best = { A, bi, end }; }
        }
      });
    }
    if (best) { drag = best; cvFx.setPointerCapture(e.pointerId); S.auto = false; $('auto').checked = false; }
  });
  cvFx.addEventListener('pointermove', e => {
    if (!drag) return;
    const A = drag.A, [mx, my] = toCanvas(e);
    // 拖的是 rest 位置，所以把屏幕点反算回图片像素：先去 model，再去当前 pose
    const b = A.sk.bones[drag.bi];
    const [lx, ly] = M.apply(M.inv(M.mul(A.model, b.skin)), mx, my);
    A.def.bones[drag.bi][drag.end] = [clamp(lx / A.sk.imgW, 0, 1), clamp(ly / A.sk.imgH, 0, 1)];
    A.sk = new Skeleton(A.def.bones, A.sk.imgW, A.sk.imgH);   // 只重建骨架，松手才重刷权重
    A.sk.solve();
  });
  cvFx.addEventListener('pointerup', () => {
    if (!drag) return;
    const A = drag.A; drag = null;
    A.rebuild(A === girl ? IMGS.girl : IMGS.boy);
  });
  $('dump').onclick = () => {
    const fmt = o => JSON.stringify(o.bones, null, 2).replace(/"(\w+)":/g, '$1:');
    const txt = `girl:\n${fmt(RIG.girl)}\n\nboy:\n${fmt(RIG.boy)}`;
    $('out').value = txt; $('out').style.display = 'block'; $('out').select();
  };
})();
