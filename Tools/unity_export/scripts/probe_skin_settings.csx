
// SanGuoXEngine skin audit: the project-wide skin weight setting Unity skins with.
//
// Every hero renderer is at quality Auto and the importer at Standard, while both the rendered
// silhouettes and BakeMesh use the first influence alone (compare_skin.py). Auto resolves to
// QualitySettings.skinWeights of the active quality level. Read-only; the active quality level is
// read and put back.
__sb.Append('{');
__P("activeQuality", __S(UnityEngine.QualitySettings.names[UnityEngine.QualitySettings.GetQualityLevel()]));
__P("activeSkinWeights", __S(UnityEngine.QualitySettings.skinWeights.ToString()));
int active = UnityEngine.QualitySettings.GetQualityLevel();
System.Collections.Generic.List<object> levels = new System.Collections.Generic.List<object>();
try {
    for (int level = 0; level < UnityEngine.QualitySettings.names.Length; level++) {
        UnityEngine.QualitySettings.SetQualityLevel(level, false);
        levels.Add(new System.Collections.Generic.Dictionary<string, object> {
            { "level", level }, { "name", UnityEngine.QualitySettings.names[level] },
            { "skinWeights", UnityEngine.QualitySettings.skinWeights.ToString() },
        });
    }
} finally {
    UnityEngine.QualitySettings.SetQualityLevel(active, false);
}
__P("activeAfterRestore", UnityEngine.QualitySettings.GetQualityLevel().ToString());
__P("levels", __J(levels));
__END();
return __sb.ToString();
