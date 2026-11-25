
// SanGuoXEngine M0 probe 4/5: a hero animator controller, its clips' curves, and a pose sample.
// Read-only: pose sampling happens in a temporary preview scene that is closed afterwards; the
// scenes open in the editor are not touched.
string TPath(UnityEngine.Transform t) {
    string p = t != null ? t.name : "";
    while (t != null && t.parent != null) {
        t = t.parent;
        p = t.name + "/" + p;
    }
    return p;
}
void CollectStates(UnityEditor.Animations.AnimatorStateMachine machine,
    System.Collections.Generic.List<UnityEditor.Animations.AnimatorState> into) {
    if (machine == null) return;
    UnityEditor.Animations.ChildAnimatorState[] states = machine.states;
    for (int i = 0; i < states.Length; i++) into.Add(states[i].state);
    UnityEditor.Animations.ChildAnimatorStateMachine[] machines = machine.stateMachines;
    for (int i = 0; i < machines.Length; i++) CollectStates(machines[i].stateMachine, into);
}
string ClipInfo(UnityEngine.AnimationClip clip) {
    string path = UnityEditor.AssetDatabase.GetAssetPath(clip);
    UnityEditor.AnimationClipSettings settings = UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);
    UnityEditor.EditorCurveBinding[] bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip);
    int rotation = 0;
    int position = 0;
    int scale = 0;
    int other = 0;
    int keys = 0;
    for (int i = 0; i < bindings.Length; i++) {
        string property = bindings[i].propertyName;
        if (property.StartsWith("m_LocalRotation")) rotation++;
        else if (property.StartsWith("m_LocalPosition")) position++;
        else if (property.StartsWith("m_LocalScale")) scale++;
        else other++;
        keys += UnityEditor.AnimationUtility.GetEditorCurve(clip, bindings[i]).length;
    }
    return "{\"kind\":\"clip\",\"name\":" + __S(clip.name)
        + ",\"path\":" + __S(path)
        + ",\"guid\":" + __S(UnityEditor.AssetDatabase.AssetPathToGUID(path))
        + ",\"length\":" + __F(clip.length)
        + ",\"frameRate\":" + __F(clip.frameRate)
        + ",\"loopTime\":" + __B(settings.loopTime)
        + ",\"startTime\":" + __F(settings.startTime)
        + ",\"stopTime\":" + __F(settings.stopTime)
        + ",\"humanMotion\":" + __B(clip.humanMotion)
        + ",\"legacy\":" + __B(clip.legacy)
        + ",\"bindingCount\":" + bindings.Length
        + ",\"rotationBindings\":" + rotation
        + ",\"positionBindings\":" + position
        + ",\"scaleBindings\":" + scale
        + ",\"otherBindings\":" + other
        + ",\"keyCount\":" + keys
        + ",\"objectReferenceBindings\":" + UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(clip).Length
        + ",\"events\":" + UnityEditor.AnimationUtility.GetAnimationEvents(clip).Length
        + "}";
}

__sb.Append('{');
string hero = "Hero_LvBu";
string controllerPath = "Assets/Actor/Hero/" + hero + "/" + hero + ".controller";
UnityEditor.Animations.AnimatorController controller =
    UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath);
__P("controllerPath", __S(controllerPath));
__P("found", __B(controller != null));

if (controller != null) {
    __P("layerCount", controller.layers.Length.ToString());

    __sb.Append("\"parameters\":[");
    foreach (UnityEngine.AnimatorControllerParameter parameter in controller.parameters) {
        __sb.Append("{\"name\":").Append(__S(parameter.name))
            .Append(",\"type\":").Append(__S(parameter.type.ToString()));
        if (parameter.type == UnityEngine.AnimatorControllerParameterType.Float)
            __sb.Append(",\"default\":").Append(__F(parameter.defaultFloat));
        else if (parameter.type == UnityEngine.AnimatorControllerParameterType.Int)
            __sb.Append(",\"default\":").Append(parameter.defaultInt.ToString());
        else if (parameter.type == UnityEngine.AnimatorControllerParameterType.Bool)
            __sb.Append(",\"default\":").Append(__B(parameter.defaultBool));
        __sb.Append("},");
    }
    if (__sb[__sb.Length - 1] == ',') __sb.Length--;
    __sb.Append("],");

    System.Collections.Generic.List<UnityEditor.Animations.AnimatorState> states =
        new System.Collections.Generic.List<UnityEditor.Animations.AnimatorState>();
    int transitions = 0;
    foreach (UnityEditor.Animations.AnimatorControllerLayer layer in controller.layers) {
        UnityEditor.Animations.AnimatorStateMachine machine = layer.stateMachine;
        if (machine == null) continue;
        CollectStates(machine, states);
        transitions += machine.anyStateTransitions.Length + machine.entryTransitions.Length;
    }
    for (int i = 0; i < states.Count; i++) transitions += states[i].transitions.Length;
    __P("stateCount", states.Count.ToString());
    __P("transitionCount", transitions.ToString());

    __sb.Append("\"states\":[");
    foreach (UnityEditor.Animations.AnimatorState state in states) {
        __sb.Append("{\"name\":").Append(__S(state.name))
            .Append(",\"tag\":").Append(__S(state.tag))
            .Append(",\"speed\":").Append(__F(state.speed))
            .Append(",\"transitions\":").Append(state.transitions.Length.ToString())
            .Append(",\"motion\":");
        UnityEngine.AnimationClip clip = state.motion as UnityEngine.AnimationClip;
        if (clip != null) {
            __sb.Append(ClipInfo(clip));
        } else if (state.motion is UnityEngine.RuntimeAnimatorController) {
            __sb.Append("{\"kind\":\"blendTree\",\"name\":").Append(__S(state.motion.name)).Append('}');
        } else {
            __sb.Append("null");
        }
        __sb.Append("},");
    }
    if (__sb[__sb.Length - 1] == ',') __sb.Length--;
    __sb.Append("],");

    // One clip in detail: where its curves live and what values they hold.
    UnityEditor.Animations.AnimatorState chosen = null;
    string[] preferred = new string[] { "NormalAttack_1", "n1", "idle_loop", "Idle", "idle" };
    for (int p = 0; p < preferred.Length && chosen == null; p++) {
        for (int i = 0; i < states.Count; i++) {
            if (states[i].name == preferred[p] && states[i].motion is UnityEngine.AnimationClip) {
                chosen = states[i];
                break;
            }
        }
    }
    for (int i = 0; i < states.Count && chosen == null; i++) {
        if (states[i].motion is UnityEngine.AnimationClip) chosen = states[i];
    }

    if (chosen != null) {
        UnityEngine.AnimationClip chosenClip = (UnityEngine.AnimationClip)chosen.motion;
        __P("chosenState", __S(chosen.name));
        __P("chosenClip", ClipInfo(chosenClip));

        UnityEditor.EditorCurveBinding[] bindings = UnityEditor.AnimationUtility.GetCurveBindings(chosenClip);
        int shown = bindings.Length < 80 ? bindings.Length : 80;
        float[] times = new float[] { 0f, chosenClip.length * 0.25f, chosenClip.length * 0.5f, chosenClip.length };
        __sb.Append("\"bindings\":[");
        for (int i = 0; i < shown; i++) {
            UnityEditor.EditorCurveBinding binding = bindings[i];
            UnityEngine.AnimationCurve curve = UnityEditor.AnimationUtility.GetEditorCurve(chosenClip, binding);
            __sb.Append("{\"path\":").Append(__S(binding.path))
                .Append(",\"property\":").Append(__S(binding.propertyName))
                .Append(",\"type\":").Append(__S(binding.type != null ? binding.type.FullName : null))
                .Append(",\"keys\":").Append(curve.length.ToString())
                .Append(",\"firstTime\":").Append(__F(curve.length > 0 ? curve.keys[0].time : 0f))
                .Append(",\"lastTime\":").Append(__F(curve.length > 0 ? curve.keys[curve.length - 1].time : 0f))
                .Append(",\"sample\":[");
            for (int k = 0; k < times.Length; k++) {
                if (k > 0) __sb.Append(',');
                __sb.Append(__F(curve.Evaluate(times[k])));
            }
            __sb.Append("]},");
        }
        if (__sb[__sb.Length - 1] == ',') __sb.Length--;
        __sb.Append("],");

        // Can the exporter sample poses? This is how M2 will produce Unity-side reference poses.
        try {
            string prefabPath = "Assets/Actor/Hero/" + hero + "/" + hero + ".prefab";
            UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
            UnityEngine.SceneManagement.Scene preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try {
                UnityEngine.GameObject instance = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(prefab, preview);
                UnityEngine.Transform[] all = instance.GetComponentsInChildren<UnityEngine.Transform>(true);
                UnityEngine.Transform bone = null;
                for (int i = 0; i < all.Length && bone == null; i++) {
                    if (all[i].name == "Bip001 Spine1" || all[i].name == "Bip001 Spine") bone = all[i];
                }
                if (bone == null && all.Length > 1) bone = all[1];

                UnityEditor.AnimationMode.StartAnimationMode();
                try {
                    UnityEditor.AnimationMode.BeginSampling();
                    UnityEditor.AnimationMode.SampleAnimationClip(instance, chosenClip, 0f);
                    UnityEditor.AnimationMode.EndSampling();
                    UnityEngine.Quaternion at0 = bone != null ? bone.localRotation : UnityEngine.Quaternion.identity;

                    UnityEditor.AnimationMode.BeginSampling();
                    UnityEditor.AnimationMode.SampleAnimationClip(instance, chosenClip, chosenClip.length * 0.5f);
                    UnityEditor.AnimationMode.EndSampling();
                    UnityEngine.Quaternion atMid = bone != null ? bone.localRotation : UnityEngine.Quaternion.identity;

                    __P("poseSampling", "{\"ok\":true,\"bone\":" + __S(bone != null ? TPath(bone) : null)
                        + ",\"rotAt0\":" + __Q4(at0)
                        + ",\"rotAtMid\":" + __Q4(atMid)
                        + ",\"differs\":" + __B(UnityEngine.Quaternion.Angle(at0, atMid) > 0.01f)
                        + "}");
                } finally {
                    UnityEditor.AnimationMode.StopAnimationMode();
                }
            } finally {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        } catch (System.Exception e) {
            __EX("poseSampling", e);
        }
    }
}

__END();
return __sb.ToString();
