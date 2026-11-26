using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

using XEngine.Echo;
using XEngine.Editor;
using XEngine.Editor.Projects;
using XEngine.Runtime;
using XEngine.Runtime.Rendering;
using XEngine.Runtime.Resources;
using XEngine.Vector;

namespace SanGuoXEngine.Editor;

/// <summary>
/// Assembles the native 桃园 scene (<c>Assets/Scenes/TaoYuan.scene</c>) from the offline export
/// (<c>Export/scene/scene.json</c>) and the import manifest (<c>Export/import/scene-assets.json</c>):
/// the full node hierarchy with transforms and static flags, MeshRenderers bound to FBX sub-asset
/// meshes and generated materials by GUID, a LODGroup per LOD node, the two baked lightmap atlas
/// pages with every renderer's atlas transform, the directional light, the panorama sky material,
/// and a scene camera posed at the close reference frame.
///
/// Recorded approximations (see docs/PORT-STATUS.md): effect materials land in M5 so their
/// renderers draw nothing; per-renderer shadow flags do not exist in the engine (shadow casting
/// follows GameObject.IsStatic, set from the static-batching flag); the ReflectionProbe needs a
/// native cubemap asset path and the post Volume (ACES/LUT/Bloom) is acceptance tuning, both M3-4+.
/// </summary>
public static class SanguoSceneBuilder
{
    public const string SceneDocumentRelativePath = "Export/scene/scene.json";
    public const string OutputSceneRelativePath = "Scenes/TaoYuan.scene";
    public const string SkyShaderRelativePath = "Environment/SkyPanorama.shader";
    public const string SkyMaterialRelativePath = "Environment/SkyDusk.mat";
    private static readonly Guid BattleVolumeProfileGuid = new("1b0f55a2-34b6-4e3c-9a74-934b238314c1");

    private const string BuiltinQuad = "Environment/Builtin/Quad.mesh";
    private const string BuiltinCube = "Environment/Builtin/Cube.mesh";
    private const string BuiltinSphere = "Environment/Builtin/Sphere.mesh";

    [MenuItem("Sanguo/Import/5 Generate Scene")]
    private static void MenuScene() => Debug.Log("[Sanguo] " + GenerateScene());

    /// <summary>The battle variant: the same scene plus the battle driver GameObject.</summary>
    public const string OutputBattleSceneRelativePath = "Scenes/TaoYuanBattle.scene";

    [MenuItem("Sanguo/Import/5b Generate Battle Scene")]
    private static void MenuBattleScene() => Debug.Log("[Sanguo] " + GenerateBattleScene());

    /// <summary>Writes TaoYuanBattle.scene — the full scene with a SanguoBattleDriver node.</summary>
    public static string GenerateBattleScene()
    {
        string rootPath = Project.Current?.RootPath ?? throw new InvalidOperationException("No project is open.");
        string assetsPath = Project.Current!.AssetsPath;

        using JsonDocument sceneDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(rootPath, SceneDocumentRelativePath)));
        using JsonDocument manifestDocument = JsonDocument.Parse(
            File.ReadAllText(SanguoSceneGenerator.FullManifestPath()));
        JsonElement scene = sceneDocument.RootElement;
        JsonElement manifest = manifestDocument.RootElement;

        List<string> warnings = new();
        Guid skyTextureGuid = Guid.Parse(manifest.GetProperty("sky")
            .GetProperty("customReflection").GetProperty("guid").GetString()!);
        Guid skyMaterialGuid = WriteSkyMaterial(assetsPath, skyTextureGuid);
        Dictionary<string, Guid> builtinMeshes = EnsureBuiltinMeshes(assetsPath, warnings);
        Scene nativeScene = BuildScene(scene, manifest, skyMaterialGuid, builtinMeshes, warnings);

        // the driver lives in the game assembly, which the editor project does not reference —
        // add it by type name (the game scripts always load before the generator runs)
        Type? driverType = Type.GetType("SanGuoXEngine.Battle.SanguoBattleDriver, SanGuoXEngine.Game")
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("SanGuoXEngine.Battle.SanguoBattleDriver"))
                .FirstOrDefault(t => t != null);
        if (driverType == null) throw new InvalidOperationException("SanguoBattleDriver type not found (game assembly not loaded).");
        GameObject driverGo = new("Battle Driver");
        nativeScene.Add(driverGo);
        driverGo.AddComponent(driverType);

        Guid sceneGuid = StableGuid("scene", "taoyuan-battle");
        SanguoHeroGenerator.WriteNative(nativeScene, OutputBattleSceneRelativePath, sceneGuid, "SceneImporter");
        int roots = 0;
        foreach (GameObject _ in nativeScene.RootObjects) roots++;
        return $"TaoYuanBattle.scene: {roots} root object(s), {warnings.Count} warning(s)";
    }

    /// <summary>Writes the sky material and the scene document. Returns a summary line.</summary>
    public static string GenerateScene()
    {
        string rootPath = Project.Current?.RootPath ?? throw new InvalidOperationException("No project is open.");
        string assetsPath = Project.Current!.AssetsPath;

        using JsonDocument sceneDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(rootPath, SceneDocumentRelativePath)));
        using JsonDocument manifestDocument = JsonDocument.Parse(
            File.ReadAllText(SanguoSceneGenerator.FullManifestPath()));
        JsonElement scene = sceneDocument.RootElement;
        JsonElement manifest = manifestDocument.RootElement;

        List<string> warnings = new();
        Guid skyTextureGuid = Guid.Parse(manifest.GetProperty("sky")
            .GetProperty("customReflection").GetProperty("guid").GetString()!);
        Guid skyMaterialGuid = WriteSkyMaterial(assetsPath, skyTextureGuid);

        // Builtin Quad/Cube/Sphere meshes for the renderers whose Unity mesh lives in the engine's
        // default resources; the engine has no builtin mesh library, so the builder writes them.
        Dictionary<string, Guid> builtinMeshes = EnsureBuiltinMeshes(assetsPath, warnings);

        Scene nativeScene = BuildScene(scene, manifest, skyMaterialGuid, builtinMeshes, warnings);

        Guid sceneGuid = StableGuid("scene", "taoyuan");
        SanguoHeroGenerator.WriteNative(nativeScene, OutputSceneRelativePath, sceneGuid, "SceneImporter");
        int roots = 0;
        foreach (GameObject _ in nativeScene.RootObjects) roots++;
        string summary = $"TaoYuan.scene: {roots} root object(s), "
            + $"{warnings.Count} warning(s)";
        foreach (string warning in warnings) summary += "\n  ! " + warning;
        return summary;
    }

    // ------------------------------------------------------------ scene assembly

    private static Scene BuildScene(JsonElement scene, JsonElement manifest, Guid skyMaterialGuid,
        Dictionary<string, Guid> builtinMeshes, List<string> warnings)
    {
        Scene nativeScene = new() { Name = "TaoYuan" };

        // Sky: the dusk panorama drawn by the custom sky material, and Unity's Skybox three-colour
        // ambient approximated with the engine's two-colour hemisphere (equator = sky/ground lerp).
        nativeScene.Skybox.Mode = Scene.SkyboxMode.Material;
        nativeScene.Skybox.CustomMaterial = new AssetRef<Material>(skyMaterialGuid);
        JsonElement ambient = scene.GetProperty("renderSettings").GetProperty("ambientSky");
        JsonElement ground = scene.GetProperty("renderSettings").GetProperty("ambientGround");
        nativeScene.Ambient.Mode = Scene.AmbientLightParams.AmbientMode.Hemisphere;
        nativeScene.Ambient.Strength = scene.GetProperty("renderSettings")
            .GetProperty("ambientIntensity").GetSingle();
        nativeScene.Ambient.SkyColor = ReadColor(ambient);
        nativeScene.Ambient.GroundColor = ReadColor(ground);

        // Baked lightmap atlas pages: the manifest's colour maps (dir/shadowmask stay on disk for
        // a later node that teaches the engine to consume them).
        JsonElement lightmaps = manifest.GetProperty("lightmaps");
        for (int i = 0; i < lightmaps.GetArrayLength(); i++)
        {
            JsonElement color = lightmaps[i].GetProperty("color");
            nativeScene.BakedLighting.Lightmaps.Add(new AssetRef<Texture2D>(
                Guid.Parse(color.GetProperty("guid").GetString()!)));
        }

        // Hierarchy first: every node in scene order (parents always precede children).
        Dictionary<string, GameObject> nodeByPath = new(StringComparer.Ordinal);
        Dictionary<string, MeshRenderer> rendererByPath = new(StringComparer.Ordinal);
        JsonElement nodes = scene.GetProperty("nodes");
        for (int i = 0; i < nodes.GetArrayLength(); i++)
            CreateNode(nodes[i], nativeScene, nodeByPath, rendererByPath, manifest, builtinMeshes, warnings);

        // The directional light: engine lights shine along the node's -Z where Unity's shine along
        // +Z, so the node takes Unity's rotation composed with a half turn about X.
        JsonElement lights = scene.GetProperty("lights");
        for (int i = 0; i < lights.GetArrayLength(); i++)
            CreateLight(lights[i], nodeByPath);

        // The scene camera sits at the close reference frame; M3-4's capture drives it per frame.
        GameObject volumeObject = new("Sanguo Post FX");
        nativeScene.Add(volumeObject);
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.IsGlobal = true;
        volume.Priority = 100;
        volume.Profile = new AssetRef<VolumeProfile>(BattleVolumeProfileGuid);

        JsonElement close = scene.GetProperty("frames").GetProperty("close");
        CreateCamera(close, nativeScene);

        return nativeScene;
    }

    private static void CreateNode(JsonElement node, Scene scene,
        Dictionary<string, GameObject> nodeByPath, Dictionary<string, MeshRenderer> rendererByPath,
        JsonElement manifest, Dictionary<string, Guid> builtinMeshes, List<string> warnings)
    {
        string path = node.GetProperty("path").GetString()!;
        int nameStart = path.LastIndexOf('/') + 1;
        GameObject go = new(path[nameStart..]) { LayerIndex = node.GetProperty("layer").GetInt32() };
        go.Enabled = node.GetProperty("active").GetBoolean();
        go.IsStatic = node.GetProperty("staticBatching").GetBoolean();

        Transform transform = go.Transform;
        transform.LocalPosition = ReadFloat3(node.GetProperty("localPosition"));
        transform.LocalRotation = ReadQuaternion(node.GetProperty("localRotation"));
        transform.LocalScale = ReadFloat3(node.GetProperty("localScale"));

        scene.Add(go);
        int parentEnd = path.LastIndexOf('/');
        if (parentEnd > 0 && nodeByPath.TryGetValue(path[..parentEnd], out GameObject? parent))
            go.SetParent(parent, worldPositionStays: false);
        nodeByPath[path] = go;

        if (node.TryGetProperty("mesh", out JsonElement mesh))
            CreateRenderer(node, go, path, rendererByPath, manifest, builtinMeshes, warnings);

        if (node.TryGetProperty("lods", out JsonElement lods))
        {
            LODGroup group = go.AddComponent<LODGroup>();
            group.Size = node.GetProperty("lodWorldSize").GetSingle();
            group.LocalReferencePoint = ReadFloat3(node.GetProperty("lodReference"));
            for (int i = 0; i < lods.GetArrayLength(); i++)
            {
                JsonElement level = lods[i];
                LODLevel lodLevel = new()
                {
                    ScreenRelativeTransitionHeight = level.GetProperty("height").GetSingle(),
                    FadeTransitionWidth = level.GetProperty("fade").GetSingle(),
                };
                foreach (JsonElement rendererPath in level.GetProperty("renderers").EnumerateArray())
                {
                    string? rendererRef = rendererPath.GetString();
                    if (rendererRef != null && rendererByPath.TryGetValue(rendererRef, out MeshRenderer? renderer))
                        lodLevel.Renderers.Add(renderer);
                }
                group.Levels.Add(lodLevel);
            }
        }
    }

    private static void CreateRenderer(JsonElement node, GameObject go, string path,
        Dictionary<string, MeshRenderer> rendererByPath, JsonElement manifest,
        Dictionary<string, Guid> builtinMeshes, List<string> warnings)
    {
        JsonElement mesh = node.GetProperty("mesh");
        string meshPath = mesh.GetProperty("path").GetString()!;
        string meshName = mesh.GetProperty("name").GetString()!;
        Guid meshGuid;
        if (meshPath.Contains("unity default resources", StringComparison.OrdinalIgnoreCase))
        {
            if (!builtinMeshes.TryGetValue(meshName, out meshGuid))
            {
                warnings.Add($"builtin mesh '{meshName}' is not generated: {path}");
                return;
            }
        }
        else
        {
            string key = meshPath + "|" + meshName;
            if (!manifest.GetProperty("meshes").TryGetProperty(key, out JsonElement entry)
                || !entry.TryGetProperty("fbxGuid", out JsonElement fbxGuidElement)
                || fbxGuidElement.GetString() is not string fbxGuid)
            {
                warnings.Add($"mesh '{key}' has no manifest entry (SpeedTree .asset?) — renderer skipped: {path}");
                return;
            }
            meshGuid = AssetEntry.DeriveSubAssetGuid(Guid.Parse(fbxGuid), entry.GetProperty("meshName").GetString()!);
        }

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = new AssetRef<Mesh>(meshGuid);
        if (node.TryGetProperty("materials", out JsonElement materialsElement))
        foreach (JsonElement material in materialsElement.EnumerateArray())
        {
            string materialPath = material.GetProperty("path").GetString()!;
            if (manifest.GetProperty("materials").TryGetProperty(materialPath, out JsonElement materialEntry)
                && materialEntry.GetProperty("guid").GetString() is string materialGuid)
            {
                renderer.Materials.Add(new AssetRef<Material>(Guid.Parse(materialGuid)));
            }
            // Materials without a manifest entry are the M5 effect materials; the renderer just
            // draws the mapped subset.
        }
        if (node.TryGetProperty("lightmapIndex", out JsonElement lightmapIndex))
        {
            renderer.LightmapIndex = lightmapIndex.GetInt32();
            if (node.TryGetProperty("lightmapScaleOffset", out JsonElement scaleOffset))
                renderer.LightmapScaleOffset = new Float4(
                    scaleOffset[0].GetSingle(), scaleOffset[1].GetSingle(),
                    scaleOffset[2].GetSingle(), scaleOffset[3].GetSingle());
        }
        rendererByPath[path] = renderer;
    }

    private static void CreateLight(JsonElement light, Dictionary<string, GameObject> nodeByPath)
    {
        if (light.GetProperty("type").GetString() != "Directional") return;
        string path = light.GetProperty("path").GetString()!;
        if (!nodeByPath.TryGetValue(path, out GameObject? go)) return;

        DirectionalLight component = go.AddComponent<DirectionalLight>();
        JsonElement color = light.GetProperty("color");
        component.Color = new Color(color[0].GetSingle(), color[1].GetSingle(),
            color[2].GetSingle(), color[3].GetSingle());
        component.Intensity = light.GetProperty("intensity").GetSingle();
        component.CastShadows = light.GetProperty("shadows").GetString() != "None";
        component.BakeMode = light.GetProperty("bakeType").GetString() == "Baked"
            ? LightBakeMode.Baked : LightBakeMode.Mixed;
        // The node record carries Unity's rotation; re-derive it from the light's own world
        // forward so the engine's -Z convention points the same way.
        JsonElement forward = light.GetProperty("forward");
        Float3 direction = -new Float3(forward[0].GetSingle(), forward[1].GetSingle(), forward[2].GetSingle());
        go.Transform.LocalRotation = Quaternion.LookRotation(direction, Float3.UnitY);
    }

    private static void CreateCamera(JsonElement frame, Scene scene)
    {
        GameObject go = new("Scene Camera");
        scene.Add(go);
        go.Transform.LocalPosition = ReadFloat3(frame.GetProperty("position"));
        go.Transform.LocalRotation = ReadQuaternion(frame.GetProperty("rotation"));
        Camera camera = go.AddComponent<Camera>();
        camera.FieldOfView = frame.GetProperty("fov").GetSingle();
        camera.ClearFlags = CameraClearFlags.Skybox;
        camera.HDR = true;
        // The engine default far plane is 100 m; the Unity capture used 4000 m and the scene
        // spans ~500 m, so without this every mid/panorama view far-clips its subject.
        camera.FarClipPlane = 4000f;
        // jws2 keeps its collision proxies (65 Unity-Cube meshes) on layer 31, which the game
        // camera's culling mask excludes; without the exclusion they render as solid cubes.
        LayerMask culling = camera.CullingMask;
        culling.RemoveLayer(31);
        camera.CullingMask = culling;
    }

    // ------------------------------------------------------------ sky material

    private static Guid WriteSkyMaterial(string assetsPath, Guid skyTextureGuid)
    {
        string shaderPath = Path.Combine(assetsPath, SkyShaderRelativePath);
        Shader shader = File.Exists(shaderPath)
            ? Shader.LoadFromFile(shaderPath)
            : throw new InvalidOperationException($"the sky shader is missing: {SkyShaderRelativePath}");
        Material material = new(shader) { Name = "SkyDusk" };
        material.SetTexture("_MainTex", new AssetRef<Texture2D>(skyTextureGuid));

        // WriteNative records the guid in the .meta, not on the asset object — the scene must
        // reference the same guid explicitly.
        Guid guid = StableGuid("material", "sky", "dusk");
        SanguoHeroGenerator.WriteNative(material, SkyMaterialRelativePath, guid, "MaterialImporter");
        return guid;
    }

    // ------------------------------------------------------------ builtin meshes

    private static Dictionary<string, Guid> EnsureBuiltinMeshes(string assetsPath, List<string> warnings)
    {
        Dictionary<string, Guid> guids = new(StringComparer.Ordinal)
        {
            ["Quad"] = EnsureBuiltinMesh(assetsPath, "Quad", BuiltinQuad, BuildQuad),
            ["Cube"] = EnsureBuiltinMesh(assetsPath, "Cube", BuiltinCube, BuildCube),
            ["Sphere"] = EnsureBuiltinMesh(assetsPath, "Sphere", BuiltinSphere, BuildSphere),
        };
        return guids;
    }

    private static Guid EnsureBuiltinMesh(string assetsPath, string name, string enginePath, Func<Mesh> build)
    {
        string absolute = Path.Combine(assetsPath, enginePath);
        Guid guid = StableGuid("builtin", "mesh", name.ToLowerInvariant());
        if (File.Exists(absolute) && ReadGuid(absolute + ".meta") == guid)
            return guid;            // already on disk: keep the file and its guid

        Mesh mesh = build();
        mesh.Name = name;
        SanguoHeroGenerator.WriteNative(mesh, enginePath, guid, "MeshImporter");
        return guid;
    }

    private static Mesh BuildQuad()
    {
        Mesh mesh = new()
        {
            Vertices = [new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(-0.5f, 0.5f, 0f), new(0.5f, 0.5f, 0f)],
            Normals = [new(0f, 0f, -1f), new(0f, 0f, -1f), new(0f, 0f, -1f), new(0f, 0f, -1f)],
            UV = [new(0f, 0f), new(1f, 0f), new(0f, 1f), new(1f, 1f)],
            Indices = [0u, 2u, 1u, 1u, 2u, 3u],
        };
        mesh.RecalculateBounds();
        mesh.Upload();
        return mesh;
    }

    private static Mesh BuildCube()
    {
        // 24 corner/face vertices like Unity's cube: 4 per face, outward normals, CCW windings.
        Float3[] faceNormals = [new(0, 0, 1), new(0, 0, -1), new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0)];
        List<Float3> vertices = [];
        List<Float3> normals = [];
        List<Float2> uvs = [];
        List<uint> indices = [];
        foreach (Float3 normal in faceNormals)
        {
            // Two in-face axes for this normal, in a winding that faces outward.
            Float3 a = MathF.Abs(normal.Z) > 0.5f ? new Float3(1, 0, 0) :
                MathF.Abs(normal.X) > 0.5f ? new Float3(0, 1, 0) : new Float3(1, 0, 0);
            Float3 b = MathF.Abs(normal.Z) > 0.5f ? new Float3(0, 1, 0) :
                MathF.Abs(normal.X) > 0.5f ? new Float3(0, 0, 1) : new Float3(0, 0, 1);
            int start = vertices.Count;
            Float3[] corners =
            [
                -a - b, a - b, a + b, -a + b,      // face corners around the centre
            ];
            foreach (Float3 corner in corners)
            {
                vertices.Add((normal + corner) * 0.5f);
                normals.Add(normal);
            }
            uvs.Add(new(0, 0)); uvs.Add(new(1, 0)); uvs.Add(new(1, 1)); uvs.Add(new(0, 1));
            indices.AddRange([(uint)start, (uint)start + 1, (uint)start + 2,
                (uint)start, (uint)start + 2, (uint)start + 3]);
        }
        Mesh mesh = new() { Vertices = [.. vertices], Normals = [.. normals], UV = [.. uvs], Indices = [.. indices] };
        mesh.RecalculateBounds();
        mesh.Upload();
        return mesh;
    }

    private static Mesh BuildSphere()
    {
        const int segments = 24, rings = 12;
        List<Float3> vertices = [];
        List<Float3> normals = [];
        List<Float2> uvs = [];
        List<uint> indices = [];
        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = MathF.PI * ring / rings;
            float sinPhi = MathF.Sin(phi), cosPhi = MathF.Cos(phi);
            for (int segment = 0; segment <= segments; segment++)
            {
                float theta = 2f * MathF.PI * segment / segments;
                Float3 normal = new(sinPhi * MathF.Cos(theta), cosPhi, sinPhi * MathF.Sin(theta));
                vertices.Add(normal * 0.5f);
                normals.Add(normal);
                uvs.Add(new(segment / (float)segments, 1f - ring / (float)rings));
            }
        }
        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                int current = ring * (segments + 1) + segment;
                int next = current + segments + 1;
                indices.AddRange([(uint)current, (uint)next, (uint)current + 1, (uint)current + 1, (uint)next, (uint)next + 1]);
            }
        }
        Mesh mesh = new() { Vertices = [.. vertices], Normals = [.. normals], UV = [.. uvs], Indices = [.. indices] };
        mesh.RecalculateBounds();
        mesh.Upload();
        return mesh;
    }

    // ------------------------------------------------------------ small helpers

    private static Float3 ReadFloat3(JsonElement array) => new(
        array[0].GetSingle(), array[1].GetSingle(), array[2].GetSingle());

    private static Float4 ReadColor(JsonElement array) => new(
        array[0].GetSingle(), array[1].GetSingle(), array[2].GetSingle(), array[3].GetSingle());

    private static Quaternion ReadQuaternion(JsonElement array) => new(
        array[0].GetSingle(), array[1].GetSingle(), array[2].GetSingle(), array[3].GetSingle());

    /// <summary>The same deterministic scheme the offline import uses (engine_meta.stable_guid).</summary>
    private static Guid StableGuid(params string[] parts)
    {
        using MD5 md5 = MD5.Create();
        byte[] digest = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("xengine:sanguo:" + string.Join(":", parts)));
        return new Guid(digest);
    }

    private static Guid? ReadGuid(string metaPath)
    {
        if (!File.Exists(metaPath)) return null;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metaPath));
        return Guid.TryParse(document.RootElement.GetProperty("guid").GetString(), out Guid guid) ? guid : null;
    }
}
