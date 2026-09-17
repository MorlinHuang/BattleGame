import bpy, bmesh, math, sys
from mathutils import Vector, Matrix

# ---------- 参数（引擎里那束花的色值，来自 ammo.js 的 ITEM.bouquet） ----------
ARGV   = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
FRAMES = int(ARGV[0]) if ARGV else 4          # 转盘帧数
RES    = int(ARGV[1]) if len(ARGV) > 1 else 320
SAMP   = int(ARGV[2]) if len(ARGV) > 2 else 64
OUT    = ARGV[3] if len(ARGV) > 3 else '/home/op/bl_rose/out/rose_'

def srgb(h):
    """材质颜色必须是 linear，直接填 sRGB 的十六进制会整体偏亮一档。"""
    h = h.lstrip('#')
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i+2], 16) / 255
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return (*out, 1.0)

C_ROSE_A = srgb('e63a62')   # 深玫红
C_ROSE_B = srgb('ff5f86')   # 亮粉
C_LEAF   = srgb('5f8f57')   # 墨绿
C_WRAP   = srgb('e8c79e')   # 暖杏包装纸（比引擎里的 f7e3cf 深一档，见下方注释）
C_INK    = srgb('3a2c26')   # 描边：角色线稿那个暖黑

# ---------- 清场 ----------
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

def toon_mat(name, color):
    """赛璐璐的硬边二分光影：Toon BSDF，size=0.5 让明暗交界落在中间，
       smooth=0 让它是一刀切而不是渐变 —— 渐变就是三维塑料感的来源。"""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    toon = nt.nodes.new('ShaderNodeBsdfToon')
    toon.component = 'DIFFUSE'
    toon.inputs['Color'].default_value = color
    toon.inputs['Size'].default_value = 0.5
    toon.inputs['Smooth'].default_value = 0.0
    nt.links.new(toon.outputs[0], out.inputs['Surface'])
    return m

MAT = {k: toon_mat(k, c) for k, c in
       [('rose_a', C_ROSE_A), ('rose_b', C_ROSE_B), ('leaf', C_LEAF), ('wrap', C_WRAP)]}

def add_mesh(name, verts, faces, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.validate()
    ob = bpy.data.objects.new(name, me)
    ob.data.materials.append(MAT[mat])
    scene.collection.objects.link(ob)
    for p in ob.data.polygons:
        p.use_smooth = False          # 平面着色：赛璐璐不要圆滑过渡
    return ob

# ---------- 一片花瓣：杯状曲面 ----------
def petal_mesh(w, h, curl, nu=7, nv=5):
    """u 横向 v 纵向。三件事一起做才读得出"玫瑰花瓣"：
       下窄、中间最宽、顶部再收一点（sin 曲线），顶边压成圆弧，横向卷曲成杯。
       第一版顶边是直的、宽度线性递增，卷起来之后每片都是一根尖刺，
       整团读成蓟花或者仙人掌。"""
    verts, faces = [], []
    for j in range(nv):
        v = j / (nv - 1)
        wide = w * (0.26 + 0.74 * math.sin(v * 2.55))
        for i in range(nu):
            u = i / (nu - 1) * 2 - 1
            verts.append((u * wide,
                          -curl * u * u * (0.35 + v),
                          v * h - u * u * h * 0.16))
    for j in range(nv - 1):
        for i in range(nu - 1):
            a = j * nu + i
            faces.append((a, a + 1, a + nu + 1, a + nu))
    return verts, faces

def rose_head(R, mat, seed=0):
    """一朵玫瑰：三层花瓣，由内到外越来越大、越来越外翻。
       花瓣数刻意压到 8 片 —— 观众端这朵花只有十几像素，片数多了 Freestyle
       的描边会糊成一团黑，反而不如少而清楚。"""
    obs = []
    # (片数, 排布半径, 外翻角, 尺寸)。半径必须远小于花瓣宽度，花瓣才会互相
    # 叠住形成一朵；第一版半径 0.30/0.52/0.78 配 0.62 的宽度，花瓣彼此够不着，
    # 渲出来是一堆散开的红碎片。
    layers = [(3, 0.09, 0.14, 0.58), (5, 0.30, 0.62, 1.00)]
    idx = 0
    for cnt, rad, tilt, scl in layers:
        for k in range(cnt):
            ang = (idx * 2.39996) + seed        # 黄金角排布，避免对齐成十字
            idx += 1
            vs, fs = petal_mesh(R * 1.05 * scl, R * 1.25 * scl, R * 0.50 * scl)
            ob = add_mesh('petal', vs, fs, mat)
            m = (Matrix.Rotation(ang, 4, 'Z')
                 @ Matrix.Translation((0, -R * rad, -R * 0.18 * scl))
                 @ Matrix.Rotation(tilt, 4, 'X'))
            ob.matrix_world = m
            obs.append(ob)
    # 花心：一个小球堵住中间，否则从上方看是个空洞
    bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=6,
                                         radius=R * 0.30, location=(0, 0, R * 0.16))
    core = bpy.context.object
    core.data.materials.append(MAT[mat])
    for p in core.data.polygons:
        p.use_smooth = False
    obs.append(core)
    return obs

# ---------- 包装纸：倒锥 ----------
bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.40, radius2=0.06,
                                depth=0.66, location=(0, 0, -0.40))
wrap = bpy.context.object
wrap.data.materials.append(MAT['wrap'])
for p in wrap.data.polygons:
    p.use_smooth = False

# ---------- 叶子：压扁拉长的球 ----------
for a, r, z in [(0.5, 0.95, 0.10), (2.6, 0.95, -0.05), (4.3, 0.92, 0.14), (3.5, 0.80, 0.26)]:
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.30,
                                         location=(math.cos(a) * r, math.sin(a) * r, z))
    lf = bpy.context.object
    lf.scale = (1.5, 0.55, 0.16)
    lf.rotation_euler = (0, 0.5, a)
    lf.data.materials.append(MAT['leaf'])
    for p in lf.data.polygons:
        p.use_smooth = False

# ---------- 七朵花头：中间一朵高，外圈六朵低，形成半球形捧花 ----------
# (方位角, 径距, 高度, 半径, 材质, 外倾角)
HEADS = [(0.0, 0.0, 0.50, 0.46, 'rose_a', 0.0)]
for k in range(6):
    phi = k * math.pi / 3
    HEADS.append((phi, 0.70, 0.12, 0.40, 'rose_b' if k % 3 == 1 else 'rose_a', 0.66))
for i, (phi, rad, z, R, mat, lean) in enumerate(HEADS):
    obs = rose_head(R, mat, seed=i * 1.1)
    # 绕"垂直于径向的水平轴"倾斜，花顶就朝外倒；直接绕 X 转的话只有正东那朵是对的
    axis = Vector((-math.sin(phi), math.cos(phi), 0)) if rad > 0 else Vector((1, 0, 0))
    M = (Matrix.Translation((math.cos(phi) * rad, math.sin(phi) * rad, z))
         @ Matrix.Rotation(lean, 4, axis))
    for ob in obs:
        ob.matrix_world = M @ ob.matrix_world

# 全部并成一个物体，转盘时整体转
bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active = wrap
bpy.ops.object.join()
BOUQUET = bpy.context.object
BOUQUET.name = 'bouquet'
# join 之后原点留在包装纸那儿，直接转 rotation_euler 是绕原点转，花头会甩着
# 画大圈飞出画外。必须把原点挪到几何中心，转盘才是"原地翻滚"。
bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')
BOUQUET.location = (0, 0, 0)

# ---------- 光：一盏硬光定明暗交界，环境补一点免得暗面死黑 ----------
light = bpy.data.lights.new('key', 'SUN')
light.angle = 0.02          # 接近平行光 = 硬边阴影，柔光会把 toon 的硬边糊掉
light.energy = 2.6
lo = bpy.data.objects.new('key', light)
lo.rotation_euler = (math.radians(52), 0, math.radians(20))
scene.collection.objects.link(lo)
world = bpy.data.worlds.new('w')
world.use_nodes = True
bg = world.node_tree.nodes['Background']
# Blender 默认的环境色是近黑的深灰，只调 strength 没用 —— Toon 的暗面本来就是
# 纯黑，没有环境光补进去就死成一片。给一点偏暖的亮灰，暗面才会落成主色的暗调，
# 这正是赛璐璐该有的样子。
bg.inputs[0].default_value = (0.86, 0.88, 0.92, 1.0)
bg.inputs[1].default_value = 1.25
scene.world = world

# ---------- 相机：必须正交 ----------
cam = bpy.data.cameras.new('cam')
cam.type = 'ORTHO'          # 透视会让逐帧缩放不一致，转盘就会"呼吸"
cam.ortho_scale = 3.3
co = bpy.data.objects.new('cam', cam)
co.location = (0, -6, 0)
co.rotation_euler = (math.radians(90), 0, 0)
scene.collection.objects.link(co)
scene.camera = co

# ---------- 渲染设置 ----------
r = scene.render
r.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = SAMP
scene.cycles.use_denoising = False      # apt 版没编 OpenImageDenoise，开了直接报错退出
r.resolution_x = r.resolution_y = RES
r.film_transparent = True               # 直接出带 alpha 的 PNG，不用品红抠像
r.image_settings.file_format = 'PNG'
r.image_settings.color_mode = 'RGBA'

# ---------- Freestyle 描边 ----------
r.use_freestyle = True
r.line_thickness_mode = 'ABSOLUTE'
r.line_thickness = 2.2
vl = scene.view_layers[0]
vl.use_freestyle = True
fs = vl.freestyle_settings
# 描边分两档。只用一档的话：粗了内部花瓣叠成一团黑，细了外轮廓撑不住。
# 外轮廓要粗（它决定这东西在底图上认不认得出），花瓣之间的分隔线要细。
def mk_lineset(name, thick, silhouette, border):
    ls = fs.linesets.new(name)
    if ls.linestyle is None:
        ls.linestyle = bpy.data.linestyles.new(name + 'Ink')
    ls.select_silhouette = silhouette
    ls.select_border = border
    ls.select_crease = False
    ls.select_edge_mark = False
    ls.linestyle.color = C_INK[:3]
    ls.linestyle.thickness = thick
    return ls

while fs.linesets:
    fs.linesets.remove(fs.linesets[0])
mk_lineset('Outline', 2.6, True, False)
mk_lineset('Inner', 1.1, False, True)
fs.crease_angle = math.radians(105)

# ---------- 转盘 ----------
for i in range(FRAMES):
    BOUQUET.rotation_euler = (i * 2 * math.pi / FRAMES, 0.75, 0.30)
    r.filepath = f'{OUT}{i:03d}'
    bpy.ops.render.render(write_still=True)
    print(f'[rose] {i+1}/{FRAMES}', flush=True)
print('ROSEDONE')
