// In play mode (TaoYuanBattle open): renders the live battle camera to a PNG and returns the
// frame stats. Expects the editor already in play mode (enter_play.cs ran first).

var scene = XEngine.Runtime.Resources.Scene.Current;
if (scene == null || scene.Name != "TaoYuanBattle") return "TaoYuanBattle is not the current scene (playing=" + XEngine.Runtime.Application.IsPlaying + ").";

XEngine.Runtime.Camera? battleCamera = null;
int drivers = 0;
System.Type driverType = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var t = assembly.GetType("SanGuoXEngine.Battle.SanguoBattleDriver");
    if (t != null) { driverType = t; break; }
}
foreach (var go in scene.AllObjects) {
    foreach (var component in go.GetComponents()) {
        if (component is XEngine.Runtime.Camera c && go.Name == "Battle Camera" && battleCamera == null) battleCamera = c;
        if (driverType != null && driverType.IsInstanceOfType(component)) drivers++;
    }
}
if (battleCamera == null) return "no Battle Camera found.";

string root = XEngine.Editor.Projects.Project.Current.RootPath;
string output = System.IO.Path.Combine(root, "Export/scene/xengine-frames/play-battle.png");
System.Type studio = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoCaptureStudio");
    if (type != null) { studio = type; break; }
}
var position = battleCamera.Transform.Position;
var rotation = battleCamera.Transform.Rotation;
studio.GetMethod("CaptureSceneFrame").Invoke(null, new object[] {
    battleCamera, position, rotation, 960, 540, output });

var stats = XEngine.Runtime.Rendering.RenderStats.Last;
return "captured play-battle.png | drivers " + drivers
    + " | drawCalls " + stats.DrawCalls + " tris " + stats.Triangles
    + " drawn " + stats.RenderablesDrawn + " lodHidden " + stats.LodRenderersHidden;
