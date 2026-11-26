// Opens TaoYuanBattle, makes GameView visible, and enters Play mode for baseline captures.

System.Type manager = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("XEngine.Editor.GUI.SceneView.EditorSceneManager");
    if (type != null) { manager = type; break; }
}
if (manager == null) return "EditorSceneManager not found.";
bool opened = (bool)manager.GetMethod("OpenScene").Invoke(null, new object[] { "Scenes/TaoYuanBattle.scene" });
if (!opened) return "OpenScene returned false.";

System.Type gameViewPanel = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("XEngine.Editor.GUI.Panels.GameViewPanel");
    if (type != null) { gameViewPanel = type; break; }
}
if (gameViewPanel == null) return "GameViewPanel type not found.";
var openMethod = gameViewPanel.GetMethod("Open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
if (openMethod != null) openMethod.Invoke(null, null);

System.Type app = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("XEngine.Editor.Core.EditorApplication");
    if (type != null) { app = type; break; }
}
if (app == null) return "EditorApplication not found.";
var instance = app.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null);
if (instance == null) return "EditorApplication.Instance not found.";
var enter = app.GetMethod("EnterPlayMode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
if (enter == null) return "EnterPlayMode not found.";
enter.Invoke(instance, null);
return "entering TaoYuanBattle play mode";
