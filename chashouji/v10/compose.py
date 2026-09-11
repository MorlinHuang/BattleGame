"""v10 合成验证：干净底版 + 独立立绘。参数集中在顶部，便于反复调。"""
from PIL import Image, ImageDraw

W, H = 960, 1334
BG_SRC = "raw/bg_A.png"           # 1024x1536
GIRL, BOY = "parts/girl.png", "parts/boy.png"

GIRL_SCALE, BOY_SCALE = 0.56, 0.60
FOOT_Y = 1150                      # 双方脚底落点
GIRL_HAND_X, BOY_HAND_X = 530, 440 # 手尖抵达的 x（girl 右边缘 / boy 左边缘）
PHONE_W, PHONE_H = 62, 116         # 手机尺寸（后续由程序绘制，这里只验证尺度）
PHONE_CX, PHONE_CY = 484, 505

def load_bg():
    bg = Image.open(BG_SRC).convert("RGBA")
    bg = bg.crop((0, 0, bg.width, bg.height - 113))     # 裁底，保留顶部
    return bg.resize((W, H), Image.LANCZOS)

def place(canvas, path, scale, right_edge=None, left_edge=None):
    im = Image.open(path).convert("RGBA")
    w, h = int(im.width * scale), int(im.height * scale)
    im = im.resize((w, h), Image.LANCZOS)
    x = right_edge - w if right_edge is not None else left_edge
    y = FOOT_Y - h
    canvas.alpha_composite(im, (x, y))
    return x, y, w, h

canvas = load_bg()
gx, gy, gw, gh = place(canvas, GIRL, GIRL_SCALE, right_edge=GIRL_HAND_X)
bx, by, bw, bh = place(canvas, BOY,  BOY_SCALE,  left_edge=BOY_HAND_X)

d = ImageDraw.Draw(canvas)
d.rounded_rectangle(
    [PHONE_CX - PHONE_W // 2, PHONE_CY - PHONE_H // 2,
     PHONE_CX + PHONE_W // 2, PHONE_CY + PHONE_H // 2],
    radius=9, fill=(28, 30, 36, 255), outline=(250, 250, 250, 255), width=3)

canvas.convert("RGB").save("v10_合成验证.png", quality=95)
print(f"girl  x={gx} y={gy} {gw}x{gh}   头顶 y={gy}")
print(f"boy   x={bx} y={by} {bw}x{bh}   头顶 y={by}")
print("saved v10_合成验证.png")
