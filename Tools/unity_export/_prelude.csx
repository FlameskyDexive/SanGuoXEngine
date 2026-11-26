
// ---- generated prelude: a minimal JSON writer built from local functions ----
// The AIBridge snippet host wraps this file inside `object Execute()`, so helpers must be local
// functions. Value helpers (__S, __F, __V3, __C, __Q4, __T) are pure: they only return strings.
// Only __P and direct __sb.Append calls write, so argument order never matters.
// Usage: __P("key", __S(text)) / __P("key", count.ToString()) / __P("flag", __B(ok));
// finish a document with __END(); and `return __sb.ToString();`.
var __sb = new System.Text.StringBuilder();

string __S(string s) {
    System.Text.StringBuilder b = new System.Text.StringBuilder();
    b.Append('"');
    if (s != null) {
        foreach (char c in s) {
            if (c == '"') b.Append("\\\"");
            else if (c == '\\') b.Append("\\\\");
            else if (c == '\n') b.Append("\\n");
            else if (c == '\r') b.Append("\\r");
            else if (c == '\t') b.Append("\\t");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
    }
    b.Append('"');
    return b.ToString();
}
string __B(bool b) { return b ? "true" : "false"; }
string __F(float f) { return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
// Serializes dictionaries, lists, arrays and scalars, so scripts assemble data as
// Dictionary<string, object> / List<object> instead of concatenating JSON by hand. Enums must be
// passed through ToString() (a bare enum would be written unquoted).
string __J(object value) {
    if (value == null) return "null";
    if (value is string) return __S((string)value);
    if (value is bool) return __B((bool)value);
    if (value is float) return __F((float)value);
    if (value is int) return ((int)value).ToString();
    if (value is long) return ((long)value).ToString();
    if (value is double) return ((double)value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    System.Collections.IDictionary dictionary = value as System.Collections.IDictionary;
    if (dictionary != null) {
        System.Text.StringBuilder b = new System.Text.StringBuilder();
        b.Append('{');
        bool first = true;
        foreach (System.Collections.DictionaryEntry entry in dictionary) {
            if (!first) b.Append(',');
            first = false;
            b.Append(__S((string)entry.Key)).Append(':').Append(__J(entry.Value));
        }
        return b.Append('}').ToString();
    }
    System.Collections.IEnumerable list = value as System.Collections.IEnumerable;
    if (list != null) {
        System.Text.StringBuilder b = new System.Text.StringBuilder();
        b.Append('[');
        bool first = true;
        foreach (object item in list) {
            if (!first) b.Append(',');
            first = false;
            b.Append(__J(item));
        }
        return b.Append(']').ToString();
    }
    return __S(value.ToString());
}
void __P(string key, string value) {
    __sb.Append(__S(key)).Append(':').Append(value == null ? "null" : value).Append(',');
}
void __END() {
    if (__sb.Length > 0 && __sb[__sb.Length - 1] == ',') __sb.Length--;
    __sb.Append('}');
}
// Writes the finished JSON document to the export folder and starts a fresh object in __sb, so a
// script can dump a large document to disk and still return a small summary.
void __SAVE(string fileName) {
    string dir = "__OUT_DIR__";
    System.IO.Directory.CreateDirectory(dir);
    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, fileName), __sb.ToString());
    __sb.Length = 0;
    __sb.Append('{');
}
// Records an exception against <key>Error and returns the message.
string __EX(string key, System.Exception e) {
    string message = e == null ? "" : e.GetType().FullName + ": " + e.Message;
    try { __P(key + "Error", __S(message)); } catch (System.Exception) { }
    return message;
}
string __V3(UnityEngine.Vector3 v) {
    return "[" + __F(v.x) + "," + __F(v.y) + "," + __F(v.z) + "]";
}
string __V4(UnityEngine.Vector4 v) {
    return "[" + __F(v.x) + "," + __F(v.y) + "," + __F(v.z) + "," + __F(v.w) + "]";
}
string __C(UnityEngine.Color c) {
    return "[" + __F(c.r) + "," + __F(c.g) + "," + __F(c.b) + "," + __F(c.a) + "]";
}
string __Q4(UnityEngine.Quaternion q) {
    return "[" + __F(q.x) + "," + __F(q.y) + "," + __F(q.z) + "," + __F(q.w) + "]";
}
// A texture reference with its asset path and (for 2D textures) the importer settings the port
// needs: colour space, type, wrapping, filtering and size.
string __T(UnityEngine.Texture t) {
    if (t == null) return "null";
    string path = UnityEditor.AssetDatabase.GetAssetPath(t);
    string info = "\"name\":" + __S(t.name) + ",\"path\":" + __S(path)
        + ",\"guid\":" + __S(UnityEditor.AssetDatabase.AssetPathToGUID(path));
    UnityEngine.Texture2D t2 = t as UnityEngine.Texture2D;
    if (t2 != null) {
        info += ",\"width\":" + t2.width + ",\"height\":" + t2.height + ",\"format\":" + __S(t2.format.ToString());
        UnityEditor.TextureImporter imp = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
        if (imp != null) {
            info += ",\"sRGB\":" + __B(imp.sRGBTexture) + ",\"type\":" + __S(imp.textureType.ToString())
                + ",\"alphaIsTransparency\":" + __B(imp.alphaIsTransparency) + ",\"mipmaps\":" + __B(imp.mipmapEnabled)
                + ",\"wrapU\":" + __S(imp.wrapMode.ToString()) + ",\"filter\":" + __S(imp.filterMode.ToString())
                + ",\"maxSize\":" + imp.maxTextureSize + ",\"npot\":" + __S(imp.npotScale.ToString())
                + ",\"compression\":" + __S(imp.textureCompression.ToString()) + ",\"readable\":" + __B(imp.isReadable);
        }
    }
    return "{" + info + "}";
}
