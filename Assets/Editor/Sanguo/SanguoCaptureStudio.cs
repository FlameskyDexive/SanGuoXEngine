using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using ImageMagick;

using XEngine.Editor;
using XEngine.Editor.Projects;
using XEngine.Runtime;
using XEngine.Runtime.Rendering;
using XEngine.Runtime.Resources;
using XEngine.Runtime.RHI;
using XEngine.Vector;

namespace SanGuoXEngine.Editor;

/// <summary>
/// Renders the heroes in the open scene under the neutral studio setup of the Unity reference
/// capture, so both engines can be compared frame for frame.
/// </summary>
/// <remarks>
/// The Unity side (<c>Tools/unity_export/scripts/capture_heroes.csx</c>) renders each hero with a
/// white directional light, flat grey ambient, a grey backdrop and no post-processing, and writes
/// the camera of every frame to <c>shots.json</c>. This class reads that file and reproduces the
/// frames: same camera transform and field of view, same light direction, same ambient and backdrop.
/// The scene is edited in memory only: what was there before (ambient, camera, light, tonemapping)
/// is put back afterwards, anything created for the studio is removed, and nothing is saved.
/// Two engine conventions are accounted for here, because the comparison depends on them: XEngine
/// lights shine along the node's -Z where Unity's shine along +Z (see <see cref="LightNodeRotation"/>),
/// and the camera target reads back as linear colour that must be encoded for display.
/// </remarks>
public static partial class SanguoCaptureStudio
{
    /// <summary>Where the Unity capture writes its frames and camera setup.</summary>
    public const string UnityShotsRelativePath = "Export/unity-refs/heroes/shots.json";
    public const string DefaultOutputRelativePath = "Export/xengine-shots/heroes";

    private sealed class StudioState
    {
        public Scene Scene = null!;
        public Camera Camera = null!;
        public DirectionalLight Sun = null!;
        public Volume? Volume;
        public TonemapVolumeComponent? Tonemap;

        // What the scene had before the studio was set up.
        public Scene.AmbientLightParams SavedAmbient;
        public Float3 SavedCameraPosition;
        public Quaternion SavedCameraRotation;
        public CameraClearFlags SavedClearFlags;
        public Color SavedClearColor;
        public float SavedFov;
        public float SavedNear;
        public float SavedFar;
        public Quaternion SavedSunRotation;
        public Color SavedSunColor;
        public float SavedSunIntensity;
        public bool SavedSunShadows;
        public int SavedTonemapType;
        public bool SavedTonemapOverride;
        public bool SavedVolumeEnabled;

        // Objects the studio had to create because the scene had no camera or light.
        public readonly List<GameObject> Created = new();
    }

    /// <summary>Renders every shot of the Unity capture. Returns a summary with warnings.</summary>
    public static string CaptureHeroes(string shotsPath, string outputDirectory)
    {
        List<string> warnings = new();
        int captured = 0;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(shotsPath));
        JsonElement setup = document.RootElement;
        int width = setup.GetProperty("width").GetInt32();
        int height = setup.GetProperty("height").GetInt32();
        JsonElement shots = setup.GetProperty("shots");

        StudioState studio = EnterStudio(setup);
        try
        {
            Directory.CreateDirectory(outputDirectory);
            foreach (JsonElement shot in shots.EnumerateArray())
            {
                string name = shot.GetProperty("name").GetString() ?? "?";
                if (!shot.GetProperty("captured").GetBoolean())
                {
                    warnings.Add($"Unity did not capture '{name}'.");
                    continue;
                }
                try
                {
                    RenderHero(studio, name, shot, width, height, Path.Combine(outputDirectory, name + ".png"));
                    captured++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Hero '{name}': {ex.Message}");
                }
            }
        }
        finally
        {
            LeaveStudio(studio);
        }

        string summary = $"{captured} frame(s) of {width}x{height} in {outputDirectory}";
        foreach (string warning in warnings) summary += "\n  ! " + warning;
        return summary;
    }

    /// <summary>Puts the open scene into the studio setup, remembering what it had before.</summary>
    private static StudioState EnterStudio(JsonElement setup)
    {
        StudioState studio = new();
        Scene scene = Scene.Current ?? throw new InvalidOperationException("No scene is open.");
        studio.Scene = scene;
        List<GameObject> objects = CollectAll(scene);

        if (Find<Camera>(objects) is Camera camera) studio.Camera = camera;
        else studio.Camera = Create<Camera>(scene, "Studio Camera", studio.Created);
        if (Find<DirectionalLight>(objects) is DirectionalLight sun) studio.Sun = sun;
        else studio.Sun = Create<DirectionalLight>(scene, "Studio Light", studio.Created);
        studio.Volume = Find<Volume>(objects);
        studio.Tonemap = studio.Volume?.ResolvedProfile?.GetOrAdd<TonemapVolumeComponent>();

        studio.SavedAmbient = scene.Ambient;
        studio.SavedCameraPosition = studio.Camera.Transform.Position;
        studio.SavedCameraRotation = studio.Camera.Transform.Rotation;
        studio.SavedClearFlags = studio.Camera.ClearFlags;
        studio.SavedClearColor = studio.Camera.ClearColor;
        studio.SavedFov = studio.Camera.FieldOfView;
        studio.SavedNear = studio.Camera.NearClipPlane;
        studio.SavedFar = studio.Camera.FarClipPlane;
        studio.SavedSunRotation = studio.Sun.Transform.Rotation;
        studio.SavedSunColor = studio.Sun.Color;
        studio.SavedSunIntensity = studio.Sun.Intensity;
        studio.SavedSunShadows = studio.Sun.CastShadows;
        studio.SavedTonemapType = studio.Tonemap?.Type ?? 0;
        studio.SavedTonemapOverride = studio.Tonemap?.OverrideType ?? false;
        studio.SavedVolumeEnabled = studio.Volume != null && studio.Volume.Enabled;

        // Neutral studio: no tone curve, flat ambient, white sun without shadows, grey backdrop.
        if (studio.Tonemap != null) SetTonemap(studio, (int)TonemapperType.None);
        scene.Ambient.Mode = Scene.AmbientLightParams.AmbientMode.Uniform;
        scene.Ambient.Color = LinearColor(setup.GetProperty("ambientSrgb"));
        scene.Ambient.Strength = 1f;
        studio.Camera.ClearFlags = CameraClearFlags.SolidColor;
        studio.Camera.ClearColor = LinearColor(setup.GetProperty("backgroundSrgb"));
        studio.Camera.NearClipPlane = 0.05f;
        studio.Camera.FarClipPlane = 100f;
        studio.Camera.FieldOfView = setup.GetProperty("verticalFov").GetSingle();
        studio.Sun.Color = LinearColor(setup.GetProperty("lightColor"));
        studio.Sun.Intensity = setup.GetProperty("lightIntensity").GetSingle();
        studio.Sun.CastShadows = false;
        studio.Sun.Transform.Rotation = LightNodeRotation(UnityEuler(ReadFloats(setup.GetProperty("lightEuler"), 3)));
        return studio;
    }

    private static void LeaveStudio(StudioState studio)
    {
        studio.Scene.Ambient = studio.SavedAmbient;
        studio.Camera.Transform.Position = studio.SavedCameraPosition;
        studio.Camera.Transform.Rotation = studio.SavedCameraRotation;
        studio.Camera.ClearFlags = studio.SavedClearFlags;
        studio.Camera.ClearColor = studio.SavedClearColor;
        studio.Camera.FieldOfView = studio.SavedFov;
        studio.Camera.NearClipPlane = studio.SavedNear;
        studio.Camera.FarClipPlane = studio.SavedFar;
        studio.Sun.Transform.Rotation = studio.SavedSunRotation;
        studio.Sun.Color = studio.SavedSunColor;
        studio.Sun.Intensity = studio.SavedSunIntensity;
        studio.Sun.CastShadows = studio.SavedSunShadows;
        if (studio.Tonemap != null)
        {
            studio.Tonemap.Type = studio.SavedTonemapType;
            studio.Tonemap.OverrideType = studio.SavedTonemapOverride;
            RefreshVolume(studio);
        }
        foreach (GameObject created in studio.Created)
        {
            studio.Scene.Remove(created);
            created.Dispose();
        }
    }

    private static void SetTonemap(StudioState studio, int type)
    {
        studio.Tonemap!.OverrideType = true;
        studio.Tonemap.Type = type;
        RefreshVolume(studio);
    }

    private static void RefreshVolume(StudioState studio)
    {
        // The blended stack is rebuilt when a volume registers.
        if (studio.Volume != null && studio.SavedVolumeEnabled)
        {
            studio.Volume.Enabled = false;
            studio.Volume.Enabled = true;
        }
    }

    private static void RenderHero(StudioState studio, string name, JsonElement shot, int width, int height, string path)
    {
        GameObject hero = InstantiateRestHero(name);
        LoadRenderAssets(hero);
        try
        {
            WriteFrame(studio, shot, width, height, path);
        }
        finally
        {
            studio.Scene.Remove(hero);
            hero.Dispose();
        }
    }

    /// <summary>Default output of <see cref="CaptureSilhouettes"/>, relative to the project root.</summary>
    public const string DefaultSilhouetteRelativePath = "Export/xengine-shots/silhouettes";

    /// <summary>Renders every skinned mesh of every hero alone, as a flat silhouette.</summary>
    /// <remarks>
    /// Unity's <c>SkinnedMeshRenderer.BakeMesh</c> lands on the first bone influence alone for every vertex
    /// whose weights matter, so the CPU bake cannot tell which weights a renderer really skins with. A
    /// silhouette of the rendered mesh can: <c>Tools/unity_export/scripts/capture_silhouettes.csx</c> draws
    /// the same frames in Unity with Unlit/Color, and <c>Tools/sanguo_import/compare_skin.py</c> compares
    /// both with projections of either reading of the weights. Each mesh is drawn with
    /// <c>Default/Unlit</c> (back faces culled, as Unlit/Color) and the other renderers of its hero
    /// switched off. The scene is edited in memory only.
    /// </remarks>
    public static string CaptureSilhouettes(string shotsPath, string outputDirectory)
    {
        List<string> warnings = new();
        int captured = 0;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(shotsPath));
        JsonElement setup = document.RootElement;
        int width = setup.GetProperty("width").GetInt32();
        int height = setup.GetProperty("height").GetInt32();

        StudioState studio = EnterStudio(setup);
        Material silhouette = new(Shader.LoadDefault(DefaultShader.Unlit));
        silhouette.SetColor("_MainColor", new Color(1f, 0f, 1f, 1f));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            foreach (JsonElement shot in setup.GetProperty("shots").EnumerateArray())
            {
                string name = shot.GetProperty("name").GetString() ?? "?";
                if (!shot.GetProperty("captured").GetBoolean())
                {
                    warnings.Add($"Unity did not capture '{name}'.");
                    continue;
                }
                GameObject hero = InstantiateRestHero(name);
                try
                {
                    LoadRenderAssets(hero);
                    string directory = Path.Combine(outputDirectory, name);
                    Directory.CreateDirectory(directory);
                    foreach (GameObject subject in hero.GetChildrenDeep())
                    {
                        if (subject.GetComponent<SkinnedMeshRenderer>() is not SkinnedMeshRenderer skinned) continue;
                        List<AssetRef<Material>> saved = new(skinned.Materials);
                        List<SkinnedMeshRenderer> switchedOff = new();
                        foreach (GameObject node in hero.GetChildrenDeep())
                        {
                            if (node.GetComponent<SkinnedMeshRenderer>() is not SkinnedMeshRenderer other || ReferenceEquals(other, skinned)) continue;
                            if (other.Enabled) switchedOff.Add(other);
                            other.Enabled = false;
                        }
                        try
                        {
                            skinned.Materials.Clear();
                            skinned.Materials.Add(new AssetRef<Material>(silhouette));
                            WriteFrame(studio, shot, width, height, Path.Combine(directory, subject.Name + ".png"));
                            captured++;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"'{name}/{subject.Name}': {ex.Message}");
                        }
                        finally
                        {
                            skinned.Materials.Clear();
                            skinned.Materials.AddRange(saved);
                            foreach (SkinnedMeshRenderer other in switchedOff) other.Enabled = true;
                        }
                    }
                }
                finally
                {
                    studio.Scene.Remove(hero);
                    hero.Dispose();
                }
            }
        }
        finally
        {
            silhouette.Dispose();
            LeaveStudio(studio);
        }

        string summary = $"{captured} silhouette(s) of {width}x{height} in {outputDirectory}";
        foreach (string warning in warnings) summary += "\n  ! " + warning;
        return summary;
    }

    /// <summary>A hero as its prefab stands, no animation applied (the M1 frames and silhouettes
    /// compare the rest pose).</summary>
    private static GameObject InstantiateRestHero(string name)
    {
        GameObject hero = InstantiateHero(name);
        if (hero.GetComponent<Animator>() is Animator animator) animator.Enabled = false;
        return hero;
    }

    private static GameObject InstantiateHero(string name)
    {
        AssetEntry entry = EditorAssetBackend.Instance.GetEntry($"Heroes/Prefabs/{name}.prefab")
            ?? throw new InvalidOperationException($"no imported prefab 'Heroes/Prefabs/{name}.prefab'.");
        if (AssetDatabase.Get(entry.Guid) is not PrefabAsset prefab)
            throw new InvalidOperationException($"'Heroes/Prefabs/{name}.prefab' is not imported as a prefab.");
        GameObject hero = prefab.Instantiate() ?? throw new InvalidOperationException($"'{name}' could not be instantiated.");
        Scene scene = Scene.Current ?? throw new InvalidOperationException("No scene is open.");
        scene.Add(hero);
        return hero;
    }

    /// <summary>Aims the studio camera as the Unity shot did, renders one frame and writes it as a PNG.</summary>
    private static void WriteFrame(StudioState studio, JsonElement shot, int width, int height, string path)
        => WriteFrame(studio, ReadFloats(shot.GetProperty("cameraPosition"), 3), ReadFloats(shot.GetProperty("cameraRotation"), 4), width, height, path);

    /// <inheritdoc cref="WriteFrame(StudioState, JsonElement, int, int, string)"/>
    private static void WriteFrame(StudioState studio, float[] position, float[] rotation, int width, int height, string path)
    {
        studio.Camera.Transform.Position = new Float3(position[0], position[1], position[2]);
        studio.Camera.Transform.Rotation = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);

        using RenderTexture target = new(width, height, true, new[] { TextureImageFormat.Color4b });
        RenderTexture? original = studio.Camera.Target;
        try
        {
            studio.Camera.Target = target;
            studio.Camera.UpdateRenderData();
            // Camera.Render draws through the default pipeline. It is the public entry point: the
            // pipeline class itself and its display-encoding switch are internal to the engine.
            studio.Camera.Render();
        }
        finally
        {
            studio.Camera.Target = original;
        }

        Texture2D texture = target.MainTexture;
        byte[] data = new byte[texture.GetSize()];
        texture.GetData(data.AsMemory());
        // The camera target holds linear colour (the clear value above reads back as exactly
        // linear(clear) * 255), so the frame is encoded for display here, as the Game view does.
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = ToDisplay(data[i]);
            data[i + 1] = ToDisplay(data[i + 1]);
            data[i + 2] = ToDisplay(data[i + 2]);
            data[i + 3] = 255;
        }
        var read = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA);
        using MagickImage image = new();
        image.ReadPixels(data, read);
        if (ReadsBackBottomRowFirst) image.Flip();
        image.Write(path, MagickFormat.Png24);
    }

    /// <summary>Brings the hero's meshes, materials and their textures into memory.
    /// <see cref="AssetRef{T}"/> loads asynchronously in the editor, and a renderer whose material
    /// is not resident yet is skipped, which renders an empty frame. A texture that is not resident
    /// is drawn as a white mask instead, and loading a material does not load its textures: every
    /// texture slot is loaded explicitly, or the frame shows untextured grey meshes.</summary>
    private static void LoadRenderAssets(GameObject hero)
    {
        foreach (GameObject node in hero.GetChildrenDeep())
        {
            if (node.GetComponent<SkinnedMeshRenderer>() is not SkinnedMeshRenderer skinned) continue;
            skinned.SharedMesh.EnsureLoaded();
            foreach (AssetRef<Material> material in skinned.Materials)
            {
                material.EnsureLoaded();
                if (material.Res == null)
                    throw new InvalidOperationException($"the material '{material.AssetID}' of '{node.Name}' did not load.");
                foreach (string name in material.Res._properties.EnumerateNames())
                {
                    AssetRef<Texture2D> texture = material.Res._properties.GetTextureRef(name);
                    // Properties of other types read as an empty reference here.
                    if (texture.IsExplicitNull) continue;
                    texture.EnsureLoaded();
                    if (texture.Res == null)
                        throw new InvalidOperationException($"the texture '{name}' of material '{material.Res.Name}' did not load.");
                }
            }
        }
    }

    /// <summary>A linear 8-bit channel encoded for display (the sRGB transfer curve).</summary>
    private static byte ToDisplay(byte linear)
    {
        float c = linear / 255f;
        float encoded = c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
        return (byte)MathF.Round(encoded * 255f);
    }

    /// <summary>Whether a camera target's bytes start at the bottom row. Measured against the Unity
    /// frames: Vulkan reads back bottom row first, Direct3D12 top row first (docs/evidence/m1/heroes.md).
    /// A backend that has not been measured is taken to read like Vulkan.</summary>
    private static bool ReadsBackBottomRowFirst =>
        Graphics.Device is not { } device || device.Backend != GraphicsBackend.Direct3D12;

    /// <summary>The rotation an XEngine light node needs to shine the way a Unity light with the
    /// given rotation does.</summary>
    /// <remarks>
    /// The two engines point a light down opposite axes. A Unity light shines along its node's +Z.
    /// An XEngine directional light shines along its node's -Z: the shader takes the node's +Z as
    /// the direction towards the light (DirectionalLight.GetForwardLightData uploads
    /// Transform.Forward and Lighting.glsl dots it with the normal), and the shadow view looks down
    /// -Transform.Forward. So the node is turned around, by a half turn about its local X axis.
    /// </remarks>
    private static Quaternion LightNodeRotation(Quaternion unityRotation)
    {
        Quaternion halfTurnAboutX = new(1f, 0f, 0f, 0f);
        return unityRotation * halfTurnAboutX;
    }

    /// <summary>A Unity Euler rotation (Z, then X, then Y applied) as a quaternion.</summary>
    private static Quaternion UnityEuler(float[] euler)
    {
        float x = euler[0] * MathF.PI / 180f, y = euler[1] * MathF.PI / 180f, z = euler[2] * MathF.PI / 180f;
        Quaternion qx = new(MathF.Sin(x / 2), 0, 0, MathF.Cos(x / 2));
        Quaternion qy = new(0, MathF.Sin(y / 2), 0, MathF.Cos(y / 2));
        Quaternion qz = new(0, 0, MathF.Sin(z / 2), MathF.Cos(z / 2));
        return qy * qx * qz;
    }

    /// <summary>An sRGB-authored colour (r, g, b) as the linear colour the engine shades and clears with.</summary>
    private static Color LinearColor(JsonElement srgb)
    {
        return new Color(ToLinear(srgb[0].GetSingle()), ToLinear(srgb[1].GetSingle()), ToLinear(srgb[2].GetSingle()), 1f);
    }

    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float[] ReadFloats(JsonElement array, int count)
    {
        float[] values = new float[count];
        for (int i = 0; i < count; i++) values[i] = array[i].GetSingle();
        return values;
    }

    private static List<GameObject> CollectAll(Scene scene)
    {
        List<GameObject> all = new();
        Stack<GameObject> pending = new();
        foreach (GameObject root in scene.RootObjects) pending.Push(root);
        while (pending.Count > 0)
        {
            GameObject current = pending.Pop();
            all.Add(current);
            foreach (GameObject child in current.Children) pending.Push(child);
        }
        return all;
    }

    private static T? Find<T>(List<GameObject> objects) where T : MonoBehaviour
    {
        foreach (GameObject gameObject in objects)
        {
            if (gameObject.GetComponent<T>() is T component) return component;
        }
        return null;
    }

    private static T Create<T>(Scene scene, string name, List<GameObject> created) where T : MonoBehaviour, new()
    {
        GameObject gameObject = new(name);
        T component = gameObject.AddComponent<T>();
        scene.Add(gameObject);
        created.Add(gameObject);
        return component;
    }
}
