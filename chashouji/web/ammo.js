/* ammo.js —— 礼物弹幕：飞过去，撞到对抗线才算数
 *
 * 分两层，跟 fx.js 的"形态 vs 题材"是同一个分法：
 *   样式（volley / single / heavy）—— 三种，管发射节奏与体量
 *   物品（发卡 / 抱枕 / 手柄 …）—— 可以无限加，只是皮
 * 观众不需要认出飞过来的是什么，光看节奏就知道这一发有多重。加新礼物只往
 * main.js 的 GIFT 表里添一行，这个文件不用动；除非要加新物品的画法。
 *
 * 命中判定用的是 frontAt(y) —— 对抗线在弹幕**自己那个高度**上的真实横坐标，
 * 不是中点。所以不同高度飞来的弹幕会在不同的行注入冲量，那条线才活得起来。
 *
 * 物品目前是代码画的：几何剪影 + 粗描边 + 高对比色。飞行物在画面上只有
 * 60~160px 而且高速移动，这个精度足够验证节奏 —— 而节奏是这个功能的全部。
 * 之后换成生图贴图，只改 ITEM 表里的函数体。
 */
'use strict';

const Ammo = (function () {
  const MAX = 48;
  const act = [], pool = [];
  const queue = [];          // 待发射：连珠的后续几颗、重投的预警期
  const warns = [];          // 预警箭头
  let frontAt = null, onHit = null, onClash = null, W = 960;

  function init(o) { frontAt = o.frontAt; onHit = o.onHit; onClash = o.onClash || (() => {}); W = o.W || 960; }

  /* 独占窗口。档 4 落地后的这一段时间里，别的东西不许出现在屏幕上 ——
     这是最强的一种表现手段，而且不需要任何新的粒子技术：观众看到的是
     "全世界让开，只有这一下"。礼物级的排队补发，常规火力直接丢掉
     （它只是火力的表现，数值那边早就算过了，少画几发不影响战况）。 */
  let lock = 0;
  const pending = [];

  /* ---------- 物品画法 ---------- */

  /* 都在原点画，调用方已经 translate + rotate 过了，r 是半宽。
     每一件都是"填充 + 深色粗描边"：底图是明亮客厅，实体靠轮廓而不是靠亮度
     才看得见 —— 跟角色睡衣有线稿是同一个道理。 */
  const INK = 'rgb(52,40,36)';
  function ink(ctx, lw) { ctx.lineWidth = lw; ctx.strokeStyle = INK; ctx.stroke(); }

  const ITEM = {
    // 发卡：一根粉色小棒加一颗珠子，连珠用
    hairpin(ctx, r) {
      ctx.beginPath(); ctx.roundRect(-r, -r * 0.3, r * 2, r * 0.6, r * 0.3);
      ctx.fillStyle = '#ff8fb8'; ctx.fill(); ink(ctx, r * 0.26);
      ctx.beginPath(); ctx.arc(-r * 0.62, 0, r * 0.46, 0, 6.2832);
      ctx.fillStyle = '#ffd9e8'; ctx.fill(); ink(ctx, r * 0.22);
    },

    // 瓜子壳：一颗水滴，深棕。男方的连珠，嗑瓜子看戏顺手就弹过去了
    seed(ctx, r) {
      ctx.beginPath();
      ctx.moveTo(r, 0);
      ctx.quadraticCurveTo(0, r * 0.66, -r, 0);
      ctx.quadraticCurveTo(0, -r * 0.66, r, 0);
      ctx.fillStyle = '#6b5136'; ctx.fill(); ink(ctx, r * 0.24);
      ctx.beginPath(); ctx.moveTo(r * 0.5, 0); ctx.lineTo(-r * 0.62, 0);
      ctx.lineWidth = r * 0.16; ctx.strokeStyle = 'rgba(255,240,220,.5)'; ctx.stroke();
    },

    // 抱枕：圆角方 + 缝线 + 两点兔耳暗示，跟沙发上那只兔抱枕呼应
    pillow(ctx, r) {
      ctx.beginPath(); ctx.roundRect(-r, -r * 0.84, r * 2, r * 1.68, r * 0.4);
      ctx.fillStyle = '#fdf2f6'; ctx.fill(); ink(ctx, r * 0.12);
      ctx.beginPath(); ctx.roundRect(-r * 0.78, -r * 0.64, r * 1.56, r * 1.28, r * 0.3);
      ctx.lineWidth = r * 0.055; ctx.strokeStyle = 'rgba(255,160,195,.85)'; ctx.stroke();
      ctx.fillStyle = '#ffb3cd';
      for (const s of [-1, 1]) {
        ctx.beginPath(); ctx.ellipse(s * r * 0.26, -r * 0.16, r * 0.1, r * 0.26, 0, 0, 6.2832); ctx.fill();
      }
    },

    // 游戏手柄：深色机身 + 一个摇杆一个按键，小但好认
    gamepad(ctx, r) {
      ctx.beginPath(); ctx.roundRect(-r, -r * 0.48, r * 2, r * 0.96, r * 0.44);
      ctx.fillStyle = '#3d4450'; ctx.fill(); ink(ctx, r * 0.12);
      ctx.beginPath(); ctx.arc(-r * 0.46, 0, r * 0.22, 0, 6.2832);
      ctx.fillStyle = '#8fe3ff'; ctx.fill(); ink(ctx, r * 0.08);
      ctx.beginPath(); ctx.arc(r * 0.44, -r * 0.08, r * 0.16, 0, 6.2832);
      ctx.fillStyle = '#ff7a7a'; ctx.fill(); ink(ctx, r * 0.07);
    },

    // 整条被子：最大的一件，格纹让它在翻滚时读得出体积
    quilt(ctx, r) {
      ctx.beginPath(); ctx.roundRect(-r, -r * 0.7, r * 2, r * 1.4, r * 0.18);
      ctx.fillStyle = '#ffe6ee'; ctx.fill(); ink(ctx, r * 0.085);
      ctx.strokeStyle = 'rgba(255,146,183,.8)'; ctx.lineWidth = r * 0.05;
      ctx.beginPath();
      for (const t of [-0.34, 0.34]) { ctx.moveTo(t * r * 2, -r * 0.7); ctx.lineTo(t * r * 2, r * 0.7); }
      for (const t of [-0.3, 0.3]) { ctx.moveTo(-r, t * r * 1.4); ctx.lineTo(r, t * r * 1.4); }
      ctx.stroke();
      // 翻出来的一角，不然大色块读成一块板子
      ctx.beginPath();
      ctx.moveTo(r, -r * 0.7); ctx.lineTo(r * 0.5, -r * 0.7); ctx.lineTo(r, -r * 0.2);
      ctx.closePath(); ctx.fillStyle = '#fff8fb'; ctx.fill(); ink(ctx, r * 0.07);
    },

    // 外卖箱：牛皮纸色 + 胶带十字，一眼是个箱子
    box(ctx, r) {
      ctx.beginPath(); ctx.roundRect(-r, -r * 0.72, r * 2, r * 1.44, r * 0.1);
      ctx.fillStyle = '#cf9760'; ctx.fill(); ink(ctx, r * 0.085);
      ctx.beginPath(); ctx.roundRect(-r, -r * 0.72, r * 2, r * 0.4, r * 0.1);
      ctx.fillStyle = '#b87f4b'; ctx.fill(); ink(ctx, r * 0.07);
      ctx.strokeStyle = 'rgba(246,238,222,.9)'; ctx.lineWidth = r * 0.13;
      ctx.beginPath();
      ctx.moveTo(0, -r * 0.72); ctx.lineTo(0, r * 0.72);
      ctx.moveTo(-r, r * 0.1); ctx.lineTo(r, r * 0.1);
      ctx.stroke();
    },
  };

  /* 外轮廓剪影 —— 残影专用。
     残影是速度残像，照着本体一笔一笔画满缝线、按键、格纹，既贵（四个残影
     就是四遍完整物品）又脏（细节在高速移动里糊成噪点）。它只需要形状。
     调用方负责 fill，这里只铺路径。 */
  const SILH = {
    hairpin(ctx, r) {
      ctx.beginPath();
      ctx.roundRect(-r, -r * 0.3, r * 2, r * 0.6, r * 0.3);
      ctx.moveTo(-r * 0.16, 0);
      ctx.arc(-r * 0.62, 0, r * 0.46, 0, 6.2832);
    },
    seed(ctx, r) {
      ctx.beginPath();
      ctx.moveTo(r, 0);
      ctx.quadraticCurveTo(0, r * 0.66, -r, 0);
      ctx.quadraticCurveTo(0, -r * 0.66, r, 0);
    },
    pillow(ctx, r) { ctx.beginPath(); ctx.roundRect(-r, -r * 0.84, r * 2, r * 1.68, r * 0.4); },
    gamepad(ctx, r) { ctx.beginPath(); ctx.roundRect(-r, -r * 0.48, r * 2, r * 0.96, r * 0.44); },
    quilt(ctx, r) { ctx.beginPath(); ctx.roundRect(-r, -r * 0.7, r * 2, r * 1.4, r * 0.18); },
    box(ctx, r) { ctx.beginPath(); ctx.roundRect(-r, -r * 0.72, r * 2, r * 1.44, r * 0.1); },
  };

  /* 自发光的颜色。
     这里的"发光"不是加光。明亮客厅底图上 lighter 是加不上去的 —— 浅绿墙
     本来就接近饱和，任何颜色加上去都只是趋向白，红色叠浅绿直接变成纯白。
     所以走的是**色晕**：普通混合的半透明高饱和色，比本体大一圈、边缘柔化。
     它靠色相从背景里跳出来，跟粒子那套"发光的一律高饱和暖橙"是同一条规矩。

     取每件物品主色的高饱和版，顺带把阵营也读出来：查岗党偏粉紫，灭迹党偏
     琥珀与青。观众看一眼弹道的颜色就知道这一发是谁打的。 */
  const AURA = {
    hairpin: [255, 64, 156], pillow: [255, 92, 164], quilt: [255, 76, 148],
    seed: [255, 148, 48], gamepad: [64, 206, 255], box: [255, 136, 40],
  };

  /* 色晕贴图一次性烘好。这台机器没有 GPU，每帧 createRadialGradient 是最贵的
     几件事之一 —— 跟 fx.js 里光斑的处理是同一个理由。六件物品六张，建完就
     不再动。 */
  const auraCache = new Map();
  function aura(rgb) {
    const key = rgb[0] + ',' + rgb[1] + ',' + rgb[2];
    let c = auraCache.get(key);
    if (c) return c;
    const S = 128;
    c = document.createElement('canvas');
    c.width = c.height = S;
    const g = c.getContext('2d');
    const rg = g.createRadialGradient(S / 2, S / 2, 0, S / 2, S / 2, S / 2);
    rg.addColorStop(0.00, `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.92)`);
    rg.addColorStop(0.34, `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.56)`);
    rg.addColorStop(0.68, `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.18)`);
    rg.addColorStop(1.00, `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0)`);
    g.fillStyle = rg; g.fillRect(0, 0, S, S);
    auraCache.set(key, c);
    return c;
  }

  /* 拖尾带的颜色：每件物品自己主色的暗调，不是统一的黑。统一用深色的话，
     拖在浅粉抱枕后面读起来像一团影子或者污渍 —— 那是"另一个东西"，而拖尾
     应该是它自己甩出来的。 */
  const TAIL = {
    hairpin: '176,64,112', seed: '58,42,26', pillow: '196,116,150',
    gamepad: '38,46,58', quilt: '198,112,148', box: '126,82,48',
  };

  /* ---------- 发射 ---------- */

  /* 重投给到 850 而不是更慢。直觉上"大件就该飞得慢"，但屏幕外到对抗线只有
     六百来像素，620 的时候光飞行就要一秒，加上预警一共一秒二 —— 中间那大半
     秒是匀速滑行，画面上什么也没发生，观众的注意力会飘走。分量感是靠体量、
     预警和落地那一下给的，不是靠拖时间。 */
  const SPEED = { volley: 1400, single: 900, heavy: 850 };
  /* 重投是唯一加速的。匀速的大件在屏幕上就是一路平移，中间大半秒什么也没
     发生 —— 拖沓的根子在匀速，不在速度值不够；单纯调快又会丢掉"看清它是
     什么"的那一段。加速两头都要：前段慢得认得出，后段砸得狠，总时长还短了
     三分之一。 */
  const ACC = { heavy: 900 };
  /* 每一发在基准速度上再抖一下。整批同速看着像传送带上排好的货，而连珠本来
     该读成"抓一把撒过去"—— 有的先到有的后到，命中的节奏才是碎的。
     重投抖得最少：它有预警，观众在等那一下，节奏不该飘。 */
  const JIT = { volley: 0.34, single: 0.26, heavy: 0.10 };

  /* g 是 main.js 的 GIFT 表里的一行。fixedY 只给诊断胶片用 —— 随机高度会让
     每次截出来的图不一样，没法比。 */
  function launch(g, fixedY, opt) {
    const o = opt || {};
    if (lock > 0 && !o.exec) {
      if (o.gift && pending.length < 6) pending.push([g, fixedY, o]);
      return;
    }
    const y0 = fixedY != null ? fixedY : 380 + Math.random() * 520;
    if (o.exec) {
      /* 处决：预警拉长到半秒多，让全场先看见它要来；体积按 1.8 倍压过来。
         这是"倾倒式"演出的简化实现 —— 等美术到位再换成真正的倾泻，
         飞行逻辑一行都不用改。 */
      warns.push({ from: g.from, y: y0, t: 0.55, max: 0.55 });
      queue.push({ t: 0.55, g, y: y0, exec: true });
      return;
    }
    /* 常规火力：一发就是一发，不走连珠那一串。
       高度要避开两个人的脸（520~610）—— 火力弹幕是连绵不断的，糊在脸上的话
       整局都看不清表情，而表情是这个玩法仅有的两个可读信息之一。礼物弹幕是
       孤立事件，遮一下无妨；常态的那一路不行。 */
    if (o.one) {
      const yy = fixedY != null ? fixedY : (Math.random() < 0.45
        ? 366 + Math.random() * 140          // 脸以上：沙发靠背那一带
        : 636 + Math.random() * 300);        // 脸以下：手和腿那一带
      queue.push({ t: 0, g, y: clampY(yy), clash: o.clash });
      return;
    }
    if (g.style === 'volley') {
      // 连珠：排成一串，间隔 70ms。每一颗单独判定、单独触发一次小命中，
      // 读起来是"哒哒哒"一串轻击而不是一下
      for (let i = 0; i < (g.n || 8); i++)
        queue.push({ t: i * 0.07, g, y: clampY(y0 + (Math.random() - 0.5) * 170) });
    } else if (g.style === 'heavy') {
      /* 重投先预警再发射。大礼物的价值一半在"全场都看见有人刷了大的"——
         冲进来之前得先让观众知道它要来，否则一个大件突然出现在画面里，
         观众只来得及看到它已经砸上了。 */
      warns.push({ from: g.from, y: y0, t: 0.25, max: 0.25 });
      queue.push({ t: 0.25, g, y: y0 });
    } else {
      queue.push({ t: 0, g, y: y0 });
    }
  }

  const clampY = (y) => y < 300 ? 300 : y > 960 ? 960 : y;
  const clamp01 = (v) => v < 0 ? 0 : v > 1 ? 1 : v;

  /* 轨迹环形缓冲的长度。原来是 8 帧，看着够，实际不够：被子 850px/s，八帧
     只往回退了 113 像素，而被子本身就有 156 像素宽 —— 拖尾比物体还短，整条
     全叠在本体底下，画了等于没画。 */
  const TRAIL = 16, TMASK = 15;

  /* 一发弹幕"拖多长"。
     取尺寸和速度里更大的那个：大件按自己的身长拖（否则拖尾埋在本体底下），
     快件按速度拖（一颗小发卡飞得再快，只按身长拖也读不出快）。两条各自都
     会在另一头失效，所以是 max 而不是二选一。 */
  const reachOf = (p) => Math.max(p.r * 2.6, Math.abs(p.vx) * 0.09);

  /* 沿轨迹往回找距离本体 dist 像素的那一点。
     残影和拖尾都按**距离**回溯，不按帧数 —— 按帧数的话同样是四个残影，
     发卡（1400px/s、半宽 22）能拖出四个身长，被子（850px/s、半宽 78）四帧
     只退 57 像素，全糊在本体上。眼睛读的是拖了多长，那是距离不是时间。 */
  function backAt(p, dist) {
    for (let k = 1; k < TRAIL; k++) {
      const idx = (p.hi + TRAIL - k) & TMASK;
      if (Math.abs(p.x - p.hx[idx]) >= dist) return idx;
    }
    return (p.hi + 1) & TMASK;
  }

  function fire(q) {
    /* 池满了就回收最老的那一发，而不是把新的丢掉。原来是直接 return ——
       观众刷了礼物，屏幕上却什么也没飞出来，这是连点时最糟糕的一种反馈。
       act 按发射顺序排，队头那一发飞得最久、离对抗线最近，让它提前退场的
       代价远小于让刚刷的这一件凭空消失。 */
    if (act.length >= MAX) pool.push(act.shift());
    const g = q.g, sp = SPEED[g.style] || 900;
    const p = pool.pop() || {};
    p.g = g; p.item = g.item; p.r = q.exec ? g.r * 1.8 : g.r; p.y = q.y;
    p.from = g.from;
    p.exec = !!q.exec;
    p.clash = !!q.clash;
    // 对撞点落在自己这一侧一点，错开一些 —— 全撞在同一条线上会读成一堵墙
    p.clashOff = 20 + Math.random() * 90;
    p.x = g.from > 0 ? -g.r - 40 : W + g.r + 40;
    p.vx = g.from * sp * (1 + (Math.random() - 0.5) * 2 * (JIT[g.style] || 0.2));
    p.ax = ACC[g.style] || 0;
    /* 处决压过来的速度只有一半。它买的是"一段没人打断的时间"—— 嗖一下飞过去
       就把这段时间还回去了。慢，才有"全场都看着它过来"。 */
    if (p.exec) { p.vx *= 0.5; p.ax *= 0.35; p.vrot *= 0.5; }
    p.rot = Math.random() * 6.283;
    // 转速跟着体量走，小东西翻得快。方向也随机，一批里有顺时针有逆时针
    p.vrot = (Math.random() - 0.5) * (g.style === 'volley' ? 26 : g.style === 'single' ? 13 : 4.5);
    /* 轨迹环形缓冲：拖尾画的是这个东西**真正走过**的地方。原先那几条速度线
       是固定画在本体后方的，跟实际路径无关，所以飞得快飞得慢看上去一个样；
       记下真实轨迹之后，拖尾长度自己就跟速度挂上钩了。 */
    p.hx = p.hx || new Float64Array(TRAIL);
    p.hr = p.hr || new Float64Array(TRAIL);
    p.hx.fill(p.x); p.hr.fill(p.rot); p.hi = 0;
    p.x0 = p.x;                // 起点，用来算"飞到哪儿了"
    p.near = 0;                // 0 刚出场 → 1 贴上对抗线，色晕靠它烧起来
    act.push(p);
  }

  /* ---------- 推进 ---------- */

  function update(dt) {
    if (lock > 0 && (lock -= dt) <= 0) {
      lock = 0;
      while (pending.length) { const [g, y, o] = pending.shift(); launch(g, y, o); }
    }
    for (let i = queue.length - 1; i >= 0; i--) {
      const q = queue[i];
      q.t -= dt;
      if (q.t <= 0) { queue.splice(i, 1); fire(q); }
    }
    for (let i = warns.length - 1; i >= 0; i--) {
      warns[i].t -= dt;
      if (warns[i].t <= 0) warns.splice(i, 1);
    }
    for (let i = act.length - 1; i >= 0; i--) {
      const p = act[i];
      if (p.ax) p.vx += p.from * p.ax * dt;
      p.x += p.vx * dt;
      p.rot += p.vrot * dt;
      /* 重投在途中让画面一直轻轻发抖。每帧加的这一点点会被 0.86/帧 的衰减
         吃掉，稳态就在 1px 上下 —— 不是震动，是压迫感。 */
      if (p.g.style === 'heavy') Particles.addShake(0.15);

      p.hi = (p.hi + 1) & TMASK;
      p.hx[p.hi] = p.x; p.hr[p.hi] = p.rot;

      const fx = frontAt(p.y);
      /* 走完了全程的多少。色晕靠它在命中前一路烧起来 —— 观众在撞上之前就
         知道这一发要到了，而这正是弹幕能制造期待的唯一窗口。 */
      const span = fx - p.x0;
      p.near = span === 0 ? 1 : clamp01((p.x - p.x0) / span);
      if (p.clash) {
        // 对冲掉的那些飞不到人身上，在中线前撞掉
        const cx = fx - p.from * p.clashOff;
        if (p.from > 0 ? p.x >= cx : p.x <= cx) {
          act.splice(i, 1); pool.push(p);
          onClash(p, cx);
          continue;
        }
      }
      const hit = p.from > 0 ? p.x >= fx : p.x <= fx;
      if (hit) {
        act.splice(i, 1); pool.push(p);
        /* 独占窗口从**命中那一刻**才开始。原来写在 launch 里，可处决要飞一秒半
           才落地 —— 等它真砸上的时候窗口早过期了，而飞行途中反倒把常规火力全
           丢光，画面空成一片。"全世界停下来看这一击"说的是这一击落地之后。 */
        if (p.exec) lock = 0.8;
        onHit(p, fx);
      } else if (p.x < -400 || p.x > W + 400) {
        // 对抗线被推到极端位置时弹幕可能追不上，别让它永远飞下去
        act.splice(i, 1); pool.push(p);
      }
    }
  }

  /* ---------- 绘制 ---------- */

  function draw(ctx) {
    // 预警箭头：贴着发射方的屏幕边缘，闪两下
    for (const w of warns) {
      const k = w.t / w.max, blink = 0.5 + Math.abs(Math.sin(k * 18)) * 0.5;
      const x = w.from > 0 ? 58 : W - 58, d = w.from;
      const col = w.from > 0 ? 'rgb(126,217,87)' : 'rgb(255,72,72)';
      ctx.save();
      ctx.globalAlpha = blink;
      ctx.translate(x, w.y);
      // 越接近发射越大，读起来是"它正在逼近"
      ctx.scale(d * (1.1 + (1 - k) * 0.7), 1.1 + (1 - k) * 0.7);
      // 三重箭头，越靠后越淡：一个静止的三角读不出方向，一串才读得出"来了"
      for (let j = 0; j < 3; j++) {
        ctx.globalAlpha = blink * (1 - j * 0.3);
        ctx.beginPath();
        const ox = -j * 26;
        ctx.moveTo(ox + 26, 0); ctx.lineTo(ox - 12, -25); ctx.lineTo(ox - 12, 25);
        ctx.closePath();
        ctx.fillStyle = col; ctx.fill();
        ctx.lineWidth = 4.5; ctx.strokeStyle = 'rgba(20,16,22,.8)'; ctx.stroke();
      }
      ctx.restore();
    }

    for (let i = 0; i < act.length; i++) {
      const p = act[i];
      const au = AURA[p.item] || [255, 140, 60];
      const auStr = `rgb(${au[0]},${au[1]},${au[2]})`;
      const tex = aura(au);
      /* 这一发拖多长。色晕、拖尾、残影全按它摊开，所以三层永远是对齐的 ——
         分别写死各自的长度，快件和大件总有一头对不上。 */
      const reach = reachOf(p);
      const tail = p.hx[backAt(p, reach)];

      /* ① 弹道色晕：沿真实轨迹摆三团，越靠后越小越淡。
         光要铺满整条路径 —— 只挂在本体上的话弹道本身是暗的，读出来是"一个
         发光的东西在移动"，而不是"它带着一道光在走"。
         只比本体大半圈：色晕一大就把物品洗白了，浅粉的被子会整块糊成发光板，
         格纹和翻角全没了。它该是物体边缘的一圈光，不是一团雾。
         越接近对抗线越浓：命中前的最后一段自己会烧起来。 */
      ctx.save();
      const gk = 0.34 + p.near * 0.40;
      for (let k = 2; k >= 0; k--) {
        const idx = k ? backAt(p, reach * k * 0.33) : p.hi;
        const f = 1 - k * 0.19;
        ctx.globalAlpha = gk * f * f;
        const gw = p.r * 1.55 * f, gh = p.r * 1.18 * f;
        ctx.drawImage(tex, p.hx[idx] - gw, p.y - gh, gw * 2, gh * 2);
      }
      ctx.restore();

      /* ② 拖尾带：从轨迹上 reach 那么远的一点收拢到本体。用暗调而不是亮色
         —— 明亮客厅底图上浅色线几乎看不见，跟粒子配色是同一条规矩：靠轮廓
         不靠亮度。长度是这一发**实际飞过**的距离，所以速度抖动一上来就看得
         出谁快谁慢。 */
      ctx.save();
      ctx.globalAlpha = 0.30;
      ctx.fillStyle = `rgb(${TAIL[p.item] || '52,40,36'})`;
      ctx.beginPath();
      ctx.moveTo(tail, p.y - p.r * 0.06);
      ctx.lineTo(p.x, p.y - p.r * 0.5);
      ctx.lineTo(p.x, p.y + p.r * 0.5);
      ctx.lineTo(tail, p.y + p.r * 0.06);
      ctx.closePath();
      ctx.fill();

      /* ③ 弹道亮芯：压在暗拖尾中间的一条细楔子。两条都要 —— 只有暗的读成
         一道划痕，只有亮的在浅底图上又浮不起来；暗的给实体感，亮的给能量感。 */
      ctx.globalAlpha = 0.48;
      ctx.fillStyle = auStr;
      ctx.beginPath();
      ctx.moveTo(tail, p.y);
      ctx.lineTo(p.x, p.y - p.r * 0.26);
      ctx.lineTo(p.x, p.y + p.r * 0.26);
      ctx.closePath();
      ctx.fill();
      ctx.restore();

      /* ④ 残影：在它身后 reach 的四分之一、二分之一…处，把**外轮廓**再画
         一遍，越远越淡越小。只画轮廓不画内部细节 —— 缝线、按键、格纹在高速
         移动里糊成噪点，而且四个残影就是四遍完整物品，那是这个文件里最贵的
         一笔开销。

         用拖尾那个暗调，不用色晕色。残影是**物体的形状**，跟色晕、亮芯不是
         一回事 —— 三层全用同一个高饱和色的话，它们会融成一条实色带，形状
         就没了。发光的靠色相，实体的靠轮廓，这条规矩在这里同样成立。

         间距按距离均分，所以残影之间永远接得上：间距大于物体宽度的话读出来
         是"一串独立的小东西"，而不是"一个东西拖出来的影"。 */
      ctx.save();
      ctx.fillStyle = `rgb(${TAIL[p.item] || '52,40,36'})`;
      const silh = SILH[p.item];
      for (let k = 4; k >= 1; k--) {
        const idx = backAt(p, reach * k * 0.25);
        ctx.globalAlpha = 0.40 - k * 0.068;
        ctx.save();
        ctx.translate(p.hx[idx], p.y);
        ctx.rotate(p.hr[idx]);
        const sc = 1 - k * 0.05;
        ctx.scale(sc, sc);
        silh(ctx, p.r);
        ctx.fill();
        ctx.restore();
      }
      ctx.restore();

      ctx.save();
      ctx.translate(p.x, p.y);
      ctx.rotate(p.rot);
      ctx.lineJoin = 'round';
      ITEM[p.item](ctx, p.r);
      ctx.restore();
    }
  }

  function clear() {
    while (act.length) pool.push(act.pop());
    queue.length = 0; warns.length = 0; pending.length = 0; lock = 0;
  }

  return { init, launch, update, draw, clear, ITEM, count: () => act.length + queue.length };
})();
