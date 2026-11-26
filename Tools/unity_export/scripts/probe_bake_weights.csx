
// SanGuoXEngine skin audit: Unity's BakeMesh against two readings of each vertex's weights.
//
// For every hero mesh it counts the multi-influence vertices whose influences do not share one skin
// matrix (the only ones where the weights matter at the rest pose) and says, for each, whether
// BakeMesh landed on the full blend or on the first influence alone:
//   asset      BakeMesh on the prefab asset's renderer
//   instance   BakeMesh on an instance of the prefab in a temporary scene, right after a camera render
// Read-only for the project: the instance lives in a temporary empty scene, the scenes open before
// are restored, and the script refuses to run when a scene has unsaved changes or play mode is on.
string[] heroes = new string[] { "Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu" };

if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
    __P("ran", __B(false));
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
    __P("ran", __B(false));
    __P("reason", __S("an open scene has unsaved changes; the editor was left untouched"));
    __END();
    return __sb.ToString();
}

System.Collections.Generic.List<object> rows = new System.Collections.Generic.List<object>();
string failure = null;
try {
    UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
        UnityEditor.SceneManagement.NewSceneMode.Single);
    UnityEngine.GameObject cameraObject = new UnityEngine.GameObject("Audit camera");
    UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
    camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    camera.targetTexture = new UnityEngine.RenderTexture(32, 32, 24);

    foreach (string hero in heroes) {
        UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
            "Assets/Actor/Hero/" + hero + "/" + hero + ".prefab");
        if (prefab == null) continue;
        UnityEngine.GameObject instance = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
        UnityEngine.SkinnedMeshRenderer[] assetRenderers = prefab.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
        UnityEngine.SkinnedMeshRenderer[] liveRenderers = instance.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
        // One render first, so the instance's skeleton has been through what a displayed frame does.
        camera.Render();
        for (int r = 0; r < assetRenderers.Length; r++) {
            UnityEngine.SkinnedMeshRenderer live = liveRenderers[r];
            UnityEngine.SkinnedMeshRenderer asset = assetRenderers[r];
            UnityEngine.Mesh mesh = asset.sharedMesh;
            UnityEngine.Mesh bakedAsset = new UnityEngine.Mesh();
            UnityEngine.Mesh bakedLive = new UnityEngine.Mesh();
            try {
                asset.BakeMesh(bakedAsset, false);
                live.BakeMesh(bakedLive, false);
                UnityEngine.Vector3[] raw = mesh.vertices;
                UnityEngine.Vector3[] fromAsset = bakedAsset.vertices;
                UnityEngine.Vector3[] fromLive = bakedLive.vertices;
                UnityEngine.BoneWeight[] weights = mesh.boneWeights;
                UnityEngine.Matrix4x4[] binds = mesh.bindposes;
                UnityEngine.Matrix4x4[] skin = new UnityEngine.Matrix4x4[asset.bones.Length];
                for (int b = 0; b < asset.bones.Length; b++)
                    skin[b] = asset.transform.worldToLocalMatrix * asset.bones[b].localToWorldMatrix * binds[b];

                int mattered = 0, assetBlend = 0, assetFirst = 0, assetNeither = 0, liveBlend = 0, liveFirst = 0, liveNeither = 0;
                for (int v = 0; v < weights.Length; v++) {
                    UnityEngine.BoneWeight w = weights[v];
                    UnityEngine.Vector3 first = skin[w.boneIndex0].MultiplyPoint3x4(raw[v]);
                    UnityEngine.Vector3 blend = UnityEngine.Vector3.zero;
                    if (w.weight0 > 0f) blend += w.weight0 * skin[w.boneIndex0].MultiplyPoint3x4(raw[v]);
                    if (w.weight1 > 0f) blend += w.weight1 * skin[w.boneIndex1].MultiplyPoint3x4(raw[v]);
                    if (w.weight2 > 0f) blend += w.weight2 * skin[w.boneIndex2].MultiplyPoint3x4(raw[v]);
                    if (w.weight3 > 0f) blend += w.weight3 * skin[w.boneIndex3].MultiplyPoint3x4(raw[v]);
                    if ((first - blend).magnitude < 0.002f) continue;   // the weights cannot show on this vertex
                    mattered++;
                    bool aBlend = (fromAsset[v] - blend).magnitude < 0.0005f;
                    bool aFirst = (fromAsset[v] - first).magnitude < 0.0005f;
                    if (aBlend) assetBlend++; else if (aFirst) assetFirst++; else assetNeither++;
                    bool lBlend = (fromLive[v] - blend).magnitude < 0.0005f;
                    bool lFirst = (fromLive[v] - first).magnitude < 0.0005f;
                    if (lBlend) liveBlend++; else if (lFirst) liveFirst++; else liveNeither++;
                }
                rows.Add(new System.Collections.Generic.Dictionary<string, object> {
                    { "hero", hero }, { "mesh", mesh.name }, { "vertices", raw.Length }, { "weightsMatter", mattered },
                    { "assetBlend", assetBlend }, { "assetFirst", assetFirst }, { "assetNeither", assetNeither },
                    { "liveBlend", liveBlend }, { "liveFirst", liveFirst }, { "liveNeither", liveNeither },
                });
            } finally {
                UnityEngine.Object.DestroyImmediate(bakedAsset);
                UnityEngine.Object.DestroyImmediate(bakedLive);
            }
        }
        UnityEngine.Object.DestroyImmediate(instance);
    }
    camera.targetTexture = null;
    UnityEngine.Object.DestroyImmediate(camera.targetTexture);
} catch (System.Exception e) {
    failure = e.GetType().FullName + ": " + e.Message;
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
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
    } catch (System.Exception e) {
        if (failure == null) failure = "restore: " + e.Message;
    }
}

__sb.Append('{');
__P("ran", __B(true));
if (failure != null) __P("error", __S(failure));
__P("activeSceneDirtyAfterRestore", __B(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty));
System.IO.Directory.CreateDirectory("__OUT_DIR__");
System.IO.File.WriteAllText(System.IO.Path.Combine("__OUT_DIR__", "bake-weights.json"), __J(rows));
__P("file", __S("bake-weights.json"));
__P("rows", rows.Count.ToString());
__END();
return __sb.ToString();
