
// SanGuoXEngine M1 capture: every hero mesh alone as a flat magenta silhouette in Unity, for the
// GPU-skinning comparison with XEngine (docs/evidence/m1/heroes.md).
//
// SkinnedMeshRenderer.BakeMesh lands on the first influence alone for every vertex whose weights
// matter (probe_bake_weights.csx), so the CPU bake cannot tell which weights the renderer really
// skins with. A silhouette of the rendered mesh can: it is compared with projections of both
// readings of the weights. Each mesh is drawn with Unity's "Unlit/Color" (back faces culled), the
// other renderers of the hero are switched off, and the camera is the one of capture_heroes.csx.
// The scene is a temporary one; nothing in the project is modified.
int Width = 512;
int Height = 768;
string[] heroes = new string[] { "Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu" };
UnityEngine.Color Background = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);

if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
    __P("captured", __B(false));
    __P("reason", __S("the editor is in (or entering) play mode"));
    __END();
    return __sb.ToString();
}
// The camera of every hero, read out of the hero capture's shots.json (written by capture_heroes.csx).
string shotsText = System.IO.File.ReadAllText(System.IO.Path.Combine("__OUT_DIR__", "..", "heroes", "shots.json"));
System.Collections.Generic.List<object> shots = new System.Collections.Generic.List<object>();
foreach (System.Text.RegularExpressions.Match shot in System.Text.RegularExpressions.Regex.Matches(shotsText,
    "\"name\":\"(?<name>[^\"]+)\".*?\"cameraPosition\":\\[(?<p>[^\\]]*)\\],\"cameraRotation\":\\[(?<q>[^\\]]*)\\]",
    System.Text.RegularExpressions.RegexOptions.Singleline)) {
    shots.Add(new System.Collections.Generic.Dictionary<string, object> {
        { "name", shot.Groups["name"].Value }, { "p", shot.Groups["p"].Value }, { "q", shot.Groups["q"].Value },
    });
}
float[] Numbers(string list) {
    string[] parts = list.Split(',');
    float[] values = new float[parts.Length];
    for (int i = 0; i < parts.Length; i++) values[i] = float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
    return values;
}
if (shots.Count == 0) {
    __P("captured", __B(false));
    __P("reason", __S("no camera found in shots.json of the Unity hero capture"));
    __END();
    return __sb.ToString();
}
int openCount = UnityEditor.SceneManagement.EditorSceneManager.sceneCount;
string[] previous = new string[openCount];
bool anyDirty = false;
for (int i = 0; i < openCount; i++) {
    UnityEngine.SceneManagement.Scene open = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
    previous[i] = open.path;
    if (open.isDirty) anyDirty = true;
}
if (anyDirty) {
    __P("captured", __B(false));
    __P("reason", __S("an open scene has unsaved changes; the editor was left untouched"));
    __END();
    return __sb.ToString();
}

string outDir = "__OUT_DIR__";
System.IO.Directory.CreateDirectory(outDir);
int captured = 0;
string failure = null;

// The quality level decides how many bones skin a vertex (jws2's Fastest level: one, Fast: two,
// every other: four). The engine skins with four, so the frames are taken at the lowest level that
// does too, as capture_heroes.csx does; the level open before is put back afterwards.
int activeQuality = UnityEngine.QualitySettings.GetQualityLevel();
int captureQuality = activeQuality;
for (int level = 0; level < UnityEngine.QualitySettings.names.Length; level++) {
    UnityEngine.QualitySettings.SetQualityLevel(level, false);
    if (UnityEngine.QualitySettings.skinWeights.ToString() == "FourBones") { captureQuality = level; break; }
}
UnityEngine.QualitySettings.SetQualityLevel(captureQuality, false);
string qualitySkinWeights = UnityEngine.QualitySettings.skinWeights.ToString();

try {
    UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
        UnityEditor.SceneManagement.NewSceneMode.Single);
    UnityEngine.GameObject cameraObject = new UnityEngine.GameObject("Silhouette Camera");
    UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
    camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    camera.backgroundColor = Background;
    // The vertical field of view of the hero capture, so the frames share their framing.
    System.Text.RegularExpressions.Match fov = System.Text.RegularExpressions.Regex.Match(shotsText, "\"verticalFov\":(?<fov>[0-9.]+)");
    camera.fieldOfView = fov.Success ? float.Parse(fov.Groups["fov"].Value, System.Globalization.CultureInfo.InvariantCulture) : 30f;
    camera.nearClipPlane = 0.05f;
    camera.farClipPlane = 100f;
    camera.allowMSAA = false;
    UnityEngine.RenderTexture target = new UnityEngine.RenderTexture(Width, Height, 24,
        UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
    UnityEngine.Texture2D frame = new UnityEngine.Texture2D(Width, Height, UnityEngine.TextureFormat.RGB24, false, false);
    UnityEngine.Material silhouette = new UnityEngine.Material(UnityEngine.Shader.Find("Unlit/Color"));
    silhouette.name = "Silhouette (temporary)";
    silhouette.color = new UnityEngine.Color(1f, 0f, 1f, 1f);

    foreach (System.Collections.Generic.Dictionary<string, object> shot in shots) {
        string shotName = (string)shot["name"];
        float[] cameraPosition = Numbers((string)shot["p"]);
        float[] cameraRotation = Numbers((string)shot["q"]);
        UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
            "Assets/Actor/Hero/" + shotName + "/" + shotName + ".prefab");
        if (prefab == null) continue;
        UnityEngine.GameObject instance = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
        try {
            UnityEngine.Renderer[] renderers = instance.GetComponentsInChildren<UnityEngine.Renderer>(true);
            string directory = System.IO.Path.Combine(outDir, shotName);
            System.IO.Directory.CreateDirectory(directory);
            foreach (UnityEngine.Renderer subject in renderers) {
                if (!(subject is UnityEngine.SkinnedMeshRenderer)) continue;
                UnityEngine.Material[][] saved = new UnityEngine.Material[renderers.Length][];
                for (int i = 0; i < renderers.Length; i++) {
                    saved[i] = renderers[i].sharedMaterials;
                    renderers[i].sharedMaterials = new UnityEngine.Material[saved[i].Length];
                    for (int s = 0; s < saved[i].Length; s++) renderers[i].sharedMaterials[s] = silhouette;
                    renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderers[i].enabled = ReferenceEquals(renderers[i], subject);
                }
                camera.transform.position = new UnityEngine.Vector3(cameraPosition[0], cameraPosition[1], cameraPosition[2]);
                camera.transform.rotation = new UnityEngine.Quaternion(cameraRotation[0], cameraRotation[1], cameraRotation[2], cameraRotation[3]);
                camera.targetTexture = target;
                camera.Render();
                UnityEngine.RenderTexture.active = target;
                frame.ReadPixels(new UnityEngine.Rect(0, 0, Width, Height), 0, 0);
                frame.Apply();
                UnityEngine.RenderTexture.active = null;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, subject.name + ".png"), frame.EncodeToPNG());
                captured++;
                for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterials = saved[i];
            }
        } finally {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }
    camera.targetTexture = null;
    UnityEngine.Object.DestroyImmediate(frame);
    target.Release();
    UnityEngine.Object.DestroyImmediate(target);
    UnityEngine.Object.DestroyImmediate(silhouette);
} catch (System.Exception e) {
    failure = e.GetType().FullName + ": " + e.Message + "\n" + e.StackTrace;
} finally {
    UnityEngine.QualitySettings.SetQualityLevel(activeQuality, false);
    try {
        bool restored = false;
        for (int i = 0; i < previous.Length; i++) {
            if (string.IsNullOrEmpty(previous[i])) continue;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previous[i],
                restored ? UnityEditor.SceneManagement.OpenSceneMode.Additive : UnityEditor.SceneManagement.OpenSceneMode.Single);
            restored = true;
        }
        if (!restored)
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
    } catch (System.Exception e) {
        if (failure == null) failure = "restore: " + e.Message;
    }
}

__sb.Append('{');
__P("captured", captured.ToString());
__P("qualityLevel", __S(UnityEngine.QualitySettings.names[captureQuality]));
__P("qualitySkinWeights", __S(qualitySkinWeights));
if (failure != null) __P("captureError", __S(failure));
__P("activeSceneDirtyAfterRestore", __B(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty));
__END();
return __sb.ToString();
