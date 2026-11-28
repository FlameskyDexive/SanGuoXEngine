
// SanGuoXEngine M2 export: the battle clips of one hero — every curve with its keys, tangents and
// weights, the clip settings and events, Unity's own sampled pose at fixed times, and a reference
// frame at three of those times (docs/evidence/m2). Read-only for the project: the hero is posed in
// a temporary empty scene through AnimationMode; the scenes open before, and the quality level, are
// restored afterwards.
//
// The frames use the camera of the M1 hero shots (Export/unity-refs/heroes/shots.json), so the
// engine reproduces the same framing. run_export.py fills in __NAME_FILTER__ (the hero) and
// __CLIP_LIST__ (the clips to take, resolved from Tools/unity_export/clip_manifest.py).
string hero = "__NAME_FILTER__";
string[] clipNames = new string[] { __CLIP_LIST__ };
string animFolder = "__ANIM_FOLDER__";
int Width = 512;
int Height = 768;
float VerticalFov = 30f;
int SampleCount = 10;                        // sampled times besides the start pose
float SamplePhase = 0.37f;                   // off the frame grid, so interpolation is exercised too
int[] ShotSamples = new int[] { 1, 4, 7 };   // which of the sampled times get a frame
UnityEngine.Vector3 LightEuler = new UnityEngine.Vector3(50f, 145f, 0f);   // as the M1 hero shots
UnityEngine.Color Ambient = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
UnityEngine.Color Background = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);

string outDir = "__OUT_DIR__";
string repoRoot = "__REPO_ROOT__";
System.IO.Directory.CreateDirectory(outDir);

// ---- JSON pieces (pure: they only return strings) ----
string __F3(UnityEngine.Vector3 v) { return "[" + __F(v.x) + "," + __F(v.y) + "," + __F(v.z) + "]"; }
string __Guid(UnityEngine.Object o) {
    if (o == null) return "null";
    string assetPath = UnityEditor.AssetDatabase.GetAssetPath(o);
    string guid = assetPath != null ? UnityEditor.AssetDatabase.AssetPathToGUID(assetPath) : "";
    return __S(guid);
}
// Path of a transform relative to the hero root; the root itself is "".
string RelPath(UnityEngine.Transform root, UnityEngine.Transform t) {
    if (t == root) return "";
    string p = t.name;
    for (UnityEngine.Transform c = t.parent; c != null && c != root; c = c.parent) p = c.name + "/" + p;
    return p;
}
void AppendPose(System.Text.StringBuilder sb, UnityEngine.Transform t) {
    sb.Append(__F3(t.localPosition)).Append(',').Append(__Q4(t.localRotation)).Append(',').Append(__F3(t.localScale));
}
void EmitCurve(System.Text.StringBuilder sb, UnityEngine.AnimationClip clip, UnityEditor.EditorCurveBinding binding) {
    UnityEngine.AnimationCurve curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding);
    sb.Append("{\"path\":").Append(__S(binding.path))
        .Append(",\"property\":").Append(__S(binding.propertyName))
        .Append(",\"type\":").Append(__S(binding.type != null ? binding.type.FullName : null))
        .Append(",\"preWrap\":").Append(curve != null ? ((int)curve.preWrapMode).ToString() : "0")
        .Append(",\"postWrap\":").Append(curve != null ? ((int)curve.postWrapMode).ToString() : "0")
        .Append(",\"keys\":[");
    if (curve != null) {
        UnityEngine.Keyframe[] keys = curve.keys;
        for (int k = 0; k < keys.Length; k++) {
            if (k > 0) sb.Append(',');
            UnityEngine.Keyframe key = keys[k];
            // time, value, inTangent, outTangent, inWeight, outWeight, weightedMode
            sb.Append('[').Append(__F(key.time)).Append(',').Append(__F(key.value)).Append(',')
                .Append(__F(key.inTangent)).Append(',').Append(__F(key.outTangent)).Append(',')
                .Append(__F(key.inWeight)).Append(',').Append(__F(key.outWeight)).Append(',')
                .Append(((int)key.weightedMode).ToString()).Append(']');
        }
    }
    sb.Append("]}");
}
string ClipAction(string clipName) { return clipName.StartsWith(hero + "_") ? clipName.Substring(hero.Length + 1) : clipName; }
// The contents of the first [..] between two offsets of a JSON text.
string Bracketed(string text, int from, int to) {
    int open = text.IndexOf('[', from);
    if (open < 0 || open > to) return "";
    int close = text.IndexOf(']', open);
    if (close < 0 || close > to) return "";
    return text.Substring(open + 1, close - open - 1);
}

__sb.Append('{');
if (hero.Length == 0 || clipNames.Length == 0) {
    __P("exported", __B(false));
    __P("reason", __S("run this script through run_export.py (it supplies the hero and the clip list)"));
    __END();
    return __sb.ToString();
}
if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
    __P("exported", __B(false));
    __P("reason", __S("the editor is in (or entering) play mode"));
    __END();
    return __sb.ToString();
}
int openCount = UnityEditor.SceneManagement.EditorSceneManager.sceneCount;
string[] previous = new string[openCount];
bool anyDirty = false;
for (int i = 0; i < openCount; i++) {
    UnityEngine.SceneManagement.Scene open = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
    previous[i] = open.path;
    if (open.isDirty) anyDirty = true;
}
if (anyDirty) {
    __P("exported", __B(false));
    __P("reason", __S("an open scene has unsaved changes; the editor was left untouched"));
    __END();
    return __sb.ToString();
}

string prefabPath = "__PREFAB_PATH__";
UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
if (prefab == null) {
    __P("exported", __B(false));
    __P("reason", __S("no monster prefab at " + prefabPath));
    __END();
    return __sb.ToString();
}

// The camera of the M1 hero shots, when they exist (they are taken before this export).
UnityEngine.Vector3 cameraPosition = new UnityEngine.Vector3(0f, 1f, 5f);
UnityEngine.Quaternion cameraRotation = UnityEngine.Quaternion.identity;
bool cameraFromShots = false;
string shotsPath = System.IO.Path.Combine(repoRoot, "Export/unity-refs/heroes/shots.json");
if (System.IO.File.Exists(shotsPath)) {
    try {
        string text = System.IO.File.ReadAllText(shotsPath);
        // A tiny scan is enough: find this hero's entry and read its camera.
        int at = text.IndexOf("\"name\": \"" + hero + "\"", System.StringComparison.Ordinal);
        if (at < 0) at = text.IndexOf("\"name\":\"" + hero + "\"", System.StringComparison.Ordinal);
        if (at >= 0) {
            int posAt = text.IndexOf("\"cameraPosition\"", at, System.StringComparison.Ordinal);
            int rotAt = text.IndexOf("\"cameraRotation\"", at, System.StringComparison.Ordinal);
            int distAt = text.IndexOf("\"distance\"", at, System.StringComparison.Ordinal);
            if (posAt > 0 && rotAt > posAt && distAt > rotAt) {
                // The first [..] after a key is its array (shots.json is written with json.dumps).
                string posText = Bracketed(text, posAt, rotAt);
                string rotText = Bracketed(text, rotAt, distAt);
                string[] p = posText.Split(',');
                string[] r = rotText.Split(',');
                if (p.Length == 3 && r.Length == 4) {
                    cameraPosition = new UnityEngine.Vector3(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
                    cameraRotation = new UnityEngine.Quaternion(float.Parse(r[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(r[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(r[2], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(r[3], System.Globalization.CultureInfo.InvariantCulture));
                    cameraFromShots = true;
                }
            }
        }
    } catch (System.Exception e) {
        UnityEngine.Debug.Log("[sanguo] could not read the M1 camera, the default camera is used: " + e.Message);
    }
}

System.Text.StringBuilder doc = new System.Text.StringBuilder(1 << 24);
System.Collections.Generic.List<string> failures = new System.Collections.Generic.List<string>();
int exportedClips = 0;
int frameCount = 0;
long keyCount = 0;
string failure = null;

// The frames are taken at the lowest level that skins with four bones per vertex, as the engine
// does; the level open before is put back afterwards (see capture_heroes.csx).
int activeQuality = UnityEngine.QualitySettings.GetQualityLevel();
int captureQuality = activeQuality;
for (int level = 0; level < UnityEngine.QualitySettings.names.Length; level++) {
    UnityEngine.QualitySettings.SetQualityLevel(level, false);
    if (UnityEngine.QualitySettings.skinWeights.ToString() == "FourBones") { captureQuality = level; break; }
}
UnityEngine.QualitySettings.SetQualityLevel(captureQuality, false);
string qualityName = UnityEngine.QualitySettings.names[captureQuality];

UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
try {
    UnityEngine.GameObject lightObject = new UnityEngine.GameObject("Key Light");
    UnityEngine.Light light = lightObject.AddComponent<UnityEngine.Light>();
    light.type = UnityEngine.LightType.Directional;
    light.color = UnityEngine.Color.white;
    light.intensity = 1f;
    light.shadows = UnityEngine.LightShadows.None;
    lightObject.transform.eulerAngles = LightEuler;
    UnityEngine.RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
    UnityEngine.RenderSettings.ambientLight = Ambient;
    UnityEngine.RenderSettings.skybox = null;
    UnityEngine.RenderSettings.fog = false;
    UnityEngine.RenderSettings.sun = light;

    UnityEngine.GameObject cameraObject = new UnityEngine.GameObject("Capture Camera");
    UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
    camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    camera.backgroundColor = Background;
    camera.fieldOfView = VerticalFov;
    camera.nearClipPlane = 0.05f;
    camera.farClipPlane = 100f;
    camera.allowMSAA = false;
    camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);

    UnityEngine.RenderTexture target = new UnityEngine.RenderTexture(Width, Height, 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
    UnityEngine.Texture2D frame = new UnityEngine.Texture2D(Width, Height, UnityEngine.TextureFormat.RGB24, false, false);
    camera.targetTexture = target;

    UnityEngine.GameObject instance = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
    try {
        UnityEngine.Transform root = instance.transform;
        UnityEngine.Transform[] nodes = instance.GetComponentsInChildren<UnityEngine.Transform>(true);
        UnityEngine.SkinnedMeshRenderer[] skinnedRenderers = instance.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
        UnityEngine.Vector3 restRootPosition = root.localPosition;

        doc.Append('{');
        doc.Append("\"hero\":").Append(__S(hero)).Append(",\"prefabPath\":").Append(__S(prefabPath))
            .Append(",\"prefabGuid\":").Append(__Guid(prefab)).Append(",\"animFolder\":").Append(__S(animFolder));
        UnityEngine.Animator prefabAnimator = prefab.GetComponent<UnityEngine.Animator>();
        doc.Append(",\"animator\":{");
        if (prefabAnimator != null) {
            doc.Append("\"applyRootMotion\":").Append(__B(prefabAnimator.applyRootMotion))
                .Append(",\"updateMode\":").Append(__S(prefabAnimator.updateMode.ToString()))
                .Append(",\"cullingMode\":").Append(__S(prefabAnimator.cullingMode.ToString()))
                .Append(",\"hasAvatar\":").Append(__B(prefabAnimator.avatar != null))
                .Append(",\"avatar\":").Append(__Guid(prefabAnimator.avatar));
        } else {
            doc.Append("\"present\":false");
        }
        doc.Append('}');

        // The rest pose: every node's local transform before any clip is sampled.
        doc.Append(",\"nodes\":[");
        for (int i = 0; i < nodes.Length; i++) {
            if (i > 0) doc.Append(',');
            doc.Append("{\"path\":").Append(__S(RelPath(root, nodes[i]))).Append(",\"position\":");
            doc.Append(__F3(nodes[i].localPosition)).Append(",\"rotation\":").Append(__Q4(nodes[i].localRotation))
                .Append(",\"scale\":").Append(__F3(nodes[i].localScale)).Append('}');
        }
        doc.Append("],\"camera\":{\"position\":").Append(__F3(cameraPosition)).Append(",\"rotation\":").Append(__Q4(cameraRotation))
            .Append(",\"fromM1Shots\":").Append(__B(cameraFromShots)).Append(",\"width\":").Append(Width).Append(",\"height\":").Append(Height)
            .Append(",\"verticalFov\":").Append(__F(VerticalFov)).Append("},");
        doc.Append("\"rootRest\":{\"position\":").Append(__F3(restRootPosition)).Append(",\"rotation\":").Append(__Q4(root.localRotation)).Append("},");
        doc.Append("\"sampling\":{\"count\":").Append(SampleCount).Append(",\"phase\":").Append(__F(SamplePhase))
            .Append(",\"shotSamples\":[").Append(ShotSamples[0]);
        for (int i = 1; i < ShotSamples.Length; i++) doc.Append(',').Append(ShotSamples[i]);
        doc.Append("]},\"clips\":[");

        string frameDir = System.IO.Path.Combine(outDir, hero);
        System.IO.Directory.CreateDirectory(frameDir);
        bool firstClip = true;
        for (int c = 0; c < clipNames.Length; c++) {
            string clipName = clipNames[c];
            string clipPath = animFolder + clipName + ".anim";
            UnityEngine.AnimationClip clip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AnimationClip>(clipPath);
            if (clip == null) {
                failures.Add(clipName + ": no clip at " + clipPath);
                continue;
            }
            try {
                // A fresh AnimationMode per clip: stopping it reverts every property the previous
                // clip wrote, so nodes this clip does not animate show the rest pose.
                UnityEditor.AnimationMode.StartAnimationMode();
                try {
                    UnityEditor.AnimationClipSettings settings = UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);
                    UnityEditor.EditorCurveBinding[] bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip);
                    UnityEngine.AnimationEvent[] events = UnityEditor.AnimationUtility.GetAnimationEvents(clip);
                    float length = clip.length;
                    float[] times = new float[SampleCount + 1];
                    times[0] = 0f;
                    for (int i = 0; i < SampleCount; i++) times[i + 1] = length * (i + SamplePhase) / SampleCount;

                    if (!firstClip) doc.Append(',');
                    firstClip = false;
                    doc.Append("{\"name\":").Append(__S(clip.name)).Append(",\"action\":").Append(__S(ClipAction(clip.name)))
                        .Append(",\"path\":").Append(__S(clipPath))
                        .Append(",\"guid\":").Append(__S(UnityEditor.AssetDatabase.AssetPathToGUID(clipPath)))
                        .Append(",\"sampleRate\":").Append(__F(clip.frameRate))
                        .Append(",\"length\":").Append(__F(length))
                        .Append(",\"humanMotion\":").Append(__B(clip.humanMotion))
                        .Append(",\"legacy\":").Append(__B(clip.legacy))
                        .Append(",\"settings\":{\"startTime\":").Append(__F(settings.startTime))
                        .Append(",\"stopTime\":").Append(__F(settings.stopTime))
                        .Append(",\"loopTime\":").Append(__B(settings.loopTime))
                        .Append(",\"cycleOffset\":").Append(__F(settings.cycleOffset))
                        .Append(",\"loopBlend\":").Append(__B(settings.loopBlend))
                        .Append(",\"loopBlendOrientation\":").Append(__B(settings.loopBlendOrientation))
                        .Append(",\"loopBlendPositionY\":").Append(__B(settings.loopBlendPositionY))
                        .Append(",\"loopBlendPositionXZ\":").Append(__B(settings.loopBlendPositionXZ))
                        .Append(",\"keepOriginalOrientation\":").Append(__B(settings.keepOriginalOrientation))
                        .Append(",\"keepOriginalPositionY\":").Append(__B(settings.keepOriginalPositionY))
                        .Append(",\"keepOriginalPositionXZ\":").Append(__B(settings.keepOriginalPositionXZ))
                        .Append(",\"heightFromFeet\":").Append(__B(settings.heightFromFeet))
                        .Append(",\"orientationOffsetY\":").Append(__F(settings.orientationOffsetY))
                        .Append(",\"level\":").Append(__F(settings.level))
                        .Append(",\"mirror\":").Append(__B(settings.mirror))
                        .Append(",\"hasAdditiveReferencePose\":").Append(__B(settings.hasAdditiveReferencePose))
                        .Append(",\"additiveReferencePoseTime\":").Append(__F(settings.additiveReferencePoseTime)).Append("}");

                    doc.Append(",\"events\":[");
                    for (int e = 0; e < events.Length; e++) {
                        if (e > 0) doc.Append(',');
                        UnityEngine.AnimationEvent ev = events[e];
                        doc.Append("{\"time\":").Append(__F(ev.time)).Append(",\"functionName\":").Append(__S(ev.functionName))
                            .Append(",\"floatParameter\":").Append(__F(ev.floatParameter))
                            .Append(",\"intParameter\":").Append(ev.intParameter)
                            .Append(",\"stringParameter\":").Append(__S(ev.stringParameter))
                            .Append(",\"objectReferenceGuid\":").Append(__Guid(ev.objectReferenceParameter))
                            .Append(",\"messageOptions\":").Append(((int)ev.messageOptions).ToString()).Append('}');
                    }
                    doc.Append("],\"curves\":[");
                    for (int b = 0; b < bindings.Length; b++) {
                        if (b > 0) doc.Append(',');
                        EmitCurve(doc, clip, bindings[b]);
                        UnityEngine.AnimationCurve curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, bindings[b]);
                        if (curve != null) keyCount += curve.length;
                    }
                    doc.Append("],\"samples\":{\"times\":[");
                    for (int i = 0; i < times.Length; i++) {
                        if (i > 0) doc.Append(',');
                        doc.Append(__F(times[i]));
                    }
                    doc.Append("],\"poses\":[");
                    UnityEngine.Vector3 rootAtStart = UnityEngine.Vector3.zero;
                    bool haveStart = false;
                    System.Collections.Generic.List<string> frameEntries = new System.Collections.Generic.List<string>();

                    for (int i = 0; i < times.Length; i++) {
                        UnityEditor.AnimationMode.BeginSampling();
                        UnityEditor.AnimationMode.SampleAnimationClip(instance, clip, times[i]);
                        UnityEditor.AnimationMode.EndSampling();
                        // Sampling places the root where the clip's root-motion curve points: an
                        // absolute position (the third hit of a combo starts 4 m ahead). At run
                        // time the Animator moves the actor by the curve's increments instead, so
                        // the root is put back by the clip's start offset; the poses and frames
                        // recorded here are the run-time ones.
                        UnityEngine.Vector3 sampledRoot = root.localPosition;
                        if (!haveStart) { rootAtStart = sampledRoot; haveStart = true; }
                        root.localPosition = restRootPosition + (sampledRoot - rootAtStart);

                        if (i > 0) doc.Append(',');
                        doc.Append('[');
                        for (int n = 0; n < nodes.Length; n++) {
                            if (n > 0) doc.Append(',');
                            doc.Append('[');
                            AppendPose(doc, nodes[n]);
                            doc.Append(']');
                        }
                        doc.Append(']');

                        bool shot = false;
                        for (int s = 0; s < ShotSamples.Length; s++) if (ShotSamples[s] == i) shot = true;
                        if (shot) {
                            // The camera keeps the M1 framing relative to where the actor is now.
                            UnityEngine.Vector3 follow = root.localPosition - restRootPosition;
                            follow.y = 0f;
                            camera.transform.position = cameraPosition + follow;
                            string file = ClipAction(clip.name) + "_p" + i + ".png";
                            // Outside play mode Unity skins a skinned mesh on its own schedule: a camera
                            // rendered right after sampling shows the previous skin (fast clips came out
                            // tens of pixels off their own sampled joints). Switching the renderers off
                            // and on makes them skin the sampled pose for this render.
                            for (int r = 0; r < skinnedRenderers.Length; r++) {
                                skinnedRenderers[r].enabled = false;
                                skinnedRenderers[r].enabled = true;
                            }
                            camera.Render();
                            UnityEngine.RenderTexture.active = target;
                            frame.ReadPixels(new UnityEngine.Rect(0, 0, Width, Height), 0, 0);
                            frame.Apply();
                            UnityEngine.RenderTexture.active = null;
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(frameDir, file), frame.EncodeToPNG());
                            frameCount++;
                            frameEntries.Add("{\"sample\":" + i + ",\"time\":" + __F(times[i])
                                + ",\"file\":" + __S(file) + ",\"follow\":" + __F3(follow) + "}");
                        }
                    }
                    doc.Append("]},\"frames\":[");
                    for (int f = 0; f < frameEntries.Count; f++) {
                        if (f > 0) doc.Append(',');
                        doc.Append(frameEntries[f]);
                    }
                    doc.Append("],\"rootStart\":").Append(__F3(rootAtStart)).Append('}');
                    exportedClips++;
                } finally {
                    UnityEditor.AnimationMode.StopAnimationMode();
                }
            } catch (System.Exception e) {
                failures.Add(clipName + ": " + e.GetType().Name + ": " + e.Message);
            }
        }
        doc.Append("]}");
    } finally {
        UnityEngine.Object.DestroyImmediate(instance);
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(frame);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
    }
} catch (System.Exception e) {
    failure = e.GetType().FullName + ": " + e.Message + "\n" + e.StackTrace;
} finally {
    UnityEngine.QualitySettings.SetQualityLevel(activeQuality, false);
    try {
        bool restored = false;
        for (int i = 0; i < previous.Length; i++) {
            if (string.IsNullOrEmpty(previous[i])) continue;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previous[i],
                restored ? UnityEditor.SceneManagement.OpenSceneMode.Additive : UnityEditor.SceneManagement.OpenSceneMode.Single);
            restored = true;
        }
        if (!restored)
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
    } catch (System.Exception e) {
        if (failure == null) failure = "restore: " + e.Message;
    }
}

string document = hero + ".json";
if (failure == null) System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, document), doc.ToString());
__P("exported", __B(failure == null));
__P("file", __S(document));
__P("hero", __S(hero));
__P("clips", exportedClips.ToString());
__P("requested", clipNames.Length.ToString());
__P("keys", keyCount.ToString());
__P("frames", frameCount.ToString());
__P("cameraFromM1Shots", __B(cameraFromShots));
__P("qualityLevel", __S(qualityName));
if (failures.Count > 0) {
    __sb.Append("\"failures\":[");
    for (int i = 0; i < failures.Count; i++) {
        if (i > 0) __sb.Append(',');
        __sb.Append(__S(failures[i]));
    }
    __sb.Append("],");
}
if (failure != null) __P("exportError", __S(failure));
__P("activeSceneDirtyAfterRestore", __B(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty));
__END();
return __sb.ToString();

