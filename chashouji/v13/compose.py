"""把导出好的角色层帧贴到固定背景上，出本地预览。

几何（尺度、重心、地面线）一律由 export_unity.py 决定，这里只负责合成：它
导出的帧已经是与画布同尺寸、人物摆好位置的透明图，在这儿再算一遍缩放和锚点
就会多出一套会各自漂移的真源 —— 之前就是这样，预览里的人物比引擎里大一圈。
"""
import os

from PIL import Image, ImageDraw, ImageFont

SRC = '../unity/BattleGame/Assets/StreamingAssets/art/frames'
BG = '../web/assets/bg.jpg'
PS = list(range(0, 101, 5))
FONT = '/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc'


def frame(p):
    ch = Image.open(f'{SRC}/f{p:03d}.png')
    bg = Image.open(BG).convert('RGBA').resize(ch.size, Image.LANCZOS)
    bg.alpha_composite(ch)
    return bg.convert('RGB')


def main():
    os.makedirs('frames', exist_ok=True)
    for p in PS:
        frame(p).save(f'frames/p{p:03d}.jpg', quality=90)

    f = ImageFont.truetype(FONT, 26)
    for tag, sel, cols in (('五档', [5, 25, 50, 75, 95], 5),
                           ('21档总览', PS, 11)):
        tw = 430 if cols == 5 else 250
        ims = [Image.open(f'frames/p{p:03d}.jpg') for p in sel]
        th = round(ims[0].height * tw / ims[0].width)
        rows = (len(sel) + cols - 1) // cols
        cv = Image.new('RGB', (tw * cols, (th + 32) * rows), (24, 26, 30))
        d = ImageDraw.Draw(cv)
        for i, (p, im) in enumerate(zip(sel, ims)):
            x, y = (i % cols) * tw, (i // cols) * (th + 32)
            cv.paste(im.resize((tw, th), Image.LANCZOS), (x, y + 32))
            d.text((x + 6, y + 3), f'{p}%', font=f, fill=(235, 235, 235))
        cv.save(f'v13_合成_{tag}.png')
    print('预览已出：frames/ 与 v13_合成_*.png')


if __name__ == '__main__':
    main()
