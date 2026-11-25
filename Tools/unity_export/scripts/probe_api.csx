
// Utility: list the public static methods of editor types, so exporter scripts are written against
// the signatures this Unity version really has instead of guessed ones. Read-only.
// `py -3 Tools/unity_export/run_export.py run probe_api --filter Range` narrows the listing by
// method-name substring (the driver substitutes the __NAME_FILTER__ token, empty by default).
__sb.Append('{');
string[] typeNames = new string[] {
    "UnityEditor.ShaderUtil",
    "UnityEditor.AnimationUtility",
    "UnityEditor.AssetDatabase",
    "UnityEditor.PrefabUtility",
    "UnityEditor.AnimationMode",
    "UnityEditor.EditorJsonUtility",
    "UnityEditor.AssetImporter",
    "UnityEditor.SceneManagement.EditorSceneManager",
    "UnityEditor.EditorApplication",
};
string filter = "__NAME_FILTER__";
__sb.Append("\"methods\":{");
foreach (string typeName in typeNames) {
    System.Type type = System.Type.GetType(typeName + ", UnityEditor");
    if (type == null) {
        foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
            System.Type found = assembly.GetType(typeName);
            if (found != null) {
                type = found;
                break;
            }
        }
    }
    __sb.Append(__S(typeName)).Append(":[");
    if (type == null) {
        __sb.Append(__S("<type not found>"));
    } else {
        System.Reflection.MethodInfo[] methods = type.GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
        foreach (System.Reflection.MethodInfo method in methods) {
            if (filter.Length > 0 && !method.Name.Contains(filter)) continue;
            System.Text.StringBuilder signature = new System.Text.StringBuilder();
            signature.Append(method.ReturnType.Name).Append(' ').Append(method.Name).Append('(');
            System.Reflection.ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++) {
                if (i > 0) signature.Append(", ");
                if (parameters[i].ParameterType.IsByRef) {
                    signature.Append(parameters[i].IsOut ? "out " : "ref ").Append(parameters[i].ParameterType.GetElementType().Name);
                } else {
                    signature.Append(parameters[i].ParameterType.Name);
                }
                signature.Append(' ').Append(parameters[i].Name);
            }
            signature.Append(')');
            __sb.Append(__S(signature.ToString())).Append(',');
        }
        if (__sb[__sb.Length - 1] == ',') __sb.Length--;
    }
    __sb.Append("],");
}
if (__sb[__sb.Length - 1] == ',') __sb.Length--;
__sb.Append('}');
__END();
return __sb.ToString();
