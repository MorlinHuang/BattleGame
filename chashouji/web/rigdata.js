/* 骨骼标定数据。坐标一律归一化 uv（0~1 相对图片宽高），换素材尺寸不用改数。
 *
 * 关节位置是在带 1% 刻度的放大图上逐个读出来的。几个反复踩的坑：
 *   · 睡衣是七分袖，袖口不是手腕 —— 袖口到腕之间还有一截小臂；
 *   · 张开的手，指尖比手掌中心远得多。手骨的势力范围要罩住整只手，罩不住
 *     的话指尖抢不过覆盖全身的 root 骨，会被钉在原地撕成细针；可也不能靠
 *     一味加粗去罩 —— 两只手在图上只隔着约 80px，加粗到够得着自己指尖的
 *     半径，同时就把另一只手也吃了进去，那只手会被拽着一起走、扯成一团肉。
 *     正解是把骨末端放到张开的五指中间，再用 capT 把势力范围沿骨轴送出去：
 *     沿轴延伸只够得到自己的指尖，横向仍然够不着隔壁那只手。
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
 * cap:[capH,capT] 势力范围沿骨轴两端的收口倍数，见 rig.js 的 boneField。肘、肩
 *   这些要弯折的接缝上，两根骨的势力范围必须大面积叠着：叠得窄，权重就在
 *   一格之内从五五开跳到独占，胳膊一折那一格就被剪开。
 * strength 不是"越大越黏"，它是跟 root 抢地盘的筹码：root 那根骨的实心核罩住
 *   整个人、权重恒为 1，乘上它的 strength 就是 0.50 —— 别的骨在某个顶点上只有
 *   把 field³ × strength 抬过 0.50 才算真的"拥有"这块皮。所以一根骨的实际地盘
 *   远小于它的 radius；袖口、指尖这些离骨轴最远的肉要归自己管，要么把核放大
 *   到那里，要么把 strength 抬上去。
 * head 的 strength 要压过上臂：耳侧那缕垂发离上臂骨轴只有 32px，落在它的
 *   实心核里，纯按距离就会被手臂拽走盖住半张脸。谁该管头发是常识，不是几何。
 * from: 蒙皮范围沿骨轴的起点。只有 girl 的上臂需要 —— 她侧着身，肩关节离脸
 *   只有 55px，让上臂骨从 head 就开始吃肉，实心核会把脸罩进去。boy 的脸离肩
 *   有 122px，给他加 from 反而把 T恤袖口撕成不动和跟着转的两半。
 * rigid: 不继承父骨的轴向拉伸。手是刚体，前臂抻长时手指不该跟着变长。
 * armBSwing: 不抓手机那条手臂绕肩拧的固定角度（屏幕上顺时针为正）。立绘里两只
 *   手伸向同一处，不拧开就叠成一团分不出指头的肉。
 * grip: 机身被 armA 的手心握住的那个点，相对机身中心、在机身自己的坐标系里。
 *   取各自靠得近的那条侧缘再往外让出半只手掌、并一上一下错开：两只手掌各有
 *   约 70px 宽，挤在同一处谁也读不出来；取机身中心则整只手连五指一起横过
 *   屏幕；贴着侧缘取，整只手又会缩到机身背后看不见 —— 手心在机身外、指尖
 *   搭进机身里，才既攥得住又不挡屏幕。
 */
const RIG = {
  girl: {
    img: 'assets/girl.png',
    anchorX: 0.40,
    grip: { A: [-74, -30] },      // 握住机身左缘偏上：手心落在机身外侧一点，五指才是搭进机身里的那截
    armBSwing: 0.26,              // 另一只手压到机身下缘（屏幕上顺时针为正）
    bones: [
      { name: 'root',      parent: null,        head: [0.330, 0.520], tail: [0.330, 0.990], core: 0.500, radius: 0.780,  strength: 0.50, cap: [3, 3] },
      { name: 'torso',     parent: 'root',      head: [0.330, 0.500], tail: [0.420, 0.165], core: 0.150, radius: 0.250,  strength: 1.05, cap: [0.8, 0.25] },
      { name: 'head',      parent: 'torso',     head: [0.420, 0.155], tail: [0.420, 0.020], core: 0.130, radius: 0.190,  strength: 4.50, cap: [0.3, 0.4] },
      { name: 'armA_up',   parent: 'torso',     head: [0.455, 0.162], tail: [0.655, 0.186], core: 0.085, radius: 0.125, strength: 2.4,  cap: [0.70, 0.55], from: 0.28 },
      { name: 'armA_fore', parent: 'armA_up',   head: [0.655, 0.186], tail: [0.800, 0.190], core: 0.075, radius: 0.115, strength: 2.4,  cap: [0.55, 0.25] },
      { name: 'armA_hand', parent: 'armA_fore', head: [0.800, 0.190], tail: [0.955, 0.140], core: 0.075, radius: 0.105, strength: 3.5,  cap: [0.10, 1.00], rigid: true },
      { name: 'armB',      parent: 'torso',     head: [0.385, 0.330], tail: [0.895, 0.265], core: 0.075, radius: 0.115, strength: 3.0,  cap: [0.10, 0.12], rigid: true },
    ],
  },
  boy: {
    img: 'assets/boy.png',
    anchorX: 0.55,
    grip: { A: [76, 30] },        // 握住机身右缘偏下 —— 与她上下错开，两只手不打架
    armBSwing: 0.30,              // 他的手臂朝左，顺时针是把另一只手抬到机身上缘
    bones: [
      { name: 'root',      parent: null,        head: [0.560, 0.480], tail: [0.560, 0.990], core: 0.500, radius: 0.780,  strength: 0.50, cap: [3, 3] },
      { name: 'torso',     parent: 'root',      head: [0.550, 0.450], tail: [0.500, 0.160], core: 0.150, radius: 0.250,  strength: 1.05, cap: [0.8, 0.25] },
      { name: 'head',      parent: 'torso',     head: [0.500, 0.150], tail: [0.500, 0.020], core: 0.130, radius: 0.190,  strength: 4.50, cap: [0.3, 0.4] },
      { name: 'armA_up',   parent: 'torso',     head: [0.420, 0.212], tail: [0.265, 0.228], core: 0.060, radius: 0.105, strength: 2.4,  cap: [0.25, 0.55] },
      { name: 'armA_fore', parent: 'armA_up',   head: [0.265, 0.228], tail: [0.140, 0.227], core: 0.050, radius: 0.095, strength: 2.4,  cap: [0.55, 0.25] },
      { name: 'armA_hand', parent: 'armA_fore', head: [0.140, 0.227], tail: [0.015, 0.200], core: 0.075, radius: 0.115, strength: 3.5,  cap: [0.10, 0.35], rigid: true },
      { name: 'armB',      parent: 'torso',     head: [0.560, 0.268], tail: [0.170, 0.300], core: 0.075, radius: 0.105, strength: 3.0,  cap: [0.10, 0.12], rigid: true },
    ],
  },
};
