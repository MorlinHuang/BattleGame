"""导出 Unity 用的角色层帧序列。

尺度基准用女方睡衣（浅粉是她独有的颜色）的面积开方，不用整组人物的 alpha
面积：生图时模型每一张的"镜头远近"都不一样 —— p=85~95 那几张人物被画小一
圈，p=100 又突然拉近，连播时整组人会呼吸式地胀缩，这比姿态跳还刺眼。整组
alpha 面积同时受姿态影响（趴下的人面积本来就小），拿它归一化等于把"镜头推
拉"和"谁趴下了"混成一个数去修，两头都修不准。而女方在 p=15 以后始终是站立
前倾的同一个人，她的睡衣面积基本只反映镜头远近 —— 实测 p=20~70 这段稳定在
325~344（±3%），两头则掉到 280 或涨到 386。

水平按 alpha 质心对齐画布中线，不按外框中心：外框边缘是甩出去的头发和伸直
的手脚，位置全看姿态，按它对齐会让重心左右漂 —— p=95→100 漂了 25px，而且
方向与 p 的走向相反。重心该往哪偏是引擎按 p 连续算的（actorX），帧本身只管
姿态。

全局系数取"最宽的一档正好塞进画布"：原图里两人总是撑满 1024 画幅，而横向
裁切切掉的是实打实的手脚 —— 实测左右各裁 40px 就损失 3~5% 的身体面积。

尺度统一后有几档宽到按质心居中就会顶出画布。硬把它推回画布内，重心就会在
相邻档之间弹 —— p=95 要推 58px 而 p=100 一点不用，硬切时整组人横向一跳。所
以把"推回去"这件事沿 p 摊开：每档至少满足自己的边界，同时不许与邻档相差超
过 RATE，多出来的偏移让附近几档分担，代价是它们的重心也略微偏离中线。摊出
来的方向正好是 p 越大整组人越靠左，与"查岗党占优就把手机拽向左"一致。
"""
import os

import numpy as np
from PIL import Image

W, H = 960, 1334
FOOT_Y = 1200          # 双方最低点（脚或膝）落在这条地面线上
MARGIN = 4             # 最宽那档到画布左右边的总余量
RATE = 15              # 相邻两档的重心横向偏移上限（px）
DST = '../unity/BattleGame/Assets/StreamingAssets/art/frames'
PS = list(range(0, 101, 5))


def measure(p):
    im = np.array(Image.open(f'parts/f{p:03d}.png'))
    rgb, al = im[..., :3].astype(np.int16), im[..., 3]
    R, G, B = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    # 浅粉睡衣：R 明显高于 G 和 B，而 G 与 B 几乎相等。白兔图案（三通道齐平）
    # 和红色滚边（R-G 远超 70）都落在判据外，男方一身白 T 黑裤更进不来。
    pink = (al > 200) & (R > 215) & (R - G > 12) & (R - G < 70) \
        & (abs(G - B) < 20) & (R - B > 8)

    a = al > 16
    ys, xs = np.nonzero(a)
    return dict(ref=pink.sum() ** 0.5, cx=xs.mean(),
                box=(xs.min(), xs.max(), ys.min(), ys.max()))


def main():
    os.makedirs(DST, exist_ok=True)
    m = {p: measure(p) for p in PS}

    ref = np.median([v['ref'] for v in m.values()])
    s = {p: ref / m[p]['ref'] for p in PS}
    widest = max((m[p]['box'][1] - m[p]['box'][0] + 1) * s[p] for p in PS)
    g = (W - MARGIN) / widest

    print(f'尺度基准中位数={ref:.1f}  全局系数={g:.4f}  最宽档归一后={widest:.0f}px')

    # 每档先算出"质心落在画布中线"时的贴图左边界，以及它在不裁切的前提下
    # 能挪动的区间；随后用区间传播把 RATE 的连续性约束并进去。
    plan = {}
    for p in PS:
        k = s[p] * g
        x0, x1, y0, y1 = m[p]['box']
        w, h = max(1, round((x1 - x0 + 1) * k)), max(1, round((y1 - y0 + 1) * k))
        ideal = W / 2 - (m[p]['cx'] - x0) * k
        plan[p] = dict(k=k, w=w, h=h, ideal=ideal,
                       lo=min(0, W - w) - ideal, hi=max(0, W - w) - ideal)

    for a, b in zip(PS, PS[1:]):                       # 前向收紧
        plan[b]['lo'] = max(plan[b]['lo'], plan[a]['lo'] - RATE)
        plan[b]['hi'] = min(plan[b]['hi'], plan[a]['hi'] + RATE)
    for a, b in zip(PS[::-1], PS[-2::-1]):             # 后向收紧
        plan[b]['lo'] = max(plan[b]['lo'], plan[a]['lo'] - RATE)
        plan[b]['hi'] = min(plan[b]['hi'], plan[a]['hi'] + RATE)

    for p in PS:
        q = plan[p]
        k, w, h = q['k'], q['w'], q['h']
        shift = float(np.clip(0, q['lo'], q['hi']))    # 在允许区间里尽量不偏
        left, top = round(q['ideal'] + shift), FOOT_Y - h

        x0, x1, y0, y1 = m[p]['box']
        im = Image.open(f'parts/f{p:03d}.png').crop((x0, y0, x1 + 1, y1 + 1))
        src = np.array(im.resize((w, h), Image.LANCZOS))

        dx0, dx1 = max(0, left), min(W, left + w)
        dy0, dy1 = max(0, top), min(H, top + h)
        cv = np.zeros((H, W, 4), np.uint8)
        cv[dy0:dy1, dx0:dx1] = src[dy0 - top:dy1 - top, dx0 - left:dx1 - left]
        Image.fromarray(cv).save(f'{DST}/f{p:03d}.png', optimize=True)

        cut = 1 - (dx1 - dx0) / w
        print(f'p={p:3d} scale={k:.3f} {w}x{h} 重心偏移={shift:6.1f} '
              f'{"裁 %.0f%%" % (cut * 100) if cut > 0.001 else "完整"} '
              f'-> {os.path.getsize(f"{DST}/f{p:03d}.png") // 1024}KB')


if __name__ == '__main__':
    main()
