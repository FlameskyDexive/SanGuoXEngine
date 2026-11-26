// Dumps the hierarchy of the imported hero models as it came out of the engine's FBX importer.
// Run inside the live editor (no scene is touched: PrefabAsset.Instantiate does not register into
// the scene, and every instance is disposed):
//
//     py -3 Tools/xengine_live/mcp_live.py --eval-file Tools/xengine_live/evals/inspect_models.cs > out.txt
//
// One line per GameObject, '|'-separated, for comparison with the Unity export's transform tree:
//     path|localPosition|worldPosition|localScale|components[|skinned: mesh, vertices, subMeshes, bones, materials]
var sb = new System.Text.StringBuilder();
XEngine.Editor.EditorRegistries.Initialize();
var backend = XEngine.Editor.EditorAssetBackend.Instance;
System.Func<XEngine.Runtime.EngineObject, string> assetName = delegate(XEngine.Runtime.EngineObject asset) {
    return asset == null ? "<unloaded>" : asset.Name;
};
System.Action<XEngine.Runtime.GameObject, string> walk = null;
walk = delegate(XEngine.Runtime.GameObject go, string path) {
    var t = go.Transform;
    sb.Append(path).Append('|')
      .Append(t.LocalPosition.X.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
      .Append(t.LocalPosition.Y.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
      .Append(t.LocalPosition.Z.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
      .Append(t.Position.X.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
      .Append(t.Position.Y.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
      .Append(t.Position.Z.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
      .Append(t.LocalScale.X.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
      .Append(t.LocalScale.Y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
      .Append(t.LocalScale.Z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append('|');
    foreach (var component in go.GetComponents())
        sb.Append(component == null ? "<missing>" : component.GetType().Name).Append(',');
    var skinned = go.GetComponent<XEngine.Runtime.SkinnedMeshRenderer>();
    if (skinned != null) {
        sb.Append("skinned: ").Append(assetName(skinned.SharedMesh.Res));
        var mesh = skinned.SharedMesh.Res;
        if (mesh != null)
            sb.Append(", v=").Append(mesh.VertexCount.ToString()).Append(", i=").Append(mesh.IndexCount.ToString()).Append(", sub=").Append(mesh.SubMeshCount.ToString());
        sb.Append(", bones=").Append(skinned.Bones == null ? -1 : skinned.Bones.Length).Append(", mats=").Append(skinned.Materials.Count);
    }
    sb.Append('\n');
    foreach (var child in go.Children)
        walk(child, path + "/" + child.Name);
};

string[] models = new string[] {
    "Heroes/Hero_ZhaoYun/Hero_ZhaoYun.fbx", "Heroes/Hero_GuanYu/Hero_GuanYu.fbx", "Heroes/Hero_LiuBei/Hero_LiuBei.fbx",
    "Heroes/Hero_ZhangFei/Hero_ZhangFei.fbx", "Heroes/Hero_LvBu/Hero_LvBu.fbx",
};
foreach (string path in models) {
    var entry = backend.GetEntry(path);
    if (entry == null) { sb.Append("## ").Append(path).Append(" | NOT IMPORTED\n"); continue; }
    var prefab = XEngine.Runtime.AssetDatabase.Get(entry.Guid) as XEngine.Runtime.Resources.PrefabAsset;
    if (prefab == null) { sb.Append("## ").Append(path).Append(" | not a PrefabAsset\n"); continue; }
    sb.Append("## ").Append(path).Append(" | type=").Append(prefab.InstanceType.ToString()).Append('\n');
    // Sub-assets: the meshes (vertex counts to compare with Unity's) and the generated Avatar.
    foreach (var sub in backend.GetSubAssets(entry.Guid)) {
        sb.Append("   sub: ").Append(sub.Name).Append(" | ").Append(sub.Type != null ? sub.Type.Name : "?").Append(" | ").Append(sub.Guid.ToString());
        var mesh = XEngine.Runtime.AssetDatabase.Get(sub.Guid) as XEngine.Runtime.Resources.Mesh;
        if (mesh != null)
            sb.Append(" | v=").Append(mesh.VertexCount.ToString()).Append(" i=").Append(mesh.IndexCount.ToString()).Append(" sub=").Append(mesh.SubMeshCount.ToString());
        sb.Append('\n');
    }
    var instance = prefab.Instantiate();
    if (instance == null) { sb.Append("Instantiate returned null\n"); continue; }
    try { walk(instance, instance.Name); }
    finally { instance.Dispose(); }
}
return sb.ToString();
