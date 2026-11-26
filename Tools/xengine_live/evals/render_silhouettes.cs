// Runs SanguoCaptureStudio.CaptureSilhouettes inside the live editor: every skinned mesh of every
// hero alone as a flat silhouette (reflection: project types are not referenceable by name from
// runtime_eval). The Unity side is Tools/unity_export/scripts/capture_silhouettes.csx; both are
// compared by Tools/sanguo_import/compare_skin.py --silhouettes.
//
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 300 --eval-file Tools/xengine_live/evals/render_silhouettes.cs
//     SANGUO_CAPTURE_OUT=Export/xengine-shots/silhouettes-d3d12 py -3 ... --graphics d3d12 ...   (another backend)
var sb = new System.Text.StringBuilder();
System.Type studio = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoCaptureStudio");
    if (type != null) { studio = type; break; }
}
if (studio == null) return "SanguoCaptureStudio is not loaded: the Editor assembly did not compile.";
string root = XEngine.Editor.Projects.Project.Current.RootPath;
string shots = System.IO.Path.Combine(root, (string)studio.GetField("UnityShotsRelativePath").GetValue(null));
string output = System.IO.Path.Combine(root, (string)studio.GetField("DefaultSilhouetteRelativePath").GetValue(null));
string redirect = System.Environment.GetEnvironmentVariable("SANGUO_CAPTURE_OUT");
if (!string.IsNullOrEmpty(redirect)) output = System.IO.Path.Combine(root, redirect);
sb.Append("backend: ").Append(XEngine.Runtime.Graphics.Device != null ? XEngine.Runtime.Graphics.Device.Backend.ToString() : "<none>").Append('\n');
try {
    sb.Append(studio.GetMethod("CaptureSilhouettes").Invoke(null, new object[] { shots, output }));
} catch (System.Reflection.TargetInvocationException error) {
    sb.Append("FAILED: ").Append(error.InnerException != null ? error.InnerException.ToString() : error.ToString());
}
return sb.ToString();
