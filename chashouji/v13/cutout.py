"""品红幕布抠像。

判据用 min(R,B)-G：幕布是纯品红，R 和 B 同时拉满而 G 压到 0，这个差值在
幕布上接近 255，而角色身上任何一块颜色都到不了 —— 浅粉睡衣虽然 R 高，但
它的 G 也高（高明度低饱和），差值只有二十几；红发夹是纯红，G 和 B 一起低，
差值是 0。所以单条软阈值就够，不需要再去保护"角色内部"，那种保护反而会把
两人躯干与手臂围出来的幕布三角当成洞填实。

去溢色只作用在半透明边缘：不透明区域的粉睡衣本身就该保留它的偏红，全局去
溢色会把整件睡衣洗灰。
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

LO, HI = 60, 150          # m 低于 LO 全不透明，高于 HI 全透明


def cutout(path):
    a = np.array(Image.open(path).convert('RGB')).astype(np.int16)
    m = np.minimum(a[..., 0], a[..., 2]) - a[..., 1]
    alpha = np.clip((HI - m) / (HI - LO), 0, 1)

    edge = ndimage.binary_dilation(alpha < 0.99, np.ones((3, 3))) & (alpha > 0.01)
    rgb = a.astype(np.float32)
    spill = np.clip((rgb[..., 0] + rgb[..., 2]) / 2 - rgb[..., 1], 0, None)
    for c in (0, 2):
        rgb[..., c] = np.where(edge, rgb[..., c] - spill * 0.6, rgb[..., c])

    out = np.concatenate([np.clip(rgb, 0, 255), (alpha * 255)[..., None]], -1)
    return Image.fromarray(out.astype(np.uint8), 'RGBA')


if __name__ == '__main__':
    for p in [int(x) for x in sys.argv[1:]]:
        im = cutout(f'raw/f{p:03d}.png')
        im.save(f'parts/f{p:03d}.png')
        ys, xs = np.where(np.array(im)[..., 3] > 16)
        print(f'p={p:3d} bbox x[{xs.min()},{xs.max()}] y[{ys.min()},{ys.max()}] '
              f'w={xs.max()-xs.min()} h={ys.max()-ys.min()}')
