// Runs SanguoDataGenerator.GenerateData in the live editor (reflection: project types are not
// referenceable by name from runtime_eval) and returns its summary.
//
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 240 --eval-file Tools/xengine_live/evals/generate_data.cs

System.Type generator = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoDataGenerator");
    if (type != null) { generator = type; break; }
}
if (generator == null) return "SanguoDataGenerator is not loaded: the Editor assembly did not compile.";
string root = XEngine.Editor.Projects.Project.Current.RootPath;
string jsonFolder = System.IO.Path.Combine(root, "Export/data");
string assetsFolder = System.IO.Path.Combine(XEngine.Editor.Projects.Project.Current.AssetsPath, "Data");
try {
    object result = generator.GetMethod("GenerateData").Invoke(null, new object[] { jsonFolder, assetsFolder });
    return result == null ? "(null)" : result.ToString();
} catch (System.Reflection.TargetInvocationException error) {
    return "FAILED: " + (error.InnerException != null ? error.InnerException.ToString() : error.ToString());
}
