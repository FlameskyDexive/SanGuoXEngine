
// SanGuoXEngine M0 probe 5/5: inventory of the 桃园 runtime scene.
// The scene is opened WITHOUT saving and the scenes that were open before are restored. If any open
// scene has unsaved changes the script does nothing, so the editor is left exactly as it was found.
// The full inventory is written to scene.json; the returned document is only a summary.
string TPath(UnityEngine.Transform t) {
    string p = t != null ? t.name : "";
    while (t != null && t.parent != null) {
        t = t.parent;
        p = t.name + "/" + p;
    }
    return p;
}
void Inc(System.Collections.Generic.Dictionary<string, int> counts, string key) {
    int value;
    counts.TryGetValue(key, out value);
    counts[key] = value + 1;
}
string Dict(System.Collections.Generic.Dictionary<string, int> counts) {
    // Sorted by count, descending, so the dominant entries come first.
    System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> rows =
        new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(counts);
    rows.Sort(delegate(System.Collections.Generic.KeyValuePair<string, int> a, System.Collections.Generic.KeyValuePair<string, int> b) {
        int byCount = b.Value.CompareTo(a.Value);
        return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
    });
    System.Text.StringBuilder b2 = new System.Text.StringBuilder();
    b2.Append('{');
    foreach (System.Collections.Generic.KeyValuePair<string, int> row in rows) {
        b2.Append(__S(row.Key)).Append(':').Append(row.Value.ToString()).Append(',');
    }
    if (b2.Length > 0 && b2[b2.Length - 1] == ',') b2.Length--;
    b2.Append('}');
    return b2.ToString();
}
void MinMax(ref UnityEngine.Vector3 min, ref UnityEngine.Vector3 max, UnityEngine.Vector3 v) {
    if (v.x < min.x) min.x = v.x;
    if (v.y < min.y) min.y = v.y;
    if (v.z < min.z) min.z = v.z;
    if (v.x > max.x) max.x = v.x;
    if (v.y > max.y) max.y = v.y;
    if (v.z > max.z) max.z = v.z;
}
string Describe(System.Exception e) {
    return e == null ? "" : e.GetType().FullName + ": " + e.Message + "\n" + e.StackTrace;
}

__sb.Append('{');
string scenePath = "Assets/Scenes/GameScenes/C-ZhuoXian_TaoYuan.unity";
__P("scenePath", __S(scenePath));

if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
    __P("opened", __B(false));
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
    __P("opened", __B(false));
    __P("reason", __S("an open scene has unsaved changes; the editor was left untouched"));
    __END();
    return __sb.ToString();
}

System.DateTime started = System.DateTime.UtcNow;
string collectError = null;
string restoreError = null;
bool collected = false;
int rootCount = 0;
int gameObjectCount = 0;
int prefabInstanceObjectCount = 0;
int lightmappedRendererCount = 0;
int lightmapColorCount = UnityEngine.LightmapSettings.lightmaps.Length;

UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
try {
    UnityEngine.GameObject[] roots = scene.GetRootGameObjects();
    rootCount = roots.Length;

    int inactiveGameObjects = 0;
    int missingScripts = 0;
    UnityEngine.Vector3 min = new UnityEngine.Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    UnityEngine.Vector3 max = new UnityEngine.Vector3(float.MinValue, float.MinValue, float.MinValue);
    bool boundsValid = false;

    System.Collections.Generic.Dictionary<string, int> componentTypes = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, int> rendererTypes = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, int> colliderTypes = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, int> lightTypes = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, int> meshSources = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, int> materials = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, int> shaders = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, string> sceneConfigJson = new System.Collections.Generic.Dictionary<string, string>();

    System.Text.StringBuilder lodBuilder = new System.Text.StringBuilder();
    lodBuilder.Append('[');
    System.Text.StringBuilder lightBuilder = new System.Text.StringBuilder();
    lightBuilder.Append('[');

    foreach (UnityEngine.GameObject root in roots) {
        UnityEngine.Transform[] transforms = root.GetComponentsInChildren<UnityEngine.Transform>(true);
        foreach (UnityEngine.Transform t in transforms) {
            UnityEngine.GameObject go = t.gameObject;
            gameObjectCount++;
            if (!go.activeInHierarchy) inactiveGameObjects++;
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(go)) prefabInstanceObjectCount++;

            UnityEngine.Component[] components = go.GetComponents<UnityEngine.Component>();
            for (int c = 0; c < components.Length; c++) {
                UnityEngine.Component component = components[c];
                if (component == null) {
                    missingScripts++;
                    continue;
                }
                string typeName = component.GetType().FullName;
                Inc(componentTypes, typeName);

                UnityEngine.MeshFilter filter = component as UnityEngine.MeshFilter;
                if (filter != null && filter.sharedMesh != null) {
                    string meshPath = UnityEditor.AssetDatabase.GetAssetPath(filter.sharedMesh);
                    Inc(meshSources, (string.IsNullOrEmpty(meshPath) ? "<builtin>" : meshPath) + "|" + filter.sharedMesh.name);
                }

                UnityEngine.Renderer renderer = component as UnityEngine.Renderer;
                if (renderer != null) {
                    Inc(rendererTypes, component.GetType().Name);
                    if (renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 0xFFFE) lightmappedRendererCount++;
                    UnityEngine.Bounds bounds = renderer.bounds;
                    MinMax(ref min, ref max, bounds.min);
                    MinMax(ref min, ref max, bounds.max);
                    boundsValid = true;

                    UnityEngine.Material[] shared = renderer.sharedMaterials;
                    for (int m = 0; m < shared.Length; m++) {
                        UnityEngine.Material material = shared[m];
                        if (material == null) {
                            Inc(materials, "<missing>");
                            continue;
                        }
                        string materialPath = UnityEditor.AssetDatabase.GetAssetPath(material);
                        Inc(materials, (string.IsNullOrEmpty(materialPath) ? "<builtin>" : materialPath) + "|" + material.name);
                        if (material.shader != null) Inc(shaders, material.shader.name);
                    }
                }

                UnityEngine.LODGroup lodGroup = component as UnityEngine.LODGroup;
                if (lodGroup != null) {
                    UnityEngine.LOD[] lods = lodGroup.GetLODs();
                    lodBuilder.Append("{\"path\":").Append(__S(TPath(lodGroup.transform)))
                        .Append(",\"position\":").Append(__V3(lodGroup.transform.position)).Append(",\"lods\":[");
                    foreach (UnityEngine.LOD lod in lods) {
                        lodBuilder.Append("{\"screenRelativeTransitionHeight\":").Append(__F(lod.screenRelativeTransitionHeight))
                            .Append(",\"renderers\":").Append(lod.renderers.Length.ToString()).Append("},");
                    }
                    if (lodBuilder[lodBuilder.Length - 1] == ',') lodBuilder.Length--;
                    lodBuilder.Append("]},");
                }

                UnityEngine.Light light = component as UnityEngine.Light;
                if (light != null) {
                    Inc(lightTypes, light.type.ToString());
                    lightBuilder.Append("{\"path\":").Append(__S(TPath(light.transform)))
                        .Append(",\"type\":").Append(__S(light.type.ToString()))
                        .Append(",\"color\":").Append(__C(light.color))
                        .Append(",\"intensity\":").Append(__F(light.intensity))
                        .Append(",\"shadows\":").Append(__S(light.shadows.ToString()))
                        .Append(",\"renderMode\":").Append(__S(light.renderMode.ToString()))
                        .Append(",\"range\":").Append(__F(light.range))
                        .Append(",\"spotAngle\":").Append(__F(light.spotAngle))
                        .Append(",\"cullingMask\":").Append(light.cullingMask.ToString())
                        .Append(",\"bakeType\":").Append(__S(light.lightmapBakeType.ToString()))
                        .Append(",\"position\":").Append(__V3(light.transform.position))
                        .Append(",\"forward\":").Append(__V3(light.transform.forward))
                        .Append(",\"eulerAngles\":").Append(__V3(light.transform.eulerAngles))
                        .Append("},");
                }

                UnityEngine.Collider collider = component as UnityEngine.Collider;
                if (collider != null) Inc(colliderTypes, component.GetType().Name);

                // The scene's own configuration components hold the sky / LUT presets.
                if (typeName.IndexOf("SceneDifferentParam", System.StringComparison.Ordinal) >= 0
                    || typeName.IndexOf("GlobalParamSetter", System.StringComparison.Ordinal) >= 0
                    || typeName.IndexOf("SceneDetailManager", System.StringComparison.Ordinal) >= 0) {
                    if (!sceneConfigJson.ContainsKey(typeName)) {
                        string json = UnityEditor.EditorJsonUtility.ToJson(component);
                        if (json.Length > 60000) json = json.Substring(0, 60000) + "...";
                        sceneConfigJson[typeName] = TPath(t) + "\n" + json;
                    }
                }
            }
        }
    }
    if (lodBuilder[lodBuilder.Length - 1] == ',') lodBuilder.Length--;
    lodBuilder.Append(']');
    if (lightBuilder[lightBuilder.Length - 1] == ',') lightBuilder.Length--;
    lightBuilder.Append(']');

    // The root objects, as a first look at how the scene is organised.
    System.Text.StringBuilder rootBuilder = new System.Text.StringBuilder();
    rootBuilder.Append('[');
    int rootsShown = roots.Length < 80 ? roots.Length : 80;
    for (int i = 0; i < rootsShown; i++) {
        UnityEngine.Transform t = roots[i].transform;
        UnityEngine.Component[] components = t.GetComponents<UnityEngine.Component>();
        rootBuilder.Append("{\"name\":").Append(__S(t.name))
            .Append(",\"active\":").Append(__B(t.gameObject.activeSelf))
            .Append(",\"children\":").Append(t.childCount.ToString())
            .Append(",\"position\":").Append(__V3(t.localPosition))
            .Append(",\"eulerAngles\":").Append(__V3(t.localEulerAngles))
            .Append(",\"scale\":").Append(__V3(t.localScale))
            .Append(",\"components\":[");
        int shown = components.Length < 8 ? components.Length : 8;
        for (int c = 0; c < shown; c++) {
            if (c > 0) rootBuilder.Append(',');
            UnityEngine.Component component = components[c];
            rootBuilder.Append(component == null ? __S("<missing script>") : __S(component.GetType().Name));
        }
        rootBuilder.Append("]},");
    }
    if (rootBuilder[rootBuilder.Length - 1] == ',') rootBuilder.Length--;
    rootBuilder.Append(']');

    UnityEngine.Terrain[] terrains = UnityEngine.Object.FindObjectsOfType<UnityEngine.Terrain>();

    __P("scenePath", __S(scenePath));
    __P("isLoaded", __B(scene.isLoaded));
    __P("rootCount", rootCount.ToString());
    __P("gameObjectCount", gameObjectCount.ToString());
    __P("inactiveGameObjectCount", inactiveGameObjects.ToString());
    __P("prefabInstanceObjectCount", prefabInstanceObjectCount.ToString());
    __P("missingScriptCount", missingScripts.ToString());
    __P("lightmappedRendererCount", lightmappedRendererCount.ToString());
    __P("terrainCount", terrains.Length.ToString());
    if (boundsValid) {
        __P("boundsMin", __V3(min));
        __P("boundsMax", __V3(max));
    }

    System.Text.StringBuilder lightmapBuilder = new System.Text.StringBuilder();
    lightmapBuilder.Append('[');
    foreach (UnityEngine.LightmapData data in UnityEngine.LightmapSettings.lightmaps) {
        lightmapBuilder.Append("{\"color\":").Append(__S(data.lightmapColor != null ? UnityEditor.AssetDatabase.GetAssetPath(data.lightmapColor) : null))
            .Append(",\"dir\":").Append(__S(data.lightmapDir != null ? UnityEditor.AssetDatabase.GetAssetPath(data.lightmapDir) : null))
            .Append(",\"shadowMask\":").Append(__S(data.shadowMask != null ? UnityEditor.AssetDatabase.GetAssetPath(data.shadowMask) : null))
            .Append("},");
    }
    if (lightmapBuilder[lightmapBuilder.Length - 1] == ',') lightmapBuilder.Length--;
    lightmapBuilder.Append(']');
    __P("lightmaps", lightmapBuilder.ToString());

    __P("renderSettings", "{\"ambientMode\":" + __S(UnityEngine.RenderSettings.ambientMode.ToString())
        + ",\"ambientSkyColor\":" + __C(UnityEngine.RenderSettings.ambientSkyColor)
        + ",\"ambientEquatorColor\":" + __C(UnityEngine.RenderSettings.ambientEquatorColor)
        + ",\"ambientGroundColor\":" + __C(UnityEngine.RenderSettings.ambientGroundColor)
        + ",\"ambientIntensity\":" + __F(UnityEngine.RenderSettings.ambientIntensity)
        + ",\"reflectionMode\":" + __S(UnityEngine.RenderSettings.defaultReflectionMode.ToString())
        + ",\"customReflectionTexture\":" + __S(UnityEngine.RenderSettings.customReflectionTexture != null ? UnityEditor.AssetDatabase.GetAssetPath(UnityEngine.RenderSettings.customReflectionTexture) : null)
        + ",\"reflectionIntensity\":" + __F(UnityEngine.RenderSettings.reflectionIntensity)
        + ",\"skybox\":" + __S(UnityEngine.RenderSettings.skybox != null ? UnityEditor.AssetDatabase.GetAssetPath(UnityEngine.RenderSettings.skybox) : null)
        + ",\"sun\":" + __S(UnityEngine.RenderSettings.sun != null ? TPath(UnityEngine.RenderSettings.sun.transform) : null)
        + ",\"fog\":" + __B(UnityEngine.RenderSettings.fog)
        + ",\"fogMode\":" + __S(UnityEngine.RenderSettings.fogMode.ToString())
        + ",\"fogColor\":" + __C(UnityEngine.RenderSettings.fogColor)
        + ",\"fogStartDistance\":" + __F(UnityEngine.RenderSettings.fogStartDistance)
        + ",\"fogEndDistance\":" + __F(UnityEngine.RenderSettings.fogEndDistance)
        + "}");

    __P("componentTypes", Dict(componentTypes));
    __P("rendererTypes", Dict(rendererTypes));
    __P("colliderTypes", Dict(colliderTypes));
    __P("lightTypes", Dict(lightTypes));
    __P("meshSources", Dict(meshSources));
    __P("materials", Dict(materials));
    __P("shaders", Dict(shaders));
    __P("lodGroups", lodBuilder.ToString());
    __P("lights", lightBuilder.ToString());
    __P("roots", rootBuilder.ToString());

    __sb.Append("\"sceneConfig\":{");
    foreach (System.Collections.Generic.KeyValuePair<string, string> entry in sceneConfigJson) {
        __sb.Append(__S(entry.Key)).Append(':').Append(__S(entry.Value)).Append(',');
    }
    if (__sb[__sb.Length - 1] == ',') __sb.Length--;
    __sb.Append("},");
    __END();

    string dir = "__OUT_DIR__";
    System.IO.Directory.CreateDirectory(dir);
    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "scene.json"), __sb.ToString());
    collected = true;
} catch (System.Exception e) {
    collectError = Describe(e);
} finally {
    // Put the editor back the way it was found.
    try {
        bool restored = false;
        for (int i = 0; i < previous.Length; i++) {
            if (string.IsNullOrEmpty(previous[i])) continue;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previous[i],
                restored ? UnityEditor.SceneManagement.OpenSceneMode.Additive : UnityEditor.SceneManagement.OpenSceneMode.Single);
            restored = true;
        }
        if (!restored) {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        }
    } catch (System.Exception e) {
        restoreError = Describe(e);
    }
}

// Summary document: the caller stores this next to scene.json.
__sb.Length = 0;
__sb.Append('{');
__P("file", __S("scene.json"));
__P("scenePath", __S(scenePath));
__P("collected", __B(collected));
__P("rootCount", rootCount.ToString());
__P("gameObjectCount", gameObjectCount.ToString());
__P("prefabInstanceObjectCount", prefabInstanceObjectCount.ToString());
__P("lightmappedRendererCount", lightmappedRendererCount.ToString());
__P("lightmapColorCount", lightmapColorCount.ToString());
__P("elapsedMs", ((long)(System.DateTime.UtcNow - started).TotalMilliseconds).ToString());
if (collectError != null) __P("collectError", __S(collectError));
if (restoreError != null) __P("restoreError", __S(restoreError));
__P("activeSceneAfterRestore", __S(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path));
__P("activeSceneDirtyAfterRestore", __B(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty));
__END();
return __sb.ToString();
