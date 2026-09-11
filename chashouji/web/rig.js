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

function distToSeg(px, py, ax, ay, bx, by) {
  const vx = bx - ax, vy = by - ay, L = vx * vx + vy * vy;
  let t = L > 0 ? ((px - ax) * vx + (py - ay) * vy) / L : 0;
  t = t < 0 ? 0 : t > 1 ? 1 : t;
  const dx = px - (ax + vx * t), dy = py - (ay + vy * t);
  return Math.hypot(dx, dy);
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
        R: b.radius * imgW,
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
      b.poseWorld = p ? M.mul(p.poseWorld, local) : local;
      b.poseAng = (p ? p.poseAng : 0) + b.restLocalAng + b.delta;
      b.skin = M.mul(b.poseWorld, b.restWorldInv);
    }
  }

  byName(n) { return this.bones.find(b => b.name === n); }

  /* 两段 IK：让 fore 骨的末端落到 (tx,ty)。在 up 的父骨已解算后调用。
     够不到时沿臂轴等比拉伸（上限 maxStretch），mesh 变形的便宜要占。 */
  ik(upName, foreName, tx, ty, bend, maxStretch = 1.15, minReach = 0.58) {
    const up = this.byName(upName), fore = this.byName(foreName);
    const par = up.parent >= 0 ? this.bones[up.parent] : null;
    if (par) { // 肩点随父骨走
      const local = M.mul(up.restLocal, M.trs(0, 0, 0, 1, 1));
      const w = M.mul(par.poseWorld, local);
      up.sx = w.e; up.sy = w.f; up.parAng = par.poseAng;
    } else { up.sx = up.hx; up.sy = up.hy; up.parAng = 0; }

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

  // 格子内按 5x5 采样，只要有一个不透明像素就保留 —— 保留边界格，
  // 剔除大片空白（空白格若跨在两条手臂之间，变形会把边缘像素拖出残影）
  const cellOn = new Uint8Array(cols * rows);
  for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
    let on = 0;
    for (let sy = 0; sy < 5 && !on; sy++) for (let sx = 0; sx < 5; sx++) {
      const x = Math.min(img.width - 1, Math.round((i + sx / 4) * cw));
      const y = Math.min(img.height - 1, Math.round((j + sy / 4) * ch));
      if (px[(y * img.width + x) * 4 + 3] > 8) { on = 1; break; }
    }
    cellOn[j * cols + i] = on;
  }

  const map = new Int32Array((cols + 1) * (rows + 1)).fill(-1);
  const pos = [], idx = [];
  const vid = (i, j) => {
    const k = j * (cols + 1) + i;
    if (map[k] < 0) { map[k] = pos.length / 2; pos.push(i * cw, j * ch); }
    return map[k];
  };
  for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
    if (!cellOn[j * cols + i]) continue;
    const a = vid(i, j), b = vid(i + 1, j), d = vid(i, j + 1), e = vid(i + 1, j + 1);
    idx.push(a, b, d, b, e, d);
  }

  // 刷权重：w = (1 - dist/R)^3 * strength，取前 4 名归一化；
  // 全落空的顶点（发梢、脚）归 root 骨，即完全不变形
  const n = pos.length / 2;
  const bone = new Float32Array(n * 4), wt = new Float32Array(n * 4);
  const bs = sk.bones, hist = new Float64Array(bs.length);
  for (let v = 0; v < n; v++) {
    const x = pos[v * 2], y = pos[v * 2 + 1];
    for (let k = 0; k < bs.length; k++) {
      const b = bs[k];
      const t = 1 - distToSeg(x, y, b.hx, b.hy, b.tx, b.ty) / b.R;
      hist[k] = t > 0 ? t * t * t * b.strength : 0;
    }
    const order = Array.from(hist.keys()).sort((p, q) => hist[q] - hist[p]).slice(0, 4);
    let sum = 0;
    for (const k of order) sum += hist[k];
    if (sum <= 0) { bone[v * 4] = 0; wt[v * 4] = 1; continue; }
    order.forEach((k, s) => { bone[v * 4 + s] = k; wt[v * 4 + s] = hist[k] / sum; });
  }
  return { pos: new Float32Array(pos), idx: new Uint32Array(idx), bone, wt, tris: idx.length / 3, verts: n };
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
