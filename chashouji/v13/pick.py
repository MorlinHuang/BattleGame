"""从每档的多个生图候选里挑一个，判据是它与左右两个邻居的差异。

一张补帧帧要做的事就是把一个大跳分成两个小跳，所以好坏看两件事：两边的
差异都要小（最大值），而且要均衡（差值）—— 偏向某一侧的候选等于没分开，
另一侧照样跳。判据是 max(d_left, d_right) + |d_left - d_right|。

差异量在归一化之后算：生图每张的镜头远近都不一样，不先按现行规则摆好位置
就比，量到的全是尺度差。
"""
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, '.')
from cutout import cutout

DST = '../unity/BattleGame/Assets/StreamingAssets/art/frames'
W, H, FOOT_Y = 960, 1334, 1200


def pink_ref(al, rgb):
    R, G, B = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    m = (al > 200) & (R > 215) & (R - G > 12) & (R - G < 70) & (abs(G - B) < 20) & (R - B > 8)
    return m.sum() ** 0.5


def place(im, ref, g):
    """按现行规则把一张抠好的 RGBA 摆到画布上（不含跨档的重心传播）。"""
    a = np.array(im)
    k = ref / pink_ref(a[..., 3], a[..., :3].astype(np.int16)) * g
    ys, xs = np.nonzero(a[..., 3] > 16)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    c = im.crop((x0, y0, x1 + 1, y1 + 1))
    w, h = max(1, round(c.width * k)), max(1, round(c.height * k))
    src = np.array(c.resize((w, h), Image.LANCZOS))
    left = int(np.clip(round(W / 2 - (xs.mean() - x0) * k), min(0, W - w), max(0, W - w)))
    top = FOOT_Y - h
    dx0, dx1, dy0, dy1 = max(0, left), min(W, left + w), max(0, top), min(H, top + h)
    cv = np.zeros((H, W, 4), np.uint8)
    cv[dy0:dy1, dx0:dx1] = src[dy0 - top:dy1 - top, dx0 - left:dx1 - left]
    return cv[..., 3] > 128


def dif(a, b):
    return (a ^ b).sum() / max(a.sum(), b.sum()) * 100


def main(ref, g, jobs):
    print(f'{"档":>5} {"候选":>4} {"vs 左":>7} {"vs 右":>7} {"失衡":>6} {"评分":>7}')
    for p, (lo, hi), cands in jobs:
        al = np.array(Image.open(f'{DST}/f{lo:03d}.png'))[..., 3] > 128
        ah = np.array(Image.open(f'{DST}/f{hi:03d}.png'))[..., 3] > 128
        base = dif(al, ah)                             # 不补这一档时的跳变
        best = None
        for name, path in cands:
            m = place(cutout(path), ref, g)
            d1, d2 = dif(al, m), dif(m, ah)
            score = max(d1, d2) + abs(d1 - d2)
            hit = best is None or score < best[0]
            print(f'{p:>5} {name:>4} {d1:6.1f}% {d2:6.1f}% {abs(d1-d2):5.1f} {score:7.1f}'
                  + ('  ←' if hit else ''))
            if hit:
                best = (score, max(d1, d2), path)
        # 补一档是为了把一次大跳拆成两次小跳。如果拆出来较大的那一段并不比
        # 原来那一跳小，这张帧就只是在中间插了个更远的落脚点，不如不补。
        if best[1] >= base:
            print(f'      → 弃用：原 {lo}↔{hi} 跳 {base:.1f}%，最好的候选拆完还有 {best[1]:.1f}%\n')
            continue
        cutout(best[2]).save(f'parts/f{p:03d}.png')
        Image.open(best[2]).save(f'raw/f{p:03d}.png')
        print(f'      → 采用，{lo}↔{hi} 的 {base:.1f}% 拆成最大 {best[1]:.1f}%\n')


if __name__ == '__main__':
    G = '/tmp/kf_generated_images/edited-'
    JOBS = [
        (17, (15, 20), [('A', G + '1789208051664-1.png'), ('B', G + '1789208051879-2.png')]),
        (22, (20, 25), [('A', G + '1789208127198-1.png'), ('B', G + '1789208127377-2.png')]),
        (32, (30, 35), [('A', G + '1789208201169-1.png'), ('B', G + '1789208201362-2.png')]),
        (62, (60, 65), [('A', G + '1789208275178-1.png'), ('B', G + '1789208275539-2.png')]),
        (72, (70, 75), [('A', G + '1789208353351-1.png'), ('B', G + '1789208353566-2.png')]),
        (82, (80, 85), [('A', G + '1789208419084-1.png'), ('B', G + '1789208419273-2.png')]),
        (87, (85, 90), [('A', G + '1789208501231-1.png'), ('B', G + '1789208501426-2.png')]),
        (92, (90, 95), [('A', G + '1789208606516-1.png'), ('B', G + '1789208606696-2.png')]),
    ]
    main(338.3, 0.7738, JOBS)
