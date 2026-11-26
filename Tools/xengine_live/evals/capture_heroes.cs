// Runs SanguoCaptureStudio.CaptureHeroes inside the live editor (reflection: project types are not
// referenceable by name from runtime_eval) and returns its summary.
//
//     py -3 Tools/unity_export/run_export.py run capture_heroes --out Export/unity-refs/heroes
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 300 --eval-file Tools/xengine_live/evals/capture_heroes.cs
//     SANGUO_CAPTURE_OUT=Export/xengine-shots/heroes-d3d12 py -3 ... --graphics d3d12 ...   (another backend)
var sb = new System.Text.StringBuilder();
System.Type studio = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoCaptureStudio");
    if (type != null) { studio = type; break; }
}
if (studio == null) return "SanguoCaptureStudio is not loaded: the Editor assembly did not compile.";
string root = XEngine.Editor.Projects.Project.Current.RootPath;
string shots = System.IO.Path.Combine(root, "SanGuoXEngine.Editor.SanguoCaptureStudio.Shots");
shots = (string)studio.GetField("UnityShotsRelativePath").GetValue(null);
shots = System.IO.Path.Combine(root, shots);
string output = System.IO.Path.Combine(root, (string)studio.GetField("DefaultOutputRelativePath").GetValue(null));
// SANGUO_CAPTURE_OUT=<dir under the project root> redirects the frames, e.g. per graphics backend.
string redirect = System.Environment.GetEnvironmentVariable("SANGUO_CAPTURE_OUT");
if (!string.IsNullOrEmpty(redirect)) output = System.IO.Path.Combine(root, redirect);
sb.Append("shots:  ").Append(shots).Append('\n');
try {
    sb.Append(studio.GetMethod("CaptureHeroes").Invoke(null, new object[] { shots, output }));
} catch (System.Reflection.TargetInvocationException error) {
    sb.Append("FAILED: ").Append(error.InnerException != null ? error.InnerException.ToString() : error.ToString());
}
return sb.ToString();
