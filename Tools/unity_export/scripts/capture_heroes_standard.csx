
// SanGuoXEngine M1 capture, second reference: the five heroes with Unity's own Standard shader.
//
// The port maps jws2's character shaders to XEngine's Default/Standard. The game's shaders are not
// Standard (they add a per-material reflection cubemap, rim light, fake skin), so a frame of the
// game look cannot tell whether the material conversion is right. This capture swaps every hero
// material for Unity's built-in "Standard" shader fed with the textures the port derived its maps
// from:
//
//   _MainTex          <- _MainTex
//   _BumpMap          <- _BumpMap
//   _MetallicGlossMap <- _MetallicGlossMap     R metallic, A smoothness: the texture already is
//                                               in Unity Standard's packing
//   _OcclusionMap     <- _MetallicGlossMap     G: also already in place
//
// Emission is left out. Materials Unity alpha-tests are switched to Standard's Cutout mode with the
// same cutoff. Light, ambient, backdrop and framing are the same as in capture_heroes.csx, so the
// frames line up with both earlier sets. The scene is a temporary one and the materials are created
// in memory; nothing in the project is modified.
int Width = 512;
int Height = 768;
float VerticalFov = 30f;
float Fill = 0.88f;
float CameraYaw = 30f * Mathf.Deg2Rad;
float CameraPitch = 8f * Mathf.Deg2Rad;
UnityEngine.Vector3 LightEuler = new UnityEngine.Vector3(50f, 145f, 0f);
string[] heroes = new string[] { "Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu" };
UnityEngine.Shader standard = UnityEngine.Shader.Find("Standard");

UnityEngine.Material ToStandard(UnityEngine.Material source, System.Collections.Generic.List<UnityEngine.Material> created) {
    if (source == null) return null;
    UnityEngine.Material material = new UnityEngine.Material(standard);
    material.name = source.name + " (Standard)";
    material.SetTexture("_MainTex", source.GetTexture("_MainTex"));
    material.SetTextureScale("_MainTex", source.GetTextureScale("_MainTex"));
    material.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));
    material.SetColor("_Color", source.HasProperty("_Color") ? source.GetColor("_Color") : UnityEngine.Color.white);
    UnityEngine.Texture normalMap = source.GetTexture("_BumpMap");
    if (normalMap != null) {
        material.SetTexture("_BumpMap", normalMap);
        material.EnableKeyword("_NORMALMAP");
    }
    UnityEngine.Texture packed = source.GetTexture("_MetallicGlossMap");
    if (packed != null) {
        material.SetTexture("_MetallicGlossMap", packed);
        material.SetTexture("_OcclusionMap", packed);
        material.EnableKeyword("_METALLICGLOSSMAP");
        material.EnableKeyword("_OCCLUSIONMAP");
        material.SetFloat("_GlossMapScale", 1f);
    }
    // jws2 alpha-tests either through the keyword or, for the hair shader, always.
    bool alphaTest = Array.IndexOf<string>(source.shaderKeywords, "_ALPHATEST_ON") >= 0
        || source.shader.name.EndsWith("S_Standard_Hair");
    if (alphaTest) {
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetFloat("_Mode", 1f);
        material.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
        material.SetFloat("_ZWrite", 1f);
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
    }
    created.Add(material);
    return material;
}

if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
    __P("captured", __B(false));
    __P("reason", __S("the editor is in (or entering) play mode"));
    __END();
    return __sb.ToString();
}
if (standard == null) {
    __P("captured", __B(false));
    __P("reason", __S("Unity's built-in Standard shader was not found"));
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
// every other: four). The engine skins with four, so the frames are taken at the lowest level that
// does too, as capture_heroes.csx does; the level open before is put back afterwards.
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
    UnityEngine.RenderSettings.ambientLight = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
    UnityEngine.RenderSettings.skybox = null;
    UnityEngine.RenderSettings.fog = false;
    UnityEngine.RenderSettings.sun = light;

    UnityEngine.GameObject cameraObject = new UnityEngine.GameObject("Capture Camera");
    UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
    camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    camera.backgroundColor = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
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
        UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Actor/Hero/" + hero + "/" + hero + ".prefab");
        if (prefab == null) {
            shots.Add(new System.Collections.Generic.Dictionary<string, object> { { "name", hero }, { "captured", false } });
            continue;
        }
        UnityEngine.GameObject instance = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
        System.Collections.Generic.List<UnityEngine.Material> created = new System.Collections.Generic.List<UnityEngine.Material>();
        try {
            foreach (UnityEngine.Renderer renderer in instance.GetComponentsInChildren<UnityEngine.Renderer>(true)) {
                UnityEngine.Material[] shared = renderer.sharedMaterials;
                UnityEngine.Material[] swapped = new UnityEngine.Material[shared.Length];
                for (int i = 0; i < shared.Length; i++) swapped[i] = ToStandard(shared[i], created);
                renderer.sharedMaterials = swapped;
            }

            UnityEngine.Bounds bounds = default;
            bool first = true;
            foreach (UnityEngine.Renderer renderer in instance.GetComponentsInChildren<UnityEngine.Renderer>(true)) {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            UnityEngine.Vector3 center = bounds.center;
            UnityEngine.Vector3 size = bounds.size;
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
            foreach (UnityEngine.Material material in created) UnityEngine.Object.DestroyImmediate(material);
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
    { "ambientSrgb", new float[] { 0.2f, 0.2f, 0.2f } },
    { "backgroundSrgb", new float[] { 0.2f, 0.2f, 0.2f } },
    { "shots", shots },
};
System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "shots.json"), __J(setup));

int capturedCount = 0;
foreach (object shot in shots) capturedCount++;
__sb.Append('{');
__P("captured", capturedCount.ToString());
__P("file", __S("shots.json"));
if (failure != null) __P("captureError", __S(failure));
__P("activeSceneAfterRestore", __S(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path));
__P("activeSceneDirtyAfterRestore", __B(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty));
__END();
return __sb.ToString();
