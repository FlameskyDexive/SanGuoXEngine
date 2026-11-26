
// SanGuoXEngine skin audit: the per-renderer skin quality of the five heroes.
//
// SkinnedMeshRenderer.quality (Auto / Bone1 / Bone2 / Bone4) limits how many influences a vertex is
// skinned with, whatever mesh.boneWeights holds. Rendered silhouettes and BakeMesh both land on the
// first influence alone for every hero mesh (compare_skin.py, probe_bake_weights.csx), which is
// what Bone1 does. Read-only: asset loads only.
string[] heroes = new string[] { "Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu" };
System.Collections.Generic.List<object> rows = new System.Collections.Generic.List<object>();
foreach (string hero in heroes) {
    UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
        "Assets/Actor/Hero/" + hero + "/" + hero + ".prefab");
    if (prefab == null) continue;
    foreach (UnityEngine.SkinnedMeshRenderer smr in prefab.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true)) {
        UnityEditor.ModelImporter importer = UnityEditor.AssetImporter.GetAtPath(
            UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMesh)) as UnityEditor.ModelImporter;
        rows.Add(new System.Collections.Generic.Dictionary<string, object> {
            { "hero", hero }, { "mesh", smr.sharedMesh.name },
            { "quality", smr.quality.ToString() },
            { "importerSkinWeights", importer != null ? importer.skinWeights.ToString() : "<no importer>" },
            { "rootBone", smr.rootBone != null ? smr.rootBone.name : null },
            { "updateWhenOffscreen", smr.updateWhenOffscreen },
        });
    }
}
__sb.Append('{');
__P("rows", rows.Count.ToString());
System.IO.Directory.CreateDirectory("__OUT_DIR__");
System.IO.File.WriteAllText(System.IO.Path.Combine("__OUT_DIR__", "unity-skin-quality.json"), __J(rows));
__P("file", __S("unity-skin-quality.json"));
__END();
return __sb.ToString();
