// Dumps the skinning data of every hero mesh as XEngine holds it, in the same shape as the Unity
// export (Tools/unity_export/scripts/export_skin.csx), for the rest-pose comparison.
// Writes Export/xengine-shots/skin/skin_<Hero>.json and returns a summary.
//
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 300 --eval-file Tools/xengine_live/evals/dump_skin.cs
var sb = new System.Text.StringBuilder();
XEngine.Editor.EditorRegistries.Initialize();
var backend = XEngine.Editor.EditorAssetBackend.Instance;
var scene = XEngine.Runtime.Resources.Scene.Current;
if (scene == null) return "no scene is open";
var culture = System.Globalization.CultureInfo.InvariantCulture;
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
string rootPath = XEngine.Editor.Projects.Project.Current.RootPath;
string outDir = System.IO.Path.Combine(rootPath, "Export", "xengine-shots", "skin");
System.IO.Directory.CreateDirectory(outDir);

System.Func<float, string> f = delegate(float v) { return v.ToString("R", culture); };
System.Func<XEngine.Vector.Float4, string> v4 = delegate(XEngine.Vector.Float4 v) {
    return "[" + f(v.X) + "," + f(v.Y) + "," + f(v.Z) + "," + f(v.W) + "]";
};
// Columns c0..c3 in a row, each (x, y, z, w): the engine stores the translation in c3.
System.Func<XEngine.Vector.Float4x4, string> mat = delegate(XEngine.Vector.Float4x4 m) {
    return "[" + v4(m.c0) + "," + v4(m.c1) + "," + v4(m.c2) + "," + v4(m.c3) + "]";
};

string[] heroes = new string[] { "Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu" };
int total = 0;
foreach (string hero in heroes) {
    var entry = backend.GetEntry("Heroes/Prefabs/" + hero + ".prefab");
    var prefab = XEngine.Runtime.AssetDatabase.Get(entry.Guid) as XEngine.Runtime.Resources.PrefabAsset;
    var instance = prefab.Instantiate();
    scene.Add(instance);
    try {
        var b = new System.Text.StringBuilder();
        b.Append("{\"hero\":\"").Append(hero).Append("\",\"renderers\":[");
        bool first = true;
        foreach (var node in instance.GetChildrenDeep()) {
            var skinned = node.GetComponent<XEngine.Runtime.SkinnedMeshRenderer>();
            if (skinned == null) continue;
            skinned.SharedMesh.EnsureLoaded();
            var mesh = skinned.SharedMesh.Res;
            mesh.GetType().GetMethod("SetReadWriteEnabled", flags).Invoke(mesh, new object[] { true });
            var bones = skinned.Bones;
            if (bones == null) return hero + ": bones resolved to null";
            var vertices = mesh.Vertices;
            var boneIndices = mesh.BoneIndices;
            var boneWeights = mesh.BoneWeights;
            var binds = mesh.BindPoses;
            if (!first) b.Append(',');
            first = false;
            total++;
            b.Append("{\"node\":\"").Append(node.Name).Append("\",\"mesh\":\"").Append(mesh.Name).Append('"');
            b.Append(",\"worldToLocal\":").Append(mat(skinned.Transform.WorldToLocalMatrix));
            b.Append(",\"bones\":[");
            for (int i = 0; i < bones.Length; i++) {
                if (i > 0) b.Append(',');
                b.Append('"').Append(skinned.BonePaths != null && i < skinned.BonePaths.Length ? skinned.BonePaths[i] : "?").Append('"');
            }
            b.Append("],\"boneWorld\":[");
            for (int i = 0; i < bones.Length; i++) {
                if (i > 0) b.Append(',');
                b.Append(mat(bones[i].LocalToWorldMatrix));
            }
            b.Append("],\"bindposes\":[");
            for (int i = 0; i < binds.Length; i++) {
                if (i > 0) b.Append(',');
                b.Append(mat(binds[i]));
            }
            b.Append("],\"raw\":[");
            for (int i = 0; i < vertices.Length; i++) {
                if (i > 0) b.Append(',');
                b.Append(f(vertices[i].X)).Append(',').Append(f(vertices[i].Y)).Append(',').Append(f(vertices[i].Z));
            }
            b.Append("],\"boneIndices\":[");
            for (int i = 0; i < boneIndices.Length; i++) {
                if (i > 0) b.Append(',');
                b.Append(f(boneIndices[i].X)).Append(',').Append(f(boneIndices[i].Y)).Append(',').Append(f(boneIndices[i].Z)).Append(',').Append(f(boneIndices[i].W));
            }
            b.Append("],\"boneWeights\":[");
            for (int i = 0; i < boneWeights.Length; i++) {
                if (i > 0) b.Append(',');
                b.Append(f(boneWeights[i].X)).Append(',').Append(f(boneWeights[i].Y)).Append(',').Append(f(boneWeights[i].Z)).Append(',').Append(f(boneWeights[i].W));
            }
            var indices = mesh.Indices;
            b.Append("],\"triangles\":[");
            for (int i = 0; i < indices.Length; i++) {
                if (i > 0) b.Append(',');
                b.Append(indices[i].ToString(culture));
            }
            b.Append("]}");
        }
        b.Append("]}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "skin_" + hero + ".json"), b.ToString());
    } finally {
        scene.Remove(instance);
        instance.Dispose();
    }
}
return total + " renderer(s) dumped to " + outDir;
