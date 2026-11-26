
// Throwaway probe: what sub-assets did the engine's model import produce for the scene FBXs, and
// do the Unity-side mesh names appear among them? Read-only.
var sb = new System.Text.StringBuilder();
var backend = XEngine.Editor.EditorAssetBackend.Instance;
string[] models = new string[] {
    "Scene/Fbx/GameLevels/Raw/Fbx/b_zx_shitai04_01.FBX",
    "Scene/Fbx/Actor/Senceobj/xiaobingyuanjing/i_luoyang_xiaobingyuanjing.FBX",
    "Scene/Fbx/GameLevels/Raw/Fbx/t_shanshi04_01.FBX",
};
foreach (string model in models) {
    var entry = backend.GetEntry(model);
    if (entry == null) { sb.Append(model).Append(" => NOT IMPORTED\n"); continue; }
    sb.Append(model).Append(" => ").Append(entry.Guid).Append('\n');
    foreach (var sub in backend.GetSubAssets(entry.Guid))
        sb.Append("   ").Append(sub.Type.Name).Append("  \"").Append(sub.Name).Append("\"  ").Append(sub.Guid).Append('\n');
}
return sb.ToString();
