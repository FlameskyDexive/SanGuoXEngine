
// SanGuoXEngine M0 probe 1/5: environment and API reachability.
// Read-only apart from one scratch file under the export folder.
__sb.Append('{');

__P("unityVersion", __S(UnityEngine.Application.unityVersion));
__P("dataPath", __S(UnityEngine.Application.dataPath));
__P("product", __S(UnityEngine.Application.productName));

bool isPlaying = false;
bool isCompiling = false;
bool isUpdating = false;
try {
    isPlaying = UnityEditor.EditorApplication.isPlaying;
    isCompiling = UnityEditor.EditorApplication.isCompiling;
    isUpdating = UnityEditor.EditorApplication.isUpdating;
    __P("editorApi", __B(true));
} catch (System.Exception e) {
    __EX("editorApi", e);
}
__P("isPlaying", __B(isPlaying));
__P("isCompiling", __B(isCompiling));
__P("isUpdating", __B(isUpdating));

// The exporters write JSON to the export folder; prove the editor process can.
bool canWrite = false;
try {
    string dir = "__OUT_DIR__";
    System.IO.Directory.CreateDirectory(dir);
    string p = System.IO.Path.Combine(dir, "scratch.txt");
    System.IO.File.WriteAllText(p, "ping");
    canWrite = System.IO.File.ReadAllText(p) == "ping";
    System.IO.File.Delete(p);
} catch (System.Exception e) {
    __EX("canWrite", e);
}
__P("canWrite", __B(canWrite));
__P("outDir", __S("__OUT_DIR__"));

// Compile-time reachability of the APIs the exporters need. A missing reference fails the whole
// script at compile time and the error names the type.
try {
    __P("editorSceneManagerAssembly", __S(typeof(UnityEditor.SceneManagement.EditorSceneManager).Assembly.GetName().Name));
    __P("sceneCount", UnityEditor.SceneManagement.EditorSceneManager.sceneCount.ToString());
    for (int i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++) {
        UnityEngine.SceneManagement.Scene s = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
        __sb.Append("\"openScene").Append(i.ToString()).Append("\":{\"path\":").Append(__S(s.path))
            .Append(",\"dirty\":").Append(__B(s.isDirty)).Append(",\"loaded\":").Append(__B(s.isLoaded)).Append("},");
    }
    UnityEngine.SceneManagement.Scene active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
    __P("activeScene", __S(active.path));
    __P("activeSceneDirty", __B(active.isDirty));
    __P("lightmapsInActiveScene", UnityEngine.LightmapSettings.lightmaps.Length.ToString());
    __P("sceneApi", __B(true));
} catch (System.Exception e) {
    __EX("sceneApi", e);
}

try {
    __sb.Append("\"apiTypes\":[").Append(__S(typeof(UnityEditor.AssetDatabase).FullName))
        .Append(',').Append(__S(typeof(UnityEditor.Animations.AnimatorController).FullName))
        .Append(',').Append(__S(typeof(UnityEditor.AnimationUtility).FullName))
        .Append(',').Append(__S(typeof(UnityEditor.EditorJsonUtility).FullName))
        .Append(',').Append(__S(typeof(UnityEditor.PrefabUtility).FullName))
        .Append(',').Append(__S(typeof(UnityEditor.TextureImporter).FullName))
        .Append(',').Append(__S(typeof(UnityEditor.ShaderUtil).FullName))
        .Append(',').Append(__S(typeof(UnityEngine.LODGroup).FullName))
        .Append(',').Append(__S(typeof(UnityEditor.AnimationMode).FullName))
        .Append("],");
} catch (System.Exception e) {
    __EX("apiTypes", e);
}

// Serialization smoke test on an existing asset (read-only): a hero material.
try {
    UnityEngine.Material mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Actor/Hero/Hero_LvBu/BattleMat/Hero_LvBu_Body.mat");
    if (mat == null) {
        __P("materialFound", __B(false));
    } else {
        __P("materialFound", __B(true));
        string json = UnityEditor.EditorJsonUtility.ToJson(mat);
        __P("materialShader", __S(mat.shader != null ? mat.shader.name : null));
        __P("materialJsonLength", json.Length.ToString());
        __P("materialJsonHasMainTex", __B(json.Contains("_MainTex")));
        __P("materialJsonSample", __S(json.Length > 1200 ? json.Substring(0, 1200) + "..." : json));
    }
} catch (System.Exception e) {
    __EX("material", e);
}

__END();
return __sb.ToString();
