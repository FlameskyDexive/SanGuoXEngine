// In play mode: moves the scene camera to one of the three Unity reference poses. The argument
// arrives via a tiny state file so the same eval serves all three frames: write
// Export/scene/pose.txt with the frame name before calling this eval.

string root = XEngine.Editor.Projects.Project.Current.RootPath;
string posePath = System.IO.Path.Combine(root, "Export/scene/pose.txt");
string name = System.IO.File.Exists(posePath) ? System.IO.File.ReadAllText(posePath).Trim() : "close";

var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
    System.IO.Path.Combine(root, "Export/scene/scene.json")));
var frame = document.RootElement.GetProperty("frames").GetProperty(name);
var position = frame.GetProperty("position");
var rotation = frame.GetProperty("rotation");

var scene = XEngine.Runtime.Resources.Scene.Current;
if (scene == null) return "no current scene (play mode not running?).";
XEngine.Runtime.Camera? camera = null;
foreach (var go in scene.AllObjects)
    foreach (var component in go.GetComponents())
        if (component is XEngine.Runtime.Camera c) { camera = c; break; }
if (camera == null) return "no camera.";
camera.Transform.Position = new XEngine.Vector.Float3(position[0].GetSingle(), position[1].GetSingle(), position[2].GetSingle());
camera.Transform.Rotation = new XEngine.Vector.Quaternion(rotation[0].GetSingle(), rotation[1].GetSingle(),
    rotation[2].GetSingle(), rotation[3].GetSingle());

var sb = "posed " + name + " at " + camera.Transform.Position.X + "," + camera.Transform.Position.Y
    + "," + camera.Transform.Position.Z + " playing=" + XEngine.Runtime.Application.IsPlaying;
return sb;
