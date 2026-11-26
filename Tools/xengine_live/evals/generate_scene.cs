// Runs SanguoSceneBuilder.GenerateScene inside the live editor (reflection: project types are not
// referenceable by name from runtime_eval) and returns its summary.
//
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 300 --eval-file Tools/xengine_live/evals/generate_scene.cs
//     XEngine.Editor --refresh-assets --project <root>      (imports the generated .scene / .mat / .mesh files)

System.Type builder = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoSceneBuilder");
    if (type != null) { builder = type; break; }
}
if (builder == null) return "SanguoSceneBuilder is not loaded: the Editor assembly did not compile.";
object[] noArguments = new object[0];
try {
    object result = builder.GetMethod("GenerateScene").Invoke(null, noArguments);
    return result == null ? "(null)" : result.ToString();
} catch (System.Reflection.TargetInvocationException error) {
    return "FAILED: " + (error.InnerException != null ? error.InnerException.ToString() : error.ToString());
}
