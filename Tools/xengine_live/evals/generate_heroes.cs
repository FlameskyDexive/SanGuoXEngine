// Runs the hero generators inside the live editor and returns their summaries.
//
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 300 --eval-file Tools/xengine_live/evals/generate_heroes.cs
//     XEngine.Editor --refresh-assets --project <root>      (imports the generated .mat / .prefab files)
//
// Project types are not referenceable by name from runtime_eval (scripts load in their own
// collectible context), hence reflection.
var sb = new System.Text.StringBuilder();
System.Type generator = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoHeroGenerator");
    if (type != null) { generator = type; break; }
}
if (generator == null) return "SanguoHeroGenerator is not loaded: the Editor assembly did not compile.";
object[] noArguments = new object[0];
string manifest = (string)generator.GetMethod("FullManifestPath").Invoke(null, noArguments);
sb.Append("manifest: ").Append(manifest).Append('\n');
foreach (string name in new string[] { "GenerateMaterials", "GeneratePrefabs" }) {
    sb.Append(name).Append(": ");
    try {
        sb.Append(generator.GetMethod(name).Invoke(null, new object[] { manifest }));
    } catch (System.Reflection.TargetInvocationException error) {
        sb.Append("FAILED: ").Append(error.InnerException != null ? error.InnerException.ToString() : error.ToString());
    }
    sb.Append('\n');
}
return sb.ToString();
