// Opens TaoYuan, renders the three Unity reference frames (close / mid / panorama) with the same
// camera poses, fov and lodBias as the Unity captures, and reports a bracketed frame's stats.
//
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 240 --eval-file Tools/xengine_live/evals/capture_frames.cs --out Export/scene/frames-result.json

string Run()
{
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

string root = XEngine.Editor.Projects.Project.Current.RootPath;
string output = System.IO.Path.Combine(root, "Export/scene/xengine-frames");
System.IO.Directory.CreateDirectory(output);

// Land every referenced asset: meshes, materials, the sky panorama and the lightmap pages.
int materials = 0;
foreach (var go in scene.AllObjects) {
    foreach (var component in go.GetComponents()) {
        if (component is XEngine.Runtime.MeshRenderer renderer) {
            renderer.Mesh.EnsureLoaded();
            foreach (var material in renderer.Materials) { material.EnsureLoaded(); materials++; }
        }
    }
}
var manifest = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
    System.IO.Path.Combine(root, "Export/import/scene-assets.json")));
var skyGuid = System.Guid.Parse(manifest.RootElement.GetProperty("sky")
    .GetProperty("customReflection").GetProperty("guid").GetString());
new XEngine.Runtime.AssetRef<XEngine.Runtime.Resources.Texture2D>(skyGuid).EnsureLoaded();
foreach (var page in scene.BakedLighting.Lightmaps) page.EnsureLoaded();

// Unity's reference quality level ("Simple") carries lodBias 0.4.
XEngine.Runtime.Rendering.QualitySettings.LodBias = 0.4f;

var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
    System.IO.Path.Combine(root, "Export/scene/scene.json")));
var frames = document.RootElement.GetProperty("frames");
var capture = studio.GetMethod("CaptureSceneFrame");
foreach (var name in new[] { "close", "mid", "panorama" }) {
    var frame = frames.GetProperty(name);
    var position = frame.GetProperty("position");
    var rotation = frame.GetProperty("rotation");
    capture.Invoke(null, new object[] {
        camera,
        new XEngine.Vector.Float3(position[0].GetSingle(), position[1].GetSingle(), position[2].GetSingle()),
        new XEngine.Vector.Quaternion(rotation[0].GetSingle(), rotation[1].GetSingle(),
            rotation[2].GetSingle(), rotation[3].GetSingle()),
        960, 540, System.IO.Path.Combine(output, name + ".png") });
}

// RenderStats only accumulates when a frame is bracketed (Scene.Render does it; the offline
// camera render does not), so run one more framed render for the stats line.
XEngine.Runtime.Rendering.RenderStats.BeginFrame();
var statsFrameData = frames.GetProperty("mid");
var midPosition = statsFrameData.GetProperty("position");
var midRotation = statsFrameData.GetProperty("rotation");
capture.Invoke(null, new object[] {
    camera,
    new XEngine.Vector.Float3(midPosition[0].GetSingle(), midPosition[1].GetSingle(), midPosition[2].GetSingle()),
    new XEngine.Vector.Quaternion(midRotation[0].GetSingle(), midRotation[1].GetSingle(),
        midRotation[2].GetSingle(), midRotation[3].GetSingle()),
    960, 540, System.IO.Path.Combine(output, "stats.png") });
XEngine.Runtime.Rendering.RenderStats.EndFrame();

var sb = new System.Text.StringBuilder();
sb.Append("frames: close/mid/panorama -> Export/scene/xengine-frames (materials ").Append(materials).Append(")\n");
var statsFrame = XEngine.Runtime.Rendering.RenderStats.Last;
sb.Append("drawCalls: ").Append(statsFrame.DrawCalls)
  .Append(", triangles: ").Append(statsFrame.Triangles)
  .Append(", renderables drawn: ").Append(statsFrame.RenderablesDrawn)
  .Append(" (culled ").Append(statsFrame.RenderablesCulled)
  .Append(", lod-hidden ").Append(statsFrame.LodRenderersHidden).Append(")\n");
sb.Append("shadow drawCalls: ").Append(statsFrame.ShadowDrawCalls)
  .Append(", shadow triangles: ").Append(statsFrame.ShadowTriangles).Append('\n');
return sb.ToString();
}

try {
    return Run();
} catch (System.Exception error) {
    return "FAILED: " + error.ToString();
}
