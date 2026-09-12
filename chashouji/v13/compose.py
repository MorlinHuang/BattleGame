"""把抠好的角色层贴到固定背景上。

归一化按 alpha bbox 的高度：各帧的最高点永远是站着那个人的头顶、最低点是
最低的脚或膝，所以 bbox 高度近似等于"站立者身高"。生图给出的人物绝对尺寸
每帧都在漂（760~1292 像素），不归一化切帧时整个人会一呼一吸。
"""
import numpy as np
from PIL import Image

W, H = 960, 1334
FOOT_Y, TARGET_H = 1160, 820
BG = '../web/assets/bg.jpg'


def frame(p):
    bg = Image.open(BG).convert('RGBA').resize((W, H), Image.LANCZOS)
    ch = Image.open(f'parts/f{p:03d}.png')
    ys, xs = np.where(np.array(ch)[..., 3] > 16)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    ch = ch.crop((x0, y0, x1 + 1, y1 + 1))

    s = TARGET_H / ch.height
    ch = ch.resize((max(1, round(ch.width * s)), TARGET_H), Image.LANCZOS)
    bg.alpha_composite(ch, (W // 2 - ch.width // 2, FOOT_Y - TARGET_H))
    return bg.convert('RGB')


if __name__ == '__main__':
    ps = list(range(0, 101, 5))
    for p in ps:
        frame(p).save(f'frames/p{p:03d}.jpg', quality=90)
    print('frames done')

    for tag, sel in (('三档', [0, 50, 100]), ('五档', [5, 25, 50, 75, 95])):
        ims = [Image.open(f'frames/p{p:03d}.jpg') for p in sel]
        tw = 430
        ims = [im.resize((tw, round(H * tw / W)), Image.LANCZOS) for im in ims]
        cv = Image.new('RGB', (tw * len(ims), ims[0].height))
        for i, im in enumerate(ims):
            cv.paste(im, (tw * i, 0))
        cv.save(f'v13_合成_{tag}.png', quality=92)
    print('proofs done')
