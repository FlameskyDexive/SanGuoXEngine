// Builds the battle scene variant (TaoYuan + SanguoBattleDriver) via reflection.

System.Type builder = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoSceneBuilder");
    if (type != null) { builder = type; break; }
}
if (builder == null) return "SanguoSceneBuilder is not loaded.";
try {
    object result = builder.GetMethod("GenerateBattleScene").Invoke(null, null);
    return result == null ? "(null)" : result.ToString();
} catch (System.Reflection.TargetInvocationException error) {
    return "FAILED: " + (error.InnerException != null ? error.InnerException.ToString() : error.ToString());
}
