
// SanGuoXEngine M0 probe 2/5: full property dump of a hero material and its textures. Read-only.
__sb.Append('{');

UnityEngine.Material mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Actor/Hero/Hero_LvBu/BattleMat/Hero_LvBu_Body.mat");
if (mat == null) {
    __P("found", __B(false));
    __END();
    return __sb.ToString();
}

__P("found", __B(true));
__P("name", __S(mat.name));
__P("path", __S(UnityEditor.AssetDatabase.GetAssetPath(mat)));
UnityEngine.Shader shader = mat.shader;
__P("shader", __S(shader != null ? shader.name : null));
if (shader != null) __P("shaderPath", __S(UnityEditor.AssetDatabase.GetAssetPath(shader)));
__P("renderQueue", mat.renderQueue.ToString());

__sb.Append("\"keywords\":[");
foreach (string keyword in mat.shaderKeywords) __sb.Append(__S(keyword)).Append(',');
if (__sb[__sb.Length - 1] == ',') __sb.Length--;
__sb.Append("],");

// Every property the shader declares, with the material's current value.
int propertyCount = shader != null ? UnityEditor.ShaderUtil.GetPropertyCount(shader) : 0;
__sb.Append("\"properties\":[");
for (int i = 0; i < propertyCount; i++) {
    string name = UnityEditor.ShaderUtil.GetPropertyName(shader, i);
    UnityEditor.ShaderUtil.ShaderPropertyType type = UnityEditor.ShaderUtil.GetPropertyType(shader, i);
    __sb.Append("{\"name\":").Append(__S(name))
        .Append(",\"type\":").Append(__S(type.ToString()))
        .Append(",\"description\":").Append(__S(UnityEditor.ShaderUtil.GetPropertyDescription(shader, i)))
        .Append(",\"value\":");

    if (!mat.HasProperty(name)) {
        __sb.Append("null");
    } else {
        switch (type) {
            case UnityEditor.ShaderUtil.ShaderPropertyType.Color: {
                __sb.Append(__C(mat.GetColor(name)));
                break;
            }
            case UnityEditor.ShaderUtil.ShaderPropertyType.Vector: {
                __sb.Append(__V4(mat.GetVector(name)));
                break;
            }
            case UnityEditor.ShaderUtil.ShaderPropertyType.Float:
            case UnityEditor.ShaderUtil.ShaderPropertyType.Range: {
                __sb.Append(__F(mat.GetFloat(name)));
                if (type == UnityEditor.ShaderUtil.ShaderPropertyType.Range) {
                    // defminmax: 1 = minimum, 2 = maximum.
                    float min = UnityEditor.ShaderUtil.GetRangeLimits(shader, i, 1);
                    float max = UnityEditor.ShaderUtil.GetRangeLimits(shader, i, 2);
                    __sb.Append(",\"range\":[").Append(__F(min)).Append(',').Append(__F(max)).Append(']');
                }
                break;
            }
            case UnityEditor.ShaderUtil.ShaderPropertyType.Int: {
                __sb.Append(mat.GetInt(name).ToString());
                break;
            }
            case UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv: {
                UnityEngine.Vector2 scale = mat.GetTextureScale(name);
                UnityEngine.Vector2 offset = mat.GetTextureOffset(name);
                __sb.Append(__T(mat.GetTexture(name)))
                    .Append(",\"scale\":[").Append(__F(scale.x)).Append(',').Append(__F(scale.y)).Append(']')
                    .Append(",\"offset\":[").Append(__F(offset.x)).Append(',').Append(__F(offset.y)).Append(']');
                break;
            }
            default: {
                __sb.Append("null");
                break;
            }
        }
    }

    __sb.Append("},");
}
if (__sb[__sb.Length - 1] == ',') __sb.Length--;
__sb.Append("],");

// The raw serialization too: it is the reference the offline material converter has to match.
__P("serializedLength", UnityEditor.EditorJsonUtility.ToJson(mat).Length.ToString());

__END();
return __sb.ToString();
