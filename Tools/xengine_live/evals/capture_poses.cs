// Runs SanguoCaptureStudio.CapturePoses inside the live editor (reflection: project types are not
// referenceable by name from runtime_eval) and returns its summary: every exported clip played on
// the real Animator, poses dumped next to Unity's samples, frames rendered where Unity rendered.
//
//     py -3 Tools/unity_export/run_export.py run export_clips --filter Hero_LvBu --out Export/clips   (per hero)
//     py -3 Tools/xengine_live/mcp_live.py --wait-compile 300 --eval-file Tools/xengine_live/evals/capture_poses.cs
//     SANGUO_POSE_OUT=Export/xengine-shots/clips-d3d12 py -3 ... --graphics d3d12 ...   (another backend)
var sb = new System.Text.StringBuilder();
System.Type studio = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("SanGuoXEngine.Editor.SanguoCaptureStudio");
    if (type != null) { studio = type; break; }
}
if (studio == null) return "SanguoCaptureStudio is not loaded: the Editor assembly did not compile.";
string root = XEngine.Editor.Projects.Project.Current.RootPath;
string shots = System.IO.Path.Combine(root, (string)studio.GetField("UnityShotsRelativePath").GetValue(null));
string clips = System.IO.Path.Combine(root, (string)studio.GetField("ClipsRelativePath").GetValue(null));
string output = System.IO.Path.Combine(root, (string)studio.GetField("DefaultPoseOutputRelativePath").GetValue(null));
// SANGUO_POSE_OUT=<dir under the project root> redirects the frames and poses, e.g. per backend.
string redirect = System.Environment.GetEnvironmentVariable("SANGUO_POSE_OUT");
if (!string.IsNullOrEmpty(redirect)) output = System.IO.Path.Combine(root, redirect);
sb.Append("shots: ").Append(shots).Append('\n');
sb.Append("clips: ").Append(clips).Append('\n');
try {
    sb.Append(studio.GetMethod("CapturePoses").Invoke(null, new object[] { shots, clips, output }));
} catch (System.Reflection.TargetInvocationException error) {
    sb.Append("FAILED: ").Append(error.InnerException != null ? error.InnerException.ToString() : error.ToString());
}
return sb.ToString();
