// Runs the clip builder inside the live editor and returns its summary.
//
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 300 --eval-file Tools/xengine_live/evals/generate_clips.cs
//     XEngine.Editor --refresh-assets --project <root>      (imports the generated .anim / .controller files)
//
// Project types are not referenceable by name from runtime_eval (scripts load in their own
// collectible context), hence reflection.
var sb = new System.Text.StringBuilder();
System.Type builder = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoClipBuilder");
    if (type != null) { builder = type; break; }
}
if (builder == null) return "SanguoClipBuilder is not loaded: the Editor assembly did not compile.";
object[] noArguments = new object[0];
try {
    string export = (string)builder.GetMethod("ExportDirectory").Invoke(null, noArguments);
    sb.Append("export: ").Append(export).Append('\n');
    sb.Append((string)builder.GetMethod("Generate").Invoke(null, new object[] { export }));
} catch (System.Reflection.TargetInvocationException error) {
    sb.Append("FAILED: ").Append(error.InnerException != null ? error.InnerException.ToString() : error.ToString());
}
return sb.ToString();
