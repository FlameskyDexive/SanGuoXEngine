
// SanGuoXEngine M3 export: the 桃园 runtime scene, fully resolved, plus three reference frames.
// The scene is opened WITHOUT saving and the scenes that were open before are restored; if any
// open scene has unsaved changes the script does nothing.
//
// scene.json carries everything the engine-side builder needs: the flattened hierarchy (every
// transform with its local TRS), each renderer's mesh / materials / shadows / lightmap slot, every
// LOD group with its levels, the colliders, the directional light, the render settings and the
// lightmap atlases. shots.json describes the three Unity reference frames the engine reproduces
// (the same camera, LOD bias and render settings).
int FrameWidth = 960;
int FrameHeight = 540;
float FrameFov = 50f;
string[] FrameNames = new string[] { "close", "mid", "panorama" };
UnityEngine.Vector3[] FramePositions = new UnityEngine.Vector3[] {
    new UnityEngine.Vector3(38f, 179f, 470f),     // close: the prop cluster ahead
    new UnityEngine.Vector3(95f, 196f, 415f),     // mid: across the field
    new UnityEngine.Vector3(300f, 280f, 330f),    // panorama: high over the play area
};
UnityEngine.Vector3[] FrameTargets = new UnityEngine.Vector3[] {
    new UnityEngine.Vector3(55f, 176f, 545f),
    new UnityEngine.Vector3(70f, 180f, 570f),
    new UnityEngine.Vector3(110f, 160f, 590f),
};

string TPath(UnityEngine.Transform t) {
    string p = t.name;
    for (UnityEngine.Transform c = t.parent; c != null; c = c.parent) p = c.name + "/" + p;
    return p;
}
string Q4(UnityEngine.Quaternion q) { return "[" + __F(q.x) + "," + __F(q.y) + "," + __F(q.z) + "," + __F(q.w) + "]"; }
string V3(UnityEngine.Vector3 v) { return "[" + __F(v.x) + "," + __F(v.y) + "," + __F(v.z) + "]"; }
// A Unity asset as the builder addresses it: project path (empty for built-in resources) + name.
string AssetRef(UnityEngine.Object o) {
    if (o == null) return "null";
    string path = UnityEditor.AssetDatabase.GetAssetPath(o);
    return "{\"path\":" + __S(path) + ",\"name\":" + __S(o.name) + "}";
}

__sb.Append('{');
string scenePath = "Assets/Scenes/GameScenes/C-ZhuoXian_TaoYuan.unity";
if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
    __P("exported", __B(false));
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
    __P("exported", __B(false));
    __P("reason", __S("an open scene has unsaved changes; the editor was left untouched"));
    __END();
    return __sb.ToString();
}

System.Text.StringBuilder doc = new System.Text.StringBuilder(1 << 26);
string failure = null;
try {
    UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,
        UnityEditor.SceneManagement.OpenSceneMode.Single);
    UnityEngine.GameObject[] roots = scene.GetRootGameObjects();
    UnityEngine.GameObject root = roots.Length > 0 ? roots[0] : null;
    if (root == null) throw new System.Exception("the scene has no root object");

    UnityEngine.Transform[] nodes = root.GetComponentsInChildren<UnityEngine.Transform>(true);
    System.Collections.Generic.Dictionary<string, UnityEngine.Transform> byPath =
        new System.Collections.Generic.Dictionary<string, UnityEngine.Transform>(nodes.Length * 2);
    for (int i = 0; i < nodes.Length; i++) byPath[TPath(nodes[i])] = nodes[i];

    doc.Append('{');
    doc.Append("\"scenePath\":").Append(__S(scenePath)).Append(",\"lodBias\":").Append(__F(UnityEngine.QualitySettings.lodBias))
        .Append(",\"qualityLevel\":").Append(__S(UnityEngine.QualitySettings.names[UnityEngine.QualitySettings.GetQualityLevel()]))
        .Append(",\"nodeCount\":").Append(nodes.Length).Append(',');

    // ---- render settings --------------------------------------------------------------
    UnityEngine.Rendering.ReflectionProbeUsage reflectionMode;
    doc.Append("\"renderSettings\":{");
    doc.Append("\"ambientMode\":").Append(__S(UnityEngine.RenderSettings.ambientMode.ToString()))
        .Append(",\"ambientSky\":").Append(__C(UnityEngine.RenderSettings.ambientSkyColor))
        .Append(",\"ambientEquator\":").Append(__C(UnityEngine.RenderSettings.ambientEquatorColor))
        .Append(",\"ambientGround\":").Append(__C(UnityEngine.RenderSettings.ambientGroundColor))
        .Append(",\"ambientIntensity\":").Append(__F(UnityEngine.RenderSettings.ambientIntensity));
    doc.Append(",\"defaultReflectionMode\":").Append(__S(UnityEngine.RenderSettings.defaultReflectionMode.ToString()));
    doc.Append(",\"customReflection\":").Append(UnityEngine.RenderSettings.customReflection != null ? AssetRef(UnityEngine.RenderSettings.customReflection) : "null");
    doc.Append(",\"reflectionIntensity\":").Append(__F(UnityEngine.RenderSettings.reflectionIntensity));
    doc.Append(",\"fog\":").Append(__B(UnityEngine.RenderSettings.fog))
        .Append(",\"fogColor\":").Append(__C(UnityEngine.RenderSettings.fogColor))
        .Append(",\"fogMode\":").Append(__S(UnityEngine.RenderSettings.fogMode.ToString()))
        .Append(",\"fogDensity\":").Append(__F(UnityEngine.RenderSettings.fogDensity))
        .Append(",\"fogStart\":").Append(__F(UnityEngine.RenderSettings.fogStartDistance))
        .Append(",\"fogEnd\":").Append(__F(UnityEngine.RenderSettings.fogEndDistance));
    doc.Append(",\"sun\":").Append(__S(UnityEngine.RenderSettings.sun != null ? TPath(UnityEngine.RenderSettings.sun.transform) : null));
    doc.Append('}').Append(',');

    // ---- lightmaps --------------------------------------------------------------------
    doc.Append("\"lightmaps\":[");
    for (int i = 0; i < UnityEngine.LightmapSettings.lightmaps.Length; i++) {
        UnityEngine.LightmapData data = UnityEngine.LightmapSettings.lightmaps[i];
        if (i > 0) doc.Append(',');
        doc.Append("{\"color\":").Append(AssetRef(data.lightmapColor))
            .Append(",\"dir\":").Append(data.lightmapDir != null ? AssetRef(data.lightmapDir) : "null")
            .Append(",\"shadowMask\":").Append(data.shadowMask != null ? AssetRef(data.shadowMask) : "null").Append('}');
    }
    doc.Append("],");

    // ---- lights -----------------------------------------------------------------------
    doc.Append("\"lights\":[");
    UnityEngine.Light[] lights = root.GetComponentsInChildren<UnityEngine.Light>(true);
    for (int i = 0; i < lights.Length; i++) {
        UnityEngine.Light light = lights[i];
        if (i > 0) doc.Append(',');
        doc.Append("{\"path\":").Append(__S(TPath(light.transform)))
            .Append(",\"type\":").Append(__S(light.type.ToString()))
            .Append(",\"color\":").Append(__C(light.color))
            .Append(",\"intensity\":").Append(__F(light.intensity))
            .Append(",\"shadows\":").Append(__S(light.shadows.ToString()))
            .Append(",\"range\":").Append(__F(light.range))
            .Append(",\"spotAngle\":").Append(__F(light.spotAngle))
            .Append(",\"bakeType\":").Append(__S(light.lightmapBakeType.ToString()))
            .Append(",\"position\":").Append(V3(light.transform.position))
            .Append(",\"forward\":").Append(V3(light.transform.forward)).Append('}');
    }
    doc.Append("],");

    // ---- nodes ------------------------------------------------------------------------
    doc.Append("\"nodes\":[");
    int exportedNodes = 0;
    for (int i = 0; i < nodes.Length; i++) {
        UnityEngine.Transform t = nodes[i];
        if (exportedNodes > 0) doc.Append(',');
        doc.Append("{\"path\":").Append(__S(TPath(t)))
            .Append(",\"active\":").Append(__B(t.gameObject.activeSelf))
            .Append(",\"layer\":").Append(t.gameObject.layer)
            .Append(",\"localPosition\":").Append(V3(t.localPosition))
            .Append(",\"localRotation\":").Append(Q4(t.localRotation))
            .Append(",\"localScale\":").Append(V3(t.localScale));

        UnityEditor.StaticEditorFlags staticFlags = UnityEditor.GameObjectUtility.GetStaticEditorFlags(t.gameObject);
        doc.Append(",\"staticBatching\":").Append(__B((staticFlags & UnityEditor.StaticEditorFlags.BatchingStatic) != 0))
            .Append(",\"staticLightmap\":").Append(__B((staticFlags & UnityEditor.StaticEditorFlags.LightmapStatic) != 0));

        UnityEngine.MeshFilter filter = t.GetComponent<UnityEngine.MeshFilter>();
        bool hasRenderer = false;
        if (filter != null && filter.sharedMesh != null) {
            doc.Append(",\"mesh\":").Append(AssetRef(filter.sharedMesh));
        }
        UnityEngine.MeshRenderer meshRenderer = t.GetComponent<UnityEngine.MeshRenderer>();
        if (meshRenderer != null) {
            hasRenderer = true;
            doc.Append(",\"castShadows\":").Append(__S(meshRenderer.shadowCastingMode.ToString()));
            doc.Append(",\"receiveShadows\":").Append(__B(meshRenderer.receiveShadows));
            int lightmapIndex = meshRenderer.lightmapIndex;
            doc.Append(",\"lightmapIndex\":").Append(lightmapIndex);
            if (lightmapIndex >= 0 && lightmapIndex < 65000) {
                UnityEngine.Vector4 scaleOffset = meshRenderer.lightmapScaleOffset;
                doc.Append(",\"lightmapScaleOffset\":[").Append(__F(scaleOffset.x)).Append(',').Append(__F(scaleOffset.y))
                    .Append(',').Append(__F(scaleOffset.z)).Append(',').Append(__F(scaleOffset.w)).Append(']');
            }
            doc.Append(",\"materials\":[");
            UnityEngine.Material[] materials = meshRenderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++) {
                if (m > 0) doc.Append(',');
                UnityEngine.Material material = materials[m];
                if (material == null) { doc.Append("null"); continue; }
                UnityEngine.Shader shader = material.shader;
                doc.Append("{\"path\":").Append(__S(UnityEditor.AssetDatabase.GetAssetPath(material)))
                    .Append(",\"name\":").Append(__S(material.name))
                    .Append(",\"shader\":").Append(shader != null ? __S(shader.name) : "null").Append('}');
            }
            doc.Append(']');
        }
        UnityEngine.ParticleSystemRenderer particleRenderer = t.GetComponent<UnityEngine.ParticleSystemRenderer>();
        if (particleRenderer != null) {
            hasRenderer = true;
            doc.Append(",\"particle\":true");
            doc.Append(",\"materials\":[");
            UnityEngine.Material[] materials = particleRenderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++) {
                if (m > 0) doc.Append(',');
                UnityEngine.Material material = materials[m];
                if (material == null) { doc.Append("null"); continue; }
                doc.Append("{\"path\":").Append(__S(UnityEditor.AssetDatabase.GetAssetPath(material)))
                    .Append(",\"name\":").Append(__S(material.name)).Append('}');
            }
            doc.Append(']');
        }
        UnityEngine.SkinnedMeshRenderer skinned = t.GetComponent<UnityEngine.SkinnedMeshRenderer>();
        if (skinned != null) {
            hasRenderer = true;
            doc.Append(",\"skinned\":true");
        }
        UnityEngine.LODGroup group = t.GetComponent<UnityEngine.LODGroup>();
        if (group != null) {
            UnityEngine.LOD[] lods = group.GetLODs();
            UnityEngine.Vector3 lossy = t.lossyScale;
            float scale = UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(lossy.x), UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(lossy.y), UnityEngine.Mathf.Abs(lossy.z)));
            doc.Append(",\"lodSize\":").Append(__F(group.size))
                .Append(",\"lodWorldSize\":").Append(__F(group.size * scale))
                .Append(",\"lodReference\":").Append(V3(group.localReferencePoint))
                .Append(",\"lods\":[");
            for (int l = 0; l < lods.Length; l++) {
                if (l > 0) doc.Append(',');
                doc.Append("{\"height\":").Append(__F(lods[l].screenRelativeTransitionHeight))
                    .Append(",\"fade\":").Append(__F(lods[l].fadeTransitionWidth))
                    .Append(",\"renderers\":[");
                for (int r = 0; r < lods[l].renderers.Length; r++) {
                    if (r > 0) doc.Append(',');
                    UnityEngine.Renderer lodRenderer = lods[l].renderers[r];
                    doc.Append(lodRenderer != null ? __S(TPath(lodRenderer.transform)) : "null");
                }
                doc.Append("]}");
            }
            doc.Append(']');
        }
        UnityEngine.Collider collider = t.GetComponent<UnityEngine.Collider>();
        if (collider != null) {
            UnityEngine.BoxCollider box = collider as UnityEngine.BoxCollider;
            UnityEngine.SphereCollider sphere = collider as UnityEngine.SphereCollider;
            UnityEngine.MeshCollider meshCollider = collider as UnityEngine.MeshCollider;
            doc.Append(",\"collider\":{\"type\":").Append(__S(collider.GetType().Name))
                .Append(",\"isTrigger\":").Append(__B(collider.isTrigger));
            if (box != null) doc.Append(",\"center\":").Append(V3(box.center)).Append(",\"size\":").Append(V3(box.size));
            if (sphere != null) doc.Append(",\"center\":").Append(V3(sphere.center)).Append(",\"radius\":").Append(__F(sphere.radius));
            if (meshCollider != null && meshCollider.sharedMesh != null) doc.Append(",\"mesh\":").Append(AssetRef(meshCollider.sharedMesh));
            doc.Append('}');
        }
        // Scripted scene components the builder degrades or notes (billboards, effect roots, markers).
        UnityEngine.Component[] components = t.GetComponents<UnityEngine.Component>();
        System.Collections.Generic.List<string> scriptNames = new System.Collections.Generic.List<string>();
        for (int c = 0; c < components.Length; c++) {
            UnityEngine.Component component = components[c];
            if (component == null) continue;
            if (component is UnityEngine.Transform || component is UnityEngine.MeshFilter || component is UnityEngine.MeshRenderer
                || component is UnityEngine.ParticleSystem || component is UnityEngine.ParticleSystemRenderer
                || component is UnityEngine.LODGroup || component is UnityEngine.Collider || component is UnityEngine.Light
                || component is UnityEngine.SkinnedMeshRenderer) continue;
            if (component is UnityEngine.MonoBehaviour) scriptNames.Add(component.GetType().Name);
        }
        if (scriptNames.Count > 0) {
            doc.Append(",\"scripts\":[");
            for (int s = 0; s < scriptNames.Count; s++) { if (s > 0) doc.Append(','); doc.Append(__S(scriptNames[s])); }
            doc.Append(']');
        }
        doc.Append('}');
        exportedNodes++;
    }
    doc.Append("],\"exportedNodeCount\":").Append(exportedNodes).Append(',');

    // ---- reference frames --------------------------------------------------------------
    UnityEngine.GameObject cameraObject = new UnityEngine.GameObject("Sanguo Scene Camera");
    UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
    camera.clearFlags = UnityEngine.CameraClearFlags.Skybox;
    camera.fieldOfView = FrameFov;
    camera.nearClipPlane = 0.3f;
    camera.farClipPlane = 4000f;
    UnityEngine.RenderTexture target = new UnityEngine.RenderTexture(FrameWidth, FrameHeight, 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
    UnityEngine.Texture2D frame = new UnityEngine.Texture2D(FrameWidth, FrameHeight, UnityEngine.TextureFormat.RGB24, false, false);
    camera.targetTexture = target;
    string frameDir = System.IO.Path.Combine("__OUT_DIR__", "frames");
    System.IO.Directory.CreateDirectory(frameDir);
    doc.Append("\"frames\":{");
    for (int i = 0; i < FrameNames.Length; i++) {
        camera.transform.position = FramePositions[i];
        camera.transform.LookAt(FrameTargets[i]);
        // The scene's own LOD bias (0.4) applies; the engine reproduces it from the lodBias record.
        if (i > 0) doc.Append(',');
        doc.Append(__S(FrameNames[i])).Append(":{\"position\":").Append(V3(camera.transform.position))
            .Append(",\"rotation\":").Append(Q4(camera.transform.rotation))
            .Append(",\"target\":").Append(V3(FrameTargets[i]))
            .Append(",\"fov\":").Append(__F(FrameFov))
            .Append(",\"width\":").Append(FrameWidth).Append(",\"height\":").Append(FrameHeight).Append('}');
        // Two renders: the first may skin/cull stale state, the second is the settled frame.
        camera.Render();
        camera.Render();
        UnityEngine.RenderTexture.active = target;
        frame.ReadPixels(new UnityEngine.Rect(0, 0, FrameWidth, FrameHeight), 0, 0);
        frame.Apply();
        UnityEngine.RenderTexture.active = null;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(frameDir, FrameNames[i] + ".png"), frame.EncodeToPNG());
    }
    doc.Append("},\"frameDir\":\"frames\"}");

    string document = "scene.json";
    System.IO.File.WriteAllText(System.IO.Path.Combine("__OUT_DIR__", document), doc.ToString());
    __P("exported", __B(true));
    __P("file", __S(document));
    __P("nodes", exportedNodes.ToString());
    __P("lights", lights.Length.ToString());
    __P("lightmaps", UnityEngine.LightmapSettings.lightmaps.Length.ToString());
} catch (System.Exception e) {
    __EX("export", e);
    __P("exported", __B(false));
} finally {
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
        __EX("restore", e);
    }
}
__END();
return __sb.ToString();
