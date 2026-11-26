
// SanGuoXEngine skin audit: is Unity's BakeMesh on the prefab asset a faithful reference?
//
// For every skinned mesh of the five heroes it compares, in renderer space:
//   asset      BakeMesh on the prefab asset's renderer (what export_skin.csx recorded)
//   cpu        the skinning formula evaluated from the asset's own data:
//              sum of weight * (renderer.worldToLocal * bone.localToWorld * bindpose) * vertex
//   instance   BakeMesh on an instance of the prefab in a temporary scene
// Read-only for the project: the instance lives in a temporary empty scene, the scenes open before
// are restored, and the script refuses to run when a scene has unsaved changes or play mode is on.
string TPath(UnityEngine.Transform t) {
    string p = t != null ? t.name : "";
    while (t != null && t.parent != null) {
        t = t.parent;
        p = t.name + "/" + p;
    }
    return p;
}
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
    foreach (string hero in heroes) {
        UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
            "Assets/Actor/Hero/" + hero + "/" + hero + ".prefab");
        if (prefab == null) continue;
        UnityEngine.GameObject instance = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
        UnityEngine.SkinnedMeshRenderer[] assetRenderers = prefab.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
        UnityEngine.SkinnedMeshRenderer[] liveRenderers = instance.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
        for (int r = 0; r < assetRenderers.Length; r++) {
            UnityEngine.SkinnedMeshRenderer asset = assetRenderers[r];
            UnityEngine.SkinnedMeshRenderer live = liveRenderers[r];
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

                // CPU skinning from the asset's transforms, and the same from the instance's.
                UnityEngine.Matrix4x4[] assetSkin = new UnityEngine.Matrix4x4[asset.bones.Length];
                UnityEngine.Matrix4x4[] liveSkin = new UnityEngine.Matrix4x4[live.bones.Length];
                for (int b = 0; b < asset.bones.Length; b++)
                    assetSkin[b] = asset.transform.worldToLocalMatrix * asset.bones[b].localToWorldMatrix * binds[b];
                for (int b = 0; b < live.bones.Length; b++)
                    liveSkin[b] = live.transform.worldToLocalMatrix * live.bones[b].localToWorldMatrix * binds[b];

                float MaxDiff(UnityEngine.Vector3[] a, System.Func<int, UnityEngine.Vector3> f) {
                    float worst = 0f;
                    for (int v = 0; v < a.Length; v++) worst = UnityEngine.Mathf.Max(worst, (a[v] - f(v)).magnitude);
                    return worst;
                }
                UnityEngine.Vector3 Skinned(int v, UnityEngine.Matrix4x4[] skin, UnityEngine.BoneWeight[] w) {
                    UnityEngine.Vector3 p = UnityEngine.Vector3.zero;
                    UnityEngine.Vector3 x = raw[v];
                    if (w[v].weight0 > 0f) p += w[v].weight0 * skin[w[v].boneIndex0].MultiplyPoint3x4(x);
                    if (w[v].weight1 > 0f) p += w[v].weight1 * skin[w[v].boneIndex1].MultiplyPoint3x4(x);
                    if (w[v].weight2 > 0f) p += w[v].weight2 * skin[w[v].boneIndex2].MultiplyPoint3x4(x);
                    if (w[v].weight3 > 0f) p += w[v].weight3 * skin[w[v].boneIndex3].MultiplyPoint3x4(x);
                    return p;
                }
                float assetVsCpu = MaxDiff(fromAsset, delegate(int v) { return Skinned(v, assetSkin, weights); });
                float liveVsCpu = MaxDiff(fromLive, delegate(int v) { return Skinned(v, liveSkin, weights); });
                float assetVsLive = MaxDiff(fromAsset, delegate(int v) { return fromLive[v]; });
                float rawVsCpu = MaxDiff(raw, delegate(int v) { return Skinned(v, liveSkin, weights); });

                // Multi-influence vertices whose influences do not share one skin matrix: the only
                // ones where a weight mistake can show at the rest pose.
                int blended = 0, blendedOff = 0, singleOff = 0;
                for (int v = 0; v < weights.Length; v++) {
                    UnityEngine.BoneWeight w = weights[v];
                    int influences = (w.weight0 > 0f ? 1 : 0) + (w.weight1 > 0f ? 1 : 0) + (w.weight2 > 0f ? 1 : 0) + (w.weight3 > 0f ? 1 : 0);
                    UnityEngine.Vector3 first = skinPoint(liveSkin, w.boneIndex0, raw[v]);
                    UnityEngine.Vector3 full = Skinned(v, liveSkin, weights);
                    bool differs = (first - full).magnitude > 0.002f;
                    if (differs) { blended++; if (influences > 1) blendedOff++; else singleOff++; }
                }
                UnityEngine.Vector3 skinPoint(UnityEngine.Matrix4x4[] skin, int bone, UnityEngine.Vector3 x) {
                    return skin[bone].MultiplyPoint3x4(x);
                }

                rows.Add(new System.Collections.Generic.Dictionary<string, object> {
                    { "hero", hero }, { "mesh", mesh.name },
                    { "vertices", raw.Length },
                    { "assetVsCpu", __F(assetVsCpu) }, { "liveVsCpu", __F(liveVsCpu) },
                    { "assetVsLive", __F(assetVsLive) }, { "rawVsCpu", __F(rawVsCpu) },
                    { "firstInfluenceDiffersFromBlend", blended }, { "ofWhichMultiInfluence", blendedOff },
                });
            } catch (System.Exception e) {
                rows.Add(new System.Collections.Generic.Dictionary<string, object> { { "hero", hero }, { "mesh", mesh.name }, { "error", __S(e.Message) } });
            } finally {
                UnityEngine.Object.DestroyImmediate(bakedAsset);
                UnityEngine.Object.DestroyImmediate(bakedLive);
            }
        }
        UnityEngine.Object.DestroyImmediate(instance);
    }
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
System.IO.File.WriteAllText(System.IO.Path.Combine("__OUT_DIR__", "bake-audit.json"), __J(rows));
__P("file", __S("bake-audit.json"));
__P("rows", rows.Count.ToString());
__END();
return __sb.ToString();
