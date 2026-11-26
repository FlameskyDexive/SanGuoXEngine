
// SanGuoXEngine skin audit: which skin weights does Unity really skin with?
//
// For every hero model: the importer's skin settings, and for the hair mesh (the one with the most
// multi-influence vertices on helper bones) how many influences each vertex has according to
//   mesh.boneWeights              the legacy 4-slot BoneWeight array
//   mesh.GetBonesPerVertex()      the imported skin data the renderer skins with
// Read-only: asset loads only.
string[] heroes = new string[] { "Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu" };
System.Collections.Generic.List<object> rows = new System.Collections.Generic.List<object>();
foreach (string hero in heroes) {
    UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
        "Assets/Actor/Hero/" + hero + "/" + hero + ".prefab");
    if (prefab == null) continue;
    UnityEngine.SkinnedMeshRenderer hair = null;
    foreach (UnityEngine.SkinnedMeshRenderer smr in prefab.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
        if (smr.name.EndsWith("Hair") || smr.name.EndsWith("Face")) { hair = smr; break; }
    if (hair == null) hair = prefab.GetComponentInChildren<UnityEngine.SkinnedMeshRenderer>(true);
    UnityEngine.Mesh mesh = hair.sharedMesh;
    UnityEditor.ModelImporter importer = UnityEditor.AssetImporter.GetAtPath(
        UnityEditor.AssetDatabase.GetAssetPath(mesh)) as UnityEditor.ModelImporter;

    UnityEngine.BoneWeight[] legacy = mesh.boneWeights;
    // The imported skin data: one count per vertex, then that many (bone index, weight) pairs per vertex.
    Unity.Collections.NativeArray<byte> perVertex = mesh.GetBonesPerVertex();
    Unity.Collections.NativeArray<UnityEngine.BoneWeight1> all = mesh.GetAllBoneWeights();
    int[] legacyHistogram = new int[5];
    int[] importedHistogram = new int[9];
    foreach (UnityEngine.BoneWeight w in legacy) {
        int n = (w.weight0 > 0f ? 1 : 0) + (w.weight1 > 0f ? 1 : 0) + (w.weight2 > 0f ? 1 : 0) + (w.weight3 > 0f ? 1 : 0);
        legacyHistogram[n]++;
    }
    foreach (byte n in perVertex) importedHistogram[System.Math.Min((int)n, 8)]++;

    // For the vertices with two or more influences: does the imported data hold the same weights?
    int compared = 0, same = 0, different = 0, onlyOneInImported = 0;
    System.Collections.Generic.List<object> examples = new System.Collections.Generic.List<object>();
    int cursor = 0;
    for (int v = 0; v < legacy.Length && v < perVertex.Length; v++) {
        UnityEngine.BoneWeight w = legacy[v];
        int influences = (w.weight0 > 0f ? 1 : 0) + (w.weight1 > 0f ? 1 : 0) + (w.weight2 > 0f ? 1 : 0) + (w.weight3 > 0f ? 1 : 0);
        int count = perVertex[v];
        if (influences < 2 && count < 2) { cursor += count; continue; }
        compared++;
        System.Collections.Generic.Dictionary<int, float> a = new System.Collections.Generic.Dictionary<int, float>();
        if (w.weight0 > 0f) a[w.boneIndex0] = w.weight0;
        if (w.weight1 > 0f) a[w.boneIndex1] = w.weight1;
        if (w.weight2 > 0f) a[w.boneIndex2] = w.weight2;
        if (w.weight3 > 0f) a[w.boneIndex3] = w.weight3;
        System.Collections.Generic.Dictionary<int, float> b = new System.Collections.Generic.Dictionary<int, float>();
        for (int k = 0; k < count; k++) {
            UnityEngine.BoneWeight1 bw = all[cursor + k];
            if (bw.weight > 0f) b[bw.boneIndex] = bw.weight;
        }
        bool equal = a.Count == b.Count;
        if (equal) foreach (System.Collections.Generic.KeyValuePair<int, float> entry in a)
            if (!b.ContainsKey(entry.Key) || UnityEngine.Mathf.Abs(b[entry.Key] - entry.Value) > 1e-4f) equal = false;
        if (equal) same++; else different++;
        if (count < 2) onlyOneInImported++;
        if (!equal && examples.Count < 6) {
            System.Collections.Generic.List<object> la = new System.Collections.Generic.List<object>();
            foreach (System.Collections.Generic.KeyValuePair<int, float> entry in a) la.Add(entry.Key + ":" + entry.Value.ToString("F4"));
            System.Collections.Generic.List<object> lb = new System.Collections.Generic.List<object>();
            foreach (System.Collections.Generic.KeyValuePair<int, float> entry in b) lb.Add(entry.Key + ":" + entry.Value.ToString("F4"));
            examples.Add(new System.Collections.Generic.Dictionary<string, object> { { "vertex", v }, { "boneWeights", la }, { "imported", lb } });
        }
        cursor += count;
    }

    rows.Add(new System.Collections.Generic.Dictionary<string, object> {
        { "hero", hero }, { "mesh", mesh.name },
        { "source", UnityEditor.AssetDatabase.GetAssetPath(mesh) },
        { "skinWeights", importer != null ? importer.skinWeights.ToString() : "<no importer>" },
        { "meshCompression", importer != null ? importer.meshCompression.ToString() : "" },
        { "optimizeGameObjects", importer != null && importer.optimizeGameObjects },
        { "animationType", importer != null ? importer.animationType.ToString() : "" },
        { "legacyHistogram", legacyHistogram }, { "importedHistogram", importedHistogram },
        { "multiInfluenceCompared", compared }, { "same", same }, { "different", different }, { "importedHasOneInfluence", onlyOneInImported },
        { "examples", examples },
    });
}
__sb.Append('{');
__P("rows", rows.Count.ToString());
System.IO.Directory.CreateDirectory("__OUT_DIR__");
System.IO.File.WriteAllText(System.IO.Path.Combine("__OUT_DIR__", "unity-skin-data.json"), __J(rows));
__P("file", __S("unity-skin-data.json"));
__END();
return __sb.ToString();
