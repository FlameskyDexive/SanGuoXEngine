
// SanGuoXEngine skin audit export: the skinning data of every hero mesh as Unity holds it, for the
// rest-pose comparison with XEngine (docs/evidence/m1/heroes.md).
//
// Read-only: asset loads and BakeMesh on the prefab asset's renderers, nothing is instantiated.
// One file per hero, skin_<Hero>.json:
//   hero, rootToWorld[16]
//   skinnedMeshes[]  node, mesh, smrToWorld[16] (row-major), bones[] (transform paths),
//                   bindposes[16 per bone] (row-major), raw[3 per vertex] (mesh space, as imported),
//                   baked[3 per vertex] (renderer space at the rest pose), weights[8 per vertex]
//                   (bone index 0-3 then weight 0-3), triangles[]
string TPath(UnityEngine.Transform t) {
    string p = t != null ? t.name : "";
    while (t != null && t.parent != null) {
        t = t.parent;
        p = t.name + "/" + p;
    }
    return p;
}
string F(float f) { return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
string Floats(float[] values) {
    System.Text.StringBuilder b = new System.Text.StringBuilder(values.Length * 10);
    b.Append('[');
    for (int i = 0; i < values.Length; i++) {
        if (i > 0) b.Append(',');
        b.Append(F(values[i]));
    }
    return b.Append(']').ToString();
}
string Ints(int[] values) {
    System.Text.StringBuilder b = new System.Text.StringBuilder(values.Length * 7);
    b.Append('[');
    for (int i = 0; i < values.Length; i++) {
        if (i > 0) b.Append(',');
        b.Append(values[i].ToString());
    }
    return b.Append(']').ToString();
}
float[] Matrix(UnityEngine.Matrix4x4 m) {
    return new float[16] {
        m.m00, m.m01, m.m02, m.m03,
        m.m10, m.m11, m.m12, m.m13,
        m.m20, m.m21, m.m22, m.m23,
        m.m30, m.m31, m.m32, m.m33,
    };
}

string[] heroes = new string[] { "Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu" };
System.Collections.Generic.List<object> written = new System.Collections.Generic.List<object>();
foreach (string hero in heroes) {
    string prefabPath = "Assets/Actor/Hero/" + hero + "/" + hero + ".prefab";
    UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
    if (prefab == null) continue;
    UnityEngine.Transform root = prefab.transform;
    System.Text.StringBuilder b = new System.Text.StringBuilder();
    b.Append("\"hero\":").Append(__S(hero)).Append(",\"rootToWorld\":").Append(Floats(Matrix(root.localToWorldMatrix)));
    b.Append(",\"skinnedMeshes\":[");
    bool firstMesh = true;
    foreach (UnityEngine.SkinnedMeshRenderer smr in prefab.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true)) {
        UnityEngine.Mesh mesh = smr.sharedMesh;
        if (mesh == null) continue;
        UnityEngine.Mesh bakedMesh = new UnityEngine.Mesh();
        try {
            smr.BakeMesh(bakedMesh, false);
            UnityEngine.BoneWeight[] weights = mesh.boneWeights;
            float[] weightValues = new float[weights.Length * 8];
            for (int i = 0; i < weights.Length; i++) {
                UnityEngine.BoneWeight w = weights[i];
                weightValues[i * 8] = w.boneIndex0; weightValues[i * 8 + 1] = w.boneIndex1;
                weightValues[i * 8 + 2] = w.boneIndex2; weightValues[i * 8 + 3] = w.boneIndex3;
                weightValues[i * 8 + 4] = w.weight0; weightValues[i * 8 + 5] = w.weight1;
                weightValues[i * 8 + 6] = w.weight2; weightValues[i * 8 + 7] = w.weight3;
            }
            UnityEngine.Matrix4x4[] binds = mesh.bindposes;
            float[] bindValues = new float[binds.Length * 16];
            for (int i = 0; i < binds.Length; i++) Matrix(binds[i]).CopyTo(bindValues, i * 16);
            float[] Flat(UnityEngine.Vector3[] vectors) {
                float[] flat = new float[vectors.Length * 3];
                for (int i = 0; i < vectors.Length; i++) {
                    flat[i * 3] = vectors[i].x; flat[i * 3 + 1] = vectors[i].y; flat[i * 3 + 2] = vectors[i].z;
                }
                return flat;
            }
            System.Text.StringBuilder bonesJson = new System.Text.StringBuilder();
            bonesJson.Append('[');
            for (int i = 0; i < smr.bones.Length; i++) {
                if (i > 0) bonesJson.Append(',');
                bonesJson.Append(__S(smr.bones[i] != null ? TPath(smr.bones[i]) : null));
            }
            bonesJson.Append(']');

            if (!firstMesh) b.Append(',');
            firstMesh = false;
            b.Append("{\"node\":").Append(__S(TPath(smr.transform))).Append(",\"mesh\":").Append(__S(mesh.name));
            b.Append(",\"smrToWorld\":").Append(Floats(Matrix(smr.transform.localToWorldMatrix)));
            b.Append(",\"bones\":").Append(bonesJson.ToString());
            b.Append(",\"bindposes\":").Append(Floats(bindValues));
            b.Append(",\"raw\":").Append(Floats(Flat(mesh.vertices)));
            b.Append(",\"baked\":").Append(Floats(Flat(bakedMesh.vertices)));
            b.Append(",\"weights\":").Append(Floats(weightValues));
            b.Append(",\"triangles\":").Append(Ints(mesh.triangles));
            b.Append('}');
        } finally {
            UnityEngine.Object.DestroyImmediate(bakedMesh);
        }
    }
    b.Append(']');
    System.IO.File.WriteAllText(System.IO.Path.Combine("__OUT_DIR__", "skin_" + hero + ".json"), "{" + b.ToString() + "}");
    written.Add(hero);
}

__sb.Append('{');
__P("written", __J(written));
if (written.Count > 0) __P("file", __S("skin_" + (string)written[written.Count - 1] + ".json"));
__END();
return __sb.ToString();
