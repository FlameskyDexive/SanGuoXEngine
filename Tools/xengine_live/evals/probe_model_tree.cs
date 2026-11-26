
// Throwaway probe: what did t_shanshi04_01.FBX import as? Prefab tree, meshes, and where the
// Unity-side mesh name lives. Read-only.
var sb = new System.Text.StringBuilder();
var backend = XEngine.Editor.EditorAssetBackend.Instance;
foreach (string model in new string[] {
    "Scene/Fbx/GameLevels/Raw/Fbx/t_shanshi04_01.FBX",
    "Scene/Fbx/GameLevels/Raw/Fbx/b_zx_shitai04_01.FBX",
    "Scene/Fbx/GameLevels/Raw/Fbx/p_xz_grass02_03.FBX",
}) {
    var entry = backend.GetEntry(model);
    if (entry == null) { sb.Append(model).Append(" => NOT IMPORTED\n"); continue; }
    sb.Append(model).Append('\n');
    var asset = XEngine.Runtime.AssetDatabase.Get(entry.Guid);
    sb.Append("   AssetDatabase.Get => ").Append(asset != null ? asset.GetType().Name : "null").Append('\n');
    var prefab = asset as XEngine.Runtime.Resources.PrefabAsset;
    if (prefab != null) {
        var instance = prefab.Instantiate();
        if (instance != null) {
            foreach (var node in instance.GetChildrenDeep()) {
                var renderer = node.GetComponent<XEngine.Runtime.MeshRenderer>();
                if (renderer != null && renderer.Mesh.Res != null) {
                    sb.Append("   node \"").Append(node.Name).Append("\" mesh \"").Append(renderer.Mesh.Res.Name)
                      .Append("\" verts=").Append(renderer.Mesh.Res.VertexCount).Append('\n');
                }
            }
            instance.Dispose();
        }
    }
    int subAssets = 0;
    foreach (var sub in backend.GetSubAssets(entry.Guid)) { subAssets++; sb.Append("   sub: ").Append(sub.Type.Name).Append(" \"").Append(sub.Name).Append("\"\n"); }
    if (subAssets == 0) sb.Append("   (no sub-assets)\n");
}
return sb.ToString();
