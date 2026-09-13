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
  let frontAt = null, onHit = null, W = 960;

  function init(o) { frontAt = o.frontAt; onHit = o.onHit; W = o.W || 960; }

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
  function launch(g, fixedY) {
    const y0 = fixedY != null ? fixedY : 380 + Math.random() * 520;
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

  function fire(q) {
    if (act.length >= MAX) return;
    const g = q.g, sp = SPEED[g.style] || 900;
    const p = pool.pop() || {};
    p.g = g; p.item = g.item; p.r = g.r; p.y = q.y;
    p.from = g.from;
    p.x = g.from > 0 ? -g.r - 40 : W + g.r + 40;
    p.vx = g.from * sp * (1 + (Math.random() - 0.5) * 2 * (JIT[g.style] || 0.2));
    p.ax = ACC[g.style] || 0;
    p.rot = Math.random() * 6.283;
    // 转速跟着体量走，小东西翻得快。方向也随机，一批里有顺时针有逆时针
    p.vrot = (Math.random() - 0.5) * (g.style === 'volley' ? 26 : g.style === 'single' ? 13 : 4.5);
    /* 轨迹环形缓冲：拖尾画的是这个东西**真正走过**的地方。原先那几条速度线
       是固定画在本体后方的，跟实际路径无关，所以飞得快飞得慢看上去一个样；
       记下真实轨迹之后，拖尾长度自己就跟速度挂上钩了。 */
    p.hx = p.hx || new Float64Array(8);
    p.hr = p.hr || new Float64Array(8);
    p.hx.fill(p.x); p.hr.fill(p.rot); p.hi = 0;
    act.push(p);
  }

  /* ---------- 推进 ---------- */

  function update(dt) {
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

      p.hi = (p.hi + 1) & 7;
      p.hx[p.hi] = p.x; p.hr[p.hi] = p.rot;

      const fx = frontAt(p.y);
      const hit = p.from > 0 ? p.x >= fx : p.x <= fx;
      if (hit) {
        act.splice(i, 1); pool.push(p);
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
      const tail = p.hx[(p.hi + 1) & 7];        // 七帧前的位置，拖尾带拉到这里

      /* 拖尾带：从轨迹最老的那一点收拢到本体。用暗调而不是亮色 —— 明亮客厅
         底图上浅色线几乎看不见，跟粒子配色是同一条规矩：靠轮廓不靠亮度。
         它的长度不是写死的，是这一发**实际飞过**的距离，所以速度抖动一上来
         就看得出谁快谁慢。 */
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
      ctx.restore();

      /* 残影：在它前四帧待过的地方，把同一个东西再画一遍，越 old 越淡越小。
         带描边一起画 —— 这是赛璐璐里的速度残影，不是发光拖影，少了那圈线
         就糊成一片。

         取连续四帧而不是每隔一帧。隔帧取的话残影之间的空隙比物体本身还宽，
         读出来是"一串独立的小东西"而不是"一个东西拖出来的影"。长度交给
         下面那条拖尾带去表达，残影只负责把中间填实。 */
      ctx.save();
      ctx.lineJoin = 'round';
      for (let k = 4; k >= 1; k--) {
        const idx = (p.hi + 8 - k) & 7;
        ctx.globalAlpha = 0.42 - k * 0.075;
        ctx.save();
        ctx.translate(p.hx[idx], p.y);
        ctx.rotate(p.hr[idx]);
        const sc = 1 - k * 0.045;
        ctx.scale(sc, sc);
        ITEM[p.item](ctx, p.r);
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
    queue.length = 0; warns.length = 0;
  }

  return { init, launch, update, draw, clear, ITEM, count: () => act.length + queue.length };
})();
