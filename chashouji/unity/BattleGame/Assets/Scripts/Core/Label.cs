using UnityEngine;

namespace Chashouji {

/* 画布坐标系里的一行字。描边用四个偏移的深色副本堆出来 —— 网页版是 strokeText，
   目的一样：HUD 的字压在背景照片上，没有描边就糊在一起。
   字体走系统动态字体：内置 Arial 没有汉字，"查岗党/灭迹党"会整行变成方框。 */
public class Label {
    readonly TextMesh[] tms;
    readonly Transform root;

    public static Font LoadCJKFont(int size) {
        string[] names = { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun", "Arial" };
        var f = Font.CreateDynamicFontFromOSFont(names, size);
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return f;
    }

    public Label(Transform parent, Font font, float pixelHeight, TextAnchor anchor, int order,
                 Color main, Color outline, float outlineWidth) {
        root = new GameObject("label").transform;
        root.SetParent(parent, false);
        int atlas = Mathf.Max(16, Mathf.RoundToInt(pixelHeight * 2f));
        float cs = pixelHeight * 10f / atlas;
        var offs = new[] {
            new Vector2(-outlineWidth, 0), new Vector2(outlineWidth, 0),
            new Vector2(0, -outlineWidth), new Vector2(0, outlineWidth),
            Vector2.zero,
        };
        tms = new TextMesh[offs.Length];
        for (int i = 0; i < offs.Length; i++) {
            var go = new GameObject(i == offs.Length - 1 ? "main" : "outline" + i);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(offs[i].x, -offs[i].y, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = atlas;
            tm.characterSize = cs;
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.MiddleRight ? TextAlignment.Right : TextAlignment.Left;
            tm.color = i == offs.Length - 1 ? main : outline;
            tm.fontStyle = FontStyle.Bold;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.sortingOrder = order + (i == offs.Length - 1 ? 1 : 0);
            tms[i] = tm;
        }
    }

    public void SetPos(float x, float y) => root.localPosition = new Vector3(x, -y, 0f);
    public void SetText(string s) { foreach (var t in tms) t.text = s; }
}
}
