"""用光流把生图的关键档补满到每 1% 一张。

相邻关键档之间的差异实测是实打实的肢体位移 —— 把 alpha 腐蚀 16px 再比较，
差异不降反升，说明不是边缘的一两像素抖动。位移正是光流能建模的东西，而且
插出来的帧天然连续：它是从两张真帧变形出来的，不会像再生一张图那样跳到另
一个姿态解上。

但光流只在两帧姿态拓扑一致时可靠。逐档试出来的临界点在四成出头：差异
40.5% 的档对插出来是干净的，48.3% 那对男方的两条腿叠成半透明重影，72.8%
那对（男方从站着变跪地，腿的前后关系整个翻过来）女方会长出两个红发夹。
所以 MAX_DIFF 取 42，超阈值的档对这里不插，直接报出来留给生图补帧。

流场在半分辨率上解再放大：TVL1 在 960x1334 上跑得慢，而流场本身是低频的，
降采样几乎不损失。
"""
import os
import re
import sys
import time

import numpy as np
from PIL import Image
from skimage.registration import optical_flow_tvl1
from skimage.transform import warp

DST = '../unity/BattleGame/Assets/Resources/frames'
MAX_DIFF = 42.0        # 超过这个差异（%）就不插，交给生图（临界点实测见文件头）
DS = 2                 # 流场降采样倍数


def load(p):
    return np.array(Image.open(f'{DST}/f{p:03d}.png'))


def signal(a):
    """给光流用的灰度：亮度乘 alpha，背景是 0，轮廓与衣纹都保留。"""
    lum = (a[..., :3].astype(np.float32) / 255) @ np.array([.299, .587, .114], np.float32)
    return lum * (a[..., 3].astype(np.float32) / 255)


def diff(a, b):
    ma, mb = a[..., 3] > 128, b[..., 3] > 128
    return (ma ^ mb).sum() / max(ma.sum(), mb.sum()) * 100


def flow(a, b):
    v, u = optical_flow_tvl1(signal(a)[::DS, ::DS], signal(b)[::DS, ::DS],
                             attachment=12, num_warp=6)
    H, W = a.shape[:2]
    up = lambda f: np.array(Image.fromarray(f).resize((W, H), Image.BILINEAR)) * DS
    return up(v), up(u)


def blend(a, b, t, v, u):
    """t 时刻的中间帧。两端各按自己那一侧的流场 warp 过来再按 t 混合。"""
    H, W = a.shape[:2]
    rr, cc = np.mgrid[0:H, 0:W].astype(np.float32)
    ca, cb = np.array([rr - t * v, cc - t * u]), np.array([rr + (1 - t) * v, cc + (1 - t) * u])
    out = np.empty((H, W, 4), np.float32)
    for c in range(4):
        fa = warp(a[..., c].astype(np.float32), ca, order=1, mode='constant')
        fb = warp(b[..., c].astype(np.float32), cb, order=1, mode='constant')
        out[..., c] = (1 - t) * fa + t * fb
    return np.clip(out, 0, 255).astype(np.uint8)


def main():
    keys = sorted(int(n[1:4]) for n in os.listdir(DST) if re.fullmatch(r'f\d{3}\.png', n))
    print(f'关键档 {len(keys)} 张：{keys}\n')

    made, skipped = 0, []
    for a, b in zip(keys, keys[1:]):
        if b - a <= 1:
            continue
        ia, ib = load(a), load(b)
        d = diff(ia, ib)
        if d > MAX_DIFF:
            skipped.append((a, b, d))
            print(f'{a:3d}↔{b:<3d} 差异 {d:5.1f}%  跳过（超 {MAX_DIFF:.0f}%，光流会撕）')
            continue
        t0 = time.time()
        v, u = flow(ia, ib)
        for p in range(a + 1, b):
            Image.fromarray(blend(ia, ib, (p - a) / (b - a), v, u)).save(
                f'{DST}/f{p:03d}.png', optimize=True)
            made += 1
        print(f'{a:3d}↔{b:<3d} 差异 {d:5.1f}%  插了 {b-a-1} 张  {time.time()-t0:.0f}s')

    have = sorted(int(n[1:4]) for n in os.listdir(DST) if re.fullmatch(r'f\d{3}\.png', n))
    print(f'\n共插出 {made} 张，现有 {len(have)}/101 档')
    if skipped:
        print(f'仍缺口 {sum(b-a-1 for a,b,_ in skipped)} 档，来自这些区间：')
        for a, b, d in skipped:
            print(f'  {a}↔{b}  差异 {d:.1f}%  —— 需要先生图把它拆小')


if __name__ == '__main__':
    main()
