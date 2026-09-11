# -*- coding: utf-8 -*-
"""把整张悟空立绘切成骨骼部件。
   每个部件 = 一个多边形区域(按优先级先到先得) + 一个枢轴点(关节在原图中的位置)。
   torso 兜底: 没被任何部件认领的像素都归躯干, 保证不丢像素、不留缝。"""
import json, os
from PIL import Image, ImageDraw

SRC = '/workspace/art/wk_clean.png'
OUT = '/workspace/art/parts'
os.makedirs(OUT, exist_ok=True)

im = Image.open(SRC).convert('RGBA')
W, H = im.size

# 优先级从上到下, 先认领先得
POLY = [
 ('head',      [(328,0),(620,0),(610,180),(600,240),(605,330),(596,392),(525,404),(522,452),(428,452),(425,404),(352,392),(345,330),(342,240),(338,180)]),
 ('plumeR',    [(346,0),(0,0),(0,706),(118,742),(186,606),(178,380),(346,190)]),
 ('plumeL',    [(602,0),(948,0),(948,706),(830,742),(762,606),(770,380),(602,190)]),
 ('handR',     [(14,838),(158,838),(146,906),(118,998),(14,998)]),
 ('handL',     [(934,838),(790,838),(802,906),(830,998),(934,998)]),
 ('foreArmR',  [(38,686),(206,686),(220,752),(184,892),(44,892)]),
 ('foreArmL',  [(910,686),(742,686),(728,752),(764,892),(904,892)]),
 ('pauldronR', [(172,385),(340,385),(342,560),(322,656),(232,672),(156,622),(152,470)]),
 ('pauldronL', [(776,385),(608,385),(606,560),(626,656),(716,672),(792,622),(796,470)]),
 ('upperArmR', [(146,596),(336,596),(320,700),(300,776),(200,786),(142,700)]),
 ('upperArmL', [(802,596),(612,596),(628,700),(648,776),(748,786),(806,700)]),
 ('tail',      [(748,1040),(882,1038),(946,1150),(936,1272),(848,1348),(772,1312),(802,1210),(772,1128)]),
 ('footR',     [(62,1452),(336,1452),(336,1658),(62,1658)]),
 ('footL',     [(886,1452),(612,1452),(612,1658),(886,1658)]),
 ('shinR',     [(186,1216),(342,1212),(348,1470),(176,1470)]),
 ('shinL',     [(762,1216),(606,1212),(600,1470),(772,1470)]),
 ('thighR',    [(196,1176),(350,1176),(350,1264),(190,1264)]),
 ('thighL',    [(752,1176),(598,1176),(598,1264),(758,1264)]),
]

# 关节枢轴 (原图坐标)。sprite 绕它旋转
PIVOT = {
 'head':(472,424), 'plumeR':(286,62), 'plumeL':(662,62),
 'pauldronR':(278,470), 'upperArmR':(278,470), 'foreArmR':(210,700), 'handR':(128,852),
 'pauldronL':(670,470), 'upperArmL':(670,470), 'foreArmL':(738,700), 'handL':(820,852),
 'thighR':(280,1080), 'shinR':(266,1268), 'footR':(252,1468),
 'thighL':(668,1080), 'shinL':(682,1268), 'footL':(696,1468),
 'tail':(766,1082), 'torso':(474,1100),
}
# 骨末端(用于算 sprite 的静止朝向与长度)
TIP = {
 'pauldronR':(210,700), 'upperArmR':(210,700), 'foreArmR':(128,852), 'handR':(70,950),
 'pauldronL':(738,700), 'upperArmL':(738,700), 'foreArmL':(820,852), 'handL':(878,950),
 'thighR':(266,1268), 'shinR':(252,1468), 'footR':(180,1600),
 'thighL':(682,1268), 'shinL':(696,1468), 'footL':(768,1600),
 'head':(472,150), 'plumeR':(64,700), 'plumeL':(884,700),
 'tail':(880,1300), 'torso':(474,470),
}

claimed = Image.new('L', (W, H), 0)
cd = ImageDraw.Draw(claimed)
meta = {}

def emit(name, mask):
    part = Image.new('RGBA', (W, H), (0,0,0,0))
    part.paste(im, (0,0), mask)
    bb = part.getbbox()
    if not bb:
        print('  !! 空部件', name); return
    part.crop(bb).save(f'{OUT}/{name}.png')
    px, py = PIVOT[name]; tx, ty = TIP[name]
    meta[name] = {'box':bb, 'w':bb[2]-bb[0], 'h':bb[3]-bb[1],
                  'pivot':[px-bb[0], py-bb[1]], 'tip':[tx-bb[0], ty-bb[1]],
                  'restPivot':[px,py], 'restTip':[tx,ty]}
    print(f'  {name:10s} {bb[2]-bb[0]:4d}x{bb[3]-bb[1]:4d}  bbox={bb}')

alpha = im.getchannel('A')
for name, poly in POLY:
    m = Image.new('L', (W, H), 0)
    ImageDraw.Draw(m).polygon(poly, fill=255)
    # 只取还没被认领的、且本身不透明的像素
    m = Image.composite(Image.new('L',(W,H),0), m, claimed)
    m = Image.eval(m, lambda v: v)
    mm = Image.new('L',(W,H),0)
    mm.paste(m, (0,0), alpha.point(lambda a: 255 if a>10 else 0))
    emit(name, mm)
    cd.polygon(poly, fill=255)

# 躯干 = 剩下所有不透明像素
rest = Image.new('L',(W,H),0)
rest.paste(alpha.point(lambda a: 255 if a>10 else 0), (0,0),
           claimed.point(lambda v: 0 if v>127 else 255))
emit('torso', rest)

json.dump(meta, open(f'{OUT}/parts.json','w'), ensure_ascii=False, indent=1)
print('部件数:', len(meta))
