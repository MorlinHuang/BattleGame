# -*- coding: utf-8 -*-
"""落地验证：只用抠出来的素材 + 干净底图 + 纯代码绘制的手机屏/HUD/特效，拼出完整界面。
用法: python3 compose_proof.py <推线进度 0~1> <输出路径>
进度 0.5 = 势均力敌; >0.5 = 查岗党(绿)占优; <0.5 = 灭迹党(红)占优。"""
import sys, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageEnhance

W, H = 960, 1334
A = "/workspace/art/chashouji/assets/"
FB = "/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc"
GREEN, RED = (64, 240, 120), (255, 72, 72)
f = lambda s: __import__("PIL.ImageFont", fromlist=["ImageFont"]).truetype(FB, s)

def fit(im, h):                                  # 等比缩到指定高
    return im.resize((max(1, round(im.width * h / im.height)), h), Image.LANCZOS)

def cover(im, w, h):                             # 等比裁切填满
    s = max(w / im.width, h / im.height)
    im = im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)
    return im.crop(((im.width - w) // 2, (im.height - h) // 2,
                    (im.width - w) // 2 + w, (im.height - h) // 2 + h))

def rr(d, box, r, fill=None, outline=None, width=1):
    d.rounded_rectangle(box, r, fill=fill, outline=outline, width=width)

def glow(layer, radius=18, gain=1.0):            # 程序化辉光
    g = layer.filter(ImageFilter.GaussianBlur(radius))
    if gain != 1.0:
        a = np.array(g); a[..., 3] = np.clip(a[..., 3] * gain, 0, 255); g = Image.fromarray(a)
    return g

def build(prog, out):
    prog = max(0.05, min(0.95, prog))
    # ---------- 1 背景层：干净底图 + 程序化左绿右红环境光 ----------
    bg = cover(Image.open(A + "bg_livingroom.png").convert("RGB"), W, H)
    bg = ImageEnhance.Color(ImageEnhance.Brightness(bg).enhance(0.42)).enhance(0.55)
    canvas = bg.convert("RGBA")
    tint = Image.new("RGBA", (W, H), (0, 0, 0, 0)); td = ImageDraw.Draw(tint)
    for x in range(0, W, 4):                     # 左绿右红横向渐变，强度由推线进度驱动
        t = x / W
        if t < 0.5: c, k = GREEN, (0.5 - t) * 2 * prog
        else:       c, k = RED,   (t - 0.5) * 2 * (1 - prog)
        td.rectangle([x, 0, x + 4, H], fill=c + (int(150 * k ** 1.3),))
    canvas = Image.alpha_composite(canvas, tint)

    # ---------- 2 特效层：程序化粒子（左绿数据流 / 右红灰烬） ----------
    fx = Image.new("RGBA", (W, H), (0, 0, 0, 0)); fd = ImageDraw.Draw(fx)
    random.seed(7)
    for _ in range(int(150 * prog)):             # 绿：向上的数据流竖条
        x, y = random.randint(0, 330), random.randint(180, H - 120)
        h = random.randint(14, 46)
        fd.rectangle([x, y, x + 3, y + h], fill=GREEN + (random.randint(60, 190),))
    for _ in range(int(150 * (1 - prog))):       # 红：飘散的灰烬碎块
        x, y = random.randint(W - 330, W), random.randint(180, H - 120)
        s = random.randint(3, 8)
        fd.rectangle([x, y, x + s, y + s], fill=RED + (random.randint(60, 190),))
    canvas = Image.alpha_composite(canvas, glow(fx, 5, 1.3))
    canvas = Image.alpha_composite(canvas, fx)

    # ---------- 3 角色层：抠像素材直接贴 ----------
    girl = fit(Image.open(A + "char_girl.png").convert("RGBA"), 545)
    boy  = fit(Image.open(A + "char_boy.png").convert("RGBA"), 495)
    canvas.alpha_composite(girl, (6, H - girl.height - 108))
    canvas.alpha_composite(boy,  (W - boy.width - 6, H - boy.height - 108))

    # ---------- 4 手机UI层：全部程序绘制，零美术 ----------
    PW, PH = 330, 660; PX, PY = (W - PW) // 2, 300
    ph = Image.new("RGBA", (PW, PH), (0, 0, 0, 0)); pd = ImageDraw.Draw(ph)
    rr(pd, [0, 0, PW, PH], 34, fill=(22, 24, 30, 255), outline=(70, 76, 90, 255), width=3)
    SX, SY, SW, SH = 12, 14, PW - 24, PH - 28
    rr(pd, [SX, SY, SX + SW, SY + SH], 24, fill=(10, 12, 16, 255))
    split = SY + SH - int(SH * prog)             # 战线：绿从下往上推
    rows, y, i = [], SY + SH - 12, 0
    while y > SY + 14:                           # 从下往上排聊天气泡
        bh = 30; bw = random.Random(i * 3).randint(120, 205); left = i % 2 == 0
        rows.append((SX + 12 if left else SX + SW - 12 - bw, y - bh, bw, bh)); y -= bh + 9; i += 1
    for (bx, by, bw, bh) in rows:
        if by > split:                           # 已恢复：绿色实心 + 马赛克内容
            rr(pd, [bx, by, bx + bw, by + bh], 9, fill=(28, 120, 66, 255), outline=GREEN + (255,), width=2)
            for k in range(int(bw // 26)):
                cx = bx + 10 + k * 24
                pd.rounded_rectangle([cx, by + 10, cx + 16, by + bh - 10], 3, fill=(176, 186, 180, 255))
        else:                                    # 已删除：焦黑碎裂 + 灰烬
            rr(pd, [bx, by, bx + bw, by + bh], 9, fill=(38, 26, 26, 190), outline=(96, 44, 44, 200), width=2)
            for k in range(3):
                cx = bx + random.Random(bx + k).randint(6, max(7, bw - 14))
                pd.rectangle([cx, by + 6, cx + 4, by + 10], fill=RED + (150,))
    pd.line([SX + 6, split, SX + SW - 6, split], fill=(255, 255, 255, 235), width=4)
    pd.line([SX + 6, split, SX + SW - 6, split], fill=GREEN + (120,), width=10)
    for t in range(11):                          # 屏侧刻度
        ty = SY + SH - int(SH * t / 10)
        pd.line([SX + SW - 7, ty, SX + SW - 1, ty], fill=(150, 160, 175, 190), width=2)
    pl = Image.new("RGBA", (W, H), (0, 0, 0, 0)); pl.alpha_composite(ph, (PX, PY))
    canvas = Image.alpha_composite(canvas, glow(pl, 26, 0.8))
    canvas.alpha_composite(ph, (PX, PY))

    # ---------- 5 HUD 层：纯 UI 控件 ----------
    hud = Image.new("RGBA", (W, H), (0, 0, 0, 0)); hd = ImageDraw.Draw(hud)
    hd.rectangle([0, 0, W, 150], fill=(8, 10, 14, 205))
    def txt(xy, s, size, fill, anchor="la", sw=4):
        hd.text(xy, s, font=f(size), fill=fill, anchor=anchor,
                stroke_width=sw, stroke_fill=(0, 0, 0, 235))
    txt((34, 22), "查岗党", 50, GREEN); txt((34, 84), "%d 证据值" % int(26000 * prog + 1800), 26, (235, 240, 245), sw=3)
    txt((W - 34, 22), "灭迹党", 50, RED, "ra"); txt((W - 34, 84), "%d 清白值" % int(26000 * (1 - prog) + 1800), 26, (235, 240, 245), "ra", 3)
    txt((W // 2, 26), "02:47", 48, (255, 255, 255), "ma")
    BX, BY, BW, BH = 34, 124, W - 68, 16         # 角力条
    rr(hd, [BX, BY, BX + BW, BY + BH], 8, fill=(18, 20, 26, 255))
    m = BX + int(BW * prog)
    rr(hd, [BX, BY, m, BY + BH], 8, fill=GREEN + (255,))
    rr(hd, [m, BY, BX + BW, BY + BH], 8, fill=RED + (255,))
    hd.ellipse([m - 13, BY - 9, m + 13, BY + BH + 9], fill=(255, 255, 255, 255))
    canvas = Image.alpha_composite(canvas, hud)

    # ---------- 6 礼物栏：抠出来的图标直接排 ----------
    gb = Image.new("RGBA", (W, H), (0, 0, 0, 0)); gd = ImageDraw.Draw(gb)
    gd.rectangle([0, H - 128, W, H], fill=(8, 10, 14, 215))
    names = [("gift_cloud", "云端备份"), ("gift_lens", "深度搜索"), ("gift_print", "生物识别"),
             ("gift_trash", "清理痕迹"), ("gift_plane", "远程追踪"), ("gift_phone", "设备扫描")]
    cw = W // 6
    for i, (fn, label) in enumerate(names):
        ic = fit(Image.open(A + fn + ".png").convert("RGBA"), 62)
        gb.alpha_composite(ic, (i * cw + (cw - ic.width) // 2, H - 118))
        gd.text((i * cw + cw // 2, H - 40), label, font=f(21), fill=(226, 232, 240),
                anchor="ma", stroke_width=3, stroke_fill=(0, 0, 0, 220))
    canvas = Image.alpha_composite(canvas, gb)

    canvas.convert("RGB").save(out)
    print("%s  %s  推线=%.2f" % (out, canvas.size, prog))

if __name__ == "__main__":
    build(float(sys.argv[1]), sys.argv[2])
