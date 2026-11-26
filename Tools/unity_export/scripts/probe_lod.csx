
// SanGuoXEngine M3 probe: how the 桃园 scene uses LOD groups, how its hierarchy is organised, and
// which objects mark gameplay points. The scene is opened WITHOUT saving and the scenes that were
// open before are restored; if any open scene has unsaved changes the script does nothing.
void Inc(System.Collections.Generic.Dictionary<string, int> counts, string key) {
    int value;
    counts.TryGetValue(key, out value);
    counts[key] = value + 1;
}
string Dict(System.Collections.Generic.Dictionary<string, int> counts) {
    System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> rows =
        new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(counts);
    rows.Sort(delegate(System.Collections.Generic.KeyValuePair<string, int> a, System.Collections.Generic.KeyValuePair<string, int> b) {
        int byCount = b.Value.CompareTo(a.Value);
        return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
    });
    System.Text.StringBuilder b2 = new System.Text.StringBuilder();
    b2.Append('{');
    foreach (System.Collections.Generic.KeyValuePair<string, int> row in rows) {
        b2.Append(__S(row.Key)).Append(':').Append(row.Value.ToString()).Append(',');
    }
    if (b2.Length > 0 && b2[b2.Length - 1] == ',') b2.Length--;
    b2.Append('}');
    return b2.ToString();
}
string TPath(UnityEngine.Transform t) {
    string p = t != null ? t.name : "";
    while (t != null && t.parent != null) {
        t = t.parent;
        p = t.name + "/" + p;
    }
    return p;
}
string GroupPath(UnityEngine.Transform group, UnityEngine.Transform root) {
    // Path of a node up to the scene root, without the object's own name.
    string p = "";
    for (UnityEngine.Transform t = group.parent; t != null && t != root; t = t.parent) p = t.name + "/" + p;
    return p;
}

__sb.Append('{');
string scenePath = "Assets/Scenes/GameScenes/C-ZhuoXian_TaoYuan.unity";
if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
    __P("opened", __B(false));
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
    __P("opened", __B(false));
    __P("reason", __S("an open scene has unsaved changes; the editor was left untouched"));
    __END();
    return __sb.ToString();
}

try {
    UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,
        UnityEditor.SceneManagement.OpenSceneMode.Single);
    UnityEngine.GameObject[] roots = scene.GetRootGameObjects();
    UnityEngine.GameObject root = roots.Length > 0 ? roots[0] : null;
    __P("opened", __B(true));
    __P("rootCount", roots.Length.ToString());
    __P("lodBias", __F(UnityEngine.QualitySettings.lodBias));
    __P("lodBiasNames", __S(string.Join(",", UnityEngine.QualitySettings.names)));
    if (root == null) { __END(); return __sb.ToString(); }

    // ---- top of the hierarchy: the root's children, and their children's names
    System.Collections.Generic.List<object> top = new System.Collections.Generic.List<object>();
    foreach (UnityEngine.Transform child in root.transform) {
        System.Collections.Generic.Dictionary<string, int> grandchildren = new System.Collections.Generic.Dictionary<string, int>();
        int descendants = 0;
        foreach (UnityEngine.Transform deep in child.GetComponentsInChildren<UnityEngine.Transform>(true)) if (deep != child) descendants++;
        foreach (UnityEngine.Transform grand in child) Inc(grandchildren, grand.name);
        top.Add(new System.Collections.Generic.Dictionary<string, object> {
            { "name", child.name }, { "active", child.gameObject.activeSelf }, { "descendants", descendants },
            { "position", new float[] { child.position.x, child.position.y, child.position.z } },
            { "scale", new float[] { child.lossyScale.x, child.lossyScale.y, child.lossyScale.z } },
            { "children", grandchildren },
        });
    }
    __sb.Append("\"top\":").Append(__J(top)).Append(',');

    // ---- LOD groups
    UnityEngine.LODGroup[] groups = root.GetComponentsInChildren<UnityEngine.LODGroup>(true);
    System.Collections.Generic.Dictionary<string, int> lodCounts = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, int> parentKinds = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, int> fadeModes = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, int> rendererCounts = new System.Collections.Generic.Dictionary<string, int>();
    int emptyLast = 0, externalRenderers = 0, disabledGroups = 0, animatedCrossFade = 0, unsorted = 0;
    float minSize = float.MaxValue, maxSize = float.MinValue, minHeight = float.MaxValue, maxHeight = float.MinValue;
    System.Collections.Generic.List<object> examples = new System.Collections.Generic.List<object>();
    System.Collections.Generic.Dictionary<string, int> thresholdValues = new System.Collections.Generic.Dictionary<string, int>();
    foreach (UnityEngine.LODGroup group in groups) {
        UnityEngine.LOD[] lods = group.GetLODs();
        Inc(lodCounts, lods.Length.ToString());
        Inc(fadeModes, group.fadeMode.ToString());
        if (!group.enabled) disabledGroups++;
        if (group.animateCrossFading) animatedCrossFade++;
        Inc(parentKinds, GroupPath(group.transform, root.transform));
        float previousHeight = float.MaxValue;
        bool sorted = true;
        int groupRenderers = 0;
        for (int i = 0; i < lods.Length; i++) {
            groupRenderers += lods[i].renderers.Length;
            Inc(rendererCounts, "lod" + i + " renderers " + lods[i].renderers.Length);
            Inc(thresholdValues, "lod" + i + " height " + lods[i].screenRelativeTransitionHeight.ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture));
            if (lods[i].screenRelativeTransitionHeight > previousHeight) sorted = false;
            previousHeight = lods[i].screenRelativeTransitionHeight;
            if (i == lods.Length - 1 && lods[i].renderers.Length == 0) emptyLast++;
            foreach (UnityEngine.Renderer r in lods[i].renderers) {
                if (r == null || !r.transform.IsChildOf(group.transform)) externalRenderers++;
            }
        }
        if (!sorted) unsorted++;
        minSize = UnityEngine.Mathf.Min(minSize, group.size);
        maxSize = UnityEngine.Mathf.Max(maxSize, group.size);
        UnityEngine.Vector3 lossy = group.transform.lossyScale;
        float worldSize = group.size * UnityEngine.Mathf.Max(lossy.x, UnityEngine.Mathf.Max(lossy.y, lossy.z));
        minHeight = UnityEngine.Mathf.Min(minHeight, worldSize);
        maxHeight = UnityEngine.Mathf.Max(maxHeight, worldSize);
        if (examples.Count < 12 && lods.Length >= 2) {
            System.Collections.Generic.List<object> levels = new System.Collections.Generic.List<object>();
            foreach (UnityEngine.LOD lod in lods) {
                System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
                foreach (UnityEngine.Renderer r in lod.renderers) names.Add(r != null ? r.gameObject.name : "<null>");
                levels.Add(new System.Collections.Generic.Dictionary<string, object> {
                    { "height", lod.screenRelativeTransitionHeight }, { "fade", lod.fadeTransitionWidth }, { "renderers", names } });
            }
            examples.Add(new System.Collections.Generic.Dictionary<string, object> {
                { "path", TPath(group.transform) }, { "size", group.size }, { "worldSize", worldSize },
                { "localReferencePoint", new float[] { group.localReferencePoint.x, group.localReferencePoint.y, group.localReferencePoint.z } },
                { "lods", levels } });
        }
    }
    __P("lodGroupCount", groups.Length.ToString());
    __sb.Append("\"lodCountPerGroup\":").Append(Dict(lodCounts)).Append(',');
    __sb.Append("\"lodParentKinds\":").Append(Dict(parentKinds)).Append(',');
    __sb.Append("\"lodFadeModes\":").Append(Dict(fadeModes)).Append(',');
    __sb.Append("\"renderersPerLod\":").Append(Dict(rendererCounts)).Append(',');
    __sb.Append("\"thresholdValues\":").Append(Dict(thresholdValues)).Append(',');
    __P("lodGroupsWithEmptyLastLod", emptyLast.ToString());
    __P("lodRenderersOutsideTheGroup", externalRenderers.ToString());
    __P("lodGroupsDisabled", disabledGroups.ToString());
    __P("lodGroupsAnimatingCrossFade", animatedCrossFade.ToString());
    __P("lodGroupsUnsortedThresholds", unsorted.ToString());
    __P("lodGroupSizeMin", __F(minSize)); __P("lodGroupSizeMax", __F(maxSize));
    __P("lodGroupWorldSizeMin", __F(minHeight)); __P("lodGroupWorldSizeMax", __F(maxHeight));
    __sb.Append("\"lodExamples\":").Append(__J(examples)).Append(',');

    // ---- renderers: static flags, shadows, lightmaps
    System.Collections.Generic.Dictionary<string, int> staticFlags = new System.Collections.Generic.Dictionary<string, int>();
    System.Collections.Generic.Dictionary<string, int> castShadows = new System.Collections.Generic.Dictionary<string, int>();
    int lightmapped = 0, lightProbes = 0, withColliders = 0;
    UnityEngine.MeshRenderer[] meshRenderers = root.GetComponentsInChildren<UnityEngine.MeshRenderer>(true);
    foreach (UnityEngine.MeshRenderer r in meshRenderers) {
        UnityEditor.StaticEditorFlags flags = UnityEditor.GameObjectUtility.GetStaticEditorFlags(r.gameObject);
        string flagText = flags == 0 ? "none" : flags.ToString();
        Inc(staticFlags, flagText);
        Inc(castShadows, r.shadowCastingMode.ToString());
        if (r.lightmapIndex >= 0 && r.lightmapIndex < 65000) lightmapped++;
        if (r.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off) lightProbes++;
        if (r.GetComponent<UnityEngine.Collider>() != null) withColliders++;
    }
    __P("meshRendererCount", meshRenderers.Length.ToString());
    __sb.Append("\"staticFlags\":").Append(Dict(staticFlags)).Append(',');
    __sb.Append("\"castShadows\":").Append(Dict(castShadows)).Append(',');
    __P("meshRenderersLightmapped", lightmapped.ToString());
    __P("meshRenderersUsingLightProbes", lightProbes.ToString());
    __P("meshRenderersWithColliders", withColliders.ToString());

    // ---- colliders and gameplay markers
    System.Collections.Generic.List<object> colliders = new System.Collections.Generic.List<object>();
    foreach (UnityEngine.BoxCollider box in root.GetComponentsInChildren<UnityEngine.BoxCollider>(true)) {
        colliders.Add(new System.Collections.Generic.Dictionary<string, object> {
            { "type", "Box" }, { "path", TPath(box.transform) }, { "active", box.gameObject.activeInHierarchy },
            { "center", new float[] { box.center.x, box.center.y, box.center.z } },
            { "size", new float[] { box.size.x, box.size.y, box.size.z } },
            { "isTrigger", box.isTrigger }, { "worldCenter", new float[] { box.transform.TransformPoint(box.center).x, box.transform.TransformPoint(box.center).y, box.transform.TransformPoint(box.center).z } },
        });
    }
    __P("colliderCount", colliders.Count.ToString());
    __sb.Append("\"colliders\":").Append(__J(colliders.GetRange(0, colliders.Count < 14 ? colliders.Count : 14))).Append(',');

    // Every MonoBehaviour type that marks or drives scene objects: one example each with its serialized state.
    System.Collections.Generic.List<object> behaviours = new System.Collections.Generic.List<object>();
    System.Collections.Generic.HashSet<string> seenTypes = new System.Collections.Generic.HashSet<string>();
    foreach (UnityEngine.MonoBehaviour behaviour in root.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true)) {
        string typeName = behaviour.GetType().Name;
        if (seenTypes.Contains(typeName)) continue;
        seenTypes.Add(typeName);
        string state = UnityEditor.EditorJsonUtility.ToJson(behaviour);
        if (state != null && state.Length > 900) state = state.Substring(0, 900) + "...";
        behaviours.Add(new System.Collections.Generic.Dictionary<string, object> { { "type", typeName }, { "path", TPath(behaviour.transform) }, { "json", state } });
    }
    __sb.Append("\"behaviourExamples\":").Append(__J(behaviours)).Append(',');

    // Objects whose name looks like a gameplay point (spawn, point, pos, ...).
    System.Collections.Generic.List<object> markers = new System.Collections.Generic.List<object>();
    foreach (UnityEngine.Transform t in root.GetComponentsInChildren<UnityEngine.Transform>(true)) {
        string lower = t.name.ToLowerInvariant();
        if (lower.Contains("spawn") || lower.Contains("point") || lower.Contains("pos") || lower.Contains("born") || lower.Contains("birth")) {
            UnityEngine.Vector3 p = t.position;
            markers.Add(new System.Collections.Generic.Dictionary<string, object> {
                { "path", TPath(t) }, { "active", t.gameObject.activeInHierarchy },
                { "position", new float[] { p.x, p.y, p.z } },
                { "euler", new float[] { t.eulerAngles.x, t.eulerAngles.y, t.eulerAngles.z } },
                { "components", t.GetComponents<UnityEngine.Component>().Length } });
        }
    }
    __P("namedPointCount", markers.Count.ToString());
    __sb.Append("\"namedPoints\":").Append(__J(markers.GetRange(0, markers.Count < 60 ? markers.Count : 60))).Append(',');
    __P("renderSceneDirty", __B(scene.isDirty));
} catch (System.Exception e) {
    __EX("probe", e);
} finally {
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
        __EX("restore", e);
    }
}
__END();
return __sb.ToString();
