using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using XEngine.Echo;
using XEngine.Editor;
using XEngine.Editor.Projects;
using XEngine.Runtime;
using XEngine.Runtime.Resources;
using XEngine.Vector;

namespace SanGuoXEngine.Editor;

/// <summary>
/// Creates the native scene materials from the manifest the offline import writes
/// (<c>Tools/sanguo_import/import_scene_assets.py</c> → <c>Export/import/scene-assets.json</c>).
/// The scene document itself (nodes, LOD groups, lightmaps) is assembled by
/// <c>SanguoSceneBuilder</c>; this class only turns the mapped materials into engine
/// <c>.mat</c> files so the builder (and the asset refresh) can reference them.
/// </summary>
public static class SanguoSceneGenerator
{
    /// <summary>The manifest, relative to the project root.</summary>
    public const string ManifestRelativePath = "Export/import/scene-assets.json";

    [MenuItem("Sanguo/Import/4 Generate Scene Materials")]
    private static void MenuMaterials() => Debug.Log("[Sanguo] " + GenerateSceneMaterials(FullManifestPath()));

    public static string FullManifestPath()
        => Path.Combine(Project.Current?.RootPath ?? throw new InvalidOperationException("No project is open."), ManifestRelativePath);

    /// <summary>Writes one native .mat per manifest material. Returns a summary with warnings.</summary>
    public static string GenerateSceneMaterials(string manifestPath)
    {
        int materials = 0;
        List<string> warnings = new();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement materialsElement = document.RootElement.GetProperty("materials");
        foreach (JsonProperty entry in materialsElement.EnumerateObject())
        {
            string unityPath = entry.Name;
            string name = entry.Value.GetProperty("name").GetString() ?? entry.Name;
            try
            {
                WriteMaterial(entry.Value, Project.Current!.AssetsPath, unityPath);
                materials++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Material '{name}' ({unityPath}): {ex.Message}");
            }
        }

        string summary = $"{materials} scene material(s), {warnings.Count} warning(s)";
        foreach (string warning in warnings) summary += "\n  ! " + warning;
        return summary;
    }

    private static void WriteMaterial(JsonElement material, string assetsPath, string unityPath)
    {
        string enginePath = material.GetProperty("enginePath").GetString()
            ?? throw new InvalidDataException($"material {unityPath} carries no enginePath.");
        Material native = new(Shader.LoadDefault(DefaultShader.Standard));
        native.Name = material.GetProperty("name").GetString() ?? "Material";

        foreach (JsonProperty property in material.GetProperty("properties").EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    // A texture, named by its engine path in the import root.
                    native.SetTexture(property.Name, new AssetRef<Texture2D>(RequireEntry(property.Value.GetString()!, "texture").Guid));
                    break;
                case JsonValueKind.Array:
                    int count = property.Value.GetArrayLength();
                    if (count == 4)
                        native.SetColor(property.Name, new Color(
                            property.Value[0].GetSingle(), property.Value[1].GetSingle(),
                            property.Value[2].GetSingle(), property.Value[3].GetSingle()));
                    else if (count == 2)
                        native.SetVector(property.Name, new Float2(property.Value[0].GetSingle(), property.Value[1].GetSingle()));
                    else
                        throw new InvalidDataException($"property '{property.Name}' is an array of {count}, expected 2 or 4.");
                    break;
                case JsonValueKind.Number:
                    native.SetFloat(property.Name, property.Value.GetSingle());
                    break;
                case JsonValueKind.Null:
                    break;   // "keep the shader default"
                default:
                    throw new InvalidDataException($"property '{property.Name}' has unsupported kind {property.Value.ValueKind}.");
            }
        }

        Guid guid = Guid.TryParse(material.GetProperty("guid").GetString(), out Guid parsed)
            ? parsed : throw new InvalidDataException($"material {unityPath} carries no valid guid.");
        SanguoHeroGenerator.WriteNative(native, enginePath, guid, "MaterialImporter");
    }

    /// <summary>The asset entry of a project-relative path (without the Assets/ prefix).</summary>
    private static AssetEntry RequireEntry(string enginePath, string what)
        => enginePath.Length > 0 && EditorAssetBackend.Instance.GetEntry(enginePath) is AssetEntry entry
            ? entry
            : throw new InvalidOperationException($"the {what} '{enginePath}' is not imported in this project.");
}
