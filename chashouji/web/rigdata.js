/* 骨骼标定数据：坐标是归一化 uv (相对各自立绘图片的宽高)。
 * 这份数据由页面上的标定模式拖出来、再导出覆盖回本文件 —— 标定工具与
 * 运行时是同一套代码，不存在"标定出来运行时对不上"。
 *
 * radius / strength 决定权重刷法：w = (1 - dist/radius)^3 * strength。
 * root 是兜底骨（radius 很大、strength 低），头发梢、腿脚这些离所有
 * 骨都远的顶点归它 —— 即完全不变形。
 */
const RIG = {
  girl: {
    img: 'assets/girl.png',
    anchorX: 0.40,          // 图中重心（双脚中点）的 u，用于摆位
    grip: { A: [-26, -38], B: [-26, 40] },   // 两只手在手机上的落点（相对机身中心）
    bones: [
      { name: 'root',      parent: null,       head: [0.33, 0.52],  tail: [0.33, 0.99],  radius: 0.78,  strength: 0.50 },
      { name: 'torso',     parent: 'root',     head: [0.33, 0.50],  tail: [0.42, 0.165], radius: 0.24,  strength: 1.05 },
      { name: 'head',      parent: 'torso',    head: [0.42, 0.155], tail: [0.42, 0.020], radius: 0.17,  strength: 1.35 },
      { name: 'armA_up',   parent: 'torso',    head: [0.50, 0.165], tail: [0.70, 0.145], radius: 0.078, strength: 2.4 },
      { name: 'armA_fore', parent: 'armA_up',  head: [0.70, 0.145], tail: [0.885, 0.133], radius: 0.088, strength: 2.4 },
      { name: 'armB_up',   parent: 'torso',    head: [0.47, 0.195], tail: [0.66, 0.238], radius: 0.078, strength: 2.4 },
      { name: 'armB_fore', parent: 'armB_up',  head: [0.66, 0.238], tail: [0.845, 0.277], radius: 0.088, strength: 2.4 },
    ],
  },
  boy: {
    img: 'assets/boy.png',
    anchorX: 0.55,
    grip: { A: [26, -38], B: [50, 20] },
    bones: [
      { name: 'root',      parent: null,       head: [0.56, 0.48],  tail: [0.56, 0.99],  radius: 0.78,  strength: 0.50 },
      { name: 'torso',     parent: 'root',     head: [0.55, 0.45],  tail: [0.50, 0.160], radius: 0.24,  strength: 1.05 },
      { name: 'head',      parent: 'torso',    head: [0.50, 0.150], tail: [0.50, 0.020], radius: 0.17,  strength: 1.35 },
      { name: 'armA_up',   parent: 'torso',    head: [0.56, 0.210], tail: [0.36, 0.230], radius: 0.078, strength: 2.4 },
      { name: 'armA_fore', parent: 'armA_up',  head: [0.36, 0.230], tail: [0.130, 0.215], radius: 0.088, strength: 2.4 },
      { name: 'armB_up',   parent: 'torso',    head: [0.60, 0.245], tail: [0.43, 0.300], radius: 0.078, strength: 2.4 },
      { name: 'armB_fore', parent: 'armB_up',  head: [0.43, 0.300], tail: [0.250, 0.305], radius: 0.088, strength: 2.4 },
    ],
  },
};
