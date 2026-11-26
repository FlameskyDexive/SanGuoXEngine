// Opens the generated TaoYuan scene, poses the scene camera at the close reference frame and
// renders a quickshot PNG (visual sanity check for the M3-3 builder).
//
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 180 --eval-file Tools/xengine_live/evals/scene_quickshot.cs --out Export/scene/quickshot-result.json
// __OUT_DIR__ is replaced by run_export.py (or set your own absolute path below when driving mcp_live directly).

System.Type manager = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("XEngine.Editor.GUI.SceneView.EditorSceneManager");
    if (type != null) { manager = type; break; }
}
if (manager == null) return "EditorSceneManager not found.";
bool opened = (bool)manager.GetMethod("OpenScene").Invoke(null, new object[] { "Scenes/TaoYuan.scene" });
if (!opened) return "OpenScene returned false.";

System.Type studio = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoCaptureStudio");
    if (type != null) { studio = type; break; }
}
if (studio == null) return "SanguoCaptureStudio not loaded.";

var scene = XEngine.Runtime.Resources.Scene.Current;
XEngine.Runtime.Camera? camera = null;
foreach (var go in scene.AllObjects)
    foreach (var component in go.GetComponents())
        if (component is XEngine.Runtime.Camera c) { camera = c; break; }
if (camera == null) return "the scene has no camera.";

var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
    System.IO.Path.Combine(XEngine.Editor.Projects.Project.Current.RootPath, "Export/scene/scene.json")));
var close = document.RootElement.GetProperty("frames").GetProperty("close");
var position = close.GetProperty("position");
var rotation = close.GetProperty("rotation");
string output = System.IO.Path.Combine(XEngine.Editor.Projects.Project.Current.RootPath, "Export/scene/taoyuan-quickshot.png");

// Land every referenced asset in memory first: async loads render as empty/white otherwise.
int loaded = 0;
foreach (var go in scene.AllObjects) {
    foreach (var component in go.GetComponents()) {
        if (component is XEngine.Runtime.MeshRenderer renderer) {
            renderer.Mesh.EnsureLoaded();
            foreach (var material in renderer.Materials) { material.EnsureLoaded(); loaded++; }
        }
    }
}
// The sky material and its panorama texture are async AssetRefs too: unloaded they draw black.
var skyMaterial = scene.Skybox.CustomMaterial;
skyMaterial.EnsureLoaded();
var manifest = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
    System.IO.Path.Combine(XEngine.Editor.Projects.Project.Current.RootPath, "Export/import/scene-assets.json")));
var skyGuid = System.Guid.Parse(manifest.RootElement.GetProperty("sky")
    .GetProperty("customReflection").GetProperty("guid").GetString());
new XEngine.Runtime.AssetRef<XEngine.Runtime.Resources.Texture2D>(skyGuid).EnsureLoaded();
foreach (var page in scene.BakedLighting.Lightmaps) page.EnsureLoaded();

studio.GetMethod("CaptureSceneFrame").Invoke(null, new object[] {
    camera,
    new XEngine.Vector.Float3(position[0].GetSingle(), position[1].GetSingle(), position[2].GetSingle()),
    new XEngine.Vector.Quaternion(rotation[0].GetSingle(), rotation[1].GetSingle(),
        rotation[2].GetSingle(), rotation[3].GetSingle()),
    960, 540, output });
return "rendered -> " + output + " (materials loaded: " + loaded + ")";
