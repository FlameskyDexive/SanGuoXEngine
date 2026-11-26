// Enters play mode with TaoYuan open so the live GameView renders the scene camera every frame.

System.Type manager = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("XEngine.Editor.GUI.SceneView.EditorSceneManager");
    if (type != null) { manager = type; break; }
}
if (manager == null) return "EditorSceneManager not found.";
bool opened = (bool)manager.GetMethod("OpenScene").Invoke(null, new object[] { "Scenes/TaoYuan.scene" });
if (!opened) return "OpenScene returned false.";

System.Type app = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("XEngine.Editor.Core.EditorApplication");
    if (type != null) { app = type; break; }
}
if (app == null) return "EditorApplication not found.";
// EnterPlayMode is an INSTANCE method on the EditorApplication singleton.
var enter = app.GetMethod("EnterPlayMode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
if (enter == null) return "EnterPlayMode not found.";
object? instance = app.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null);
if (instance == null) {
    var instanceField = app.GetField("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    instance = instanceField != null ? instanceField.GetValue(null) : null;
}
if (instance == null) return "EditorApplication.Instance not found.";
enter.Invoke(instance, null);
return "entering play mode";
