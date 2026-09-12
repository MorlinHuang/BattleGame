using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chashouji.EditorTools {

/* 一键把工程整备成"打开就能 Play"的状态，也供 -batchmode 调用。
   干三件事：
     1. 色彩空间设成 Gamma —— 网页版是画布 2D，颜色都按 sRGB 直接写的，
        工程若跑在 Linear，同样的数值会偏；shader 里虽然做了换算，但 Gamma 下
        才是逐像素一致。
     2. 把自写 shader 塞进 Always Included Shaders —— 场景是运行时用代码搭的，
        材质也是 new 出来的，打包器扫不到这个 shader，出包后会静默变成洋红。
     3. 生成 Assets/Scenes/Main.unity 并填进 Build Settings。 */
public static class BuildTool {

    const string SCENE_PATH = "Assets/Scenes/Main.unity";

    [MenuItem("查手机/整备工程（色彩空间 + shader + 场景）")]
    public static void Setup() {
        SetColorSpace();
        EnsureShader();
        MakeScene();
        AssetDatabase.SaveAssets();
        Debug.Log("[BuildTool] 工程整备完成：" + SCENE_PATH);
    }

    static void SetColorSpace() {
        if (PlayerSettings.colorSpace != ColorSpace.Gamma) {
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            Debug.Log("[BuildTool] 色彩空间 -> Gamma");
        }
        PlayerSettings.productName = "BattleGame";
        PlayerSettings.companyName = "Hylyre";
        PlayerSettings.defaultScreenWidth = 960;
        PlayerSettings.defaultScreenHeight = 1334;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
    }

    /* 角色层改走预渲染帧之后，运行时只剩顶点色这一个自定义 shader 了。
       它只被代码 Shader.Find 引用，没有任何资源指向它，不登记进
       Always Included 就会在打包时被剥掉，编辑器里正常、exe 里全是粉红。 */
    static void EnsureShader() {
        var sh = Shader.Find(Gfx.VERTEX_SHADER);
        if (sh == null) { Debug.LogWarning("[BuildTool] 找不到 shader，编译过了吗？"); return; }
        var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
        if (gs == null) { Debug.LogWarning("[BuildTool] 读不到 GraphicsSettings.asset"); return; }
        var so = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        for (int i = 0; i < arr.arraySize; i++)
            if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) return;
        arr.InsertArrayElementAtIndex(arr.arraySize);
        arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
        so.ApplyModifiedProperties();
        Debug.Log("[BuildTool] Always Included Shaders += " + sh.name);
    }

    static void MakeScene() {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 667f;
        cam.transform.position = new Vector3(480f, -667f, -100f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.047f, 0.055f, 0.071f, 1f);

        var go = new GameObject("Director");
        go.AddComponent<Director>();

        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SCENE_PATH, true) };
    }

    /// -batchmode -executeMethod Chashouji.EditorTools.BuildTool.BuildWindows
    public static void BuildWindows() {
        Setup();
        string root = Directory.GetParent(Application.dataPath).FullName;
        string outDir = Path.Combine(root, "Build");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(root, "log"));

        var opt = new BuildPlayerOptions {
            scenes = new[] { SCENE_PATH },
            locationPathName = Path.Combine(outDir, "BattleGame.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        var rep = BuildPipeline.BuildPlayer(opt);
        var sum = rep.summary;
        string msg = $"[BuildTool] 出包 {sum.result}  {sum.totalSize / 1024 / 1024}MB  用时 {sum.totalTime}";
        File.AppendAllText(Path.Combine(root, "log", "build_result.log"),
                           DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + "\n");
        Debug.Log(msg);
        if (sum.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    /// 只编译 + 整备，不出包，给 CI/远程校验用
    public static void Verify() {
        Setup();
        Debug.Log("[BuildTool] Verify 完成");
    }
}
}
