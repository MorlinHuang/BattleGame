/* fx.js —— 粒子与打击感
 *
 * 全部特效走同一个粒子池 + 预渲染贴图。这台机器没有 GPU（软渲染），所以
 * 避开 shadowBlur / filter / 每帧 createRadialGradient —— 光斑一次性烘到离屏
 * canvas，之后只 drawImage。
 *
 * 这里只有"怎么画"，没有"画什么"：具体某件礼物炸出羽毛还是星星，由 main.js
 * 的配方表决定。粒子形态刻意留成通用的五种，换题材皮不用动这个文件。
 *
 * 打击感的四个手段都不需要改角色帧素材 —— 角色是预渲染帧，做不了受击变形，
 * 但顿帧、整体位移、染色、缩放这四样都作用在贴图之外：
 *   hitStop  命中瞬间冻住整个世界几十毫秒，打击感有一半来自这个
 *   shake    屏幕震动
 *   flash    全屏白闪
 *   punch    给角色层的冲击位移与缩放脉冲（由 main.js 读走）
 */
'use strict';

const Particles = (function () {
  const MAX = 1200;
  const act = [];            // 活跃粒子
  const pool = [];           // 回收池，避免每帧 new

  let shake = 0;             // 屏幕震动强度（像素）
  let flash = 0;             // 全屏白闪 0..1
  let stop = 0;              // 顿帧剩余时长（秒）
  const off = { x: 0, y: 0 };   // 当前震动偏移，main.js 读它来平移整个画面

  /* ---------- 预渲染贴图 ---------- */

  const glowCache = new Map();
  function glow(rgb) {
    const key = rgb[0] + ',' + rgb[1] + ',' + rgb[2];
    let c = glowCache.get(key);
    if (c) return c;
    const S = 128;
    c = document.createElement('canvas');
    c.width = c.height = S;
    const g = c.getContext('2d');
    const rg = g.createRadialGradient(S / 2, S / 2, 0, S / 2, S / 2, S / 2);
    rg.addColorStop(0.00, 'rgba(255,255,255,1)');
    rg.addColorStop(0.22, `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.95)`);
    rg.addColorStop(0.55, `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.35)`);
    rg.addColorStop(1.00, `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0)`);
    g.fillStyle = rg;
    g.fillRect(0, 0, S, S);
    glowCache.set(key, c);
    return c;
  }

  /* 绒絮用普通混合、先于亮部画。全走 lighter 会让它显得飘、没有体积 ——
     羽毛和灰尘是实体，不是光。 */
  const softCache = new Map();
  function soft(rgb) {
    const key = rgb[0] + ',' + rgb[1] + ',' + rgb[2];
    let c = softCache.get(key);
    if (c) return c;
    const S = 96;
    c = document.createElement('canvas');
    c.width = c.height = S;
    const g = c.getContext('2d');
    const rg = g.createRadialGradient(S / 2, S / 2, 0, S / 2, S / 2, S / 2);
    rg.addColorStop(0.0, `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.9)`);
    rg.addColorStop(0.5, `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.42)`);
    rg.addColorStop(1.0, `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0)`);
    g.fillStyle = rg;
    g.fillRect(0, 0, S, S);
    softCache.set(key, c);
    return c;
  }

  /* ---------- 粒子 ---------- */

  /* kind 只有五种，都是形态而非题材：
       dot   光斑，会从 r 涨到 r1
       spark 沿速度方向的短亮线，速度越快拉得越长
       ring  扩散的椭圆环（贴地看所以压扁）
       chip  翻滚的小片，羽毛/塑料碎/纸屑都用它
       soft  绒絮，普通混合，用作灰尘与绒毛 */
  function spawn(o) {
    if (act.length >= MAX) return null;
    const p = pool.pop() || {};
    p.kind = o.kind; p.x = o.x; p.y = o.y;
    p.vx = o.vx || 0; p.vy = o.vy || 0;
    p.g = o.g || 0; p.drag = o.drag != null ? o.drag : 1;
    p.life = p.maxLife = o.life;
    p.r = o.r || 4; p.r1 = o.r1 != null ? o.r1 : p.r;
    p.rgb = o.rgb || [255, 255, 255];
    p.a = o.a != null ? o.a : 1;
    p.rot = o.rot || 0; p.vrot = o.vrot || 0;
    p.w = o.w || 0; p.h = o.h || 0;
    p.lw = o.lw || 3;
    p.sway = o.sway || 0;      // 横向摆幅，羽毛飘落用
    p.seed = Math.random() * 6.283;
    act.push(p);
    return p;
  }

  function update(dt) {
    for (let i = act.length - 1; i >= 0; i--) {
      const p = act[i];
      p.life -= dt;
      if (p.life <= 0) { act.splice(i, 1); pool.push(p); continue; }
      p.vy += p.g * dt;
      if (p.drag !== 1) { const d = Math.pow(p.drag, dt * 60); p.vx *= d; p.vy *= d; }
      p.x += (p.vx + (p.sway ? Math.cos(p.seed + p.life * 3.1) * p.sway : 0)) * dt;
      p.y += p.vy * dt;
      p.rot += p.vrot * dt;
    }
    /* 衰减都写成 pow(k, dt*60) 而不是 k*dt：后者会让节奏随帧率漂移，
       这个坑在对抗线那边已经踩过一次。 */
    shake *= Math.pow(0.86, dt * 60);
    if (shake < 0.15) shake = 0;
    flash *= Math.pow(0.80, dt * 60);
    if (flash < 0.004) flash = 0;

    const a = Math.random() * 6.283;
    off.x = Math.cos(a) * shake;
    off.y = Math.sin(a) * shake * 0.6;   // 竖屏，横向震得多一点更像撞击
  }

  function draw(ctx) {
    // 第一趟：绒絮与碎片，普通混合，它们是实体
    ctx.save();
    for (let i = 0; i < act.length; i++) {
      const p = act[i];
      if (p.kind !== 'soft' && p.kind !== 'chip') continue;
      const k = p.life / p.maxLife;
      const alpha = p.a * (k > 0.85 ? (1 - k) / 0.15 : k / 0.85);   // 快入慢出
      if (alpha <= 0.01) continue;
      ctx.globalAlpha = alpha;
      if (p.kind === 'soft') {
        const r = p.r + (p.r1 - p.r) * (1 - k);
        ctx.drawImage(soft(p.rgb), p.x - r, p.y - r, r * 2, r * 2);
      } else {
        ctx.save();
        ctx.translate(p.x, p.y);
        ctx.rotate(p.rot);
        ctx.fillStyle = `rgb(${p.rgb[0]},${p.rgb[1]},${p.rgb[2]})`;
        // 翻滚时按 cos 压扁，读起来是一片薄东西在空中打转
        ctx.fillRect(-p.w / 2, -p.h / 2 * Math.abs(Math.cos(p.rot * 1.7)), p.w, p.h);
        ctx.restore();
      }
    }
    ctx.restore();

    // 第二趟：发光部分
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < act.length; i++) {
      const p = act[i];
      if (p.kind === 'soft' || p.kind === 'chip') continue;
      const k = p.life / p.maxLife;
      const alpha = p.a * (k > 0.85 ? (1 - k) / 0.15 : k / 0.85);
      if (alpha <= 0.01) continue;
      ctx.globalAlpha = alpha;
      const rgb = p.rgb;

      if (p.kind === 'dot') {
        const r = p.r + (p.r1 - p.r) * (1 - k);
        ctx.drawImage(glow(rgb), p.x - r, p.y - r, r * 2, r * 2);

      } else if (p.kind === 'spark') {
        const sp = Math.hypot(p.vx, p.vy);
        const len = Math.min(26, 4 + sp * 0.028);
        const nx = sp ? p.vx / sp : 1, ny = sp ? p.vy / sp : 0;
        ctx.strokeStyle = `rgb(${rgb[0]},${rgb[1]},${rgb[2]})`;
        ctx.lineWidth = p.lw;
        ctx.lineCap = 'round';
        ctx.beginPath();
        ctx.moveTo(p.x, p.y);
        ctx.lineTo(p.x - nx * len, p.y - ny * len);
        ctx.stroke();

      } else if (p.kind === 'ring') {
        const r = p.r + (p.r1 - p.r) * (1 - k);
        ctx.strokeStyle = `rgb(${rgb[0]},${rgb[1]},${rgb[2]})`;
        ctx.lineWidth = p.lw * k + 0.6;
        ctx.beginPath();
        ctx.ellipse(p.x, p.y, r, r * 0.5, 0, 0, 6.2832);
        ctx.stroke();
      }
    }
    ctx.restore();
  }

  /* 全屏白闪。画在最上层，连 HUD 一起罩住 —— 只罩画面的话会显得闪光是
     "场景里的光"，而它要的是"这一下很重"。
     用普通混合而不是 lighter：底图是明亮的客厅（浅绿墙、米色地板），lighter
     叠上去立刻过曝成一片白，人物全糊。可用的白闪强度是底图亮度定的，暗色
     战场能用 0.5，这里 0.22 就到顶了。 */
  function drawFlash(ctx, w, h) {
    if (flash <= 0.004) return;
    ctx.save();
    ctx.fillStyle = `rgba(255,252,246,${flash})`;
    ctx.fillRect(0, 0, w, h);
    ctx.restore();
  }

  /* 顿帧：返回本帧实际该走多少时间。命中瞬间把世界冻住，其余照常。
     注意冻的是游戏时间不是渲染 —— 画面照常刷新，只是一切都不动。 */
  function tick(dt) {
    if (stop > 0) { stop -= dt; return 0; }
    return dt;
  }

  function hitStop(sec) { stop = Math.max(stop, sec); }
  function addShake(v) { shake = Math.min(30, shake + v); }
  function addFlash(v) { flash = Math.max(flash, v); }
  function clear() { while (act.length) pool.push(act.pop()); shake = flash = stop = 0; }

  return {
    spawn, update, draw, drawFlash, tick,
    hitStop, addShake, addFlash, clear,
    off, count: () => act.length,
  };
})();
