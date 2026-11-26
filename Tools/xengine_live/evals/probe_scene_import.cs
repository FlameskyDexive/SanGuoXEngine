
// Throwaway probe: which of the scene's models imported into a usable PrefabAsset, and which have
// Mesh sub-assets? Uses the asset database directly per manifest model guid. Read-only.
string manifestPath = System.IO.Path.Combine(XEngine.Editor.Projects.Project.Current.RootPath, "Export/import/scene-assets.json");
var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(manifestPath)).RootElement;
var backend = XEngine.Editor.EditorAssetBackend.Instance;
int total = 0, prefab = 0, missing = 0, other = 0, withMeshSub = 0, withoutSub = 0;
var sb = new System.Text.StringBuilder();
foreach (var model in json.GetProperty("models").EnumerateObject()) {
    string guid = model.Value.GetProperty("guid").GetString();
    string enginePath = model.Value.GetProperty("enginePath").GetString();
    total++;
    var entry = backend.GetEntry(enginePath);
    if (entry == null) { missing++; if (missing <= 8) sb.Append("  no entry: ").Append(enginePath).Append('\n'); continue; }
    var asset = XEngine.Runtime.AssetDatabase.Get(entry.Guid);
    if (asset is XEngine.Runtime.Resources.PrefabAsset) {
        prefab++;
        bool hasMesh = false;
        foreach (var sub in backend.GetSubAssets(entry.Guid)) if (sub.Type == typeof(XEngine.Runtime.Resources.Mesh)) hasMesh = true;
        if (hasMesh) withMeshSub++;
        else { withoutSub++; if (withoutSub <= 8) sb.Append("  no mesh sub: ").Append(enginePath).Append('\n'); }
    } else {
        other++;
        if (other <= 8) sb.Append("  other (").Append(asset != null ? asset.GetType().Name : "null").Append("): ").Append(enginePath).Append('\n');
    }
}
return $"models={total} prefab={prefab} withMeshSub={withMeshSub} noMeshSub={withoutSub} other={other} missing={missing}\n" + sb.ToString();
