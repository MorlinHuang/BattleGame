"""网页素材准备：边缘颜色扩散 → 缩放 → 压缩。

edge_extend 是必须的：抠像后透明区 RGB 仍是品红幕布色，GPU 双线性采样
和 LANCZOS 缩放都会把它混进不透明边缘，出一圈紫边。先把主体颜色向外
扩散若干像素，再缩放，采样到的永远是主体色。
"""
import os
import numpy as np
from PIL import Image
from scipy import ndimage

SRC, DST = 'v10', 'web/assets'
K = np.ones((3, 3), np.float32)


def edge_extend(im, iters=14):
    a = np.array(im).astype(np.float32)
    rgb, al = a[..., :3].copy(), a[..., 3]
    mask = al > 8
    for _ in range(iters):
        m = mask.astype(np.float32)
        cnt = ndimage.convolve(m, K, mode='constant')
        acc = np.stack([ndimage.convolve(rgb[..., c] * m, K, mode='constant') for c in range(3)], -1)
        grown = cnt > 0
        fill = grown & ~mask
        if not fill.any():
            break
        rgb[fill] = (acc / np.maximum(cnt, 1)[..., None])[fill]
        mask |= grown
    return Image.fromarray(np.concatenate([rgb, al[..., None]], -1).astype(np.uint8), 'RGBA')


os.makedirs(DST, exist_ok=True)
bg = Image.open(f'{SRC}/raw/bg_A.png').convert('RGB')
bg = bg.crop((0, 0, bg.width, bg.height - 113)).resize((960, 1334), Image.LANCZOS)
bg.save(f'{DST}/bg.jpg', quality=88, optimize=True)
print('bg', bg.size, os.path.getsize(f'{DST}/bg.jpg') // 1024, 'KB')

for n in ('girl', 'boy'):
    im = edge_extend(Image.open(f'{SRC}/parts/{n}.png').convert('RGBA'))
    w = 760
    im = im.resize((w, round(im.height * w / im.width)), Image.LANCZOS)
    im.save(f'{DST}/{n}.png', optimize=True)
    print(n, im.size, os.path.getsize(f'{DST}/{n}.png') // 1024, 'KB')
