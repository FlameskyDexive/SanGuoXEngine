
// SanGuoXEngine M3 probe: how Unity picks a LOD level, measured outside play mode. Every renderer
// of a sample group's LOD levels gets a temporary flat unlit material (red / green / blue per
// level) and a temporary camera renders the group from several distances, fields of view and
// aspect ratios; the colour that reaches the frame tells the selected level. Unity updates LOD
// selection on its own schedule, so every sample takes several editor ticks: the job schedules
// itself on EditorApplication.update, writes probe_lod_select_job.json into the export folder when
// done, and then restores the scenes that were open before. Nothing is saved.
int Height = 96;
float[] Fovs = new float[] { 30f, 60f };
float[] Aspects = new float[] { 1f, 1.777f };
int SampleGroups = 12;
string outDir = "__OUT_DIR__";
string target = System.IO.Path.Combine(outDir, "probe_lod_select_job.json");
System.IO.Directory.CreateDirectory(outDir);
if (System.IO.File.Exists(target)) System.IO.File.Delete(target);
string scenePath = "Assets/Scenes/GameScenes/C-ZhuoXian_TaoYuan.unity";

__sb.Append('{');
if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) { __P("scheduled", __B(false)); __P("reason", __S("play mode")); __END(); return __sb.ToString(); }
int openCount = UnityEditor.SceneManagement.EditorSceneManager.sceneCount;
string[] previous = new string[openCount];
bool anyDirty = false;
for (int i = 0; i < openCount; i++) {
    UnityEngine.SceneManagement.Scene open = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
    previous[i] = open.path;
    if (open.isDirty) anyDirty = true;
}
if (anyDirty) { __P("scheduled", __B(false)); __P("reason", __S("an open scene has unsaved changes")); __END(); return __sb.ToString(); }

try {
    UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,
        UnityEditor.SceneManagement.OpenSceneMode.Single);
    UnityEngine.GameObject root = scene.GetRootGameObjects()[0];
    UnityEngine.Shader flat = UnityEngine.Shader.Find("Unlit/Color");
    if (flat == null) throw new System.Exception("Unlit/Color shader not found");
    UnityEngine.Color[] palette = new UnityEngine.Color[] {
        new UnityEngine.Color(1f, 0f, 0f, 1f), new UnityEngine.Color(0f, 1f, 0f, 1f), new UnityEngine.Color(0f, 0f, 1f, 1f) };
    UnityEngine.Material[] flats = new UnityEngine.Material[palette.Length];
    for (int i = 0; i < flats.Length; i++) {
        flats[i] = new UnityEngine.Material(flat);
        flats[i].SetColor("_Color", palette[i]);
    }
    UnityEngine.GameObject cameraObject = new UnityEngine.GameObject("LOD Probe Camera");
    UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
    camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    camera.backgroundColor = UnityEngine.Color.black;
    camera.nearClipPlane = 0.3f;
    camera.farClipPlane = 4000f;
    camera.allowMSAA = false;
    UnityEngine.RenderTexture renderTarget = new UnityEngine.RenderTexture(96, Height, 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
    UnityEngine.Texture2D frame = new UnityEngine.Texture2D(96, Height, UnityEngine.TextureFormat.RGB24, false, false);
    camera.targetTexture = renderTarget;

    // Sample groups: up to three levels, one renderer per level, spread over the scene.
    UnityEngine.LODGroup[] all = root.GetComponentsInChildren<UnityEngine.LODGroup>();
    System.Collections.Generic.List<UnityEngine.LODGroup> chosen = new System.Collections.Generic.List<UnityEngine.LODGroup>();
    for (int g = 0; g < all.Length && chosen.Count < SampleGroups; g += 9) {
        UnityEngine.LOD[] lods = all[g].GetLODs();
        if (lods.Length < 2) continue;
        bool simple = true;
        foreach (UnityEngine.LOD lod in lods) if (lod.renderers.Length != 1 || lod.renderers[0] == null) simple = false;
        if (simple) chosen.Add(all[g]);
    }
    int count = chosen.Count;
    UnityEngine.LOD[][] lodSets = new UnityEngine.LOD[count][];
    UnityEngine.Renderer[][] renderers = new UnityEngine.Renderer[count][];
    UnityEngine.Material[][][] saved = new UnityEngine.Material[count][][];
    UnityEngine.Vector3[] references = new UnityEngine.Vector3[count];
    float[] worldSizes = new float[count];
    for (int g = 0; g < count; g++) {
        UnityEngine.LODGroup group = chosen[g];
        lodSets[g] = group.GetLODs();
        renderers[g] = new UnityEngine.Renderer[lodSets[g].Length];
        saved[g] = new UnityEngine.Material[lodSets[g].Length][];
        for (int i = 0; i < lodSets[g].Length; i++) {
            UnityEngine.Renderer r = lodSets[g][i].renderers[0];
            renderers[g][i] = r;
            saved[g][i] = r.sharedMaterials;
            UnityEngine.Material[] flatSet = new UnityEngine.Material[r.sharedMaterials.Length];
            for (int m = 0; m < flatSet.Length; m++) flatSet[m] = flats[i];
            r.sharedMaterials = flatSet;
        }
        references[g] = group.transform.TransformPoint(group.localReferencePoint);
        UnityEngine.Vector3 lossy = group.transform.lossyScale;
        worldSizes[g] = group.size * UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(lossy.x), UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(lossy.y), UnityEngine.Mathf.Abs(lossy.z)));
    }
    UnityEngine.Vector3 direction = new UnityEngine.Vector3(0.55f, 0.45f, 0.70f).normalized;
    float[] steps = new float[] { 0.8f, 1f, 1.25f, 1.6f, 2f, 2.5f, 3.1f, 3.9f, 4.9f, 6.1f, 7.6f, 9.5f, 12f, 15f, 19f, 23f, 29f, 36f, 45f, 57f, 71f, 89f, 111f, 139f, 173f, 216f };
    System.Text.StringBuilder result = new System.Text.StringBuilder();
    result.Append("{\"lodBias\":").Append(__F(UnityEngine.QualitySettings.lodBias)).Append(",\"groups\":[");
    int gIndex = 0, fIndex = 0, aIndex = 0, sIndex = 0, wait = 0;
    bool moved = false, firstSample = true, groupWritten = false;
    UnityEditor.EditorApplication.CallbackFunction tick = null;
    tick = delegate {
        try {
            if (gIndex >= count) {
                UnityEditor.EditorApplication.update -= tick;
                result.Append("]}]}");
                System.IO.File.WriteAllText(target, result.ToString());
                for (int g = 0; g < count; g++) for (int i = 0; i < renderers[g].Length; i++) renderers[g][i].sharedMaterials = saved[g][i];
                UnityEngine.Object.DestroyImmediate(frame);
                renderTarget.Release();
                UnityEngine.Object.DestroyImmediate(renderTarget);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                foreach (UnityEngine.Material material in flats) UnityEngine.Object.DestroyImmediate(material);
                bool restored = false;
                for (int i = 0; i < previous.Length; i++) {
                    if (string.IsNullOrEmpty(previous[i])) continue;
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previous[i], restored ? UnityEditor.SceneManagement.OpenSceneMode.Additive : UnityEditor.SceneManagement.OpenSceneMode.Single);
                    restored = true;
                }
                if (!restored) UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                return;
            }
            if (!moved) {
                if (!groupWritten) {
                    if (gIndex > 0) result.Append("]},");
                    result.Append("{\"path\":").Append(__S(chosen[gIndex].transform.name)).Append(",\"worldSize\":").Append(__F(worldSizes[gIndex])).Append(",\"thresholds\":[");
                    for (int i = 0; i < lodSets[gIndex].Length; i++) { if (i > 0) result.Append(','); result.Append(__F(lodSets[gIndex][i].screenRelativeTransitionHeight)); }
                    result.Append("],\"samples\":[");
                    groupWritten = true;
                    firstSample = true;
                }
                float distance = UnityEngine.Mathf.Max(0.5f, worldSizes[gIndex] * steps[sIndex]);
                camera.fieldOfView = Fovs[fIndex];
                renderTarget.width = (int)(Height * Aspects[aIndex]);
                camera.aspect = Aspects[aIndex];
                camera.transform.position = references[gIndex] + direction * distance;
                camera.transform.LookAt(references[gIndex]);
                moved = true;
                wait = 4;
                return;
            }
            if (wait > 0) { wait--; return; }
            camera.Render();
            UnityEngine.RenderTexture.active = renderTarget;
            frame.Resize(renderTarget.width, Height);
            frame.ReadPixels(new UnityEngine.Rect(0, 0, renderTarget.width, Height), 0, 0);
            frame.Apply();
            UnityEngine.RenderTexture.active = null;
            UnityEngine.Color32[] pixels = frame.GetPixels32();
            // Only the middle of the frame: the camera looks at the group's reference point, and at
            // large distances other painted groups enter the frame at its sides.
            int[] counts = new int[3];
            int centerX = renderTarget.width / 2, centerY = Height / 2, radius = 10;
            for (int y = centerY - radius; y <= centerY + radius; y++) {
                for (int x = centerX - radius; x <= centerX + radius; x++) {
                    UnityEngine.Color32 c = pixels[y * renderTarget.width + x];
                    if (c.r > 160 && c.g < 90 && c.b < 90) counts[0]++;
                    else if (c.g > 160 && c.r < 90 && c.b < 90) counts[1]++;
                    else if (c.b > 160 && c.r < 90 && c.g < 90) counts[2]++;
                }
            }
            int level = -1; int best = 0;
            for (int i = 0; i < 3; i++) if (counts[i] > best) { best = counts[i]; level = i; }
            System.Text.StringBuilder flags = new System.Text.StringBuilder();
            for (int i = 0; i < renderers[gIndex].Length; i++) {
                if (i > 0) flags.Append(',');
                flags.Append(renderers[gIndex][i].isVisible ? "true" : "false");
            }
            if (!firstSample) result.Append(',');
            firstSample = false;
            result.Append("{\"fov\":").Append(__F(Fovs[fIndex])).Append(",\"aspect\":").Append(__F(Aspects[aIndex]))
                .Append(",\"distance\":").Append(__F(UnityEngine.Mathf.Max(0.5f, worldSizes[gIndex] * steps[sIndex])))
                .Append(",\"level\":").Append(level).Append(",\"pixels\":").Append(best).Append(",\"visible\":[").Append(flags.ToString()).Append("]}");
            sIndex++;
            if (sIndex >= steps.Length) { sIndex = 0; aIndex++; if (aIndex >= Aspects.Length) { aIndex = 0; fIndex++; if (fIndex >= Fovs.Length) { fIndex = 0; gIndex++; groupWritten = false; } } }
            moved = false;
        } catch (System.Exception e) {
            UnityEditor.EditorApplication.update -= tick;
            System.IO.File.WriteAllText(target, "{\"error\":" + __S(e.ToString()) + "}");
        }
    };
    UnityEditor.EditorApplication.update += tick;
    __P("scheduled", __B(true));
    __P("groups", count.ToString());
    __P("samples", (count * Fovs.Length * Aspects.Length * 26).ToString());
    __P("file", __S("probe_lod_select_job.json"));
} catch (System.Exception e) {
    __EX("probe", e);
}
__END();
return __sb.ToString();
