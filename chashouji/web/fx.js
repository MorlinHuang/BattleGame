/* fx.js —— 粒子与打击感
 *
 * 全部特效走同一个粒子池 + 预渲染贴图。这台机器没有 GPU（软渲染），所以
 * 避开 shadowBlur / filter / 每帧 createRadialGradient —— 光斑一次性烘到离屏
 * canvas，之后只 drawImage。
 *
 * 这里只有"怎么画"，没有"画什么"：具体某件礼物炸出羽毛还是星星，由 main.js
 * 的配方表决定。粒子形态刻意留成通用的六种，换题材皮不用动这个文件。
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
  let cool = 0;              // 顿帧不应期剩余：这段时间里只接受更重的一击
  let lvl = 0;               // 正在演的这次顿帧有多重，用来判断谁能打断谁
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

  /* kind 只有六种，都是形态而非题材：
       dot   光斑，会从 r 涨到 r1
       spark 沿速度方向的短亮线，速度越快拉得越长
       ring  扩散的椭圆环（贴地看所以压扁）
       chip  翻滚的小片，羽毛/塑料碎/纸屑都用它
       star  五角星，会旋转、会缩小
       soft  绒絮，普通混合，用作灰尘与绒毛

     chip、star 和 heart 都吃 edge（描边色）。底图是明亮客厅，实体不描边就糊
     在浅绿墙和米色地板里 —— 这跟角色睡衣有线稿是同一个道理，赛璐璐风格
     里"看得见"靠的是轮廓不是亮度。发光那三种（dot/spark/ring）没有描边，
     它们本来就该是光。 */
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
    p.edge = o.edge || null;   // 实体描边色，明亮底图上靠它把碎片从背景里拔出来
    p.spin = o.spin || 0;      // 绕出生点公转的半径，星星绕头转用
    p.sway = o.sway || 0;      // 横向摆幅，羽毛飘落用
    p.seed = Math.random() * 6.283;
    /* 颜色串和贴图在出生时就定下来。它们整个生命周期都不变，而写在 draw 里
       就是每帧、每颗粒子都重新拼一次字符串、让浏览器重新解析一次颜色 ——
       场上常有三五百颗粒子，这笔账一秒钟要算上万次。 */
    p.fill = `rgb(${p.rgb[0]},${p.rgb[1]},${p.rgb[2]})`;
    p.line = p.edge ? `rgb(${p.edge[0]},${p.edge[1]},${p.edge[2]})` : null;
    p.tex = p.kind === 'soft' ? soft(p.rgb) : p.kind === 'dot' ? glow(p.rgb) : null;
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

  /* 淡入是绝对时间，淡出才按寿命比例。
     原来两头都按比例（前 15% 淡入），短命的火花看不出问题，但羽毛活 2 秒，
     15% 就是 300 毫秒 —— 枕头炸开之后要等三分之一秒羽毛才显形，命中最该看
     见东西的那一刻画面是空的。"入场"是一个固定的瞬间，与这个粒子打算活多久
     没有任何关系。 */
  const FADE_IN = 0.045;
  function fade(p) {
    const inn = Math.min(1, (p.maxLife - p.life) / FADE_IN);
    const out = Math.min(1, p.life / (p.maxLife * 0.34));
    return inn * out;
  }

  /* 五角星路径。半径 r，内凹到 0.42 —— 再瘦就变成海星，再胖就读成花。 */
  /* 爱心。用两段三次贝塞尔拼出来，比心形参数方程便宜得多，形状也更"图标"
     一点 —— 这里要的是观众一眼认出的那个符号，不是数学上正确的心脏线。
     顶点在 -r*0.62 而不是 -r，因为爱心的视觉重心明显偏下，按外接盒居中的话
     一堆爱心飘起来会整体显得往上飘了半个身位。 */
  function heartPath(ctx, r) {
    ctx.beginPath();
    ctx.moveTo(0, r * 0.92);
    ctx.bezierCurveTo(-r * 1.08, r * 0.10, -r * 0.62, -r * 1.02, 0, -r * 0.34);
    ctx.bezierCurveTo(r * 0.62, -r * 1.02, r * 1.08, r * 0.10, 0, r * 0.92);
    ctx.closePath();
  }

  function starPath(ctx, r) {
    ctx.beginPath();
    for (let i = 0; i < 10; i++) {
      const a = -1.5708 + i * 0.6283, rr = i % 2 ? r * 0.42 : r;
      const x = Math.cos(a) * rr, y = Math.sin(a) * rr;
      i ? ctx.lineTo(x, y) : ctx.moveTo(x, y);
    }
    ctx.closePath();
  }

  function draw(ctx) {
    // 第一趟：绒絮、碎片、星星，普通混合，它们是实体
    ctx.save();
    ctx.lineJoin = 'round';
    for (let i = 0; i < act.length; i++) {
      const p = act[i];
      if (p.kind !== 'soft' && p.kind !== 'chip' && p.kind !== 'star' && p.kind !== 'heart' && p.kind !== 'card') continue;
      const k = p.life / p.maxLife;
      const alpha = p.a * fade(p);
      if (alpha <= 0.01) continue;
      ctx.globalAlpha = alpha;
      if (p.kind === 'soft') {
        const r = p.r + (p.r1 - p.r) * (1 - k);
        ctx.drawImage(p.tex, p.x - r, p.y - r, r * 2, r * 2);
      } else if (p.kind === 'star' || p.kind === 'heart') {
        const r = p.r + (p.r1 - p.r) * (1 - k);
        const ph = p.seed + (1 - k) * 7.4;
        ctx.save();
        ctx.translate(p.x + (p.spin ? Math.cos(ph) * p.spin : 0),
                      p.y + (p.spin ? Math.sin(ph) * p.spin * 0.42 : 0));
        ctx.rotate(p.rot);
        (p.kind === 'heart' ? heartPath : starPath)(ctx, r);
        ctx.fillStyle = p.fill;
        ctx.fill();
        if (p.line) { ctx.lineWidth = p.lw; ctx.strokeStyle = p.line; ctx.stroke(); }
        ctx.restore();
      } else if (p.kind === 'card') {
        /* 卡片：一张照片。它跟 chip 的区别不是参数而是**语义** —— chip 是
           "空中翻滚的薄片"，带描边时圆角被拉到半高满值、短边收成半圆，
           再配合按 cos(rot) 的压扁，无论怎么调参数都只会读成一颗胶囊。
           而照片必须始终是个有直角的矩形，否则"是不是照片"就没了，那正是
           这个粒子全部的意义。所以它不压扁、不圆角，只是斜着飘。
           内芯那一块是相纸中间的画面：没有它就只是一张白纸。 */
        ctx.save();
        ctx.translate(p.x, p.y);
        ctx.rotate(p.rot);
        const hw = p.w / 2, hh2 = p.h / 2;
        ctx.fillStyle = p.fill;
        ctx.fillRect(-hw, -hh2, p.w, p.h);
        if (p.line) {
          ctx.lineWidth = p.lw; ctx.strokeStyle = p.line;
          ctx.strokeRect(-hw, -hh2, p.w, p.h);
          ctx.globalAlpha = alpha * 0.5;
          ctx.fillStyle = p.line;
          ctx.fillRect(-hw * 0.74, -hh2 * 0.82, p.w * 0.74, p.h * 0.58);
        }
        ctx.restore();

      } else {
        ctx.save();
        ctx.translate(p.x, p.y);
        ctx.rotate(p.rot);
        // 翻滚时按 cos 压扁，读起来是一片薄东西在空中打转
        const hh = p.h / 2 * Math.abs(Math.cos(p.rot * 1.7));
        ctx.fillStyle = p.fill;
        if (p.line) {
          // 胶囊形：羽毛和碎屑都不是方砖，描边一上去直角就很扎眼。圆角给到
          // 半高的满值，短边直接收成半圆 —— 长宽比大的时候读成羽毛，接近
          // 正方的时候读成碎片，一个形状覆盖两种题材。
          ctx.beginPath();
          const rr = Math.min(p.w, hh * 2) * 0.5;
          ctx.roundRect(-p.w / 2, -hh, p.w, hh * 2, rr);
          ctx.fill();
          ctx.lineWidth = p.lw; ctx.strokeStyle = p.line;
          ctx.stroke();
        } else {
          ctx.fillRect(-p.w / 2, -hh, p.w, hh * 2);
        }
        ctx.restore();
      }
    }
    ctx.restore();

    // 第二趟：发光部分
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < act.length; i++) {
      const p = act[i];
      if (p.kind === 'soft' || p.kind === 'chip' || p.kind === 'star' || p.kind === 'heart' || p.kind === 'card') continue;
      const k = p.life / p.maxLife;
      const alpha = p.a * fade(p);
      if (alpha <= 0.01) continue;
      ctx.globalAlpha = alpha;

      if (p.kind === 'dot') {
        const r = p.r + (p.r1 - p.r) * (1 - k);
        ctx.drawImage(p.tex, p.x - r, p.y - r, r * 2, r * 2);

      } else if (p.kind === 'spark') {
        const sp = Math.hypot(p.vx, p.vy);
        const len = Math.min(26, 4 + sp * 0.028);
        const nx = sp ? p.vx / sp : 1, ny = sp ? p.vy / sp : 0;
        ctx.strokeStyle = p.fill;
        ctx.lineWidth = p.lw;
        ctx.lineCap = 'round';
        ctx.beginPath();
        ctx.moveTo(p.x, p.y);
        ctx.lineTo(p.x - nx * len, p.y - ny * len);
        ctx.stroke();

      } else if (p.kind === 'ring') {
        const r = p.r + (p.r1 - p.r) * (1 - k);
        ctx.strokeStyle = p.fill;
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
    if (cool > 0) { cool -= dt; if (cool <= 0) lvl = 0; }
    if (stop > 0) { stop -= dt; return 0; }
    return dt;
  }

  /* 顿帧必须有不应期。
     "冻住"之所以有力，是因为它打断了流动 —— 而连点时命中本来就是连续的，
     每一击都冻的话顿帧首尾相接，世界就长期停摆：帧率一点没掉，观众却读成
     "卡了"。实测连点时有 65% 的帧是冻住的，把礼物间隔放宽三倍还是 65%，
     说明撑起它的不是礼物密度，是"每一击都冻"这条规则本身。
     所以冻完之后，要让世界正常流动更长的一段时间。比例给到 1:2 而不是 1:1：
     命中越密集，顿帧越该退让 —— 每秒有九件礼物落地的时候根本没有"这一击"
     可言，观众要看的是整体的热闹，而一半时间不动的画面只会读成掉帧。
     更重的一击可以打断正在演的那一次 —— 大礼物压过点赞，这个优先级跟别处
     是一致的。 */
  function hitStop(sec) {
    if (sec <= 0) return;
    if (cool > 0 && sec <= lvl) return;
    stop = Math.max(stop, sec);
    lvl = sec;
    cool = sec * 3;          // 冻 sec，再流动 2*sec —— 占空比到顶三分之一
  }
  function addShake(v) { shake = Math.min(30, shake + v); }
  function addFlash(v) { flash = Math.max(flash, v); }
  function clear() { while (act.length) pool.push(act.pop()); shake = flash = stop = cool = lvl = 0; }

  return {
    spawn, update, draw, drawFlash, tick,
    hitStop, addShake, addFlash, clear,
    off, count: () => act.length,
  };
})();
