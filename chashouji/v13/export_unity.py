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

尺度修正带上限（SCALE_CAP）：偶尔有一张被模型画得特别小（p=82 的基准只有
中位数的 77%），把它修到位就得放大三成，而原图两人本来就撑满画幅，放大后
横向撑爆画布 —— 全局系数被它一张拖低，101 档跟着一起缩小 7%。宁可让这一
档人物比别人小一点。

帧只输出人物实际占的那条横带（FRAME_TOP 往下 FRAME_H 高），不是整块画布：
上面三百多行、下面一百多行全是透明像素，白占三成显存；而且 DXT 压缩要求边长
是 4 的倍数，画布高 1334 不是，Unity 导入时只能退回未压缩的 RGBA32 —— 93 张
就是 454MB。裁成 960x900 之后压到 77MB。引擎侧按同一个 FRAME_TOP 摆放。

全局系数只保证"脚所在的那条横带"塞进画布（VISIBLE_BAND），不保证整个包围
盒：生图的画幅被两人的头发撑得很宽，尤其是女方甩出去的长发，按包围盒定系数
会让整组人缩小两成。切口出现在画面中间（一只鞋齐刷刷没了）是缺陷，切口落在
画面左右边缘（头发延伸出屏幕）是正常构图 —— 这两件事不该用同一个约束。所以
下半身必须完整，上面飘出去多少不管。

尺度统一后有几档宽到按质心居中就会顶出画布。硬把它推回画布内，重心就会在
相邻档之间弹 —— p=95 要推 58px 而 p=100 一点不用，硬切时整组人横向一跳。所
以把"推回去"这件事沿 p 摊开：每档至少满足自己的边界，同时不许与邻档相差超
过 RATE，多出来的偏移让附近几档分担，代价是它们的重心也略微偏离中线。摊出
来的方向正好是 p 越大整组人越靠左，与"查岗党占优就把手机拽向左"一致。
"""
import os
import re

import numpy as np
from PIL import Image

W, H = 960, 1334       # 游戏画布
FRAME_TOP, FRAME_H = 308, 900   # 帧纹理覆盖画布上的哪一条横带，引擎侧同值
FOOT_Y = 1200          # 双方最低点（脚或膝）落在这条地面线上
MARGIN = 4             # 最宽那档到画布左右边的总余量
VISIBLE_BAND = 0.25    # 人物下多少比例的高度必须完整落在画布内（脚与小腿）
SCALE_CAP = 1.20       # 单档尺度修正上限，见下方注释
RATE = 15              # 每 5% 档距允许的重心横向偏移上限（px），按实际档距缩放
DST = '../unity/BattleGame/Assets/Resources/frames'
# 档位集合由 parts/ 里实际有哪些帧决定：补帧阶段它是不等间距的，
# 光流补满之后才回到每 1% 一张。
PS = sorted(int(n[1:4]) for n in os.listdir('parts') if re.fullmatch(r'f\d{3}\.png', n))


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
    y0, y1 = ys.min(), ys.max()
    # 必须完整可见的那一段：从脚底往上量 VISIBLE_BAND 的身高，双方的鞋和小腿
    # 都在里面。头发和肘部外缘不在，它们可以飘到画布外。
    band = a[y1 - round((y1 - y0 + 1) * VISIBLE_BAND):y1 + 1]
    bx = np.nonzero(band)[1]
    return dict(ref=pink.sum() ** 0.5, cx=xs.mean(),
                box=(xs.min(), xs.max(), y0, y1), vis=(bx.min(), bx.max()))


def main():
    # 先清空：光流补出来的中间帧也躺在这个目录里，关键档一变它们就过期了，
    # 留着会跟新导出的帧混在一起。补帧由 interp_frames.py 在这之后重跑。
    os.makedirs(DST, exist_ok=True)
    for n in os.listdir(DST):
        if re.fullmatch(r'f\d{3}\.png', n):
            os.remove(os.path.join(DST, n))

    m = {p: measure(p) for p in PS}

    ref = np.median([v['ref'] for v in m.values()])
    s = {p: min(ref / m[p]['ref'], SCALE_CAP) for p in PS}
    widest = max((m[p]['vis'][1] - m[p]['vis'][0] + 1) * s[p] for p in PS)
    g = (W - MARGIN) / widest

    print(f'尺度基准中位数={ref:.1f}  全局系数={g:.4f}  最宽档的可见横带={widest:.0f}px')

    # 每档先算出"质心落在画布中线"时的贴图左边界，以及它在不裁切的前提下
    # 能挪动的区间；随后用区间传播把 RATE 的连续性约束并进去。
    plan = {}
    for p in PS:
        k = s[p] * g
        x0, x1, y0, y1 = m[p]['box']
        w, h = max(1, round((x1 - x0 + 1) * k)), max(1, round((y1 - y0 + 1) * k))
        ideal = W / 2 - (m[p]['cx'] - x0) * k
        # 允许的贴图左边界区间，只约束可见横带：它的左端不许越过画布左边，
        # 右端不许越过画布右边。包围盒本身超出去多少不管。
        v0, v1 = ((v - x0) * k for v in m[p]['vis'])
        plan[p] = dict(k=k, w=w, h=h, ideal=ideal,
                       lo=-v0 - ideal, hi=(W - v1) - ideal)

    def tighten(a, b):
        r = RATE * abs(b - a) / 5                      # 档距越密，允许的位移越小
        plan[b]['lo'] = max(plan[b]['lo'], plan[a]['lo'] - r)
        plan[b]['hi'] = min(plan[b]['hi'], plan[a]['hi'] + r)

    for a, b in zip(PS, PS[1:]):
        tighten(a, b)
    for a, b in zip(PS[::-1], PS[-2::-1]):
        tighten(a, b)

    for p in PS:
        q = plan[p]
        k, w, h = q['k'], q['w'], q['h']
        shift = float(np.clip(0, q['lo'], q['hi']))    # 在允许区间里尽量不偏
        left = round(q['ideal'] + shift)
        top = FOOT_Y - h

        x0, x1, y0, y1 = m[p]['box']
        im = Image.open(f'parts/f{p:03d}.png').crop((x0, y0, x1 + 1, y1 + 1))
        src = np.array(im.resize((w, h), Image.LANCZOS))

        top -= FRAME_TOP                               # 换算到帧纹理的坐标系
        if top < 0 or top + h > FRAME_H:
            raise SystemExit(f'p={p} 的人物（{h}px 高，顶在 {top}）超出 FRAME_H='
                             f'{FRAME_H} 这条横带，改常量并同步引擎侧')
        dx0, dx1 = max(0, left), min(W, left + w)
        cv = np.zeros((FRAME_H, W, 4), np.uint8)
        cv[top:top + h, dx0:dx1] = src[:, dx0 - left:dx1 - left]
        Image.fromarray(cv).save(f'{DST}/f{p:03d}.png', optimize=True)

        v0, v1 = (round((v - x0) * k) for v in m[p]['vis'])
        if left + v0 < 0 or left + v1 > W:
            raise SystemExit(f'p={p} 的可见横带被画布切掉了，区间传播的 RATE 太松')
        out = 1 - (dx1 - dx0) / w
        print(f'p={p:3d} scale={k:.3f} {w}x{h} 重心偏移={shift:6.1f} '
              f'{"溢出画布 %.0f%%" % (out * 100) if out > 0.001 else "全在画布内"} '
              f'-> {os.path.getsize(f"{DST}/f{p:03d}.png") // 1024}KB')


if __name__ == '__main__':
    main()
