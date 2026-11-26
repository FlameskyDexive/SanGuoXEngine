// Opens the generated TaoYuan scene in the live editor and reports what loaded: object counts,
// LODGroups, lightmap pages, skybox mode, and how many renderers carry a mesh + material binding.
//
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 120 --eval-file Tools/xengine_live/evals/open_scene.cs

System.Type manager = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("XEngine.Editor.GUI.SceneView.EditorSceneManager");
    if (type != null) { manager = type; break; }
}
if (manager == null) return "EditorSceneManager not found.";
var openMethod = manager.GetMethod("OpenScene", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
if (openMethod == null) return "OpenScene method not found.";
bool opened = (bool)openMethod.Invoke(null, new object[] { "Scenes/TaoYuan.scene" });
if (!opened) return "OpenScene returned false.";

var sb = new System.Text.StringBuilder();
sb.Append("opened: ").Append(opened).Append('\n');
var scene = XEngine.Runtime.Resources.Scene.Current;
sb.Append("scene: ").Append(scene != null ? scene.Name : "(null)").Append('\n');
if (scene != null) {
    int total = 0, roots = 0, renderers = 0, withMesh = 0, withMaterial = 0, lightmapped = 0, lods = 0, lights = 0, cameras = 0;
    foreach (var go in scene.AllObjects) {
        total++;
        if (go.Transform.Parent == null) roots++;
        foreach (var component in go.GetComponents()) {
            switch (component) {
                case XEngine.Runtime.MeshRenderer r:
                    renderers++;
                    if (r.Mesh.AssetID != System.Guid.Empty) withMesh++;
                    if (r.Materials.Count > 0) withMaterial++;
                    if (r.LightmapIndex >= 0) lightmapped++;
                    break;
                case XEngine.Runtime.LODGroup: lods++; break;
                case XEngine.Runtime.DirectionalLight: lights++; break;
                case XEngine.Runtime.Camera: cameras++; break;
            }
        }
    }
    sb.Append("roots: ").Append(roots).Append(", objects: ").Append(total).Append('\n');
    sb.Append("renderers: ").Append(renderers).Append(" (mesh ").Append(withMesh)
       .Append(", materials ").Append(withMaterial).Append(", lightmapped ").Append(lightmapped).Append(")\n");
    sb.Append("lodGroups: ").Append(lods).Append(", lights: ").Append(lights).Append(", cameras: ").Append(cameras).Append('\n');
    sb.Append("skybox mode: ").Append(scene.Skybox.Mode).Append('\n');
    sb.Append("lightmap pages: ").Append(scene.BakedLighting.Lightmaps.Count).Append('\n');
    sb.Append("ambient: ").Append(scene.Ambient.Mode).Append('\n');
}
return sb.ToString();
