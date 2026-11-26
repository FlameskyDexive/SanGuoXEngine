// Renders ONLY the panorama frame and echoes the camera's world pose before/after, to check
// whether CaptureSceneFrame's pose assignment actually sticks.

System.Type manager = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("XEngine.Editor.GUI.SceneView.EditorSceneManager");
    if (type != null) { manager = type; break; }
}
bool opened = (bool)manager.GetMethod("OpenScene").Invoke(null, new object[] { "Scenes/TaoYuan.scene" });
if (!opened) return "OpenScene returned false.";

System.Type studio = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoCaptureStudio");
    if (type != null) { studio = type; break; }
}

var scene = XEngine.Runtime.Resources.Scene.Current;
XEngine.Runtime.Camera? camera = null;
foreach (var go in scene.AllObjects)
    foreach (var component in go.GetComponents())
        if (component is XEngine.Runtime.Camera c) { camera = c; break; }

var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
    System.IO.Path.Combine(XEngine.Editor.Projects.Project.Current.RootPath, "Export/scene/scene.json")));
var frame = document.RootElement.GetProperty("frames").GetProperty("panorama");
var position = frame.GetProperty("position");
var rotation = frame.GetProperty("rotation");

var sb = new System.Text.StringBuilder();
var before = camera.Transform.Position;
sb.Append("before: ").Append(before.X).Append(',').Append(before.Y).Append(',').Append(before.Z).Append('\n');

studio.GetMethod("CaptureSceneFrame").Invoke(null, new object[] {
    camera,
    new XEngine.Vector.Float3(position[0].GetSingle(), position[1].GetSingle(), position[2].GetSingle()),
    new XEngine.Vector.Quaternion(rotation[0].GetSingle(), rotation[1].GetSingle(),
        rotation[2].GetSingle(), rotation[3].GetSingle()),
    960, 540, System.IO.Path.Combine(XEngine.Editor.Projects.Project.Current.RootPath, "Export/scene/xengine-frames/panorama-probe.png") });

var after = camera.Transform.Position;

// Wildly different pose: 1000 m up, looking straight down.
studio.GetMethod("CaptureSceneFrame").Invoke(null, new object[] {
    camera,
    new XEngine.Vector.Float3(100f, 1000f, 500f),
    new XEngine.Vector.Quaternion(0.7071068f, 0f, 0f, 0.7071068f),
    960, 540, System.IO.Path.Combine(XEngine.Editor.Projects.Project.Current.RootPath, "Export/scene/xengine-frames/overhead-probe.png") });
sb.Append("after: ").Append(after.X).Append(',').Append(after.Y).Append(',').Append(after.Z).Append('\n');
return sb.ToString();
