/* 骨骼标定数据。坐标一律归一化 uv（0~1 相对图片宽高），换素材尺寸不用改数。
 *
 * 关节位置是在带 1% 刻度的放大图上逐个读出来的。几个反复踩的坑：
 *   · 睡衣是七分袖，袖口不是手腕 —— 袖口到腕之间还有一截小臂；
 *   · 张开的手，指尖比手掌中心远得多，手骨的 radius 要罩住整只手，
 *     罩不住的话指尖抢不过覆盖全身的 root 骨，会被钉在原地撕成细针。
 *
 * 只有靠中线那条手臂(armA)分三段做 IK，因为它要够到手机。另一条手臂
 * 不参与任何动作，就只给一根刚体骨整条跟着躯干走 —— 给它分段骨没有
 * 任何好处，只会在每个关节处多出一道可能撕裂的接缝。
 *
 * core / radius：core 是"实心核"半径，按肢体自身的粗细量（袖子下缘离骨轴
 *   54px，core 就给 65px），核内一律满权重；core 到 radius 是过渡带，要落在
 *   背景上。过渡带压到肉上，同一条袖子里相邻两排顶点就会一个跟着手臂走、
 *   一个被 root 钉住，中间那条边被拉成拖影。骨轴也必须压在肢体中线上：偏上
 *   一点，下缘就掉出核外。
 * cap:[capH,capT] 势力范围沿骨轴两端的收口倍数，见 rig.js 的 boneField。
 * head 的 strength 要压过上臂：耳侧那缕垂发离上臂骨轴只有 32px，落在它的
 *   实心核里，纯按距离就会被手臂拽走盖住半张脸。谁该管头发是常识，不是几何。
 * from: 蒙皮范围沿骨轴的起点。只有 girl 的上臂需要 —— 她侧着身，肩关节离脸
 *   只有 55px，让上臂骨从 head 就开始吃肉，实心核会把脸罩进去。boy 的脸离肩
 *   有 122px，给他加 from 反而把 T恤袖口撕成不动和跟着转的两半。
 * rigid: 不继承父骨的轴向拉伸。手是刚体，前臂抻长时手指不该跟着变长。
 * grip: armA 的腕该落到机身上的哪个点，相对手机中心。两人一上一下各扣
 *   一端 —— 手掌各有约 70px 宽，挤在同一处谁也读不出来。
 */
const RIG = {
  girl: {
    img: 'assets/girl.png',
    anchorX: 0.40,
    grip: { A: [-8, -41] },       // 腕落在机身上部，手掌压住屏幕右上
    bones: [
      { name: 'root',      parent: null,        head: [0.330, 0.520], tail: [0.330, 0.990], core: 0.500, radius: 0.780,  strength: 0.50, cap: [3, 3] },
      { name: 'torso',     parent: 'root',      head: [0.330, 0.500], tail: [0.420, 0.165], core: 0.150, radius: 0.250,  strength: 1.05, cap: [0.8, 0.25] },
      { name: 'head',      parent: 'torso',     head: [0.420, 0.155], tail: [0.420, 0.020], core: 0.130, radius: 0.190,  strength: 4.50, cap: [0.3, 0.4] },
      { name: 'armA_up',   parent: 'torso',     head: [0.455, 0.162], tail: [0.655, 0.186], core: 0.085, radius: 0.125, strength: 2.4,  cap: [0.45, 0.25], from: 0.28 },
      { name: 'armA_fore', parent: 'armA_up',   head: [0.655, 0.186], tail: [0.800, 0.190], core: 0.075, radius: 0.115, strength: 2.4,  cap: [0.25, 0.25] },
      { name: 'armA_hand', parent: 'armA_fore', head: [0.800, 0.190], tail: [0.945, 0.163], core: 0.105, radius: 0.155, strength: 3.5,  cap: [0.10, 0.35], rigid: true },
      { name: 'armB',      parent: 'torso',     head: [0.385, 0.330], tail: [0.895, 0.265], core: 0.060, radius: 0.115, strength: 1.6,  cap: [0.10, 0.12], rigid: true },
    ],
  },
  boy: {
    img: 'assets/boy.png',
    anchorX: 0.55,
    grip: { A: [0, 42] },         // 腕落在机身下部，手掌压住屏幕左下 —— 与她上下错开
    bones: [
      { name: 'root',      parent: null,        head: [0.560, 0.480], tail: [0.560, 0.990], core: 0.500, radius: 0.780,  strength: 0.50, cap: [3, 3] },
      { name: 'torso',     parent: 'root',      head: [0.550, 0.450], tail: [0.500, 0.160], core: 0.150, radius: 0.250,  strength: 1.05, cap: [0.8, 0.25] },
      { name: 'head',      parent: 'torso',     head: [0.500, 0.150], tail: [0.500, 0.020], core: 0.130, radius: 0.190,  strength: 4.50, cap: [0.3, 0.4] },
      { name: 'armA_up',   parent: 'torso',     head: [0.420, 0.212], tail: [0.265, 0.228], core: 0.060, radius: 0.105, strength: 2.4,  cap: [0.25, 0.25] },
      { name: 'armA_fore', parent: 'armA_up',   head: [0.265, 0.228], tail: [0.140, 0.227], core: 0.050, radius: 0.095, strength: 2.4,  cap: [0.25, 0.25] },
      { name: 'armA_hand', parent: 'armA_fore', head: [0.140, 0.227], tail: [0.015, 0.200], core: 0.075, radius: 0.115, strength: 3.5,  cap: [0.10, 0.35], rigid: true },
      { name: 'armB',      parent: 'torso',     head: [0.560, 0.268], tail: [0.170, 0.300], core: 0.055, radius: 0.105, strength: 1.6,  cap: [0.10, 0.12], rigid: true },
    ],
  },
};
