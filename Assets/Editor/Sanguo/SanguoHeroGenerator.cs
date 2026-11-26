using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using XEngine.Animation;
using XEngine.Animation.Humanoid;
using XEngine.Echo;
using XEngine.Editor;
using XEngine.Editor.Importers;
using XEngine.Editor.Projects;
using XEngine.Runtime;
using XEngine.Runtime.Resources;
using XEngine.Vector;

namespace SanGuoXEngine.Editor;

/// <summary>
/// Creates the native hero materials and prefabs from the manifest the offline import writes
/// (<c>Tools/sanguo_import/import_heroes.py</c> → <c>Export/import/heroes.json</c>).
/// </summary>
/// <remarks>
/// The import has already done the conversion work that needs the Unity project: it copied the FBX
/// files and textures, derived the surface and emission maps, and decided which engine shader and
/// properties stand in for each Unity material. This class only turns that decision into engine
/// objects, so it holds no knowledge of Unity:
/// <list type="bullet">
/// <item>materials: a <see cref="Material"/> on <c>Default/Standard</c> or
/// <c>Default/StandardAnisotropic</c>, written as a native <c>.mat</c>;</item>
/// <item>prefabs: the engine's own FBX model instantiated, every skinned mesh bound to its
/// materials, an <see cref="Animator"/> on the root bound to the Avatar the model import generated,
/// written as a native <c>.prefab</c>.</item>
/// </list>
/// Run from the menu (Sanguo/Import) or from a live editor through <c>runtime_eval</c> (see
/// <c>Tools/xengine_live/evals/generate_heroes.cs</c>).
/// </remarks>
public static class SanguoHeroGenerator
{
    /// <summary>The manifest, relative to the project root.</summary>
    public const string ManifestRelativePath = "Export/import/heroes.json";

    private const string ColorProperty = "_MainColor";

    /// <summary>How a manifest property value is applied to a material.</summary>
    public enum PropertyKind { Texture, Vector2, Color, Float }

    /// <summary>One material property as the manifest states it.</summary>
    public readonly record struct MaterialProperty(string Name, PropertyKind Kind, string? TexturePath, float[] Values);

    private sealed class Report
    {
        public int Materials;
        public int Prefabs;
        public int Bindings;
        public readonly List<string> Warnings = new();

        public override string ToString()
        {
            string text = $"{Materials} material(s), {Prefabs} prefab(s), {Bindings} material binding(s), {Warnings.Count} warning(s)";
            foreach (string warning in Warnings) text += "\n  ! " + warning;
            return text;
        }
    }

    // ------------------------------------------------------------------ menu

    [MenuItem("Sanguo/Import/1 Generate Hero Materials")]
    private static void MenuMaterials() => Debug.Log("[Sanguo] " + GenerateMaterials(FullManifestPath()));

    [MenuItem("Sanguo/Import/2 Generate Hero Prefabs")]
    private static void MenuPrefabs() => Debug.Log("[Sanguo] " + GeneratePrefabs(FullManifestPath()));

    [MenuItem("Sanguo/Import/3 Generate Animation Clips")]
    private static void MenuClips() => Debug.Log("[Sanguo] " + SanguoClipBuilder.Generate(SanguoClipBuilder.ExportDirectory()));

    public static string FullManifestPath()
        => Path.Combine(Project.Current?.RootPath ?? throw new InvalidOperationException("No project is open."), ManifestRelativePath);

    // ------------------------------------------------------------------ properties (no engine state)

    /// <summary>Reads a manifest material's properties. A null value means "keep the shader default"
    /// and produces no entry.</summary>
    public static List<MaterialProperty> ReadMaterialProperties(JsonElement properties)
    {
        List<MaterialProperty> result = new();
        foreach (JsonProperty property in properties.EnumerateObject())
        {
            JsonElement value = property.Value;
            if (value.ValueKind == JsonValueKind.Null) continue;

            if (property.Name is "_MainTex" or "_NormalTex" or "_SurfaceTex" or "_EmissionTex")
                result.Add(new MaterialProperty(property.Name, PropertyKind.Texture, value.GetString(), Array.Empty<float>()));
            else if (property.Name is "_Tiling" or "_Offset")
                result.Add(new MaterialProperty(property.Name, PropertyKind.Vector2, null, ReadNumbers(value, 2)));
            else if (property.Name == ColorProperty)
                result.Add(new MaterialProperty(property.Name, PropertyKind.Color, null, ReadNumbers(value, 4)));
            else
                result.Add(new MaterialProperty(property.Name, PropertyKind.Float, null, new[] { value.GetSingle() }));
        }
        return result;
    }

    private static float[] ReadNumbers(JsonElement value, int count)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count)
            throw new InvalidDataException($"expected an array of {count} numbers.");
        float[] numbers = new float[count];
        for (int i = 0; i < count; i++) numbers[i] = value[i].GetSingle();
        return numbers;
    }

    // ------------------------------------------------------------------ materials

    /// <summary>Writes one native .mat per manifest material. Returns a summary with warnings.</summary>
    public static string GenerateMaterials(string manifestPath)
    {
        Report report = new();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        string root = document.RootElement.GetProperty("root").GetString() ?? "";
        foreach (JsonProperty entry in document.RootElement.GetProperty("materials").EnumerateObject())
        {
            JsonElement material = entry.Value;
            string name = material.GetProperty("name").GetString() ?? entry.Name;
            try
            {
                WriteMaterial(material, root);
                report.Materials++;
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"Material '{name}': {ex.Message}");
            }
        }
        return report.ToString();
    }

    private static void WriteMaterial(JsonElement material, string root)
    {
        string shaderName = material.GetProperty("shader").GetString() ?? "Standard";
        Material native = new Material(Shader.LoadDefault(shaderName == "StandardAnisotropic" ? DefaultShader.StandardAnisotropic : DefaultShader.Standard));
        native.Name = material.GetProperty("name").GetString() ?? "Material";

        foreach (MaterialProperty property in ReadMaterialProperties(material.GetProperty("properties")))
        {
            switch (property.Kind)
            {
                case PropertyKind.Texture:
                    native.SetTexture(property.Name, new AssetRef<Texture2D>(RequireEntry(property.TexturePath ?? "", "texture").Guid));
                    break;
                case PropertyKind.Vector2:
                    native.SetVector(property.Name, new Float2(property.Values[0], property.Values[1]));
                    break;
                case PropertyKind.Color:
                    native.SetColor(property.Name, new Color(property.Values[0], property.Values[1], property.Values[2], property.Values[3]));
                    break;
                case PropertyKind.Float:
                    native.SetFloat(property.Name, property.Values[0]);
                    break;
            }
        }

        string enginePath = material.GetProperty("enginePath").GetString() ?? "";
        WriteNative(native, enginePath, ParseGuid(material.GetProperty("guid"), "material"), "MaterialImporter");
    }

    // ------------------------------------------------------------------ prefabs

    /// <summary>Writes one native prefab per manifest hero: the FBX model with its materials bound
    /// and an Animator on the root. Returns a summary with warnings.</summary>
    public static string GeneratePrefabs(string manifestPath)
    {
        Report report = new();
        EditorAssetBackend backend = EditorAssetBackend.Instance;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));

        // Material engine path -> guid, so a prefab can reference a material that was generated
        // in this session before the asset database imported the file.
        Dictionary<string, Guid> materialGuids = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in document.RootElement.GetProperty("materials").EnumerateObject())
            materialGuids[entry.Value.GetProperty("enginePath").GetString() ?? ""] = ParseGuid(entry.Value.GetProperty("guid"), "material");

        foreach (JsonElement hero in document.RootElement.GetProperty("heroes").EnumerateArray())
        {
            string name = hero.GetProperty("name").GetString() ?? "?";
            try
            {
                WriteHeroPrefab(hero, backend, materialGuids, report);
                report.Prefabs++;
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"Hero '{name}': {ex.Message}");
            }
        }
        return report.ToString();
    }

    private static void WriteHeroPrefab(JsonElement hero, EditorAssetBackend backend, Dictionary<string, Guid> materialGuids, Report report)
    {
        string name = hero.GetProperty("name").GetString() ?? "?";
        string modelPath = hero.GetProperty("model").GetString() ?? "";
        AssetEntry modelEntry = RequireEntry(modelPath, "model");
        if (AssetDatabase.Get(modelEntry.Guid) is not PrefabAsset model)
            throw new InvalidOperationException($"'{modelPath}' is not imported as a model yet (run the asset refresh first).");
        GameObject instance = model.Instantiate() ?? throw new InvalidOperationException($"'{modelPath}' could not be instantiated.");
        try
        {
            instance.Name = name;

            foreach (JsonElement skinned in hero.GetProperty("skinnedMeshes").EnumerateArray())
            {
                string node = skinned.GetProperty("node").GetString() ?? "";
                GameObject target = FindByPath(instance, node)
                    ?? throw new InvalidOperationException($"no node '{node}' in the imported model.");
                SkinnedMeshRenderer? renderer = target.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null)
                    throw new InvalidOperationException($"node '{node}' has no SkinnedMeshRenderer in the imported model.");

                renderer.Materials.Clear();
                foreach (JsonElement materialPath in skinned.GetProperty("materials").EnumerateArray())
                {
                    string path = materialPath.GetString() ?? "";
                    if (!materialGuids.TryGetValue(path, out Guid guid))
                        throw new InvalidOperationException($"skinned mesh '{node}' uses material '{path}', which is not in the manifest.");
                    renderer.Materials.Add(new AssetRef<Material>(guid));
                }
                report.Bindings++;
            }

            // Unity drives every hero through a Generic rig avatar with root motion on. The model
            // import registered the Avatar as a sub-asset but attaches no Animator when the FBX
            // carries no animation, so the component is added here.
            Animator animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
            animator.Avatar = new AssetRef<Avatar>(FindAvatarGuid(backend, modelEntry.Guid, name));
            // Unity drives the hero by the root-motion curves of its clips: the Animator moves the
            // actor by their increments. The clips and their controller are generated by
            // SanguoClipBuilder from the same GUID rule, so the reference is stable.
            animator.ApplyRootMotion = true;
            animator.Controller = new AssetRef<AnimatorController>(SanguoClipBuilder.StableGuid("hero-controller", name));

            string enginePath = hero.GetProperty("prefabPath").GetString() ?? "";
            WritePrefab(instance, enginePath, ParseGuid(hero.GetProperty("guid"), "prefab"));
        }
        finally
        {
            instance.Dispose();
        }
    }

    private static Guid FindAvatarGuid(EditorAssetBackend backend, Guid modelGuid, string heroName)
    {
        foreach (SubAssetEntry sub in backend.GetSubAssets(modelGuid))
        {
            if (sub.Type == typeof(Avatar) || sub.Name == heroName + "Avatar")
                return sub.Guid;
        }
        throw new InvalidOperationException($"the model of '{heroName}' has no Avatar sub-asset (animationType must be Generic with avatarDefinition 0).");
    }

    private static GameObject? FindByPath(GameObject root, string path)
    {
        GameObject current = root;
        foreach (string segment in path.Split('/'))
        {
            GameObject? next = null;
            foreach (GameObject child in current.Children)
            {
                if (string.Equals(child.Name, segment, StringComparison.Ordinal))
                {
                    next = child;
                    break;
                }
            }
            if (next == null) return null;
            current = next;
        }
        return current;
    }

    // ------------------------------------------------------------------ lookup + Echo write helpers

    private static Guid ParseGuid(JsonElement element, string what)
        => Guid.TryParse(element.GetString(), out Guid guid) ? guid : throw new InvalidDataException($"the manifest carries no valid guid for the {what}.");

    /// <summary>The asset entry of a project-relative path (without the Assets/ prefix).</summary>
    private static AssetEntry RequireEntry(string enginePath, string what)
        => enginePath.Length > 0 && EditorAssetBackend.Instance.GetEntry(enginePath) is AssetEntry entry
            ? entry
            : throw new InvalidOperationException($"the {what} '{enginePath}' is not imported in this project.");

    /// <summary>Serializes an EngineObject with its $type envelope and writes file + meta.</summary>
    internal static void WriteNative(EngineObject asset, string enginePath, Guid guid, string importerName)
    {
        string absolute = Path.Combine(Project.Current!.AssetsPath, enginePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        EchoObject echo = Serializer.Serialize(typeof(object), asset)
            ?? throw new InvalidOperationException($"failed to serialize '{asset.Name}'.");
        File.WriteAllText(absolute, echo.WriteToString());
        EditorRegistries.Initialize();
        AssetImporter? importer = EditorRegistries.CreateImporterByName(importerName);
        MetaFile.Write(MetaFile.GetMetaPath(absolute), new MetaFileData
        {
            Guid = guid,
            ImporterType = importerName,
            ImporterVersion = importer?.Version ?? 1,
        });
    }

    /// <summary>Serializes a GameObject tree as a native .prefab (the shape PrefabImporter reads).</summary>
    private static void WritePrefab(GameObject source, string enginePath, Guid guid)
    {
        source.ClearPrefabDataRecursive();
        Guid savedId = source.AssetID;
        source.AssetID = Guid.Empty;
        try
        {
            WriteNative(source, enginePath, guid, "PrefabImporter");
        }
        finally
        {
            source.AssetID = savedId;
        }
    }
}
