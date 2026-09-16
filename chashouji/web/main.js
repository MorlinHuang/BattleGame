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

  /* 挨一下之后的反应。冲击沿对抗线传播、角色被推开又弹回，两件事各有一套
     参数：线是软的（传得快、留得久），人是硬的（推得动、马上站回来）。 */
  waveSpread: 7.0,   // 冲量向相邻行传播的速率
  waveDecay: 0.945,  // 冲量每帧的留存；再高线会晃到一秒开外，像被风吹着
  hitK: 620,         // 角色回中的弹力
  hitDamp: 0.90,     // 角色横向速度的阻尼
  punchDecay: 0.88,  // 缩放脉冲的衰减
  tintDecay: 0.82,   // 染色的衰减
};

/* 数值参数表 —— 整局的手感全在这十来个数上，集中一处方便手改。
   模型是**两层**的：礼物注入的是"火力"，双方火力互相对冲，**只有净差值**
   才把手机往一边拽。两边火力相等时刷得再凶手机也不动 —— 那正是拔河该有的
   样子，也是这个玩法最长的一段时间。
   （错误的做法是让礼物直接加进度：那样没有对冲、先刷的人白刷、一次爆发就能
   结束比赛。） */
const NUM = {
  BURN: 0.05,      // 对冲系数：双方等量消耗，由火力少的一方定速
  LOSS: 0.012,     // 自然流失：势头会过去。时间常数 83 秒
  DPS: 0.08,       // 每 1000 点火力差，每秒把手机推动几个百分点
  /* 稳态时  净差 = 注入速度差 / LOSS  —— 对冲项在两边完全相同，推导时直接
     消掉了。所以手机移动的快慢只取决于"两边刷礼物的速度差"，与刷了多少总量
     无关：都在猛刷就差值小、画面激烈而手机不动；一方停手就立刻被拽走。 */
  SHOT: 9,         // 每消耗这么多火力打出一发弹幕 —— 弹幕就是火力的消耗形式
  MATCH: 720,      // 单局 12 分钟
  EDGE_HOLD: 3,    // 推到端点还要按住这么久才算赢
  SUDDEN_LEAD: 35, // 领先这么多个百分点，持续 SUDDEN_WAIT 秒就进绝杀
  SUDDEN_WAIT: 60,
  SUDDEN: 30,      // 绝杀倒计时
  STAND_AT: 8,     // 进入最后这么多个百分点触发反击时刻
  STAND: 120,      // 反击时刻时长：劣势方注入翻倍，全局只触发一次
  SHIELD_AT: 10,   // 濒死护盾：最后这么多个百分点内，伤害先扣劣势方的火力
  SHIELD_MAX: 0.75,// 濒死减伤的上限：再能扛也不能扛到推不动
  /* 手机的最大速度。净差是没有上限的 —— 大哥一秒注入 600 而对面只有 80 时，
     净差能到四万，折合 3.5 个百分点每秒，十四秒就从中点推到底。那不叫碾压，
     那叫没有过程：观众还没看清发生了什么，比赛已经结束。
     拔河的物理直觉也是这样 —— 再大的力气，手机也不可能瞬间飞过去。
     0.85 表示最快也要一分钟才能从中点推到端点。 */
  MAXDPS: 0.85,
};

const S = {
  p: 50, t: 0, auto: true, line: 3,   // line: 0 全无 / 1 原发光柱 / 2 地面战线+指针 / 3 只要指针
  fA: 0, fB: 0,                       // 火力：A=查岗党(左) B=灭迹党(右)
  budA: 0, budB: 0,                   // 发射预算：火力消耗到一发弹幕的量就打一发
  debA: 0, debB: 0, debKA: 0, debKB: 0,  // 受到的注入减益：剩余秒数与折扣
  clock: 0, phase: 'idle',            // idle 不跑数值（诊断与老演示模式）/ play / sudden / over
  edge: 0, big: 0, sudden: 0,
  stand: 0, standUsed: false,
  winner: 0,
};
const FX = {
  phoneX: MID, phoneY: P.phoneY,
  rowOff: new Array(ROWS).fill(0), rowHeat: new Array(ROWS).fill(0),
  rowImp: new Array(ROWS).fill(0),   // 冲击波，独立于常规形变
  struggle: 1, actorX: 0, jit: 0,
  hitX: 0, hitV: 0,                  // 角色被推开的位移与速度
  punch: 0,                          // 缩放脉冲
  tint: [255, 255, 255], tintA: 0,   // 命中染色
};

const clamp = (v, a, b) => v < a ? a : v > b ? b : v;
const rgba = (c, a) => `rgba(${c[0]},${c[1]},${c[2]},${a})`;
// 帧率无关的指数趋近：min(1,dt*k) 会让节奏随帧率漂移
const approach = (dt, k) => 1 - Math.exp(-k * dt);

/* ---------- 数值层：算出 S.p ---------- */

/* battle 算"手机该往哪走"，derive 算"算出来之后画面长什么样"。分成两个函数
   是因为它们回答的是两个问题，而把它们混在一起正是上一版最大的错误：礼物
   直接改了 S.p，等于把火力这一层整个删掉了。 */
function battle(dt) {
  if (S.phase !== 'play' && S.phase !== 'sudden') return;

  // 减益倒计时。它作用在**注入**上（见 giveGift），不作用在手机上
  if (S.debA > 0 && (S.debA -= dt) <= 0) S.debKA = 0;
  if (S.debB > 0 && (S.debB -= dt) <= 0) S.debKB = 0;
  if (S.stand > 0) S.stand -= dt;

  /* 对冲：双方等量消耗，速度由火力**少**的一方决定。
     这一条是整个模型的关键，它自带一根橡皮筋 —— 劣势方火力少，所以流失慢、
     补起来划算；而优势方想维持差距就得一直喂。不必另外再加"劣势方 x1.5"
     那种补丁，橡皮筋是从机制里长出来的。 */
  const lo = Math.min(S.fA, S.fB);
  const burn = NUM.BURN * lo;
  const useA = (burn + NUM.LOSS * S.fA) * dt;
  const useB = (burn + NUM.LOSS * S.fB) * dt;
  S.fA = Math.max(0, S.fA - useA);
  S.fB = Math.max(0, S.fB - useB);

  /* 打出去的火力就是屏幕上的弹幕。消耗多少就打多少发 —— 于是"对冲掉的那
     部分"和"穿过去的那部分"在画面上是分开的：前者在中线撞掉，后者才砸到人
     身上。观众刷了礼物手机没动时，屏幕上有答案：你的东西被对面在半空撞掉了。 */
  const denA = burn + NUM.LOSS * S.fA, denB = burn + NUM.LOSS * S.fB;
  S.budA += useA; S.budB += useB;
  emitFire(+1, denA > 0 ? burn / denA : 0);
  emitFire(-1, denB > 0 ? burn / denB : 0);

  // 只有净差值才动手机
  const diff = S.fA - S.fB;
  let dmg = (diff / 1000) * NUM.DPS * dt;

  /* 濒死护盾：快被推到头时，火力越足越扛得住。照搬螂人杀"最后 100 滴血，
     兵力够就优先掉兵"的意图 —— 刷礼物能直接保命，而且看得见：火力条长就是
     在替你挡。

     ⚠️ 它**不能去扣火力存量**。试过那种写法，结果是一条正反馈：护盾吃掉劣势
     方的火力 → min 变小 → 对冲跟着变弱 → 优势方的火力不再被烧掉 → 差值反而
     越拉越大。实测净差冲到理论值（Δ注入/LOSS）的 2.4 倍，越接近终点崩得越快。
     根因是火力同时担着两个职责：它既是护盾的燃料，又是对冲的输入，扣一处动
     两处。所以护盾只能按**比例**减伤，不碰存量。 */
  if (dmg !== 0) {
    const losing = dmg > 0 ? -1 : +1;                    // 正在挨打的一方
    const room = losing > 0 ? S.p : 100 - S.p;           // 他离输还有多远
    if (room < NUM.SHIELD_AT) {
      const mine = losing > 0 ? S.fA : S.fB, his = losing > 0 ? S.fB : S.fA;
      const ratio = mine / (his + 1);
      dmg *= 1 - Math.min(NUM.SHIELD_MAX, ratio * 1.5);
    }
  }
  const cap = NUM.MAXDPS * dt;
  if (dmg > cap) dmg = cap; else if (dmg < -cap) dmg = -cap;
  S.p = clamp(S.p + dmg, 0, 100);

  /* 反击时刻：第一次被推到最后 8 个百分点时，劣势方注入翻倍两分钟，全局只
     触发一次。放大的是注入不是伤害 —— 在两层模型里，"更有力"只能是更多火力。 */
  if (!S.standUsed && (S.p < NUM.STAND_AT || S.p > 100 - NUM.STAND_AT)) {
    S.standUsed = true; S.stand = NUM.STAND;
  }

  // 绝杀：一直被压着就别耗了，给 30 秒最后的机会
  const lead = Math.abs(S.p - 50) * 2;
  if (S.phase === 'play') {
    S.big = lead >= NUM.SUDDEN_LEAD * 2 ? S.big + dt : 0;
    if (S.big >= NUM.SUDDEN_WAIT) { S.phase = 'sudden'; S.sudden = NUM.SUDDEN; }
  } else if ((S.sudden -= dt) <= 0) { finish(S.p > 50 ? +1 : -1); return; }

  /* 推到端点还得按住三秒。没有这一条，一次爆发擦过端点就结束比赛，前十分钟
     全部作废 —— 观众读到的是"输得莫名其妙"，不是"输得精彩"。 */
  if (S.p >= 100 || S.p <= 0) {
    S.edge += dt;
    if (S.edge >= NUM.EDGE_HOLD) { finish(S.p >= 100 ? +1 : -1); return; }
  } else S.edge = 0;

  if ((S.clock -= dt) <= 0) finish(S.p > 53 ? +1 : S.p < 47 ? -1 : 0);
}

function finish(who) { S.phase = 'over'; S.winner = who; }

/* 火力转成弹幕。clash 的那些飞到中线就互相撞掉，只有剩下的才砸到人身上 ——
   这是"对冲"唯一的可视化，没有它观众看不懂自己刷的东西去哪了。 */
function emitFire(side, clashRatio) {
  const bud = side > 0 ? 'budA' : 'budB';
  let n = 0;
  while (S[bud] >= NUM.SHOT && n < 3) { S[bud] -= NUM.SHOT; n++; }
  for (let i = 0; i < n; i++) {
    const g = GIFT[side > 0 ? 'hairpin' : 'seed'];
    Ammo.launch(g, null, { one: true, clash: Math.random() < clashRatio });
  }
}

/* 送一件礼物。数值走火力，表现走弹幕 —— 两件事同一个入口，但不是同一层。 */
function giveGift(side, key) {
  const it = SHOP[key]; if (!it) return;
  const deb = side > 0 ? S.debKA : S.debKB;
  const loser = S.p < 50 ? +1 : -1;                 // 谁正落后
  const boost = (S.stand > 0 && side === loser) ? 2 : 1;
  const amt = it.push * (1 - deb) * boost;
  if (side > 0) S.fA += amt; else S.fB += amt;

  /* 高档礼物的第二维度：压制。光靠 push 拉开差距会逼出很难看的数值，而
     "让对方刷的每一件都打折"才是贵真正买到的东西。 */
  if (it.tier === 3) hexDebuff(-side, 0.30, 5);
  if (it.tier === 4) {
    hexDebuff(-side, 0.50, 8);
    // 直接削存量是唯一能瞬间改变差值的手段，也是翻盘的唯一来源
    if (side > 0) S.fB *= 0.5; else S.fA *= 0.5;
  }
  if (it.tier >= 1) {
    const g = GIFT[ITEM_OF[side > 0 ? 'L' : 'R'][it.tier]];
    Ammo.launch(g, null, { gift: true, exec: it.tier === 4 });
  } else {
    // 免费档不飞实体，只在自己那侧冒一小串火花 —— 它买的是参与感，不是战力
    RECIPE.star.burst(side > 0 ? 46 : W - 46, 300 + Math.random() * 520, -side, 0.3);
  }
}

function hexDebuff(side, k, sec) {
  if (side > 0) { S.debKA = Math.max(S.debKA, k); S.debA = Math.max(S.debA, sec); }
  else { S.debKB = Math.max(S.debKB, k); S.debB = Math.max(S.debB, sec); }
}

function startMatch() {
  S.p = 50; S.fA = S.fB = 0; S.budA = S.budB = 0;
  S.debA = S.debB = S.debKA = S.debKB = 0;
  S.clock = NUM.MATCH; S.phase = 'play';
  S.edge = S.big = S.sudden = S.stand = 0; S.standUsed = false; S.winner = 0;
  S.auto = false;
  Ammo.clear(); Particles.clear();
}

/* ---------- 表现层：由 S.p 派生画面 ---------- */
function derive(dt) {
  const bias = (S.p - 50) / 50;
  // p 大 = 查岗党(女方,在左)占优 = 手机被拽向左
  const target = MID - Math.sign(bias) * Math.pow(Math.abs(bias), P.curve) * P.half;
  FX.phoneX += (target - FX.phoneX) * approach(dt, 4.2);

  FX.struggle = 1 - Math.abs(bias) * 0.78;          // 僵持度：五五开时最高
  FX.jit = Math.sin(S.t * 47) * 2.4 * FX.struggle;
  FX.phoneY = P.phoneY + Math.sin(S.t * 9.3) * 6 * FX.struggle - Math.abs(bias) * 14;

  /* 角色被推开又站回来：弹簧-阻尼，不是单纯衰减 —— 单纯衰减只有"飘回去"，
     看不出"被推动了"。挨一下给的是速度不是位移。 */
  FX.hitV += -FX.hitX * P.hitK * dt;
  FX.hitV *= Math.pow(P.hitDamp, dt * 60);
  FX.hitX += FX.hitV * dt;
  FX.actorX = (FX.phoneX - MID) * P.drag + FX.hitX;

  FX.punch *= Math.pow(P.punchDecay, dt * 60);
  if (FX.punch < 0.002) FX.punch = 0;
  FX.tintA *= Math.pow(P.tintDecay, dt * 60);
  if (FX.tintA < 0.004) FX.tintA = 0;

  /* 对抗线上的冲击波：命中那一行注入冲量，随后沿线上下传播并衰减。它与
     rowOff 分开演化、最后一起读 —— rowOff 管"谁在推"（慢、由 p 决定），
     冲量管"刚刚挨了一下"（快、由事件决定）。混在一个数组里的话，一次命中
     会被 shapeRate 的趋近吃掉大半，读不出撞击。 */
  const im = FX.rowImp, nim = new Array(ROWS);
  for (let r = 0; r < ROWS; r++) {
    const nb = ((r > 0 ? im[r - 1] : im[r]) + (r < ROWS - 1 ? im[r + 1] : im[r])) / 2;
    nim[r] = (im[r] + (nb - im[r]) * approach(dt, P.waveSpread)) * Math.pow(P.waveDecay, dt * 60);
  }
  for (let r = 0; r < ROWS; r++) im[r] = Math.abs(nim[r]) < 0.05 ? 0 : nim[r];

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

/* ---------- 命中：一次礼物/点赞落地时发生的全部事情 ---------- */

/* 一次命中同时动五样东西：粒子、对抗线冲量、角色位移与染色、屏幕震动、顿帧。
   写成单一入口而不是散在各处，是因为这五样的强度必须一起缩放 —— 分开调的话
   小礼物会震得比大礼物还狠，而观众读到的"这一下有多重"正是它们的合力。

   side: +1 打向查岗党(左/女方)，-1 打向灭迹党(右/男方)
   power: 1 点赞级  2 普通礼物  3 大礼物 */
function impact(side, y, power, recipe) {
  const r = recipe || RECIPE.thud;
  const s = power >= 4 ? 2.8 : power === 3 ? 1.7 : power === 2 ? 1.0 : 0.55;
  const x = frontAt(y);

  // 冲量注入命中高度那一行，方向朝被打的一侧
  const d = clamp((y - TOP) / (BOT - TOP), 0, 1) * (ROWS - 1);
  const i0 = clamp(Math.floor(d), 0, ROWS - 1);
  FX.rowImp[i0] += -side * 40 * s;
  if (i0 > 0) FX.rowImp[i0 - 1] += -side * 22 * s;
  if (i0 < ROWS - 1) FX.rowImp[i0 + 1] += -side * 22 * s;

  FX.hitV += -side * 320 * s;
  FX.punch = Math.max(FX.punch, 0.045 * s);
  /* 染色只是"挨了一下"的提示，不是照明。超过 0.21 角色的线稿和睡衣花纹就被
     洗掉了，而那正是这个玩法唯一能看的东西 —— 所以它有一个**可读性天花板**，
     档 3 起就顶在那儿，档 4 不会更红。写成 min(天花板, …) 而不是 min(1.4, s)，
     是因为后者看起来像"按分量缩放"，实际从档 3 就封死了，读代码会被骗一次。
     档 4 强在独占那 0.8 秒，不在染得更狠。 */
  const TINT_MAX = 0.21;
  FX.tint = r.tint; FX.tintA = Math.max(FX.tintA, Math.min(TINT_MAX, 0.15 * s));

  Particles.addShake(7 * s);
  Particles.addFlash(power >= 4 ? 0.34 : power >= 3 ? 0.22 : power >= 2 ? 0.10 : 0.03);
  /* 点赞级不顿帧。连珠一串八颗，每颗都冻 35ms 的话，这串"哒哒哒"就被拆成
     八次停顿 —— 而它的表现力全在快。顿帧留给看得出分量的那两档，在那里它
     才是"全世界停下来看这一击"，而不是一段接一段的停摆。 */
  /* 档 4 是"全世界停下来看这一击"：冻 320ms，配合 ammo.js 里的独占窗口，
     这段时间别的礼物只排队不落地。它买的不是更大的数字，是一段没人打断的时间。 */
  Particles.hitStop(power >= 4 ? 0.32 : power >= 3 ? 0.11 : power >= 2 ? 0.07 : 0);

  r.burst(x, y, side, s);
}

/* 配方表：一件礼物炸出什么，只在这里定义。形态（dot/spark/ring/chip/star/soft）
   是通用的，换题材皮不用动 fx.js。

   颜色有一条硬规矩，是这张底图逼出来的：客厅是浅绿墙 + 米色地板，**白色在
   这上面几乎加不亮**。所以发光的那几种一律用高饱和暖橙 —— 它靠色相跳出来
   而不是靠亮度；实体那几种一律深色 + 描边 —— 它靠轮廓跳出来。上一版整套
   用的奶白和浅米，在胶片上基本看不见。

   thud 是通用撞击，任何还没单独配方的东西都落到它上面。 */
const INK = [58, 44, 38];        // 描边色，取角色线稿那个暖黑
const EMBER = [255, 156, 38];    // 发光基色，高饱和暖橙

const RECIPE = {
  thud: {
    tint: [255, 224, 186],
    burst(x, y, side, s) {
      Particles.spawn({ kind: 'dot', x, y, r: 16 * s, r1: 70 * s, life: 0.20,
                        rgb: [255, 196, 110], a: 0.9 });
      Particles.spawn({ kind: 'ring', x, y, r: 10 * s, r1: 120 * s, life: 0.38,
                        rgb: EMBER, lw: 6 * s });
      // 第二道环晚 70ms 出场，读起来是"砰—砰"两下而不是一下
      setTimeout(() => Particles.spawn({ kind: 'ring', x, y, r: 8 * s, r1: 180 * s,
                        life: 0.44, rgb: [255, 132, 54], lw: 4 * s }), 70);
      /* 火花给足数量。画布 960x1334，二三十个粒子铺开就只剩零星几点，
         读不出"炸开"—— 这里的密度感是靠数量堆的，不是靠单颗更亮。 */
      for (let i = 0; i < Math.round(26 * s); i++) {
        const a = (Math.random() - 0.5) * 2.2;
        const sp = (240 + Math.random() * 560) * s;
        Particles.spawn({ kind: 'spark', x, y, vx: -side * Math.cos(a) * sp, vy: Math.sin(a) * sp - 110,
                          g: 980, drag: 0.985, life: 0.24 + Math.random() * 0.3,
                          rgb: i % 4 ? EMBER : [255, 238, 150],
                          lw: 1.6 + Math.random() * 2.4 * s });
      }
      for (let i = 0; i < Math.round(14 * s); i++) {
        Particles.spawn({ kind: 'soft', x: x + (Math.random() - 0.5) * 60 * s, y: y + (Math.random() - 0.3) * 40,
                          vx: -side * (40 + Math.random() * 150) * s, vy: -20 - Math.random() * 80,
                          g: 90, drag: 0.94, r: 10 * s, r1: (46 + Math.random() * 34) * s,
                          life: 0.7 + Math.random() * 0.7, rgb: [150, 128, 110], a: 0.30 });
      }
      // 翻滚的小片：撞击总要崩下点什么，没有它只有光，像是凭空亮了一下
      for (let i = 0; i < Math.round(9 * s); i++) {
        const a = (Math.random() - 0.5) * 2.4;
        const sp = (170 + Math.random() * 330) * s;
        Particles.spawn({ kind: 'chip', x, y, vx: -side * Math.cos(a) * sp, vy: Math.sin(a) * sp - 200,
                          g: 780, drag: 0.99, life: 0.7 + Math.random() * 0.6,
                          w: 6 + Math.random() * 8 * s, h: 4 + Math.random() * 6 * s,
                          rot: Math.random() * 6.28, vrot: (Math.random() - 0.5) * 16,
                          rgb: [122, 96, 78], edge: INK, lw: 1.6, a: 0.95 });
      }
    },
  },

  /* 枕头砸脸。全场唯一一个不出火花的配方 —— 枕头砸下去的是一团闷响和漫天
     绒毛，给它配火花就成了爆炸。冲击感全靠数量和滞空：羽毛重力只有常规的
     八分之一、阻力极大，所以命中半秒之后画面里还在飘，而火花那时候早没了。 */
  feather: {
    tint: [255, 238, 240],
    burst(x, y, side, s) {
      Particles.spawn({ kind: 'dot', x, y, r: 22 * s, r1: 72 * s, life: 0.20,
                        rgb: [255, 206, 198], a: 0.40 });
      // 绒絮：贴着撞击点炸开的那一蓬，用来糊住撞击瞬间。别给多 —— 它是浅色
      // 的，在浅色沙发前面堆厚了就是一团白雾，把羽毛的形状全吃掉。
      for (let i = 0; i < Math.round(11 * s); i++) {
        const a = Math.random() * 6.283;
        Particles.spawn({ kind: 'soft', x, y, vx: Math.cos(a) * (70 + Math.random() * 240) * s,
                          vy: Math.sin(a) * (60 + Math.random() * 180) * s - 90,
                          g: 60, drag: 0.92, r: 14 * s, r1: (48 + Math.random() * 38) * s,
                          life: 0.5 + Math.random() * 0.6, rgb: [214, 200, 206], a: 0.26 });
      }
      /* 羽毛本体：sway 左右摆，慢慢打着旋往下落。
         尺寸和数量是按"直播画面上看得见"定的，不是按真羽毛定的 —— 一根真
         羽毛在 960 宽的画布上只有十几像素，观众端再缩一半就是几个像素的白
         点，等于没有。阻力也不能给真实值：0.958 每帧意味着三分之一秒后羽毛
         就地停住，全堆在命中点上，看着像一摊泡沫而不是炸开的枕头。 */
      for (let i = 0; i < Math.round(30 * s); i++) {
        const a = (Math.random() - 0.5) * 2.8;
        const sp = (200 + Math.random() * 560) * s;
        Particles.spawn({ kind: 'chip', x: x + (Math.random() - 0.5) * 60, y: y + (Math.random() - 0.5) * 80,
                          vx: -side * Math.cos(a) * sp, vy: Math.sin(a) * sp - 170,
                          g: 150, drag: 0.988, sway: 40 + Math.random() * 55,
                          life: 1.5 + Math.random() * 1.4,
                          w: 20 + Math.random() * 17 * s, h: 12 + Math.random() * 9,
                          rot: Math.random() * 6.28, vrot: (Math.random() - 0.5) * 5.0,
                          rgb: i % 5 ? [252, 250, 250] : [250, 232, 236], edge: [138, 118, 122],
                          lw: 1.9, a: 1 });
      }
    },
  },

  /* 打懵了：头顶转圈的星星。这是全套里最"卡通"的一个，也是最省事的一个 ——
     星星本身就是观众对"挨了一下"的默认图示，不需要任何解释。
     spin 让它绕着命中点公转，不是原地飞散：飞散读成爆炸，公转才读成眩晕。 */
  star: {
    tint: [255, 242, 196],
    burst(x, y, side, s) {
      Particles.spawn({ kind: 'dot', x, y, r: 14 * s, r1: 74 * s, life: 0.18,
                        rgb: [255, 214, 96], a: 0.85 });
      Particles.spawn({ kind: 'ring', x, y, r: 8 * s, r1: 108 * s, life: 0.32,
                        rgb: [255, 196, 72], lw: 5 * s });
      // 公转的大星星：数量少，每颗都要看得清，所以描边给足
      for (let i = 0; i < Math.round(7 * s); i++) {
        const a = Math.random() * 6.283;
        Particles.spawn({ kind: 'star', x: x - side * 20, y: y - 40 - Math.random() * 60,
                          vx: -side * (20 + Math.random() * 90), vy: -60 - Math.random() * 90,
                          g: 180, drag: 0.95, spin: 26 + Math.random() * 34,
                          r: (13 + Math.random() * 11) * s, r1: 3,
                          life: 0.7 + Math.random() * 0.6,
                          rot: a, vrot: (Math.random() - 0.5) * 7,
                          rgb: [255, 208, 56], edge: [126, 74, 18], lw: 2.4, a: 1 });
      }
      // 小星星飞散，补密度
      for (let i = 0; i < Math.round(11 * s); i++) {
        const a = (Math.random() - 0.5) * 2.8;
        const sp = (200 + Math.random() * 440) * s;
        Particles.spawn({ kind: 'star', x, y, vx: -side * Math.cos(a) * sp, vy: Math.sin(a) * sp - 180,
                          g: 640, drag: 0.982, r: (6 + Math.random() * 6) * s, r1: 2,
                          life: 0.5 + Math.random() * 0.45,
                          rot: a, vrot: (Math.random() - 0.5) * 14,
                          rgb: [255, 226, 120], edge: [150, 96, 24], lw: 1.6, a: 1 });
      }
      for (let i = 0; i < Math.round(14 * s); i++) {
        const a = (Math.random() - 0.5) * 2.4;
        const sp = (260 + Math.random() * 480) * s;
        Particles.spawn({ kind: 'spark', x, y, vx: -side * Math.cos(a) * sp, vy: Math.sin(a) * sp - 120,
                          g: 900, drag: 0.985, life: 0.2 + Math.random() * 0.22,
                          rgb: [255, 198, 70], lw: 1.4 + Math.random() * 2 * s });
      }
    },
  },

  /* 硬东西砸碎（遥控器、马克杯）。碎片一律深色 —— 这是三个配方里唯一能在
     米色地板上自带对比的，所以它不描边也认得出，描边只是为了和另外两个
     配方看起来是同一套东西。 */
  debris: {
    tint: [226, 238, 255],
    burst(x, y, side, s) {
      Particles.spawn({ kind: 'dot', x, y, r: 18 * s, r1: 82 * s, life: 0.16,
                        rgb: [255, 236, 190], a: 0.95 });
      Particles.spawn({ kind: 'ring', x, y, r: 12 * s, r1: 150 * s, life: 0.30,
                        rgb: [255, 176, 60], lw: 7 * s });
      // 碎片：重、快、弹不起来，落地就停 —— 和羽毛正好是两个极端
      for (let i = 0; i < Math.round(24 * s); i++) {
        const a = (Math.random() - 0.5) * 2.5;
        const sp = (300 + Math.random() * 620) * s;
        const dark = i % 3 === 0;
        Particles.spawn({ kind: 'chip', x, y, vx: -side * Math.cos(a) * sp, vy: Math.sin(a) * sp - 300,
                          g: 1450, drag: 0.995, life: 0.55 + Math.random() * 0.5,
                          w: 5 + Math.random() * 12 * s, h: 4 + Math.random() * 8 * s,
                          rot: Math.random() * 6.28, vrot: (Math.random() - 0.5) * 22,
                          rgb: dark ? [48, 54, 64] : [96, 106, 120], edge: INK, lw: 1.5, a: 1 });
      }
      for (let i = 0; i < Math.round(30 * s); i++) {
        const a = (Math.random() - 0.5) * 2.0;
        const sp = (320 + Math.random() * 700) * s;
        Particles.spawn({ kind: 'spark', x, y, vx: -side * Math.cos(a) * sp, vy: Math.sin(a) * sp - 140,
                          g: 1020, drag: 0.984, life: 0.18 + Math.random() * 0.26,
                          rgb: i % 3 ? EMBER : [255, 244, 176],
                          lw: 1.4 + Math.random() * 2.6 * s });
      }
      // 一小撮灰，落在碎片后面，撞击点不至于干干净净
      for (let i = 0; i < Math.round(9 * s); i++) {
        Particles.spawn({ kind: 'soft', x: x + (Math.random() - 0.5) * 70 * s, y: y + (Math.random() - 0.2) * 50,
                          vx: -side * (50 + Math.random() * 170) * s, vy: -30 - Math.random() * 70,
                          g: 70, drag: 0.93, r: 12 * s, r1: (50 + Math.random() * 40) * s,
                          life: 0.6 + Math.random() * 0.6, rgb: [138, 132, 128], a: 0.34 });
      }
    },
  },
};

/* 礼物有两张表，因为它是两件事。

   SHOP —— **数值**。九件抖音平台礼物，相对价值直接继承《螂人杀》已验证的
   兵数比例（push = 兵数 ÷ 100）：点赞 1、仙女棒 100、魔法镜 2000、
   爱的爆炸 23000、神秘空投 60000……这一套在两个上线玩法里都跑过，不必重定。
   push 注入的是**火力**，不是进度 —— 进度由双方火力的净差积分出来。

   GIFT —— **表现**。一件物品飞出去长什么样：样式、体积、命中配方。

   两张表用 tier 连起来：数值分九档跟着平台礼物走，表现只做五档。档数的上限
   不是价格带定的，是"观众能不能分辨"定的 —— 单投和重投靠体积(56 vs 78)加
   顿帧(70 vs 110ms)已经是可分辨的临界，中间再插一档做出来也白做。所以多件
   平台礼物共用一档表现，扩档只靠质变：有没有实体 → 震不震 → 染不染色 →
   独不独占屏幕。 */
const SHOP = {
  like:    { tier: 0, push: 0.01, name: '点赞' },
  six:     { tier: 0, push: 0.06, name: '666' },
  wand:    { tier: 1, push: 1,    name: '仙女棒' },
  chest:   { tier: 1, push: 10,   name: '技能宝箱' },
  mirror:  { tier: 2, push: 20,   name: '魔法镜' },
  battery: { tier: 2, push: 110,  name: '能量电池' },
  boom:    { tier: 3, push: 230,  name: '爱的爆炸' },
  mic:     { tier: 3, push: 360,  name: '派对话筒' },
  drop:    { tier: 4, push: 600,  name: '神秘空投' },
};

// tier → 该阵营飞出去的是什么。档 4 暂时复用重投的物品（见 exec），等处决演出的美术到位再换
const ITEM_OF = {
  L: [null, 'hairpin', 'pillow', 'quilt', 'quilt'],
  R: [null, 'seed',    'gamepad', 'box',  'box'],
};

/* 三种样式的差别是节奏与体量，不是物品：
     volley 连珠  一串小件快速飞来，每颗单独命中 —— 也是常规火力用的那一种
     single 单投  单件中等速度，看得清是什么东西
     heavy  重投  先预警再慢慢压过来
   观众不需要认出飞过来的是什么，光看节奏就知道这一发有多重。

   push 是这件物品被直接发射时的默认注入量（诊断胶片会用到）；走 SHOP 送礼
   时以 SHOP 的 push 为准 —— 同一个抱枕，魔法镜刷出来和能量电池刷出来
   份量差 5.5 倍，但飞起来是同一个东西。 */
const GIFT = {
  // 查岗党（女方，在左，from=+1）
  hairpin: { from: +1, style: 'volley', item: 'hairpin', r: 22, n: 8, power: 1, recipe: 'star',    push: 1 },
  pillow:  { from: +1, style: 'single', item: 'pillow',  r: 56,       power: 2, recipe: 'feather', push: 20 },
  quilt:   { from: +1, style: 'heavy',  item: 'quilt',   r: 78,       power: 3, recipe: 'feather', push: 230 },
  // 灭迹党（男方，在右，from=-1）
  seed:    { from: -1, style: 'volley', item: 'seed',    r: 21, n: 8, power: 1, recipe: 'star',    push: 1 },
  gamepad: { from: -1, style: 'single', item: 'gamepad', r: 52,       power: 2, recipe: 'debris',  push: 20 },
  box:     { from: -1, style: 'heavy',  item: 'box',     r: 74,       power: 3, recipe: 'debris',  push: 230 },
};

function sampleRow(arr, y) {
  const d = clamp((y - TOP) / (BOT - TOP), 0, 1) * (ROWS - 1);
  const i = clamp(Math.floor(d), 0, ROWS - 2), f = d - i;
  const p0 = arr[Math.max(0, i - 1)], p1 = arr[i], p2 = arr[i + 1], p3 = arr[Math.min(ROWS - 1, i + 2)];
  return p1 + 0.5 * f * (p2 - p0 + f * (2 * p0 - 5 * p1 + 4 * p2 - p3 + f * (3 * (p1 - p2) + p3 - p0)));
}
// 对抗线在高度 y 处的横坐标 —— 手机、光柱、刻度、地面分色全读这一个函数
const frontAt = (y) => FX.phoneX + sampleRow(FX.rowOff, y) + sampleRow(FX.rowImp, y);
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

  /* punch 是缩放脉冲，tint 是命中染色 —— 角色是预渲染帧，做不了受击变形，
     打击反馈只能来自贴图之外。缩放以脚底为锚，人挨了一下会"胀"一下但脚不
     离地；染色走 source-atop，只盖在已画出的角色像素上，不会糊到背景。 */
  draw(ctx, p, offsetX, punch, tint, tintA) {
    const i = this.map[clamp(Math.round(clamp(p, 0, 100)), 0, 100)];
    const k = 1 + (punch || 0);
    const w = FRAME_W * k, h = FRAME_H * k;
    const foot = FRAME_TOP + FRAME_H;
    const dx = offsetX + (FRAME_W - w) / 2;
    ctx.drawImage(this.imgs[i], dx, foot - h, w, h);
    if (tintA > 0.004) {
      ctx.save();
      ctx.globalCompositeOperation = 'source-atop';
      ctx.fillStyle = rgba(tint, tintA);
      // source-atop 只会落在已经画出来的像素上，所以填满整张画布是白费的 ——
      // 角色帧就占中间那条横带，按它的实际矩形填，结果一模一样
      ctx.fillRect(dx, foot - h, w, h);
      ctx.restore();
    }
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

/* 对抗线的替代画法（?line=2）。
   原来那根贯穿全屏的发光柱（?line=1）在这张底图上表现力差，根因和白闪过曝
   是同一个：它走 lighter，而底图是明亮客厅 —— 浅绿墙本来就接近饱和，往上
   加光几乎不改变什么，只剩一团雾；龙王那边同样的写法很炸，是因为它的底图
   是暗色战场。更要命的是它正好横在两个人中间，把抢手机的手和脸挡掉了，而
   那是这个玩法唯一值得看的东西。

   所以战线改成在地上走：深色带 + 势力色描边，靠轮廓而不是靠发光，明亮底图
   上反而更显眼；又完全不挡人。读数一点没少 —— 战线横坐标仍然是 frontAt，
   和手机、刻度尺、地面辉光同一个源。 */
function drawFrontGround(ctx, bias) {
  const R = P.rug, col = Math.abs(bias) < 0.06 ? [250, 250, 250] : (bias > 0 ? GREEN : RED);
  const N = 14;
  const xs = [], ys = [], ws = [];
  for (let i = 0; i <= N; i++) {
    const d = i / N, y = R.top + (R.bot - R.top) * d;
    ys.push(y); xs.push(frontAt(y) + FX.jit);
    ws.push(8 + d * 20);          // 下宽上窄，跟着地毯的透视走
  }
  ctx.save();
  ctx.beginPath();
  ctx.moveTo(R.tl, R.top); ctx.lineTo(R.tr, R.top);
  ctx.lineTo(R.br, R.bot); ctx.lineTo(R.bl, R.bot); ctx.closePath();
  ctx.clip();
  ctx.beginPath();
  for (let i = 0; i <= N; i++) (i ? ctx.lineTo : ctx.moveTo).call(ctx, xs[i] - ws[i], ys[i]);
  for (let i = N; i >= 0; i--) ctx.lineTo(xs[i] + ws[i], ys[i]);
  ctx.closePath();
  /* 双描边：外圈暗、内圈势力色。单描边在地毯的绿和地板的米色上各有一段
     会掉对比，而这条带子从地毯一直压到地板边缘，横跨两种底色。 */
  ctx.fillStyle = 'rgba(22,18,26,.70)';
  ctx.fill();
  ctx.lineWidth = 7; ctx.strokeStyle = 'rgba(12,10,16,.55)'; ctx.stroke();
  ctx.lineWidth = 3.5; ctx.strokeStyle = rgba(col, .98); ctx.stroke();
  ctx.restore();
}

/* 战线在画面顶端的读数：一个不会被任何东西挡住的指针。
   它能脱离地面带单独存在（?line=3，默认），而且这正是推荐的用法 —— 地上
   那条带子横在两个人的腿中间，激烈的时候被挡掉大半，剩下的半截读起来像
   一根立在地上的杆子；指针在画面顶端，既不挡人也永远看得见。
   完全不画（?line=0）也能看出谁占优（地面辉光、血条、刻度尺都在），但读不
   出战线此刻**具体**压在哪一条竖线上，而手机位移就是这个玩法的进度条。 */
function drawFrontMark(ctx, bias) {
  const col = Math.abs(bias) < 0.06 ? [255, 255, 255] : (bias > 0 ? GREEN : RED);
  const x = frontAt(TOP) + FX.jit, y = TOP - 2;
  ctx.save();
  ctx.strokeStyle = 'rgba(12,14,20,.6)'; ctx.lineWidth = 11;
  ctx.beginPath(); ctx.moveTo(x, y + 6); ctx.lineTo(x, y + 52); ctx.stroke();
  ctx.strokeStyle = rgba(col, .98); ctx.lineWidth = 6;
  ctx.beginPath(); ctx.moveTo(x, y + 6); ctx.lineTo(x, y + 52); ctx.stroke();
  ctx.fillStyle = rgba(col, 1);
  ctx.beginPath();
  ctx.moveTo(x, y + 16); ctx.lineTo(x - 21, y - 16); ctx.lineTo(x + 21, y - 16);
  ctx.closePath(); ctx.fill();
  ctx.lineWidth = 3.5; ctx.strokeStyle = 'rgba(12,14,20,.7)'; ctx.stroke();
  ctx.restore();
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

  if (S.phase !== 'idle') {
    /* 火力条 —— 第二个属性必须看得见。观众刷了礼物、手机没动，屏幕上要是
       没有任何交代，他会认为这游戏是假的。这两条细带就是那个交代：它们一起
       涨说明双方在对拼（手机自然不动），一条比另一条长出来的那截才是战况。
       开方是为了让小额也看得出动静 —— 线性的话几百点火力在几千的量程里
       几乎不动一根头发。 */
    const bar = (x, w, dir, f, c) => {
      const k = Math.min(1, Math.sqrt(f / 3000));
      ctx.fillStyle = 'rgba(8,10,13,.55)'; ctx.fillRect(x, 109, w, 10);
      const fw = w * k, gx = dir > 0 ? x : x + w - fw;
      ctx.fillStyle = rgba(c, .86); ctx.fillRect(gx, 109, fw, 10);
    };
    bar(92, 276, 1, S.fA, GREEN);
    bar(572, 296, -1, S.fB, RED);

    const mm = Math.max(0, S.clock);
    ctx.textAlign = 'center'; ctx.font = 'bold 27px ui-monospace,Menlo,monospace';
    ctx.lineWidth = 5; ctx.strokeStyle = 'rgba(0,0,0,.75)';
    let tip = `${mm / 60 | 0}:${String(mm % 60 | 0).padStart(2, '0')}`, col = '#fff';
    if (S.phase === 'sudden') { tip = `绝杀 ${S.sudden.toFixed(0)}`; col = '#ff5a5a'; }
    else if (S.phase === 'over') { tip = S.winner > 0 ? '查岗党胜' : S.winner < 0 ? '灭迹党胜' : '平局'; col = '#ffd45a'; }
    else if (S.stand > 0) { tip = `反击 ${S.stand.toFixed(0)}`; col = '#ffd45a'; }
    ctx.strokeText(tip, 480, 42); ctx.fillStyle = col; ctx.fillText(tip, 480, 42);
  }
  ctx.restore();
}

/* ---------- 启动 ---------- */
const load = (src) => new Promise((ok, no) => { const i = new Image(); i.onload = () => ok(i); i.onerror = no; i.src = src; });

(async function boot() {
  const cvBg = document.getElementById('bg'), cvCh = document.getElementById('ch'), cvFx = document.getElementById('fx');
  const bctx = cvBg.getContext('2d'), cctx = cvCh.getContext('2d'), fctx = cvFx.getContext('2d');
  initTex();

  /* ?sim=1 纯数值快进：不渲染、不发弹幕，只跑 battle，用来核对局长和手感。
     数值调参不该靠看画面 —— 一局十二分钟，肉眼比对两组参数根本比不出来，
     而这里两秒钟就能把三个场景各跑一遍。
       simA/simB  两边每秒注入多少火力（一个爱的爆炸是 230）
       simT       最多模拟多少秒
     例：?sim=1&simA=23&simB=11.5  = 优势方每 10 秒一个爱的爆炸、劣势方一半 */
  const Q0 = new URLSearchParams(location.search);
  if (Q0.has('sim')) {
    const injA = +(Q0.get('simA') || 23), injB = +(Q0.get('simB') || 0);
    const cap = +(Q0.get('simT') || 1800);
    Ammo.launch = () => {};                     // 纯数值，不要表现层
    startMatch();
    const H = 1 / 30;
    let el = 0, mark50 = -1, mark80 = -1;
    while (S.phase !== 'over' && el < cap) {
      S.fA += injA * H; S.fB += injB * H;
      battle(H); el += H;
      const lead = Math.abs(S.p - 50);
      if (mark50 < 0 && lead >= 25) mark50 = el;
      if (mark80 < 0 && lead >= 40) mark80 = el;
    }
    const fmt = (v) => v < 0 ? '—' : `${v / 60 | 0}:${String(v % 60 | 0).padStart(2, '0')}`;
    document.getElementById('msg').textContent =
      `注入 ${injA}:${injB}／秒 → 结束于 ${fmt(el)}  p=${S.p.toFixed(1)}  `
      + `火力 ${S.fA.toFixed(0)}:${S.fB.toFixed(0)}  净差 ${(S.fA - S.fB).toFixed(0)}  `
      + `过75%档 ${fmt(mark50)}  过90%档 ${fmt(mark80)}  `
      + `${S.winner > 0 ? '查岗党胜' : S.winner < 0 ? '灭迹党胜' : '平/未分'}`;
    document.title = 'SIMDONE ' + document.getElementById('msg').textContent;
    return;
  }


  /* 弹幕命中的是对抗线在**它自己那个高度**上的横坐标，不是中点。所以从不同
     高度飞来的弹幕会在不同的行注入冲量 —— 在这之前所有命中都发生在 phoneY
     一个位置上，那条线永远只在同一处抖。 */
  Ammo.init({
    W, frontAt,
    /* 命中只负责演出，**不改进度**。进度是双方火力净差积分出来的（见 battle）——
       让命中再推一次，等于同一份伤害算两遍，而且会把"两边都在刷时手机不动"
       这条最要紧的手感破坏掉。弹幕是火力的表现形式，不是伤害的来源。 */
    onHit(p) {
      impact(-p.from, p.y, p.exec ? 4 : p.g.power, RECIPE[p.g.recipe]);
    },
    /* 对冲掉的那些在中线互相撞掉：粒子照爆，但不推角色、不染色、不顿帧。
       它要回答的问题只有一个 —— "我刷了礼物怎么手机没动"。答案就在画面上：
       你的东西被对面在半空撞掉了。 */
    onClash(p, x) {
      RECIPE[p.g.recipe].burst(x, p.y, p.from, 0.42);
      Particles.addShake(0.6);
    },
  });

  /* 气泡挂在手机上 —— phonePos 是对抗线在手机高度上的横坐标，跟手机、刻度尺、
     地面辉光同一个源，所以消息永远是从正在被抢的那部手机里冒出来的。 */
  Bubble.init({ phoneAt: phonePos });

  const bg = await load('assets/bg.jpg');
  /* 每 1% 一张。缺的档位解码失败是预期内的，取 null 交给 FrameSeq 映射到邻居。 */
  const frames = await Promise.all(
    Array.from({ length: 101 }, (_, p) =>
      load(`assets/frames/f${String(p).padStart(3, '0')}.png`).catch(() => null)));
  const seq = new FrameSeq(frames);
  document.getElementById('msg').textContent =
    `${frames.filter(Boolean).length}/101 档 · 每 1%`;

  const Q = new URLSearchParams(location.search);

  /* ?live=1&liveA=&liveB=&livet= 真实对局截图模式。
     数值可以用 ?sim 验，但"火力条好不好读""对撞看不看得出来""处决够不够狠"
     只能看画面。快进到指定秒数再渲染，就能截到任意战况下的那一帧。 */
  let liveA = 0, liveB = 0, live = false, freeze = false, stopAll = false;
  if (Q.has('live')) {
    live = true;
    liveA = +(Q.get('liveA') || 23); liveB = +(Q.get('liveB') || 11.5);
    startMatch();
    const warm = clamp(+(Q.get('livet') || 0), 0, 600);
    // liveGift 在预热的最后一刻送一件礼物出去 —— 顿帧、独占、处决这些只在
    // 落地后的零点几秒里存在，不指定时刻的话截不到
    const gk = Q.get('liveGift'), gAt = clamp(+(Q.get('liveAt') || warm), 0, 600);
    for (let k = 0; k < warm * 30; k++) {
      S.fA += liveA / 30; S.fB += liveB / 30;
      if (gk && k === Math.floor(gAt * 30)) giveGift(+(Q.get('liveSide') || 1), gk);
      battle(1 / 30); Ammo.update(1 / 30); Particles.update(1 / 30); S.t += 1 / 30; derive(1 / 30);
    }
    // 预热完冻住**进度**：战况定在这一刻，而火力、弹幕、粒子照跑 —— 截图要的
    // 是"打到这个比分时画面是活的什么样"，不是一张静止的死图
    if (Q.get('liveFreeze') === '1') freeze = true;
    // liveStop 把整个世界停在预热结束那一帧：要看清"处决落地的瞬间"只能这样，
    // 它只存在零点几秒，主循环再跑一下就过去了
    if (Q.get('liveStop') === '1') { stopAll = true; live = false; }
  }
  if (Q.has('p')) { S.p = clamp(+Q.get('p'), 0, 100); S.auto = false; }
  if (Q.get('auto') === '0') S.auto = false;
  if (Q.has('line')) S.line = clamp(+Q.get('line') | 0, 0, 3);
  // ?zoom=1 用画布原生尺寸铺开，截图时才看得清脸和手的实际画法
  if (Q.get('zoom') === '1') document.getElementById('stage').style.width = W + 'px';
  document.getElementById('pv').value = S.p;
  document.getElementById('auto').checked = S.auto;
  for (let i = 0; i < 90; i++) derive(1 / 60);   // 预热，让指数趋近收敛到位

  /* 震动只作用在"正在发生冲突的东西"上 —— 对抗线、角色、粒子、刻度尺。
     房间和地毯不动：机位是固定的，整幅画面一起震就得把背景放大做 overscan
     才不露边，而背景一放大，地毯四角那组标定坐标就全偏了。HUD 也不震，它
     不在场景里。 */
  /* 拆成三段是因为它本来就是三张画布、三种代价：底版每帧重画一张 960x1334
     的照片，角色每帧重画一张 900 高的 PNG，特效层则随着场上有多少东西线性
     涨。"哪一层在拖后腿"只有分开计时才答得出，而合在一个函数里就只能猜。 */
  function renderBg() {
    const bias = (S.p - 50) / 50;
    const ox = Particles.off.x, oy = Particles.off.y;
    bctx.clearRect(0, 0, W, H);
    bctx.drawImage(bg, 0, 0, W, H);
    drawGround(bctx, bias);
    bctx.save(); bctx.translate(ox, oy);
    if (S.line === 1) drawLine(bctx);
    else if (S.line === 2) drawFrontGround(bctx, bias);
    bctx.restore();
  }

  function renderActors() {
    const ox = Particles.off.x, oy = Particles.off.y;
    cctx.clearRect(0, 0, W, H);
    cctx.save(); cctx.translate(ox, oy);
    seq.draw(cctx, S.p, FX.actorX, FX.punch, FX.tint, FX.tintA);
    cctx.restore();
  }

  function renderFx() {
    const bias = (S.p - 50) / 50;
    const ox = Particles.off.x, oy = Particles.off.y;
    fctx.clearRect(0, 0, W, H);
    fctx.save(); fctx.translate(ox, oy);
    /* line=1 要在角色之上再叠一遍，否则光柱全程被两具身体挡死；line=2 的
       带子在地上，挡住了也没关系 —— 顶端那个指针替它做读数。 */
    if (S.line === 1) drawLine(fctx, 0.42);
    else if (S.line >= 2) drawFrontMark(fctx, bias);
    drawRuler(fctx);
    // 气泡在弹幕之下：它贴在后面那堵墙上，弹幕是前景，飞过时该压过去
    Bubble.draw(fctx);
    /* 弹幕在角色之上、粒子之下：它飞向两个人中间，画在角色底下的话命中前
       最后那段就被身体挡掉了；而粒子是命中的爆炸，该盖在弹幕上面。 */
    Ammo.draw(fctx);
    Particles.draw(fctx);
    fctx.restore();
    drawHUD(fctx, S.p);
    Particles.drawFlash(fctx, W, H);
  }

  function render() { renderBg(); renderActors(); renderFx(); }

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

  /* ?ammostrip=N 看一发弹幕从出场到命中的全过程。飞行节奏是这个功能的全部，
     而它在静止截图里根本不存在 —— 得把整条轨迹按时间摊开才看得出速度合不
     合适、预警够不够长、命中点准不准。
     ?ammogift=<礼物名> 选哪一件，?ammoy=<高度> 固定发射高度（随机高度会让
     每次截出来的图不一样，没法对比）。 */
  if (Q.has('ammostrip')) {
    const n = clamp(+Q.get('ammostrip') | 0, 2, 12);
    const MS = clamp(+(Q.get('ammoms') || 110), 16, 400) / 1000;
    const sc = 0.5, gname = Q.get('ammogift') || 'pillow';
    const g = GIFT[gname] || GIFT.pillow;
    S.auto = false; S.t = 3.0;
    for (let k = 0; k < 150; k++) derive(1 / 60);

    const out = document.createElement('canvas');
    out.width = n * W * sc; out.height = H * sc;
    const o = out.getContext('2d');
    o.fillStyle = '#0c0e12'; o.fillRect(0, 0, out.width, out.height);

    Ammo.launch(g, clamp(+(Q.get('ammoy') || 560), 300, 960));
    let el = 0;
    for (let i = 0; i < n; i++) {
      const step = i === 0 ? 1 / 60 : MS;
      for (let k = 0; k < Math.max(1, Math.round(step * 60)); k++) {
        const d = Particles.tick(1 / 60);
        Particles.update(1 / 60);
        Ammo.update(d);
        Bubble.update(d, FX.struggle);
        derive(d);
      }
      el += step;
      render();
      const dx = i * W * sc;
      for (const c of [cvBg, cvCh, cvFx]) o.drawImage(c, dx, 0, W * sc, H * sc);
      o.fillStyle = 'rgba(0,0,0,.66)'; o.fillRect(dx, 0, 168, 26);
      o.fillStyle = '#fff'; o.font = '600 15px system-ui';
      o.fillText(`+${Math.round(el * 1000)}ms 弹${Ammo.count()} 粒${Particles.count()}`, dx + 8, 18);
      if (i === 0) {
        o.fillStyle = 'rgba(0,0,0,.66)'; o.fillRect(dx, 26, 168, 24);
        o.fillStyle = '#ffd36b';
        o.fillText(`${gname} · ${g.style} · p=${S.p.toFixed(0)}`, dx + 8, 42);
      }
    }
    const stage = document.getElementById('stage');
    stage.style.width = out.width + 'px';
    stage.style.aspectRatio = `${out.width}/${out.height}`;
    stage.innerHTML = '';
    out.style.cssText = 'position:absolute;inset:0;width:100%;height:100%';
    stage.appendChild(out);
    return;
  }

  /* ?bubblestrip=N 专看气泡：每格推一条，四种轮流。
     混在别的胶片里碰运气是拍不到的 —— 气泡自己按一两秒的节奏随机冒，想看
     的那一种（比如语音条、撤回提示）多半正好没出现；而要检查的恰恰是它们
     堆起来之后挡不挡脸、四种底色在这张墙上分不分得开。
     ?bubblems=M 每格间隔，默认 420ms。 */
  if (Q.has('bubblestrip')) {
    const n = clamp(+Q.get('bubblestrip') | 0, 2, 12);
    const MS = clamp(+(Q.get('bubblems') || 420), 16, 2000) / 1000;
    const sc = 0.5, types = ['text', 'hot', 'sys', 'voice'];
    S.auto = false; S.t = 3.0;
    for (let k = 0; k < 150; k++) derive(1 / 60);
    Bubble.clear();

    const out = document.createElement('canvas');
    out.width = n * W * sc; out.height = H * sc;
    const o = out.getContext('2d');
    o.fillStyle = '#0c0e12'; o.fillRect(0, 0, out.width, out.height);
    for (let i = 0; i < n; i++) {
      Bubble.push(types[i % 4]);
      for (let k = 0; k < Math.max(1, Math.round(MS * 60)); k++) {
        Bubble.update(1 / 60, FX.struggle);
        derive(1 / 60);
      }
      render();
      const dx = i * W * sc;
      for (const c of [cvBg, cvCh, cvFx]) o.drawImage(c, dx, 0, W * sc, H * sc);
      o.fillStyle = 'rgba(0,0,0,.70)'; o.fillRect(dx, 0, 150, 26);
      o.fillStyle = '#ffd36b'; o.font = '600 15px system-ui';
      o.fillText(`+${Math.round(i * MS * 1000)}ms 气泡${Bubble.count()}`, dx + 8, 18);
    }
    const stage = document.getElementById('stage');
    stage.style.width = out.width + 'px';
    stage.style.aspectRatio = `${out.width}/${out.height}`;
    stage.innerHTML = '';
    out.style.cssText = 'position:absolute;inset:0;width:100%;height:100%';
    stage.appendChild(out);
    return;
  }

  /* ?linestrip=1 把对抗线的三种画法并排摆着比。三档各截一张再拼是不行的
     —— 每张 headless 截图要等一百秒，而且三张之间 S.t 不同，线的抖动相位
     对不上，看到的差别有一半是相位差不是画法差。 */
  if (Q.has('linestrip')) {
    const sc = 0.5, names = ['line=0 全去掉', 'line=1 原发光柱', 'line=2 地面战线+指针', 'line=3 只要指针'];
    S.auto = false; S.t = 3.0;
    for (let k = 0; k < 150; k++) derive(1 / 60);
    const out = document.createElement('canvas');
    out.width = names.length * W * sc; out.height = H * sc;
    const o = out.getContext('2d');
    o.fillStyle = '#0c0e12'; o.fillRect(0, 0, out.width, out.height);
    for (let i = 0; i < names.length; i++) {
      S.line = i;
      render();
      const dx = i * W * sc;
      for (const c of [cvBg, cvCh, cvFx]) o.drawImage(c, dx, 0, W * sc, H * sc);
      o.fillStyle = 'rgba(0,0,0,.72)'; o.fillRect(dx, 0, 160, 26);
      o.fillStyle = '#ffd36b'; o.font = '600 15px system-ui';
      o.fillText(`${names[i]}  p=${S.p.toFixed(0)}`, dx + 8, 18);
    }
    const stage = document.getElementById('stage');
    stage.style.width = out.width + 'px';
    stage.style.aspectRatio = `${out.width}/${out.height}`;
    stage.innerHTML = '';
    out.style.cssText = 'position:absolute;inset:0;width:100%;height:100%';
    stage.appendChild(out);
    return;
  }

  /* ?fxstrip=N 出一条特效胶片：打一下，然后每 MS 毫秒抓一格。
     特效是瞬时的，单张截图什么也验证不了 —— 只有把同一次命中的前后若干
     毫秒并排摆着，才看得出顿帧有没有生效、冲击波是不是沿线传出去了、
     粒子的衰减节奏对不对。 */
  if (Q.has('fxstrip')) {
    const n = clamp(+Q.get('fxstrip') | 0, 2, 12);
    const MS = clamp(+(Q.get('fxms') || 60), 16, 400) / 1000;
    const sc = 0.5, power = clamp(+(Q.get('fxpower') || 3), 1, 3);
    const rcp = RECIPE[Q.get('fxrecipe')] || RECIPE.thud;
    S.auto = false; S.t = 3.0;
    for (let k = 0; k < 150; k++) derive(1 / 60);

    const out = document.createElement('canvas');
    out.width = n * W * sc; out.height = H * sc;
    const o = out.getContext('2d');
    o.fillStyle = '#0c0e12'; o.fillRect(0, 0, out.width, out.height);

    impact(-1, FX.phoneY, power, rcp);
    let el = 0;
    for (let i = 0; i < n; i++) {
      /* 第一格只推进一帧。命中那一瞬间粒子都还没展开，拿一整格去拍它等于
         白扔八分之一的胶片宽度。 */
      const step = i === 0 ? 1 / 60 : MS;
      for (let k = 0; k < Math.max(1, Math.round(step * 60)); k++) {
        const d = Particles.tick(1 / 60);   // 与主循环同构：粒子走真实时间，逻辑走 d
        Particles.update(1 / 60);
        derive(d);
      }
      el += step;
      render();
      const dx = i * W * sc;
      for (const c of [cvBg, cvCh, cvFx]) o.drawImage(c, dx, 0, W * sc, H * sc);
      o.fillStyle = 'rgba(0,0,0,.66)'; o.fillRect(dx, 0, 132, 26);
      o.fillStyle = '#fff'; o.font = '600 15px system-ui';
      o.fillText(`+${Math.round(el * 1000)}ms 粒子${Particles.count()}`, dx + 8, 18);
      if (i === 0) {
        o.fillStyle = 'rgba(0,0,0,.66)'; o.fillRect(dx, 26, 132, 24);
        o.fillStyle = '#ffd36b';
        o.fillText(`${Q.get('fxrecipe') || 'thud'} p${power} line${S.line}`, dx + 8, 42);
      }
    }
    const stage = document.getElementById('stage');
    stage.style.width = out.width + 'px';
    stage.style.aspectRatio = `${out.width}/${out.height}`;
    stage.innerHTML = '';
    out.style.cssText = 'position:absolute;inset:0;width:100%;height:100%';
    stage.appendChild(out);
    return;
  }

  /* ?bench=1 —— 连点压测。"卡不卡"是主观的，而它在静止截图里完全不存在：
     要么拿到每一帧的真实耗时，要么就是在猜。这个模式关掉自动演示、用固定
     dt 推进（弹幕节奏每次跑都一样，两次测量才可比），按 benchrate 的间隔
     轮流发六件礼物，逐层计时，最后把读数画在画布上 —— 读数得跟着截图一起
     出来，否则在无头环境里取不回来。

     ?benchoff=ammo|part|both 用差值法定位热点：把某一层的绘制换成空函数再
     跑一遍，两次的差就是那一层的代价。比在渲染代码里埋计时点干净 ——
     生产代码一行都不用改。 */
  if (Q.has('bench')) {
    const N = clamp(+(Q.get('benchframes') || 420), 60, 2000);
    const RATE = clamp(+(Q.get('benchrate') || 110), 40, 2000) / 1000;
    const bOff = Q.get('benchoff') || '';
    if (bOff === 'ammo' || bOff === 'both') Ammo.draw = () => {};
    if (bOff === 'part' || bOff === 'both') Particles.draw = () => {};

    /* 压测必须走真实链路：礼物注入火力 → 火力自动派弹幕 → 一部分在中线对撞。
       直接 Ammo.launch 测出来的是旧模型的密度，而新模型场上还多着常规火力那
       一路，负载完全是另一回事。 */
    /* 礼物组合要贴近真实分布：小额是绝大多数，神秘空投八件里才有一件。
       四种等概率轮流的话，每 1.2 秒就来一次处决，那不是压测是造假 —— 独占窗口
       会一直开着，常规火力全被挡掉，量出来的负载比真实情况低一个数量级。 */
    const gnames = ['wand', 'wand', 'mirror', 'wand', 'boom', 'wand', 'mirror', 'drop'];
    const T = { logic: 0, bg: 0, ch: 0, fx: 0, all: 0 };
    const each = new Float64Array(N);
    const M = { logic: 0, bg: 0, ch: 0, fx: 0, all: 0, at: 0 };
    let bi = 0, bacc = 0, gi = 0, maxP = 0, maxA = 0, sumP = 0, sumA = 0, drops = 0, froze = 0;
    S.t = 3.0;
    document.getElementById('auto').checked = false;
    startMatch();
    for (let k = 0; k < 150; k++) derive(1 / 60);

    const benchStep = () => {
      const dt = 1 / 60;
      const t0 = performance.now();
      bacc += dt;
      if (bacc >= RATE) { bacc -= RATE; giveGift(gi % 2 ? +1 : -1, gnames[gi++ % gnames.length]); }
      battle(dt);
      const d = Particles.tick(dt);
      /* 冻结帧占比 —— 帧率正常但画面不动，观众读到的同样是"卡"。每次命中
         都冻 35~110ms，而连点时命中是密集的，顿帧会一段一段接上。这个数
         在耗时曲线上完全看不出来：那些帧的渲染一切正常，只是世界没动。 */
      if (d === 0) froze++;
      Particles.update(dt);
      Ammo.update(d);
      Bubble.update(d, FX.struggle);
      S.t += d;
      derive(d);
      const t1 = performance.now(); renderBg();
      const t2 = performance.now(); renderActors();
      const t3 = performance.now(); renderFx();
      const t4 = performance.now();
      T.logic += t1 - t0; T.bg += t2 - t1; T.ch += t3 - t2; T.fx += t4 - t3; T.all += t4 - t0;
      each[bi] = t4 - t0;
      /* 均值说明不了卡顿 —— 均值 3ms 的同时可以有一帧 39ms，而观众看到的
         就是那一帧。所以每层都要留峰值，否则只知道"有尖峰"，不知道尖峰在
         哪一层；再记下它出现在第几帧，用来分辨"开头一次性的预热"和"运行中
         周期性发作"。 */
      if (t1 - t0 > M.logic) M.logic = t1 - t0;
      if (t2 - t1 > M.bg) M.bg = t2 - t1;
      if (t3 - t2 > M.ch) M.ch = t3 - t2;
      if (t4 - t3 > M.fx) M.fx = t4 - t3;
      if (t4 - t0 > M.all) { M.all = t4 - t0; M.at = bi; }
      if (t4 - t0 > 16.7) drops++;
      const np = Particles.count(), na = Ammo.count();
      sumP += np; sumA += na;
      if (np > maxP) maxP = np;
      if (na > maxA) maxA = na;
      if (++bi < N) { requestAnimationFrame(benchStep); return; }

      const sorted = Array.from(each).sort((a, b) => a - b);
      const p95 = sorted[Math.floor(N * 0.95)], worst = sorted[N - 1];
      const rows = [
        `连点压测  ${N} 帧 · 每 ${Math.round(RATE * 1000)}ms 一件礼物` + (bOff ? `  [关掉 ${bOff}]` : ''),
        ``,
        `           均值      峰值`,
        `每帧总计   ${(T.all / N).toFixed(2)}      ${M.all.toFixed(1)} ms  (第 ${M.at} 帧)`,
        `  逻辑     ${(T.logic / N).toFixed(2)}      ${M.logic.toFixed(1)}`,
        `  底版层   ${(T.bg / N).toFixed(2)}      ${M.bg.toFixed(1)}`,
        `  角色层   ${(T.ch / N).toFixed(2)}      ${M.ch.toFixed(1)}`,
        `  特效层   ${(T.fx / N).toFixed(2)}      ${M.fx.toFixed(1)}`,
        ``,
        `p95 ${p95.toFixed(2)}ms   掉帧(>16.7ms) ${drops}/${N} = ${(drops * 100 / N).toFixed(1)}%`,
        `世界被顿帧冻住 ${froze}/${N} 帧 = ${(froze * 100 / N).toFixed(1)}%`,
        `粒子 均 ${(sumP / N).toFixed(0)} 峰 ${maxP}     弹幕 均 ${(sumA / N).toFixed(1)} 峰 ${maxA}`,
        `16.7ms = 60fps    33.3ms = 30fps`,
      ];
      fctx.fillStyle = 'rgba(6,8,12,.93)';
      fctx.fillRect(24, 150, W - 48, 46 + rows.length * 42);
      fctx.strokeStyle = '#ffd36b'; fctx.lineWidth = 3;
      fctx.strokeRect(24, 150, W - 48, 46 + rows.length * 42);
      fctx.textAlign = 'left'; fctx.textBaseline = 'middle';
      rows.forEach((r, k) => {
        fctx.font = (k === 0 ? 'bold 30px ' : '600 30px ') + 'ui-monospace,Menlo,monospace';
        fctx.fillStyle = k === 0 ? '#ffd36b' : (k === 2 ? '#8fe3ff' : '#e8ecf2');
        fctx.fillText(r, 48, 196 + k * 42);
      });
      document.getElementById('stat').textContent = rows.join(' | ');
      // 无头环境里没法按时间猜跑完没跑完，用标题当完成信号，外面轮询它
      document.title = 'BENCHDONE';
    };
    requestAnimationFrame(benchStep);
    return;
  }

  let last = performance.now(), fps = 0, fr = 0, acc = 0, dir = 1;
  function frame(now) {
    if (stopAll) { render(); requestAnimationFrame(frame); return; }
    const raw = Math.min(.05, (now - last) / 1000); last = now;
    fr++; acc += raw;
    if (acc >= .5) { fps = fr / acc; fr = 0; acc = 0; }
    /* 顿帧冻住的是游戏逻辑（角色姿态、对抗线、进度），特效照真实时间走。
       两者用的是不同的时钟：定格是为了让观众多看两眼"他被打中了"，而火花
       和闪光正是这一下的可视化 —— 把它们一起冻住，爆炸就会迟到一百毫秒，
       读起来是"闪了一下、卡住、然后才炸开"。 */
    const dt = Particles.tick(raw);
    Particles.update(raw);
    /* 弹幕走 dt，跟游戏逻辑一起冻。这和粒子走真实时间并不矛盾：爆炸是"刚刚
       这一下"的可视化，冻住它就迟到了；而正在飞的弹幕是**下一下**的前奏，
       顿帧的意思就是全世界停下来看这一击，此刻别的东西还在飞就散掉了。 */
    Ammo.update(dt);
    // 气泡跟着逻辑时钟：顿帧时它也该停，那半秒全世界都在看刚才那一击
    Bubble.update(dt, FX.struggle);
    S.t += dt;

    /* 对局跑数值，调试台跑演示。两者互斥：idle 下 battle 不动，进度由滑块或
       自动演示给；play 下滑块失效，进度只能由火力差推出来。混在一起的话
       "礼物到底推了多少"永远说不清。 */
    if (live) { S.fA += liveA * raw; S.fB += liveB * raw; }
    const keep = S.p; battle(dt); if (freeze) S.p = keep;
    if (S.auto && S.phase === 'idle') {
      S.p += dir * dt * 9 * (0.35 + Math.abs(Math.sin(S.t * .27)) * 1.5);
      if (S.p > 97) { S.p = 97; dir = -1; } if (S.p < 3) { S.p = 3; dir = 1; }
    }
    if (S.phase !== 'idle') document.getElementById('pv').value = S.p;
    else if (S.auto) document.getElementById('pv').value = S.p;
    derive(dt);
    render();
    const mm = Math.max(0, S.clock);
    document.getElementById('stat').textContent =
      (S.phase === 'idle'
        ? `p=${S.p.toFixed(1)}  对抗线x=${phonePos()[0].toFixed(0)}  f${String(seq.shown).padStart(3, '0')}  `
        : `p=${S.p.toFixed(1)}  火力 ${S.fA.toFixed(0)}:${S.fB.toFixed(0)}  净差${(S.fA - S.fB).toFixed(0)}  `
          + `${(mm / 60 | 0)}:${String(mm % 60 | 0).padStart(2, '0')}`
          + (S.phase === 'sudden' ? `  绝杀${S.sudden.toFixed(0)}` : '')
          + (S.stand > 0 ? `  反击${S.stand.toFixed(0)}` : '')
          + (S.phase === 'over' ? `  ${S.winner > 0 ? '查岗党胜' : S.winner < 0 ? '灭迹党胜' : '平局'}` : '') + '  ')
      + `粒子${Particles.count()}  ${fps.toFixed(0)}fps`;
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);

  const pv = document.getElementById('pv');
  pv.oninput = () => { if (S.phase !== 'idle') return; S.p = +pv.value; S.auto = false; document.getElementById('auto').checked = false; };
  document.getElementById('auto').onchange = e => S.auto = e.target.checked;
  const nudge = d => { if (S.phase !== 'idle') return;
                       S.auto = false; document.getElementById('auto').checked = false;
                       S.p = clamp(S.p + d, 0, 100); pv.value = S.p; };
  /* 按钮既推进度也打一下：进度是玩法，命中是演出，观众看到的是同一件事。
     side 取推力的反方向 —— 查岗党加分等于灭迹党挨了一下。 */
  const power = () => +document.getElementById('pw').value;
  const hit = (d, rcp) => {
    nudge(d);
    impact(d > 0 ? -1 : +1, FX.phoneY, power(), rcp);
  };
  document.getElementById('hitL').onclick = () => hit(+7, RECIPE.thud);
  document.getElementById('hitR').onclick = () => hit(-7, RECIPE.thud);
  // 三个配方都由查岗党打出去，落在灭迹党身上；力度由上面那个下拉决定
  /* 礼物按钮只负责发射，进度和特效都等弹幕真的撞上对抗线才结算 —— 玩法和
     演出走的是同一个事件，观众看到的因果关系才对得上。 */
  /* 礼物按钮注入火力，而不是直接发弹幕。发不发、发几颗由火力的消耗量决定
     （见 emitFire）—— 于是"刷得越多扔得越密"是从数值里长出来的，不是写死的。 */
  for (const b of document.querySelectorAll('[data-shop]')) {
    b.onclick = () => {
      if (S.phase === 'idle') startMatch();
      giveGift(+b.dataset.side, b.dataset.shop);
    };
  }
  document.getElementById('start').onclick = () => {
    if (S.phase === 'idle') { startMatch(); document.getElementById('start').textContent = '回到调试台'; }
    else { S.phase = 'idle'; S.fA = S.fB = 0; Ammo.clear(); document.getElementById('start').textContent = '开始对局'; }
  };
  const lv = document.getElementById('lv');
  lv.value = S.line;
  lv.onchange = () => S.line = +lv.value;
})();
