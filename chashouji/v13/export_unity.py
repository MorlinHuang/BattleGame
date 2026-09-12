"""导出 Unity 用的角色层帧序列。

归一化基准用 alpha 面积开方，不用外框高度：同样两个人换个姿势，占的
面积几乎不变，而外框高度会随"谁跪下了"剧烈起伏 —— p=80 男方倒下时两人
横向拉开、外框只有 760 高，按高度归一化会把整组人放大 45%，宽度直接
撑到 1104 溢出画布。

每帧输出成与游戏画布同尺寸的透明图，人物已经摆好位置，Unity 端只要
整张贴上去就行，不需要再算缩放和锚点。
"""
import numpy as np
from PIL import Image

W, H = 960, 1334
FOOT_Y = 1200          # 双方最低点（脚或膝）落在这条地面线上
BASE_H = 880           # 中位帧的人物高度
DST = '../unity/BattleGame/Assets/StreamingAssets/art/frames'
PS = list(range(0, 101, 5))


def stats(p):
    al = np.array(Image.open(f'parts/f{p:03d}.png'))[..., 3]
    a = al > 16
    ys, xs = np.where(a)
    return a.sum(), (xs.min(), xs.max(), ys.min(), ys.max())


def main():
    import os
    os.makedirs(DST, exist_ok=True)
    info = {p: stats(p) for p in PS}
    a0 = np.median([v[0] for v in info.values()])
    h0 = np.median([v[1][3] - v[1][2] for v in info.values()])

    for p in PS:
        area, (x0, x1, y0, y1) = info[p]
        s = (a0 / area) ** 0.5 * BASE_H / h0
        im = Image.open(f'parts/f{p:03d}.png').crop((x0, y0, x1 + 1, y1 + 1))
        w, h = max(1, round(im.width * s)), max(1, round(im.height * s))
        im = im.resize((w, h), Image.LANCZOS)

        cv = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        cv.alpha_composite(im, (W // 2 - w // 2, FOOT_Y - h),
                           source=(max(0, w // 2 - W // 2), 0, w, h))
        cv.save(f'{DST}/f{p:03d}.png', optimize=True)
        print(f'p={p:3d} scale={s:.3f} {w}x{h} -> {os.path.getsize(f"{DST}/f{p:03d}.png")//1024}KB')


if __name__ == '__main__':
    main()
