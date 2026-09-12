/* 网格变形 + 半骨骼运行时。
 *
 * 设计要点：
 * - 骨骼坐标一律用归一化 uv (0~1 相对图片宽高) 定义，换素材尺寸不用改数。
 * - 权重在运行时按"顶点到骨轴线段的距离"自动刷，不需要外部权重文件，
 *   于是"标定工具"就是本页面的调参面板 —— 标定数据与运行时永远一致。
 * - 手腕位置由两段 IK 反解，目标点是手机握点，所以"手抓不住手机"
 *   在构造上不可能发生。
 */

// ---------- 2D 仿射：{a,b,c,d,e,f}，x' = a*x + c*y + e ----------
const M = {
  id: () => ({ a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 }),
  mul: (m, n) => ({            // 先 n 后 m
    a: m.a * n.a + m.c * n.b, b: m.b * n.a + m.d * n.b,
    c: m.a * n.c + m.c * n.d, d: m.b * n.c + m.d * n.d,
    e: m.a * n.e + m.c * n.f + m.e, f: m.b * n.e + m.d * n.f + m.f,
  }),
  inv: (m) => {
    const det = m.a * m.d - m.b * m.c, id = 1 / det;
    return {
      a: m.d * id, b: -m.b * id, c: -m.c * id, d: m.a * id,
      e: (m.c * m.f - m.d * m.e) * id, f: (m.b * m.e - m.a * m.f) * id,
    };
  },
  trs: (x, y, rot, sx = 1, sy = 1) => {
    const co = Math.cos(rot), si = Math.sin(rot);
    return { a: co * sx, b: si * sx, c: -si * sy, d: co * sy, e: x, f: y };
  },
  apply: (m, x, y) => [m.a * x + m.c * y + m.e, m.b * x + m.d * y + m.f],
  // 转 mat3 列主序，喂给 WebGL
  toGL: (m, out, o) => {
    out[o] = m.a; out[o + 1] = m.b; out[o + 2] = 0;
    out[o + 3] = m.c; out[o + 4] = m.d; out[o + 5] = 0;
    out[o + 6] = m.e; out[o + 7] = m.f; out[o + 8] = 1;
  },
};

/* 顶点对某根骨的归属度 0~1。
   骨的势力范围是一个"实心核 + 过渡带"的胶囊：核半径 core 对应肢体本身的
   粗细，核内一律满权重；从 core 到 radius 才线性衰减到 0。这一层是必须的 ——
   若从骨轴就开始衰减，肢体边缘天然落在陡降区，同一条袖子里相邻两排顶点
   一个跟着手臂走、一个被 root 钉住，边就被拉成拖影；而一味加大半径去救它，
   手臂骨又会伸手够到头发和脸上去。过渡带该落在背景上，不该落在肉上。
   沿轴两端还要收口：越过蒙皮起点 from 之前 capH 倍骨长、或 tail 端 capT 倍
   骨长，就归零。from 默认 0，上臂要单独往后挪：旋转中心在肩关节，可肩膀
   那截肉是属于躯干的 —— 肩关节离脸只有 55px，让上臂骨从 head 就开始吃肉，
   它的实心核会把脸一起罩进去，手臂一转半张脸跟着走。 */
function boneField(px, py, b) {
  const vx = b.tx - b.hx, vy = b.ty - b.hy, L = vx * vx + vy * vy;
  const t = L > 0 ? ((px - b.hx) * vx + (py - b.hy) * vy) / L : 0;
  const tc = t < 0 ? 0 : t > 1 ? 1 : t;
  const d = Math.hypot(px - (b.hx + vx * tc), py - (b.hy + vy * tc));
  /* 沿轴收口是一个独立的衰减因子，不能拿它去缩整个胶囊：按比例缩半径的话，
     实心核的边界会横着扫过肢体，同一排相邻两个顶点，一个还在核里权重满格、
     一个已经掉出核外几乎归零 —— 肘和肩这种要弯折的接缝，就在那一格上被
     剪开。乘成因子之后，权重沿骨轴一格一格匀着让给下一根骨。 */
  const k = 1 - Math.min(1, t < b.from ? (b.from - t) / b.capH : t > 1 ? (t - 1) / b.capT : 0);
  if (k <= 0 || d >= b.R) return 0;
  return k * (d <= b.R0 ? 1 : (b.R - d) / (b.R - b.R0));
}

// ---------- 骨架 ----------
class Skeleton {
  /* bones: [{name, parent, head:[u,v], tail:[u,v], radius, strength}] */
  constructor(bones, imgW, imgH) {
    this.imgW = imgW; this.imgH = imgH;
    this.bones = bones.map((b, i) => {
      const hx = b.head[0] * imgW, hy = b.head[1] * imgH;
      const tx = b.tail[0] * imgW, ty = b.tail[1] * imgH;
      return {
        ...b, idx: i,
        parent: b.parent == null ? -1 : bones.findIndex(x => x.name === b.parent),
        hx, hy, tx, ty,
        len: Math.hypot(tx - hx, ty - hy),
        restAng: Math.atan2(ty - hy, tx - hx),
        R: b.radius * imgW, R0: (b.core != null ? b.core : b.radius * 0.62) * imgW,
        capH: b.cap ? b.cap[0] : 0.25, capT: b.cap ? b.cap[1] : 0.25,
        from: b.from || 0,
        delta: 0, scale: 1,
      };
    });
    for (const b of this.bones) {
      b.restWorld = M.trs(b.hx, b.hy, b.restAng);
      b.restWorldInv = M.inv(b.restWorld);
      const p = b.parent >= 0 ? this.bones[b.parent] : null;
      b.restLocal = p ? M.mul(p.restWorldInv, b.restWorld) : b.restWorld;
      b.restLocalAng = b.restAng - (p ? p.restAng : 0);
    }
  }

  reset() { for (const b of this.bones) { b.delta = 0; b.scale = 1; } }

  /* 解算所有骨的 pose，须按父先子后的顺序（bones 数组本身即拓扑序） */
  solve() {
    for (const b of this.bones) {
      const p = b.parent >= 0 ? this.bones[b.parent] : null;
      const local = M.mul(b.restLocal, M.trs(0, 0, b.delta, b.scale, 1));
      const w = p ? M.mul(p.poseWorld, local) : local;
      b.poseAng = (p ? p.poseAng : 0) + b.restLocalAng + b.delta;
      /* 刚体骨只继承父骨的落点与朝向，不继承父骨为够到目标而做的轴向拉伸。
         手是刚体：前臂抻长时手指没有理由跟着变长。 */
      b.poseWorld = b.rigid ? M.trs(w.e, w.f, b.poseAng) : w;
      b.skin = M.mul(b.poseWorld, b.restWorldInv);
    }
  }

  byName(n) { return this.bones.find(b => b.name === n); }

  /* 两段 IK：让 fore 骨的末端落到 (tx,ty)。在 up 的父骨已解算后调用。
     够不到时沿臂轴等比拉伸（上限 maxStretch），mesh 变形的便宜要占。 */
  /* 骨起点在当前姿势下的落点。手该沿哪个方向伸出去，得先知道肩落在哪。 */
  headOf(name) {
    const b = this.byName(name), p = b.parent >= 0 ? this.bones[b.parent] : null;
    if (!p) return [b.hx, b.hy];
    const w = M.mul(p.poseWorld, b.restLocal);
    return [w.e, w.f];
  }

  ik(upName, foreName, tx, ty, bend, maxStretch = 1.15, minReach = 0.58) {
    const up = this.byName(upName), fore = this.byName(foreName);
    const par = up.parent >= 0 ? this.bones[up.parent] : null;
    [up.sx, up.sy] = this.headOf(upName);      // 肩点随父骨走
    up.parAng = par ? par.poseAng : 0;

    let L1 = up.len, L2 = fore.len;
    const dx = tx - up.sx, dy = ty - up.sy;
    let d = Math.hypot(dx, dy);
    let st = 1;
    if (d > L1 + L2) { st = Math.min(maxStretch, d / (L1 + L2)); L1 *= st; L2 *= st; }
    d = Math.min(d, L1 + L2 - 1e-3);
    d = Math.max(d, (L1 + L2) * minReach);      // 折得比这更狠，网格就撕成面条了

    const base = Math.atan2(dy, dx);
    const cosA = (d * d + L1 * L1 - L2 * L2) / (2 * d * L1);
    const A = Math.acos(Math.max(-1, Math.min(1, cosA)));
    const angUp = base + bend * A;
    const ex = up.sx + L1 * Math.cos(angUp), ey = up.sy + L1 * Math.sin(angUp);
    const angFore = Math.atan2(ty - ey, tx - ex);

    up.delta = angUp - up.parAng - up.restLocalAng;
    up.scale = st;
    fore.delta = angFore - angUp - fore.restLocalAng;
    fore.scale = st;
  }
}

// ---------- 网格 + 权重 ----------
function buildMesh(img, sk, cols, rows) {
  const c = document.createElement('canvas');
  c.width = img.width; c.height = img.height;
  const g = c.getContext('2d', { willReadFrequently: true });
  g.drawImage(img, 0, 0);
  const px = g.getImageData(0, 0, img.width, img.height).data;
  const cw = img.width / cols, ch = img.height / rows;

  /* 格子内按 5x5 采样数不透明像素，占比达门槛才保留。
     "只要有一个就保留"会把指缝、两条手臂之间那些几乎全是背景的格子也留下，
     它们把本不相连的两块皮缝成一张布 —— IK 一拉开，这块布就被撕成细长的
     尖刺，看上去像手指被抻长了。门槛低于 1/3 的格子，本来也没什么可画的。 */
  const MINCOV = 0.30;
  const cellCov = new Float32Array(cols * rows);
  for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
    let hit = 0;
    for (let sy = 0; sy < 5; sy++) for (let sx = 0; sx < 5; sx++) {
      const x = Math.min(img.width - 1, Math.round((i + sx / 4) * cw));
      const y = Math.min(img.height - 1, Math.round((j + sy / 4) * ch));
      if (px[(y * img.width + x) * 4 + 3] > 8) hit++;
    }
    cellCov[j * cols + i] = hit / 25;
  }

  const map = new Int32Array((cols + 1) * (rows + 1)).fill(-1);
  const pos = [], idx = [];
  const vid = (i, j) => {
    const k = j * (cols + 1) + i;
    if (map[k] < 0) { map[k] = pos.length / 2; pos.push(i * cw, j * ch); }
    return map[k];
  };
  const triCov = [];
  for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
    const cov = cellCov[j * cols + i];
    if (cov < MINCOV) continue;
    const a = vid(i, j), b = vid(i + 1, j), d = vid(i, j + 1), e = vid(i + 1, j + 1);
    idx.push(a, b, d, b, e, d);
    triCov.push(cov, cov);
  }

  /* 刷权重：w = (1 - dist/R)^3 * strength，取前 4 名归一化。
     root 始终以一个基础权重在场，而不是"其他骨全落空时才顶上" —— 归一化
     会抹掉"总权重其实很弱"这个事实：骨的势力边缘上，一个 2e-6 的权重归一化
     后照样是 100%，而紧挨着的顶点因为差一点点落到范围外、权重为 0 归了 root。
     一个完全跟着手走、一个完全不动，中间那条边就被拉成一道尖刺。有了基础
     权重，弱势区会平滑地交还给 root，权重场处处连续。 */
  const ROOTFLOOR = 0.012;
  const n = pos.length / 2;
  const bone = new Float32Array(n * 4), wt = new Float32Array(n * 4);
  const bs = sk.bones, hist = new Float64Array(bs.length);
  const chainOf = bs.map(b => (b.name.match(/^(arm[AB])/) || [, 'body'])[1]);
  const rootIdx = bs.findIndex(b => b.parent < 0);
  for (let v = 0; v < n; v++) {
    const x = pos[v * 2], y = pos[v * 2 + 1];
    for (let k = 0; k < bs.length; k++) {
      const b = bs[k];
      const t = boneField(x, y, b);
      hist[k] = t > 0 ? t * t * t * b.strength : 0;
    }
    hist[rootIdx] = Math.max(hist[rootIdx], ROOTFLOOR);
    const order = Array.from(hist.keys()).sort((p, q) => hist[q] - hist[p]).slice(0, 4);
    /* 一个顶点不能同时归两条互不相连的手臂。两臂在图上隔着背景，共用顶点
       就等于被两边同时拉扯，会在它们之间扯出一片橡皮膜。只留权重最高的
       那条链，另一条清零 —— body 不清，手臂和身体本来就是连着的。 */
    let arm = null;
    for (const k of order) {
      const ch = chainOf[k];
      if (ch === 'body') continue;
      if (arm === null) arm = ch; else if (ch !== arm) hist[k] = 0;
    }
    let sum = 0;
    for (const k of order) sum += hist[k];
    order.forEach((k, s) => { bone[v * 4 + s] = k; wt[v * 4 + s] = hist[k] / sum; });
  }

  /* 剔除跨接三角形。两条手臂之间隔着背景，可网格是一整张连通的布，把它们
     缝在了一起 —— IK 把一条手臂拉走，这块布就被撕成一道竖条，顺带把袖口
     的像素拉成长长的拖影。判据是三角形的顶点分属两条不同的手臂链：手臂
     内部不会出现这种组合，出现了就说明这个三角形横跨了本不相连的两块皮。 */
  /* 两个顶点权重向量的 L1 距离。同一块皮上相邻顶点的权重是平滑过渡的；
     差异突然拉满只有一个意思 —— 这条边其实横跨了两块不该连在一起的皮。 */
  const wdiff = (a, b) => {
    let d = 0;
    for (let i = 0; i < 4; i++) {
      const bi = bone[a * 4 + i]; let wb = 0;
      for (let j = 0; j < 4; j++) if (bone[b * 4 + j] === bi) { wb = wt[b * 4 + j]; break; }
      d += Math.abs(wt[a * 4 + i] - wb);
    }
    for (let j = 0; j < 4; j++) {
      const bj = bone[b * 4 + j]; let shared = false;
      for (let i = 0; i < 4; i++) if (bone[a * 4 + i] === bj) { shared = true; break; }
      if (!shared) d += wt[b * 4 + j];
    }
    return d;
  };

  const keep = [];
  for (let i = 0, t = 0; i < idx.length; i += 3, t++) {
    const arms = new Set();
    for (let k = 0; k < 3; k++) {
      const v = idx[i + k];
      // 看全部四个权重而不只是主导骨：一个顶点可能主导 root，却有近半的
      // 权重挂在另一条手臂上，只看主导骨会把这种跨接的三角形漏过去
      for (let w = 0; w < 4; w++) {
        if (wt[v * 4 + w] <= 0.2) continue;
        const ch = chainOf[bone[v * 4 + w]];
        if (ch !== 'body') arms.add(ch);
      }
    }
    if (arms.size > 1) continue;
    /* 轮廓边上、一多半是背景的格子：上排顶点还在袖子里跟着手臂走，下排已经
       落到骨的势力范围外被 root 钉住，这条边会被拉成一道拖影。那种格子里本来
       也没多少可画的，切掉比撕开好看。门槛要卡得很严 —— 稍一放宽，袖口和
       T恤边缘这些权重本就该变的地方会被切出缺口，比拖影更难看。 */
    if (triCov[t] < 0.50) {
      const [a, b, c] = [idx[i], idx[i + 1], idx[i + 2]];
      if (Math.max(wdiff(a, b), wdiff(b, c), wdiff(c, a)) > 1.4) continue;
    }
    keep.push(idx[i], idx[i + 1], idx[i + 2]);
  }
  return { pos: new Float32Array(pos), idx: new Uint32Array(keep), bone, wt, tris: keep.length / 3, verts: n };
}

// ---------- WebGL 渲染 ----------
const VS = `#version 300 es
in vec2 aPos; in vec4 aBone; in vec4 aWt;
uniform mat3 uBones[10];
uniform mat3 uModel;
uniform vec2 uRes, uImg;
out vec2 vUV;
void main(){
  mat3 m = uBones[int(aBone.x)]*aWt.x + uBones[int(aBone.y)]*aWt.y
         + uBones[int(aBone.z)]*aWt.z + uBones[int(aBone.w)]*aWt.w;
  vec3 p = uModel * (m * vec3(aPos,1.0));
  vUV = aPos/uImg;
  gl_Position = vec4(p.x/uRes.x*2.0-1.0, 1.0-p.y/uRes.y*2.0, 0.0, 1.0);
}`;
const FS = `#version 300 es
precision highp float;
in vec2 vUV; out vec4 o;
uniform sampler2D uTex; uniform vec4 uTint; uniform float uWire;
void main(){
  vec4 c = texture(uTex, vUV);
  if(uWire > 0.5){ o = vec4(0.1,1.0,0.4,0.35*c.a); return; }
  c.rgb = mix(c.rgb, uTint.rgb, uTint.a);
  o = c;
}`;

function compile(gl, type, src) {
  const s = gl.createShader(type);
  gl.shaderSource(s, src); gl.compileShader(s);
  if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
  return s;
}

class Renderer {
  constructor(gl) {
    this.gl = gl;
    const p = gl.createProgram();
    gl.attachShader(p, compile(gl, gl.VERTEX_SHADER, VS));
    gl.attachShader(p, compile(gl, gl.FRAGMENT_SHADER, FS));
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
    this.p = p;
    this.u = {};
    for (const k of ['uModel', 'uRes', 'uImg', 'uTex', 'uTint', 'uWire'])
      this.u[k] = gl.getUniformLocation(p, k);
    this.uBones = gl.getUniformLocation(p, 'uBones[0]');
    this.boneBuf = new Float32Array(90);
    gl.enable(gl.BLEND);
    gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
  }

  upload(mesh, img) {
    const gl = this.gl, vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    const mk = (data, loc, size) => {
      const b = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, b);
      gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);
      gl.enableVertexAttribArray(loc);
      gl.vertexAttribPointer(loc, size, gl.FLOAT, false, 0, 0);
    };
    mk(mesh.pos, gl.getAttribLocation(this.p, 'aPos'), 2);
    mk(mesh.bone, gl.getAttribLocation(this.p, 'aBone'), 4);
    mk(mesh.wt, gl.getAttribLocation(this.p, 'aWt'), 4);
    const ib = gl.createBuffer();
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ib);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, mesh.idx, gl.STATIC_DRAW);
    gl.bindVertexArray(null);

    const tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.generateMipmap(gl.TEXTURE_2D);
    return { vao, tex, count: mesh.idx.length };
  }

  draw(gpu, sk, model, tint, wire) {
    const gl = this.gl;
    gl.useProgram(this.p);
    sk.bones.forEach((b, i) => M.toGL(b.skin, this.boneBuf, i * 9));
    gl.uniformMatrix3fv(this.uBones, false, this.boneBuf);
    const mb = new Float32Array(9); M.toGL(model, mb, 0);
    gl.uniformMatrix3fv(this.u.uModel, false, mb);
    gl.uniform2f(this.u.uRes, gl.canvas.width, gl.canvas.height);
    gl.uniform2f(this.u.uImg, sk.imgW, sk.imgH);
    // tint 的 RGB 沿用本项目 0~255 的写法，这里换算成 GLSL 的 0~1
    gl.uniform4f(this.u.uTint, tint[0] / 255, tint[1] / 255, tint[2] / 255, tint[3]);
    gl.uniform1f(this.u.uWire, wire ? 1 : 0);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, gpu.tex);
    gl.uniform1i(this.u.uTex, 0);
    gl.bindVertexArray(gpu.vao);
    gl.drawElements(wire ? gl.LINES : gl.TRIANGLES, gpu.count, gl.UNSIGNED_INT, 0);
    gl.bindVertexArray(null);
  }
}
