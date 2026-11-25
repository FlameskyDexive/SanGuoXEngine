
// SanGuoXEngine M0 probe 3/5: anatomy of the five hero prefabs. Read-only (asset load only).
string TPath(UnityEngine.Transform t) {
    string p = t != null ? t.name : "";
    while (t != null && t.parent != null) {
        t = t.parent;
        p = t.name + "/" + p;
    }
    return p;
}

__sb.Append('{');
string[] heroes = new string[] { "Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu" };
__sb.Append("\"heroes\":[");
foreach (string hero in heroes) {
    string path = "Assets/Actor/Hero/" + hero + "/" + hero + ".prefab";
    UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    __sb.Append("{\"name\":").Append(__S(hero)).Append(",\"path\":").Append(__S(path));
    if (prefab == null) {
        __sb.Append(",\"found\":false},");
        continue;
    }
    __sb.Append(",\"found\":true,\"guid\":").Append(__S(UnityEditor.AssetDatabase.AssetPathToGUID(path))).Append(',');

    UnityEngine.Transform[] transforms = prefab.GetComponentsInChildren<UnityEngine.Transform>(true);
    __P("transformCount", transforms.Length.ToString());

    UnityEngine.Animator animator = prefab.GetComponentInChildren<UnityEngine.Animator>(true);
    __sb.Append("\"animator\":");
    if (animator == null) {
        __sb.Append("null");
    } else {
        UnityEngine.RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        __sb.Append("{\"path\":").Append(__S(TPath(animator.transform)))
            .Append(",\"isHumanoid\":").Append(__B(animator.isHuman))
            .Append(",\"applyRootMotion\":").Append(__B(animator.applyRootMotion))
            .Append(",\"avatar\":").Append(__S(animator.avatar != null ? UnityEditor.AssetDatabase.GetAssetPath(animator.avatar) : null))
            .Append(",\"controller\":").Append(__S(controller != null ? UnityEditor.AssetDatabase.GetAssetPath(controller) : null))
            .Append(",\"cullingMode\":").Append(__S(animator.cullingMode.ToString()));

        UnityEditor.Animations.AnimatorController animatorController = controller as UnityEditor.Animations.AnimatorController;
        if (animatorController != null) {
            __sb.Append(",\"layers\":[");
            foreach (UnityEditor.Animations.AnimatorControllerLayer layer in animatorController.layers) {
                __sb.Append("{\"name\":").Append(__S(layer.name))
                    .Append(",\"weight\":").Append(__F(layer.defaultWeight))
                    .Append(",\"blending\":").Append(__S(layer.blendingMode.ToString()))
                    .Append(",\"stateMachine\":").Append(__S(layer.stateMachine != null ? layer.stateMachine.name : null))
                    .Append(",\"syncedLayerIndex\":").Append(layer.syncedLayerIndex.ToString())
                    .Append("},");
            }
            if (__sb[__sb.Length - 1] == ',') __sb.Length--;
            __sb.Append(']');
        }
        __sb.Append('}');
    }
    __sb.Append(',');

    UnityEngine.SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
    __sb.Append("\"skinnedMeshes\":[");
    foreach (UnityEngine.SkinnedMeshRenderer smr in renderers) {
        UnityEngine.Mesh mesh = smr.sharedMesh;
        string meshPath = "";
        string meshGuid = "";
        long meshId = 0;
        if (mesh != null) {
            meshPath = UnityEditor.AssetDatabase.GetAssetPath(mesh);
            UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out meshGuid, out meshId);
        }

        __sb.Append("{\"path\":").Append(__S(TPath(smr.transform)))
            .Append(",\"mesh\":").Append(__S(mesh != null ? mesh.name : null))
            .Append(",\"meshPath\":").Append(__S(meshPath))
            .Append(",\"meshGuid\":").Append(__S(meshGuid))
            .Append(",\"meshFileId\":\"").Append(meshId.ToString()).Append('"');
        if (mesh != null) {
            __sb.Append(",\"vertices\":").Append(mesh.vertexCount.ToString())
                .Append(",\"triangles\":").Append((mesh.triangles.Length / 3).ToString())
                .Append(",\"subMeshes\":").Append(mesh.subMeshCount.ToString())
                .Append(",\"bindposes\":").Append(mesh.bindposes.Length.ToString());
        }
        UnityEngine.Bounds bounds = smr.localBounds;
        __sb.Append(",\"localBoundsCenter\":").Append(__V3(bounds.center))
            .Append(",\"localBoundsExtents\":").Append(__V3(bounds.extents))
            .Append(",\"rootBone\":").Append(__S(smr.rootBone != null ? TPath(smr.rootBone) : null))
            .Append(",\"boneCount\":").Append(smr.bones.Length.ToString())
            .Append(",\"materials\":[");

        UnityEngine.Material[] materials = smr.sharedMaterials;
        for (int i = 0; i < materials.Length; i++) {
            if (i > 0) __sb.Append(',');
            UnityEngine.Material material = materials[i];
            if (material == null) {
                __sb.Append("null");
            } else {
                __sb.Append("{\"name\":").Append(__S(material.name))
                    .Append(",\"path\":").Append(__S(UnityEditor.AssetDatabase.GetAssetPath(material)))
                    .Append(",\"shader\":").Append(__S(material.shader != null ? material.shader.name : null))
                    .Append('}');
            }
        }
        __sb.Append("],\"bones\":[");
        for (int i = 0; i < smr.bones.Length; i++) {
            if (i > 0) __sb.Append(',');
            __sb.Append(__S(smr.bones[i] != null ? TPath(smr.bones[i]) : null));
        }
        __sb.Append("]},");
    }
    if (__sb[__sb.Length - 1] == ',') __sb.Length--;
    __sb.Append("],");

    // The full transform tree: skeleton, attachment sockets and every component on them.
    __sb.Append("\"transforms\":[");
    foreach (UnityEngine.Transform t in transforms) {
        UnityEngine.Component[] components = t.GetComponents<UnityEngine.Component>();
        __sb.Append("{\"path\":").Append(__S(TPath(t)))
            .Append(",\"active\":").Append(__B(t.gameObject.activeSelf))
            .Append(",\"position\":").Append(__V3(t.localPosition))
            .Append(",\"rotation\":").Append(__Q4(t.localRotation))
            .Append(",\"scale\":").Append(__V3(t.localScale))
            .Append(",\"components\":[");
        for (int c = 0; c < components.Length; c++) {
            if (c > 0) __sb.Append(',');
            UnityEngine.Component component = components[c];
            __sb.Append(component == null ? __S("<missing script>") : __S(component.GetType().FullName));
        }
        __sb.Append("]},");
    }
    if (__sb[__sb.Length - 1] == ',') __sb.Length--;
    __sb.Append("]},");
}
if (__sb[__sb.Length - 1] == ',') __sb.Length--;
__sb.Append(']');
__END();

// The document is large; it goes to disk and only a summary is returned.
__SAVE("heroes.json");
__P("file", __S("heroes.json"));
__P("heroCount", heroes.Length.ToString());
__P("ok", __B(true));
__END();
return __sb.ToString();
