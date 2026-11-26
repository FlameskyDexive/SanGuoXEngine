
// SanGuoXEngine M1 export: everything the native material / prefab generators need for the five
// heroes. Read-only (asset loads only, nothing is instantiated into an open scene).
// Writes heroes.json (full document) into the export folder and returns a small summary.
//
// Document shape:
//   heroes[]   prefab anatomy: animator, skinned meshes (node, mesh, bones, root-space bounds,
//              material slots), full transform tree
//   models     FBX by asset path: Unity ModelImporter settings + a sample mesh
//   materials  material by asset path: shader, render queue, keywords, every property value
//   textures   texture by Unity guid: path, size, TextureImporter settings, Standalone override
string TPath(UnityEngine.Transform t) {
    string p = t != null ? t.name : "";
    while (t != null && t.parent != null) {
        t = t.parent;
        p = t.name + "/" + p;
    }
    return p;
}
float[] V3(UnityEngine.Vector3 v) { return new float[] { v.x, v.y, v.z }; }
float[] Q4(UnityEngine.Quaternion q) { return new float[] { q.x, q.y, q.z, q.w }; }
float[] C4(UnityEngine.Color c) { return new float[] { c.r, c.g, c.b, c.a }; }
float[] V4(UnityEngine.Vector4 v) { return new float[] { v.x, v.y, v.z, v.w }; }
float[] V2(UnityEngine.Vector2 v) { return new float[] { v.x, v.y }; }
System.Collections.Generic.Dictionary<string, object> BoundsJson(UnityEngine.Bounds b) {
    return new System.Collections.Generic.Dictionary<string, object> { { "center", V3(b.center) }, { "extents", V3(b.extents) } };
}
System.Collections.Generic.Dictionary<string, object> AssetJson(UnityEngine.Object o) {
    if (o == null) return null;
    string guid = "";
    long id = 0;
    UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out guid, out id);
    return new System.Collections.Generic.Dictionary<string, object> {
        { "name", o.name },
        { "path", UnityEditor.AssetDatabase.GetAssetPath(o) },
        { "guid", guid },
        { "fileId", id.ToString() },
    };
}

// Collected while walking the heroes, dumped at the end.
System.Collections.Generic.Dictionary<string, UnityEngine.Material> materials =
    new System.Collections.Generic.Dictionary<string, UnityEngine.Material>(System.StringComparer.OrdinalIgnoreCase);
System.Collections.Generic.Dictionary<string, UnityEngine.Texture2D> textures =
    new System.Collections.Generic.Dictionary<string, UnityEngine.Texture2D>(System.StringComparer.Ordinal);
System.Collections.Generic.Dictionary<string, UnityEngine.Mesh> meshes =
    new System.Collections.Generic.Dictionary<string, UnityEngine.Mesh>(System.StringComparer.OrdinalIgnoreCase);

void CollectMaterial(UnityEngine.Material material) {
    if (material == null) return;
    string path = UnityEditor.AssetDatabase.GetAssetPath(material);
    if (materials.ContainsKey(path)) return;
    materials[path] = material;
    UnityEngine.Shader shader = material.shader;
    int count = shader != null ? UnityEditor.ShaderUtil.GetPropertyCount(shader) : 0;
    for (int i = 0; i < count; i++) {
        if (UnityEditor.ShaderUtil.GetPropertyType(shader, i) != UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv) continue;
        UnityEngine.Texture2D texture = material.GetTexture(UnityEditor.ShaderUtil.GetPropertyName(shader, i)) as UnityEngine.Texture2D;
        if (texture == null) continue;
        string guid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(texture));
        if (guid.Length > 0) textures[guid] = texture;
    }
}
void CollectMesh(UnityEngine.Mesh mesh) {
    if (mesh == null) return;
    string path = UnityEditor.AssetDatabase.GetAssetPath(mesh);
    if (path.Length > 0 && !meshes.ContainsKey(path)) meshes[path] = mesh;
}

// ---------------------------------------------------------------- heroes
string[] heroes = new string[] { "Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu" };
System.Collections.Generic.List<object> heroList = new System.Collections.Generic.List<object>();
int smrTotal = 0;
foreach (string hero in heroes) {
    string prefabPath = "Assets/Actor/Hero/" + hero + "/" + hero + ".prefab";
    UnityEngine.GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
    if (prefab == null) {
        heroList.Add(new System.Collections.Generic.Dictionary<string, object> { { "name", hero }, { "found", false } });
        continue;
    }
    UnityEngine.Transform root = prefab.transform;
    System.Collections.Generic.Dictionary<string, object> entry = new System.Collections.Generic.Dictionary<string, object>();
    entry["name"] = hero;
    entry["found"] = true;
    entry["prefabPath"] = prefabPath;
    entry["prefabGuid"] = UnityEditor.AssetDatabase.AssetPathToGUID(prefabPath);
    entry["rootScale"] = V3(root.localScale);
    entry["rootPosition"] = V3(root.localPosition);
    entry["rootRotation"] = Q4(root.localRotation);

    UnityEngine.Animator animator = prefab.GetComponentInChildren<UnityEngine.Animator>(true);
    if (animator != null) {
        entry["animator"] = new System.Collections.Generic.Dictionary<string, object> {
            { "path", TPath(animator.transform) },
            { "isHuman", animator.isHuman },
            { "applyRootMotion", animator.applyRootMotion },
            { "hasTransformHierarchy", animator.hasTransformHierarchy },
            { "controller", AssetJson(animator.runtimeAnimatorController) },
            { "avatar", AssetJson(animator.avatar) },
        };
    }

    // Skinned meshes: node, mesh, bones, bounds in root space, material slots (one per sub-mesh).
    UnityEngine.SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
    UnityEngine.Vector3 min = new UnityEngine.Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    UnityEngine.Vector3 max = new UnityEngine.Vector3(float.MinValue, float.MinValue, float.MinValue);
    bool boundsValid = false;
    System.Collections.Generic.List<object> smrList = new System.Collections.Generic.List<object>();
    foreach (UnityEngine.SkinnedMeshRenderer smr in renderers) {
        UnityEngine.Mesh mesh = smr.sharedMesh;
        CollectMesh(mesh);

        // Root-space bounds of the skinned mesh at its current (rest) pose, from the baked vertices.
        // SkinnedMeshRenderer.localBounds is relative to the root bone, so it cannot be used here.
        UnityEngine.Vector3 smrMin = new UnityEngine.Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        UnityEngine.Vector3 smrMax = new UnityEngine.Vector3(float.MinValue, float.MinValue, float.MinValue);
        UnityEngine.Mesh baked = new UnityEngine.Mesh();
        try {
            smr.BakeMesh(baked, false);
            UnityEngine.Matrix4x4 toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            UnityEngine.Vector3[] vertices = baked.vertices;
            for (int v = 0; v < vertices.Length; v++) {
                UnityEngine.Vector3 p = toRoot.MultiplyPoint3x4(vertices[v]);
                if (p.x < smrMin.x) smrMin.x = p.x;
                if (p.y < smrMin.y) smrMin.y = p.y;
                if (p.z < smrMin.z) smrMin.z = p.z;
                if (p.x > smrMax.x) smrMax.x = p.x;
                if (p.y > smrMax.y) smrMax.y = p.y;
                if (p.z > smrMax.z) smrMax.z = p.z;
                if (p.x < min.x) min.x = p.x;
                if (p.y < min.y) min.y = p.y;
                if (p.z < min.z) min.z = p.z;
                if (p.x > max.x) max.x = p.x;
                if (p.y > max.y) max.y = p.y;
                if (p.z > max.z) max.z = p.z;
            }
            boundsValid = boundsValid || vertices.Length > 0;
        } catch (System.Exception e) {
            __EX("bakeMesh", e);
        } finally {
            UnityEngine.Object.DestroyImmediate(baked);
        }

        System.Collections.Generic.Dictionary<string, object> smrEntry = new System.Collections.Generic.Dictionary<string, object>();
        smrEntry["node"] = TPath(smr.transform);
        smrEntry["enabled"] = smr.enabled;
        smrEntry["activeInHierarchy"] = smr.gameObject.activeInHierarchy;
        smrEntry["mesh"] = AssetJson(mesh);
        if (mesh != null) {
            smrEntry["vertexCount"] = mesh.vertexCount;
            smrEntry["triangleCount"] = mesh.triangles.Length / 3;
            smrEntry["subMeshCount"] = mesh.subMeshCount;
            smrEntry["uv2"] = mesh.uv2 != null && mesh.uv2.Length > 0;
            smrEntry["tangents"] = mesh.tangents != null && mesh.tangents.Length > 0;
            smrEntry["colors"] = mesh.colors != null && mesh.colors.Length > 0;
            smrEntry["boneWeights"] = mesh.boneWeights.Length;
            smrEntry["bindposes"] = mesh.bindposes.Length;
            smrEntry["bounds"] = BoundsJson(mesh.bounds);
        }
        smrEntry["localBoundsRelativeToRootBone"] = BoundsJson(smr.localBounds);
        smrEntry["rootSpaceBounds"] = new System.Collections.Generic.Dictionary<string, object> { { "min", V3(smrMin) }, { "max", V3(smrMax) } };
        smrEntry["rootBone"] = smr.rootBone != null ? TPath(smr.rootBone) : null;
        System.Collections.Generic.List<object> bones = new System.Collections.Generic.List<object>();
        for (int i = 0; i < smr.bones.Length; i++) bones.Add(smr.bones[i] != null ? TPath(smr.bones[i]) : null);
        smrEntry["bones"] = bones;

        System.Collections.Generic.List<object> slots = new System.Collections.Generic.List<object>();
        UnityEngine.Material[] mats = smr.sharedMaterials;
        for (int i = 0; i < mats.Length; i++) {
            UnityEngine.Material material = mats[i];
            if (material == null) {
                slots.Add(null);
                continue;
            }
            CollectMaterial(material);
            slots.Add(new System.Collections.Generic.Dictionary<string, object> {
                { "path", UnityEditor.AssetDatabase.GetAssetPath(material) },
                { "name", material.name },
                { "shader", material.shader != null ? material.shader.name : null },
                { "usedBySubMesh", mesh == null || i < mesh.subMeshCount },
            });
        }
        smrEntry["materials"] = slots;
        smrList.Add(smrEntry);
        smrTotal++;
    }
    entry["skinnedMeshes"] = smrList;
    if (boundsValid) {
        entry["rootSpaceBounds"] = new System.Collections.Generic.Dictionary<string, object> { { "min", V3(min) }, { "max", V3(max) } };
    }

    // Any plain MeshRenderers (the generator has to know about them even if there are none).
    int plainCount = 0;
    foreach (UnityEngine.MeshRenderer renderer in prefab.GetComponentsInChildren<UnityEngine.MeshRenderer>(true))
        if (!(renderer is UnityEngine.SkinnedMeshRenderer)) plainCount++;
    entry["meshRendererCount"] = plainCount;

    // Full transform tree: skeleton, sockets and the components on them.
    UnityEngine.Transform[] transforms = prefab.GetComponentsInChildren<UnityEngine.Transform>(true);
    System.Collections.Generic.List<object> transformList = new System.Collections.Generic.List<object>();
    foreach (UnityEngine.Transform t in transforms) {
        UnityEngine.Component[] components = t.GetComponents<UnityEngine.Component>();
        System.Collections.Generic.List<object> componentNames = new System.Collections.Generic.List<object>();
        foreach (UnityEngine.Component component in components)
            componentNames.Add(component == null ? "<missing script>" : component.GetType().Name);
        transformList.Add(new System.Collections.Generic.Dictionary<string, object> {
            { "path", TPath(t) },
            { "parent", t.parent != null ? TPath(t.parent) : null },
            { "active", t.gameObject.activeSelf },
            { "position", V3(t.localPosition) },
            { "rotation", Q4(t.localRotation) },
            { "scale", V3(t.localScale) },
            { "components", componentNames },
        });
    }
    entry["transformCount"] = transforms.Length;
    entry["transforms"] = transformList;
    heroList.Add(entry);
}

// ---------------------------------------------------------------- FBX models
System.Collections.Generic.Dictionary<string, object> modelDocs = new System.Collections.Generic.Dictionary<string, object>();
foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.Mesh> pair in meshes) {
    UnityEngine.Mesh mesh = pair.Value;
    System.Collections.Generic.Dictionary<string, object> model = new System.Collections.Generic.Dictionary<string, object>();
    model["guid"] = UnityEditor.AssetDatabase.AssetPathToGUID(pair.Key);
    UnityEditor.ModelImporter importer = UnityEditor.AssetImporter.GetAtPath(pair.Key) as UnityEditor.ModelImporter;
    if (importer != null) {
        model["importer"] = new System.Collections.Generic.Dictionary<string, object> {
            { "globalScale", importer.globalScale },
            { "useFileScale", importer.useFileScale },
            { "importAnimation", importer.importAnimation },
            { "animationType", importer.animationType.ToString() },
            { "avatarSetup", importer.avatarSetup.ToString() },
            { "generateSecondaryUV", importer.generateSecondaryUV },
            { "importNormals", importer.importNormals.ToString() },
            { "importTangents", importer.importTangents.ToString() },
            { "meshCompression", importer.meshCompression.ToString() },
            { "skinWeights", importer.skinWeights.ToString() },
            { "isReadable", importer.isReadable },
            { "importBlendShapes", importer.importBlendShapes },
            { "bakeAxisConversion", importer.bakeAxisConversion },
            { "optimizeGameObjects", importer.optimizeGameObjects },
            { "weldVertices", importer.weldVertices },
        };
    }
    model["sample"] = new System.Collections.Generic.Dictionary<string, object> {
        { "name", mesh.name },
        { "vertexCount", mesh.vertexCount },
        { "bounds", BoundsJson(mesh.bounds) },
    };
    modelDocs[pair.Key] = model;
}

// ---------------------------------------------------------------- materials
System.Func<UnityEngine.Material, object> MaterialJson = delegate(UnityEngine.Material material) {
    UnityEngine.Shader shader = material.shader;
    string path = UnityEditor.AssetDatabase.GetAssetPath(material);
    System.Collections.Generic.Dictionary<string, object> doc = new System.Collections.Generic.Dictionary<string, object>();
    doc["name"] = material.name;
    doc["path"] = path;
    doc["guid"] = UnityEditor.AssetDatabase.AssetPathToGUID(path);
    doc["shader"] = shader != null ? shader.name : null;
    doc["shaderPath"] = shader != null ? UnityEditor.AssetDatabase.GetAssetPath(shader) : null;
    doc["renderQueue"] = material.renderQueue;
    doc["keywords"] = new System.Collections.Generic.List<string>(material.shaderKeywords);
    System.Collections.Generic.List<object> properties = new System.Collections.Generic.List<object>();
    int count = shader != null ? UnityEditor.ShaderUtil.GetPropertyCount(shader) : 0;
    for (int i = 0; i < count; i++) {
        string name = UnityEditor.ShaderUtil.GetPropertyName(shader, i);
        UnityEditor.ShaderUtil.ShaderPropertyType type = UnityEditor.ShaderUtil.GetPropertyType(shader, i);
        System.Collections.Generic.Dictionary<string, object> property = new System.Collections.Generic.Dictionary<string, object>();
        property["name"] = name;
        property["type"] = type.ToString();
        object value = null;
        if (material.HasProperty(name)) {
            switch (type) {
                case UnityEditor.ShaderUtil.ShaderPropertyType.Color: { value = C4(material.GetColor(name)); break; }
                case UnityEditor.ShaderUtil.ShaderPropertyType.Vector: { value = V4(material.GetVector(name)); break; }
                case UnityEditor.ShaderUtil.ShaderPropertyType.Float:
                case UnityEditor.ShaderUtil.ShaderPropertyType.Range: { value = material.GetFloat(name); break; }
                case UnityEditor.ShaderUtil.ShaderPropertyType.Int: { value = material.GetInt(name); break; }
                case UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv: {
                    UnityEngine.Texture2D texture = material.GetTexture(name) as UnityEngine.Texture2D;
                    System.Collections.Generic.Dictionary<string, object> texEnv = new System.Collections.Generic.Dictionary<string, object>();
                    texEnv["texture"] = texture != null ? UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(texture)) : null;
                    texEnv["scale"] = V2(material.GetTextureScale(name));
                    texEnv["offset"] = V2(material.GetTextureOffset(name));
                    value = texEnv;
                    break;
                }
                default: { value = null; break; }
            }
        }
        property["value"] = value;
        properties.Add(property);
    }
    doc["properties"] = properties;
    return doc;
};
System.Collections.Generic.Dictionary<string, object> materialDocs = new System.Collections.Generic.Dictionary<string, object>();
foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.Material> pair in materials)
    materialDocs[pair.Key] = MaterialJson(pair.Value);

// ---------------------------------------------------------------- textures
System.Collections.Generic.Dictionary<string, object> textureDocs = new System.Collections.Generic.Dictionary<string, object>();
foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.Texture2D> pair in textures) {
    UnityEngine.Texture2D texture = pair.Value;
    string path = UnityEditor.AssetDatabase.GetAssetPath(texture);
    System.Collections.Generic.Dictionary<string, object> doc = new System.Collections.Generic.Dictionary<string, object>();
    doc["name"] = texture.name;
    doc["path"] = path;
    doc["width"] = texture.width;
    doc["height"] = texture.height;
    doc["format"] = texture.format.ToString();
    UnityEditor.TextureImporter importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    if (importer != null) {
        System.Collections.Generic.Dictionary<string, object> settings = new System.Collections.Generic.Dictionary<string, object>();
        settings["sRGB"] = importer.sRGBTexture;
        settings["textureType"] = importer.textureType.ToString();
        settings["alphaSource"] = importer.alphaSource.ToString();
        settings["alphaIsTransparency"] = importer.alphaIsTransparency;
        settings["mipmaps"] = importer.mipmapEnabled;
        settings["filterMode"] = importer.filterMode.ToString();
        settings["wrapMode"] = importer.wrapMode.ToString();
        settings["maxTextureSize"] = importer.maxTextureSize;
        settings["npotScale"] = importer.npotScale.ToString();
        settings["compression"] = importer.textureCompression.ToString();
        settings["readable"] = importer.isReadable;
        // Windows builds take the Standalone override when one is set.
        try {
            UnityEditor.TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            if (standalone != null && standalone.overridden) {
                settings["standalone"] = new System.Collections.Generic.Dictionary<string, object> {
                    { "maxTextureSize", standalone.maxTextureSize },
                    { "format", standalone.format.ToString() },
                    { "compressionQuality", standalone.compressionQuality },
                };
            }
        } catch (System.Exception e) {
            __EX("standaloneOverride", e);
        }
        doc["importer"] = settings;
    }
    textureDocs[pair.Key] = doc;
}

System.Collections.Generic.Dictionary<string, object> document = new System.Collections.Generic.Dictionary<string, object>();
document["heroes"] = heroList;
document["models"] = modelDocs;
document["materials"] = materialDocs;
document["textures"] = textureDocs;
string exportDir = "__OUT_DIR__";
System.IO.Directory.CreateDirectory(exportDir);
System.IO.File.WriteAllText(System.IO.Path.Combine(exportDir, "heroes.json"), __J(document));

__sb.Append('{');
__P("file", __S("heroes.json"));
__P("heroCount", heroes.Length.ToString());
__P("skinnedMeshCount", smrTotal.ToString());
__P("materialCount", materials.Count.ToString());
__P("textureCount", textures.Count.ToString());
__P("modelCount", meshes.Count.ToString());
__P("ok", __B(true));
__END();
return __sb.ToString();
