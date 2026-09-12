using System.Collections.Generic;
using UnityEngine;

namespace Chashouji {

public class RigMesh {
    public Vector2[] rest;      // 静止顶点，图片像素坐标（y 向下）
    public Vector2[] uv;
    public int[] tris;
    public int[] bone;          // 每顶点 4 个骨索引
    public float[] wt;          // 每顶点 4 个权重
    public int verts, triCount;
}

public static class MeshRig {

    const float MINCOV = 0.30f;
    /* root 始终以一个基础权重在场，而不是"其他骨全落空时才顶上" —— 归一化会
       抹掉"总权重其实很弱"这个事实：骨的势力边缘上，一个 2e-6 的权重归一化后
       照样是 100%，而紧挨着的顶点因为差一点点落到范围外、权重为 0 归了 root。
       一个完全跟着手走、一个完全不动，中间那条边就被拉成一道尖刺。 */
    const float ROOTFLOOR = 0.012f;

    public static RigMesh Build(Texture2D img, Skeleton sk, int cols, int rows) {
        int iw = img.width, ih = img.height;
        var px = img.GetPixels32();
        float cw = (float)iw / cols, ch = (float)ih / rows;

        /* 格子内按 5x5 采样数不透明像素，占比达门槛才保留。
           "只要有一个就保留"会把指缝、两条手臂之间那些几乎全是背景的格子也留下，
           它们把本不相连的两块皮缝成一张布 —— IK 一拉开，这块布就被撕成细长的
           尖刺，看上去像手指被抻长了。 */
        var cellCov = new float[cols * rows];
        for (int j = 0; j < rows; j++) for (int i = 0; i < cols; i++) {
            int hit = 0;
            for (int sy = 0; sy < 5; sy++) for (int sx = 0; sx < 5; sx++) {
                int x = Mathf.Min(iw - 1, Mathf.RoundToInt((i + sx / 4f) * cw));
                int y = Mathf.Min(ih - 1, Mathf.RoundToInt((j + sy / 4f) * ch));
                if (px[(ih - 1 - y) * iw + x].a > 8) hit++;   // Texture2D 行序自下而上
            }
            cellCov[j * cols + i] = hit / 25f;
        }

        var map = new int[(cols + 1) * (rows + 1)];
        for (int i = 0; i < map.Length; i++) map[i] = -1;
        var pos = new List<Vector2>();
        var idx = new List<int>();
        var triCov = new List<float>();

        System.Func<int, int, int> vid = (i, j) => {
            int k = j * (cols + 1) + i;
            if (map[k] < 0) { map[k] = pos.Count; pos.Add(new Vector2(i * cw, j * ch)); }
            return map[k];
        };

        for (int j = 0; j < rows; j++) for (int i = 0; i < cols; i++) {
            float cov = cellCov[j * cols + i];
            if (cov < MINCOV) continue;
            int a = vid(i, j), b = vid(i + 1, j), d = vid(i, j + 1), e = vid(i + 1, j + 1);
            idx.Add(a); idx.Add(b); idx.Add(d);
            idx.Add(b); idx.Add(e); idx.Add(d);
            triCov.Add(cov); triCov.Add(cov);
        }

        int n = pos.Count;
        var bone = new int[n * 4];
        var wt = new float[n * 4];
        var bs = sk.bones;
        var hist = new float[bs.Count];
        var chainOf = new string[bs.Count];
        for (int k = 0; k < bs.Count; k++)
            chainOf[k] = bs[k].name.StartsWith("armA") ? "armA"
                       : bs[k].name.StartsWith("armB") ? "armB" : "body";
        int rootIdx = 0;
        for (int k = 0; k < bs.Count; k++) if (bs[k].parent < 0) { rootIdx = k; break; }

        var order = new int[4];
        for (int v = 0; v < n; v++) {
            float x = pos[v].x, y = pos[v].y;
            for (int k = 0; k < bs.Count; k++) {
                float t = Skeleton.BoneField(x, y, bs[k]);
                hist[k] = t > 0f ? t * t * t * bs[k].strength : 0f;
            }
            hist[rootIdx] = Mathf.Max(hist[rootIdx], ROOTFLOOR);

            // 取前 4 名
            for (int s = 0; s < 4; s++) {
                int best = -1; float bv = float.NegativeInfinity;
                for (int k = 0; k < bs.Count; k++) {
                    bool taken = false;
                    for (int q = 0; q < s; q++) if (order[q] == k) { taken = true; break; }
                    if (taken) continue;
                    if (hist[k] > bv) { bv = hist[k]; best = k; }
                }
                order[s] = best;
            }

            /* 一个顶点不能同时归两条互不相连的手臂。两臂在图上隔着背景，共用顶点
               就等于被两边同时拉扯，会在它们之间扯出一片橡皮膜。只留权重最高的
               那条链，另一条清零 —— body 不清，手臂和身体本来就是连着的。 */
            string arm = null;
            for (int s = 0; s < 4; s++) {
                string chn = chainOf[order[s]];
                if (chn == "body") continue;
                if (arm == null) arm = chn; else if (chn != arm) hist[order[s]] = 0f;
            }
            float sum = 0f;
            for (int s = 0; s < 4; s++) sum += hist[order[s]];
            for (int s = 0; s < 4; s++) {
                bone[v * 4 + s] = order[s];
                wt[v * 4 + s] = sum > 0f ? hist[order[s]] / sum : (s == 0 ? 1f : 0f);
            }
        }

        var keep = new List<int>();
        var arms = new HashSet<string>();
        for (int i = 0, t = 0; i < idx.Count; i += 3, t++) {
            arms.Clear();
            for (int k = 0; k < 3; k++) {
                int v = idx[i + k];
                // 看全部四个权重而不只是主导骨：一个顶点可能主导 root，却有近半的
                // 权重挂在另一条手臂上，只看主导骨会把这种跨接的三角形漏过去
                for (int w = 0; w < 4; w++) {
                    if (wt[v * 4 + w] <= 0.2f) continue;
                    string chn = chainOf[bone[v * 4 + w]];
                    if (chn != "body") arms.Add(chn);
                }
            }
            if (arms.Count > 1) continue;
            /* 轮廓边上、一多半是背景的格子：上排顶点还在袖子里跟着手臂走，下排已经
               落到骨的势力范围外被 root 钉住，这条边会被拉成一道拖影。门槛要卡得很
               严 —— 稍一放宽，袖口和 T恤边缘这些权重本就该变的地方会被切出缺口。 */
            if (triCov[t] < 0.50f) {
                int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                float m = Mathf.Max(WDiff(bone, wt, a, b), Mathf.Max(WDiff(bone, wt, b, c), WDiff(bone, wt, c, a)));
                if (m > 1.4f) continue;
            }
            keep.Add(idx[i]); keep.Add(idx[i + 1]); keep.Add(idx[i + 2]);
        }

        var rest = new Vector2[n];
        var uv = new Vector2[n];
        for (int v = 0; v < n; v++) {
            rest[v] = pos[v];
            // 图片 y 向下，Unity 贴图 v 向上
            uv[v] = new Vector2(pos[v].x / iw, 1f - pos[v].y / ih);
        }
        return new RigMesh {
            rest = rest, uv = uv, tris = keep.ToArray(), bone = bone, wt = wt,
            verts = n, triCount = keep.Count / 3,
        };
    }

    /* 两个顶点权重向量的 L1 距离。同一块皮上相邻顶点的权重是平滑过渡的；
       差异突然拉满只有一个意思 —— 这条边其实横跨了两块不该连在一起的皮。 */
    static float WDiff(int[] bone, float[] wt, int a, int b) {
        float d = 0f;
        for (int i = 0; i < 4; i++) {
            int bi = bone[a * 4 + i]; float wb = 0f;
            for (int j = 0; j < 4; j++) if (bone[b * 4 + j] == bi) { wb = wt[b * 4 + j]; break; }
            d += Mathf.Abs(wt[a * 4 + i] - wb);
        }
        for (int j = 0; j < 4; j++) {
            int bj = bone[b * 4 + j]; bool shared = false;
            for (int i = 0; i < 4; i++) if (bone[a * 4 + i] == bj) { shared = true; break; }
            if (!shared) d += wt[b * 4 + j];
        }
        return d;
    }
}
}
