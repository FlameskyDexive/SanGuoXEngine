
// SanGuoXEngine M1 capture: the five heroes under a neutral studio setup in Unity, for the
// side-by-side comparison with XEngine (docs/evidence/m1/heroes.md).
//
// A temporary empty scene is rendered into a RenderTexture and saved as PNG; no post-processing
// runs (no tonemapping), so the frames show the game's own shaders under plain lighting. The
// camera and light setup of every frame is written to shots.json, which the XEngine side reads to
// reproduce the same framing. The frames are taken at the project's lowest quality level that skins
// with four bones per vertex, as the engine does (jws2's active "Fastest" level skins with one).
//
// The editor is left as it was found: the scenes open before are restored, and the script refuses
// to run when an open scene has unsaved changes or the editor is in play mode.
int Width = 512;
int Height = 768;
float VerticalFov = 30f;
float Fill = 0.88f;                                   // share of the frame's height the hero takes
float CameraYaw = 30f * Mathf.Deg2Rad;               // from the front (+Z), towards +X
float CameraPitch = 8f * Mathf.Deg2Rad;             // looking slightly down
UnityEngine.Vector3 LightEuler = new UnityEngine.Vector3(50f, 145f, 0f);   // from the front-left, above
UnityEngine.Color Ambient = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);   // flat ambient (sRGB-authored)
UnityEngine.Color Background = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
string[] heroes = new string[] { "Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu" };

__sb.Append('{');
if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
    __P("captured", __B(false));
    __P("reason", __S("the editor is in (or entering) play mode"));
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
System.Collections.Generic.List<object> shots = new System.Collections.Generic.List<object>();
string failure = null;

// The quality level decides how many bones skin a vertex (jws2's Fastest level: one, Fast: two,
// every other: four), and it also decides what the game's shaders do. The engine skins with four,
// so the frames are taken at the lowest level that does too; the level open before is put back
// afterwards. Nothing is saved to the project.
int activeQuality = UnityEngine.QualitySettings.GetQualityLevel();
int captureQuality = activeQuality;
for (int level = 0; level < UnityEngine.QualitySettings.names.Length; level++) {
    UnityEngine.QualitySettings.SetQualityLevel(level, false);
    if (UnityEngine.QualitySettings.skinWeights.ToString() == "FourBones") { captureQuality = level; break; }
}
UnityEngine.QualitySettings.SetQualityLevel(captureQuality, false);
string qualityName = UnityEngine.QualitySettings.names[captureQuality];
string qualitySkinWeights = UnityEngine.QualitySettings.skinWeights.ToString();

UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
try {
    UnityEngine.GameObject lightObject = new UnityEngine.GameObject("Key Light");
    UnityEngine.Light light = lightObject.AddComponent<UnityEngine.Light>();
    light.type = UnityEngine.LightType.Directional;
    light.color = UnityEngine.Color.white;
    light.intensity = 1f;
    light.shadows = UnityEngine.LightShadows.None;
    lightObject.transform.eulerAngles = LightEuler;
    UnityEngine.RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
    UnityEngine.RenderSettings.ambientLight = Ambient;
    UnityEngine.RenderSettings.skybox = null;
    UnityEngine.RenderSettings.fog = false;
    UnityEngine.RenderSettings.sun = light;

    UnityEngine.GameObject cameraObject = new UnityEngine.GameObject("Capture Camera");
    UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
    camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    camera.backgroundColor = Background;
    camera.fieldOfView = VerticalFov;
    camera.nearClipPlane = 0.05f;
    camera.farClipPlane = 100f;
    camera.allowMSAA = false;

    UnityEngine.RenderTexture target = new UnityEngine.RenderTexture(Width, Height, 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
    UnityEngine.Texture2D frame = new UnityEngine.Texture2D(Width, Height, UnityEngine.TextureFormat.RGB24, false, false);
    UnityEngine.Vector3 direction = new UnityEngine.Vector3(Mathf.Sin(CameraYaw) * Mathf.Cos(CameraPitch), Mathf.Sin(CameraPitch), Mathf.Cos(CameraYaw) * Mathf.Cos(CameraPitch));
    float halfHeight = Mathf.Tan(VerticalFov * 0.5f * Mathf.Deg2Rad);
    float aspect = (float)Width / Height;
    foreach (string hero in heroes) {
        string prefabPath = "Assets/Actor/Hero/" + hero + "/" + hero + ".prefab";
        UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
        if (prefab == null) {
            shots.Add(new System.Collections.Generic.Dictionary<string, object> { { "name", hero }, { "captured", false } });
            continue;
        }
        UnityEngine.GameObject instance = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
        try {
            UnityEngine.Bounds bounds = default;
            bool first = true;
            foreach (UnityEngine.Renderer renderer in instance.GetComponentsInChildren<UnityEngine.Renderer>(true)) {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            UnityEngine.Vector3 center = bounds.center;
            UnityEngine.Vector3 size = bounds.size;
            // Far enough that the hero's height fills the frame, and that its widest extent fits the width.
            float distance = Mathf.Max(size.y * 0.5f / (Fill * halfHeight),
                Mathf.Max(size.x, size.z) * 0.5f / (0.9f * halfHeight * aspect));
            UnityEngine.Vector3 position = center + direction * distance;
            camera.transform.position = position;
            camera.transform.LookAt(center, UnityEngine.Vector3.up);
            camera.targetTexture = target;
            camera.Render();
            UnityEngine.RenderTexture.active = target;
            frame.ReadPixels(new UnityEngine.Rect(0, 0, Width, Height), 0, 0);
            frame.Apply();
            UnityEngine.RenderTexture.active = null;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, hero + ".png"), frame.EncodeToPNG());

            shots.Add(new System.Collections.Generic.Dictionary<string, object> {
                { "name", hero },
                { "captured", true },
                { "file", hero + ".png" },
                { "boundsCenter", new float[] { center.x, center.y, center.z } },
                { "boundsSize", new float[] { size.x, size.y, size.z } },
                { "cameraPosition", new float[] { position.x, position.y, position.z } },
                { "cameraRotation", new float[] { camera.transform.rotation.x, camera.transform.rotation.y, camera.transform.rotation.z, camera.transform.rotation.w } },
                { "distance", distance },
            });
        } finally {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }
    camera.targetTexture = null;
    UnityEngine.Object.DestroyImmediate(frame);
    target.Release();
    UnityEngine.Object.DestroyImmediate(target);
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
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
    } catch (System.Exception e) {
        if (failure == null) failure = "restore: " + e.Message;
    }
}

System.Collections.Generic.Dictionary<string, object> setup = new System.Collections.Generic.Dictionary<string, object> {
    { "width", Width }, { "height", Height }, { "verticalFov", VerticalFov },
    { "qualityLevel", qualityName }, { "qualitySkinWeights", qualitySkinWeights },
    { "lightEuler", new float[] { LightEuler.x, LightEuler.y, LightEuler.z } },
    { "lightColor", new float[] { 1f, 1f, 1f } }, { "lightIntensity", 1f },
    { "ambientSrgb", new float[] { Ambient.r, Ambient.g, Ambient.b } },
    { "backgroundSrgb", new float[] { Background.r, Background.g, Background.b } },
    { "colorSpace", UnityEngine.QualitySettings.activeColorSpace.ToString() },
    { "shots", shots },
};
System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "shots.json"), __J(setup));

int capturedCount = 0;
foreach (object shot in shots) capturedCount++;
__P("captured", capturedCount.ToString());
__P("file", __S("shots.json"));
if (failure != null) __P("captureError", __S(failure));
__P("activeSceneAfterRestore", __S(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path));
__P("activeSceneDirtyAfterRestore", __B(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty));
__END();
return __sb.ToString();
