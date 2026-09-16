/* bubble.js —— 手机上方的聊天气泡
 *
 * 这个玩法争的是一部手机，而手机里到底有什么，此前画面上一个字都没交代。
 * 观众看到的是两个人在拔河，看不到他们**为什么**拔河 —— 气泡补的就是这个。
 * 消息一条条冒出来，争夺的理由才立得住，而且它每隔一两秒就给观众一个新的
 * 阅读目标，这是直播画面最缺的东西。
 *
 * 位置是按底版量出来的：手机在 y≈650，两个人的脸在 520~610，而 y=150~470
 * 那一整片（沙发上沿到血条之间的墙）完全空着。所以气泡从手机正上方升起，
 * 穿过两人中间那条缝，最后停在墙面上 —— 不停在手机边上，是因为那里全是手
 * 和脸，而那是这个玩法唯一值得看的东西。对抗线当初被改掉也是同一个理由。
 *
 * 四种消息各有各的作用，凑在一起就是"这部手机为什么不能给你看"：
 *   text   日常的一来一往，给密度
 *   hot    暧昧的那几句，给动机
 *   sys    撤回、正在输入 —— 什么都没说，但最让人往下想
 *   voice  一条没听过的语音，同上
 * 全程不出现任何露骨的词，观众自己脑补的永远比写出来的狠。
 */
'use strict';

const Bubble = (function () {
  const MAX = 3;            // 同屏上限。再多就成了刷屏，单条反而没人读
  /* 飘多高、让多少，是被上下两头夹出来的：
       下边界 —— 出生点在手机上方 44px（y≈516），那里正好是两人中间的沙发，
                不挡脸；再低就贴到抢手机的那两双手上了
       上边界 —— 血条在 y=74~105，堆到第四条时不能顶进去
     所以 RISE + (MAX-1)*STACK 必须 ≤ 341，最高那条的顶边才停在 150 左右。 */
  const RISE = 140;         // 从出生点往上飘多远（减速趋近，不是匀速）
  /* 让位的距离必须**大于**气泡自己的高度，否则两条上下贴死，读起来是一坨。
     大气泡高 64，所以 76 留出 12px 的缝。同屏上限跟着从 4 压到 3 —— 约束是
     RISE + (MAX-1)*STACK ≤ 341（血条下沿到出生点之间的那片墙）。 */
  const STACK = 76;         // 新的进来时，旧的往上让这么多 —— 像聊天记录往上顶
  const LIFE = 4.6;
  const FADE = 0.9;         // 末尾这段时间淡出

  const act = [];
  let phoneAt = null, gap = 1.2;

  /* 文字宽度得测，不能估。中文和数字宽度差很多，估出来的气泡不是撑爆就是
     留一大块空白。离屏 ctx 测一次就够，结果缓存在消息上。 */
  const mctx = document.createElement('canvas').getContext('2d');
  const FONT = '600 34px system-ui,"PingFang SC","Microsoft YaHei",sans-serif';
  const FONT_S = '600 26px system-ui,"PingFang SC","Microsoft YaHei",sans-serif';

  /* 消息池。权重就是出现频率 —— 日常最多，暧昧的那几句要稀，稀才有分量；
     系统提示和语音条各占一小份，它们是"留白"，多了就不灵了。 */
  const MSG = [
    { t: 'text', s: '在吗', w: 3 },
    { t: 'text', s: '睡了没', w: 3 },
    { t: 'text', s: '怎么不回我', w: 3 },
    { t: 'text', s: '洗澡去了', w: 3 },
    { t: 'text', s: '刚开完会', w: 2 },
    { t: 'text', s: '这么晚还没睡？', w: 2 },
    { t: 'text', s: '明天有空吗', w: 2 },
    { t: 'text', s: '记得吃饭', w: 2 },
    { t: 'text', s: '那我等你', w: 2 },
    { t: 'hot',  s: '我好想你', w: 2 },
    { t: 'hot',  s: '今天梦到你了', w: 1 },
    { t: 'hot',  s: '还是你懂我', w: 1 },
    { t: 'hot',  s: '什么时候见一面', w: 1 },
    { t: 'hot',  s: '别被发现了', w: 1 },
    { t: 'sys',  s: '对方撤回了一条消息', w: 2 },
    { t: 'sys',  s: '对方正在输入…', w: 2 },
    { t: 'sys',  s: '3 条未读消息', w: 1 },
    { t: 'voice', s: '7″', w: 1 },
    { t: 'voice', s: '12″', w: 1 },
    { t: 'voice', s: '23″', w: 1 },
  ];
  const TOTAL_W = MSG.reduce((a, m) => a + m.w, 0);

  function pick() {
    let r = Math.random() * TOTAL_W;
    for (const m of MSG) { r -= m.w; if (r <= 0) return m; }
    return MSG[0];
  }

  /* 三种底色。白的是日常，粉的是那几句，灰的是系统提示。
     都带深色描边 —— 明亮客厅底图上，浅色块不描边就糊进墙里，跟碎片、跟角色
     线稿是同一条规矩。 */
  const SKIN = {
    text:  { bg: '#fbfbfd', line: 'rgba(46,40,52,.88)', fg: '#2c2830', lw: 3.0 },
    hot:   { bg: '#ffe2ee', line: 'rgba(206,64,116,.95)', fg: '#96214f', lw: 3.4 },
    sys:   { bg: 'rgba(226,226,232,.94)', line: 'rgba(96,94,106,.7)', fg: '#5e5c68', lw: 2.2 },
    voice: { bg: '#fbfbfd', line: 'rgba(46,40,52,.88)', fg: '#2c2830', lw: 3.0 },
  };

  function init(o) { phoneAt = o.phoneAt; }

  /* 推一条新消息。旧的整体往上让一格 —— 读起来就是聊天记录在往上滚，而不是
     几个气泡各飘各的。 */
  function push(forceType) {
    let m;
    if (forceType) {
      const sub = MSG.filter(x => x.t === forceType);
      m = sub[Math.random() * sub.length | 0] || MSG[0];
    } else m = pick();
    const small = m.t === 'sys';
    if (m.cw == null) { mctx.font = small ? FONT_S : FONT; m.cw = mctx.measureText(m.s).width; }
    const pad = small ? 22 : 30;
    const w = m.cw + pad * 2 + (m.t === 'voice' ? 58 : 0);   // 语音条左边还要放喇叭
    const h = small ? 50 : 64;

    for (const b of act) b.st++;
    if (act.length >= MAX) act.shift();

    const [px, py] = phoneAt();
    act.push({ m, w, h, st: 0, x: px, y: py - 44, rise: 0, age: 0, life: LIFE });
  }

  function update(dt, struggle) {
    /* 越僵持发得越急。五五开的时候两个人谁也拽不动，画面上信息量最少 ——
       正好让手机替他们说话；而一边倒的时候胜负本身已经够看了。 */
    gap -= dt;
    if (gap <= 0) { push(); gap = (1.5 + Math.random() * 1.3) * (1.35 - (struggle || 0) * 0.5); }

    for (let i = act.length - 1; i >= 0; i--) {
      const b = act[i];
      b.age += dt; b.life -= dt;
      if (b.life <= 0) { act.splice(i, 1); continue; }
      // 减速上升：冒出来那一下快，之后慢慢停住。匀速飘会读成"气球"
      b.rise += (RISE - b.rise) * (1 - Math.exp(-2.4 * dt));
    }
  }

  function draw(ctx) {
    for (let i = 0; i < act.length; i++) {
      const b = act[i], m = b.m, sk = SKIN[m.t];
      const small = m.t === 'sys';
      /* 弹入：0.6 → 冲过 1 一点点 → 收回 1。消息是"跳"出来的，线性放大读起来
         像是慢慢显影，那是另一种情绪。 */
      const e = Math.min(1, b.age / 0.16);
      const sc = e < 1 ? 0.62 + 0.52 * e - 0.14 * e * e : 1;
      const alpha = Math.min(1, b.life / FADE) * Math.min(1, b.age / 0.07);
      if (alpha <= 0.01) continue;

      const y = b.y - b.rise - b.st * STACK;
      const w2 = b.w / 2, h2 = b.h / 2;

      ctx.save();
      ctx.globalAlpha = alpha;
      ctx.translate(b.x, y);
      ctx.scale(sc, sc);
      ctx.lineJoin = 'round';

      /* 尾巴只给最新的那一条。上面几条已经是"记录"了，都拖着尾巴的话，读起来
         是每一条从下面那条里冒出来的 —— 而它们其实都来自同一部手机。
         先画尾巴，再让气泡本体盖住它的上边，两个形状才连得成一个。 */
      if (!small && b.st === 0) {
        ctx.beginPath();
        ctx.moveTo(-16, h2 - 2); ctx.lineTo(-2, h2 + 17); ctx.lineTo(10, h2 - 2);
        ctx.closePath();
        ctx.fillStyle = sk.bg; ctx.fill();
        ctx.lineWidth = sk.lw; ctx.strokeStyle = sk.line; ctx.stroke();
      }

      ctx.beginPath();
      ctx.roundRect(-w2, -h2, b.w, b.h, small ? h2 : 20);
      ctx.fillStyle = sk.bg; ctx.fill();
      ctx.lineWidth = sk.lw; ctx.strokeStyle = sk.line; ctx.stroke();

      ctx.fillStyle = sk.fg;
      ctx.textBaseline = 'middle';
      if (m.t === 'voice') {
        /* 语音条：三根高低不一的竖线 + 时长。观众听不到里面是什么，而这正是
           它比任何一句台词都有劲的地方。 */
        ctx.textAlign = 'left';
        ctx.font = FONT;
        const bx = -w2 + 30;
        for (let k = 0; k < 3; k++) {
          const hh = 10 + k * 8;
          ctx.fillRect(bx + k * 12, -hh / 2, 6, hh);
        }
        ctx.fillText(m.s, bx + 52, 1);
      } else {
        ctx.textAlign = 'center';
        ctx.font = small ? FONT_S : FONT;
        ctx.fillText(m.s, 0, 1);
      }
      ctx.restore();
    }
  }

  function clear() { act.length = 0; gap = 1.2; }

  return { init, push, update, draw, clear, count: () => act.length };
})();
