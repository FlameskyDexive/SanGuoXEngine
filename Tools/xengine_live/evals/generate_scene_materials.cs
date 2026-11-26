// Runs the scene material generator inside the live editor and returns its summary.
//
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 300 --eval-file Tools/xengine_live/evals/generate_scene_materials.cs
//     XEngine.Editor --refresh-assets --project <root>      (imports the generated .mat files)
var sb = new System.Text.StringBuilder();
System.Type generator = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoSceneGenerator");
    if (type != null) { generator = type; break; }
}
if (generator == null) return "SanguoSceneGenerator is not loaded: the Editor assembly did not compile.";
object[] noArguments = new object[0];
string manifest = (string)generator.GetMethod("FullManifestPath").Invoke(null, noArguments);
sb.Append("manifest: ").Append(manifest).Append('\n');
try {
    sb.Append((string)generator.GetMethod("GenerateSceneMaterials").Invoke(null, new object[] { manifest }));
} catch (System.Reflection.TargetInvocationException error) {
    sb.Append("FAILED: ").Append(error.InnerException != null ? error.InnerException.ToString() : error.ToString());
}
return sb.ToString();
