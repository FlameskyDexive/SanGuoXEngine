
// SanGuoXEngine M3 export: every asset the 桃园 scene references — meshes (file + mesh name),
// model importer settings, materials with every property value, textures with importer settings.
// Read-only: the scene is opened WITHOUT saving and the scenes open before are restored.
//
// Document shape (scene-assets.json):
//   meshes    by "file|meshName": file path/guid, mesh name, vertex count, bounds, UV2 presence
//   models    by FBX path: ModelImporter settings
//   materials by path: shader, render queue, keywords, every property value (TexEnv → guid)
//   textures  by guid: path, size, TextureImporter settings
string TPath(UnityEngine.Transform t) {
    string p = t.name;
    for (UnityEngine.Transform c = t.parent; c != null; c = c.parent) p = c.name + "/" + p;
    return p;
}
float[] V2(UnityEngine.Vector2 v) { return new float[] { v.x, v.y }; }
float[] V3(UnityEngine.Vector3 v) { return new float[] { v.x, v.y, v.z }; }
float[] V4(UnityEngine.Vector4 v) { return new float[] { v.x, v.y, v.z, v.w }; }
float[] C4(UnityEngine.Color c) { return new float[] { c.r, c.g, c.b, c.a }; }
System.Collections.Generic.Dictionary<string, object> BoundsJson(UnityEngine.Bounds b) {
    return new System.Collections.Generic.Dictionary<string, object> { { "center", V3(b.center) }, { "extents", V3(b.extents) } };
}

System.Collections.Generic.Dictionary<string, UnityEngine.Mesh> meshes =
    new System.Collections.Generic.Dictionary<string, UnityEngine.Mesh>(System.StringComparer.Ordinal);
System.Collections.Generic.Dictionary<string, UnityEngine.Material> materials =
    new System.Collections.Generic.Dictionary<string, UnityEngine.Material>(System.StringComparer.OrdinalIgnoreCase);
System.Collections.Generic.Dictionary<string, UnityEngine.Texture2D> textures =
    new System.Collections.Generic.Dictionary<string, UnityEngine.Texture2D>(System.StringComparer.Ordinal);

void CollectTexture(UnityEngine.Texture texture) {
    UnityEngine.Texture2D t2 = texture as UnityEngine.Texture2D;
    if (t2 == null) return;
    string guid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(t2));
    if (guid.Length > 0) textures[guid] = t2;
}
void CollectMaterial(UnityEngine.Material material) {
    if (material == null) return;
    string path = UnityEditor.AssetDatabase.GetAssetPath(material);
    if (materials.ContainsKey(path)) return;
    materials[path] = material;
    UnityEngine.Shader shader = material.shader;
    int count = shader != null ? UnityEditor.ShaderUtil.GetPropertyCount(shader) : 0;
    for (int i = 0; i < count; i++) {
        if (UnityEditor.ShaderUtil.GetPropertyType(shader, i) != UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv) continue;
        CollectTexture(material.GetTexture(UnityEditor.ShaderUtil.GetPropertyName(shader, i)));
    }
}
void CollectMesh(UnityEngine.Mesh mesh) {
    if (mesh == null) return;
    string key = UnityEditor.AssetDatabase.GetAssetPath(mesh) + "|" + mesh.name;
    if (!meshes.ContainsKey(key)) meshes[key] = mesh;
}

__sb.Append('{');
string scenePath = "Assets/Scenes/GameScenes/C-ZhuoXian_TaoYuan.unity";
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

try {
    UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,
        UnityEditor.SceneManagement.OpenSceneMode.Single);
    UnityEngine.GameObject root = scene.GetRootGameObjects()[0];

    UnityEngine.MeshFilter[] filters = root.GetComponentsInChildren<UnityEngine.MeshFilter>(true);
    for (int i = 0; i < filters.Length; i++) CollectMesh(filters[i].sharedMesh);
    UnityEngine.SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
    for (int i = 0; i < skinned.Length; i++) CollectMesh(skinned[i].sharedMesh);
    UnityEngine.MeshCollider[] meshColliders = root.GetComponentsInChildren<UnityEngine.MeshCollider>(true);
    for (int i = 0; i < meshColliders.Length; i++) CollectMesh(meshColliders[i].sharedMesh);
    UnityEngine.MeshRenderer[] meshRenderers = root.GetComponentsInChildren<UnityEngine.MeshRenderer>(true);
    for (int i = 0; i < meshRenderers.Length; i++) {
        UnityEngine.Material[] mats = meshRenderers[i].sharedMaterials;
        for (int m = 0; m < mats.Length; m++) CollectMaterial(mats[m]);
    }
    UnityEngine.ParticleSystemRenderer[] particles = root.GetComponentsInChildren<UnityEngine.ParticleSystemRenderer>(true);
    for (int i = 0; i < particles.Length; i++) {
        UnityEngine.Material[] mats = particles[i].sharedMaterials;
        for (int m = 0; m < mats.Length; m++) CollectMaterial(mats[m]);
    }

    // ---- meshes -----------------------------------------------------------------------
    System.Text.StringBuilder doc = new System.Text.StringBuilder(1 << 24);
    doc.Append("{\"meshes\":{");
    bool first = true;
    foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.Mesh> pair in meshes) {
        UnityEngine.Mesh mesh = pair.Value;
        string path = UnityEditor.AssetDatabase.GetAssetPath(mesh);
        if (!first) doc.Append(',');
        first = false;
        doc.Append(__S(path + "|" + mesh.name)).Append(':');
        doc.Append("{\"file\":").Append(__S(path)).Append(",\"guid\":").Append(__S(UnityEditor.AssetDatabase.AssetPathToGUID(path)))
            .Append(",\"meshName\":").Append(__S(mesh.name))
            .Append(",\"vertexCount\":").Append(mesh.vertexCount)
            .Append(",\"hasUV2\":").Append(__B(mesh.uv2 != null && mesh.uv2.Length == mesh.vertexCount))
            .Append(",\"subMeshCount\":").Append(mesh.subMeshCount)
            .Append(",\"bounds\":").Append(__J(BoundsJson(mesh.bounds))).Append('}');
    }
    doc.Append("},\"models\":{");
    // ---- model importer settings (one entry per FBX file) ------------------------------
    System.Collections.Generic.Dictionary<string, bool> modelFiles = new System.Collections.Generic.Dictionary<string, bool>();
    foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.Mesh> pair in meshes) {
        string path = UnityEditor.AssetDatabase.GetAssetPath(pair.Value);
        if (path.Length == 0 || modelFiles.ContainsKey(path)) continue;
        modelFiles[path] = true;
    }
    first = true;
    foreach (string path in modelFiles.Keys) {
        if (!first) doc.Append(',');
        first = false;
        UnityEditor.ModelImporter importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.ModelImporter;
        doc.Append(__S(path)).Append(":{\"guid\":").Append(__S(UnityEditor.AssetDatabase.AssetPathToGUID(path))).Append(',');
        if (importer != null) {
            doc.Append("\"importer\":{\"globalScale\":").Append(__F(importer.globalScale))
                .Append(",\"useFileScale\":").Append(__B(importer.useFileScale))
                .Append(",\"importAnimation\":").Append(__B(importer.importAnimation))
                .Append(",\"animationType\":").Append(__S(importer.animationType.ToString()))
                .Append(",\"generateSecondaryUV\":").Append(__B(importer.generateSecondaryUV))
                .Append(",\"importNormals\":").Append(__S(importer.importNormals.ToString()))
                .Append(",\"importTangents\":").Append(__S(importer.importTangents.ToString()))
                .Append(",\"meshCompression\":").Append(__S(importer.meshCompression.ToString()))
                .Append(",\"isReadable\":").Append(__B(importer.isReadable))
                .Append(",\"weldVertices\":").Append(__B(importer.weldVertices)).Append("},");
        }
        // "Library/unity default resources" is a virtual path (built-in Quad / Cube): no file.
        doc.Append("\"fileSize\":").Append(System.IO.File.Exists(path) ? new System.IO.FileInfo(path).Length : 0).Append('}');
    }
    doc.Append("},");

    // ---- materials ---------------------------------------------------------------------
    doc.Append("\"materials\":{");
    first = true;
    foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.Material> pair in materials) {
        UnityEngine.Material material = pair.Value;
        UnityEngine.Shader shader = material.shader;
        if (!first) doc.Append(',');
        first = false;
        doc.Append(__S(pair.Key)).Append(":{\"name\":").Append(__S(material.name))
            .Append(",\"guid\":").Append(__S(UnityEditor.AssetDatabase.AssetPathToGUID(pair.Key)))
            .Append(",\"shader\":").Append(shader != null ? __S(shader.name) : "null")
            .Append(",\"renderQueue\":").Append(material.renderQueue)
            .Append(",\"keywords\":").Append(__J(new System.Collections.Generic.List<string>(material.shaderKeywords)))
            .Append(",\"properties\":{");
        int count = shader != null ? UnityEditor.ShaderUtil.GetPropertyCount(shader) : 0;
        bool firstProp = true;
        for (int i = 0; i < count; i++) {
            string name = UnityEditor.ShaderUtil.GetPropertyName(shader, i);
            UnityEditor.ShaderUtil.ShaderPropertyType type = UnityEditor.ShaderUtil.GetPropertyType(shader, i);
            if (!firstProp) doc.Append(',');
            firstProp = false;
            doc.Append(__S(name)).Append(":{\"type\":").Append(__S(type.ToString()));
            if (material.HasProperty(name)) {
                switch (type) {
                    case UnityEditor.ShaderUtil.ShaderPropertyType.Color: doc.Append(",\"value\":").Append(__J(C4(material.GetColor(name)))); break;
                    case UnityEditor.ShaderUtil.ShaderPropertyType.Vector: doc.Append(",\"value\":").Append(__J(V4(material.GetVector(name)))); break;
                    case UnityEditor.ShaderUtil.ShaderPropertyType.Float:
                    case UnityEditor.ShaderUtil.ShaderPropertyType.Range: doc.Append(",\"value\":").Append(__F(material.GetFloat(name))); break;
                    case UnityEditor.ShaderUtil.ShaderPropertyType.Int: doc.Append(",\"value\":").Append(material.GetInt(name)); break;
                    case UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv: {
                        UnityEngine.Texture texture = material.GetTexture(name);
                        doc.Append(",\"texture\":").Append(texture != null ? __S(UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(texture))) : "null")
                            .Append(",\"texturePath\":").Append(texture != null ? __S(UnityEditor.AssetDatabase.GetAssetPath(texture)) : "null")
                            .Append(",\"scale\":").Append(__J(V2(material.GetTextureScale(name))))
                            .Append(",\"offset\":").Append(__J(V2(material.GetTextureOffset(name))));
                        CollectTexture(texture);
                        break;
                    }
                    default: break;
                }
            }
            doc.Append('}');
        }
        doc.Append("}}");
    }
    doc.Append("},");

    // ---- textures -----------------------------------------------------------------------
    doc.Append("\"textures\":{");
    first = true;
    foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.Texture2D> pair in textures) {
        UnityEngine.Texture2D texture = pair.Value;
        string path = UnityEditor.AssetDatabase.GetAssetPath(texture);
        if (!first) doc.Append(',');
        first = false;
        doc.Append(__S(pair.Key)).Append(":{\"name\":").Append(__S(texture.name))
            .Append(",\"path\":").Append(__S(path))
            .Append(",\"width\":").Append(texture.width).Append(",\"height\":").Append(texture.height)
            .Append(",\"format\":").Append(__S(texture.format.ToString()))
            .Append(",\"fileSize\":").Append(path.Length > 0 && System.IO.File.Exists(path) ? new System.IO.FileInfo(path).Length : 0);
        UnityEditor.TextureImporter importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
        if (importer != null) {
            doc.Append(",\"importer\":{\"sRGB\":").Append(__B(importer.sRGBTexture))
                .Append(",\"textureType\":").Append(__S(importer.textureType.ToString()))
                .Append(",\"alphaSource\":").Append(__S(importer.alphaSource.ToString()))
                .Append(",\"alphaIsTransparency\":").Append(__B(importer.alphaIsTransparency))
                .Append(",\"mipmaps\":").Append(__B(importer.mipmapEnabled))
                .Append(",\"filterMode\":").Append(__S(importer.filterMode.ToString()))
                .Append(",\"wrapMode\":").Append(__S(importer.wrapMode.ToString()))
                .Append(",\"maxTextureSize\":").Append(importer.maxTextureSize)
                .Append(",\"npotScale\":").Append(__S(importer.npotScale.ToString()))
                .Append(",\"compression\":").Append(__S(importer.textureCompression.ToString()))
                .Append(",\"readable\":").Append(__B(importer.isReadable)).Append('}');
        }
        doc.Append('}');
    }
    doc.Append("}}");

    string document = "scene-assets.json";
    System.IO.File.WriteAllText(System.IO.Path.Combine("__OUT_DIR__", document), doc.ToString());
    __P("exported", __B(true));
    __P("file", __S(document));
    __P("meshes", meshes.Count.ToString());
    __P("models", modelFiles.Count.ToString());
    __P("materials", materials.Count.ToString());
    __P("textures", textures.Count.ToString());
} catch (System.Exception e) {
    __EX("export", e);
    __P("exported", __B(false));
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
