using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

using XEngine.Runtime;
using XEngine.Runtime.Resources;
using XEngine.Vector;

namespace SanGuoXEngine.Editor;

/// <summary>
/// The animation half of the capture studio: plays every exported clip of every hero on the real
/// Animator and records what the engine poses, next to what Unity sampled for the same clip.
/// </summary>
public static partial class SanguoCaptureStudio
{
    /// <summary>The Unity clip export (see <see cref="SanguoClipBuilder"/>), relative to the project root.</summary>
    public const string ClipsRelativePath = "Export/clips";

    /// <summary>Default output of <see cref="CapturePoses"/>, relative to the project root.</summary>
    public const string DefaultPoseOutputRelativePath = "Export/xengine-shots/clips";

    /// <summary>How finely the Animator is stepped towards a sampled time. Root motion is applied
    /// per step, as it is per frame in play mode.</summary>
    private const float Step = 1f / 120f;

    // The editor does not run an Animator's per-frame update outside play mode. Update() advances
    // the graph but leaves the actor where it is ("without extracting root motion"); this is the
    // step that update ends with in play mode, applied here for the same result.
    private static readonly MethodInfo ApplyRootMotion = typeof(Animator).GetMethod(
        "ExtractRootMotion", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>
    /// For every hero document in <paramref name="clipsDirectory"/>: plays each clip from its start
    /// to the times Unity sampled, writes the pose of every node at those times to
    /// <c>poses.json</c> and renders the frames Unity rendered (same camera, following the actor as
    /// the Unity capture did) next to it. Returns a summary with warnings.
    /// </summary>
    public static string CapturePoses(string shotsPath, string clipsDirectory, string outputDirectory)
    {
        List<string> warnings = new();
        int captured = 0, sampled = 0;
        using JsonDocument shotsDocument = JsonDocument.Parse(File.ReadAllText(shotsPath));
        JsonElement setup = shotsDocument.RootElement;
        int width = setup.GetProperty("width").GetInt32();
        int height = setup.GetProperty("height").GetInt32();

        StudioState studio = EnterStudio(setup);
        try
        {
            Directory.CreateDirectory(outputDirectory);
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "poses.json"));
            using Utf8JsonWriter poses = new(stream);
            poses.WriteStartObject();
            foreach (string document in Directory.GetFiles(clipsDirectory, "Hero_*.json"))
            {
                string hero = Path.GetFileNameWithoutExtension(document);
                using JsonDocument export = JsonDocument.Parse(File.ReadAllText(document));
                JsonElement root = export.RootElement;
                List<string> nodes = new();
                foreach (JsonElement node in root.GetProperty("nodes").EnumerateArray()) nodes.Add(node.GetProperty("path").GetString() ?? "");
                float[] rest = ReadFloats(root.GetProperty("rootRest").GetProperty("position"), 3);
                float[] camera = ReadFloats(root.GetProperty("camera").GetProperty("position"), 3);
                float[] cameraRotation = ReadFloats(root.GetProperty("camera").GetProperty("rotation"), 4);

                poses.WritePropertyName(hero);
                poses.WriteStartObject();
                poses.WritePropertyName("nodes");
                poses.WriteStartArray();
                foreach (string node in nodes) poses.WriteStringValue(node);
                poses.WriteEndArray();
                poses.WritePropertyName("clips");
                poses.WriteStartObject();
                string frameDirectory = Path.Combine(outputDirectory, hero);
                Directory.CreateDirectory(frameDirectory);
                foreach (JsonElement clip in root.GetProperty("clips").EnumerateArray())
                {
                    string action = clip.GetProperty("action").GetString() ?? "?";
                    try
                    {
                        (int frames, int samples) = CaptureClipPose(studio, hero, clip, nodes, rest, camera, cameraRotation,
                            width, height, frameDirectory, poses);
                        captured += frames;
                        sampled += samples;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"{hero}/{action}: {ex.Message}");
                    }
                }
                poses.WriteEndObject();
                poses.WriteEndObject();
            }
            poses.WriteEndObject();
            poses.Flush();
        }
        finally
        {
            LeaveStudio(studio);
        }

        string summary = $"{captured} frame(s) and {sampled} sampled pose(s) of {width}x{height} in {outputDirectory}";
        foreach (string warning in warnings) summary += "\n  ! " + warning;
        return summary;
    }

    private static (int Frames, int Samples) CaptureClipPose(StudioState studio, string hero, JsonElement clip, List<string> nodes,
        float[] rest, float[] camera, float[] cameraRotation, int width, int height, string frameDirectory, Utf8JsonWriter poses)
    {
        string action = clip.GetProperty("action").GetString() ?? "?";
        JsonElement samples = clip.GetProperty("samples");
        float[] times = ReadFloats(samples.GetProperty("times"), samples.GetProperty("times").GetArrayLength());
        // Sample index -> the frame Unity rendered at that time.
        Dictionary<int, JsonElement> frames = new();
        foreach (JsonElement frame in clip.GetProperty("frames").EnumerateArray())
            frames[frame.GetProperty("sample").GetInt32()] = frame;

        GameObject heroObject = InstantiateHero(hero);
        try
        {
            LoadRenderAssets(heroObject);
            Animator animator = heroObject.GetComponent<Animator>()
                ?? throw new InvalidOperationException("the hero prefab has no Animator.");
            animator.Controller.EnsureLoaded();
            // In play mode the Animator binds its controller by itself; the editor does not run
            // that update, so the controller is compiled here (waiting for every clip it uses).
            animator.SetControllerAsset(animator.Controller.Res!);
            if (!animator.HasState(action))
                throw new InvalidOperationException($"the controller has no state '{action}'.");

            List<Transform> transforms = new(nodes.Count);
            for (int n = 0; n < nodes.Count; n++)
                transforms.Add(FindNode(heroObject, nodes[n]) ?? throw new InvalidOperationException($"no node '{nodes[n]}' in the hero prefab."));

            animator.CrossFade(action, 0f, 0);
            Advance(animator, 0f);

            poses.WritePropertyName(action);
            poses.WriteStartObject();
            poses.WritePropertyName("times");
            poses.WriteStartArray();
            foreach (float time in times) poses.WriteNumberValue(time);
            poses.WriteEndArray();
            poses.WritePropertyName("poses");
            poses.WriteStartArray();

            int captured = 0;
            float clock = 0f;
            for (int i = 0; i < times.Length; i++)
            {
                // Step up to the sampled time; the last step lands exactly on it.
                while (clock < times[i] - 1e-6f)
                {
                    float delta = MathF.Min(Step, times[i] - clock);
                    Advance(animator, delta);
                    clock += delta;
                }

                poses.WriteStartArray();
                for (int n = 0; n < transforms.Count; n++) WritePose(poses, transforms[n]);
                poses.WriteEndArray();

                if (!frames.TryGetValue(i, out JsonElement frame)) continue;
                // Same framing as Unity: the M1 camera, moved along with how far the actor travelled.
                Float3 travelled = heroObject.Transform.LocalPosition - new Float3(rest[0], rest[1], rest[2]);
                travelled.Y = 0f;
                WriteFrame(studio, new[] { camera[0] + travelled.X, camera[1], camera[2] + travelled.Z }, cameraRotation, width, height,
                    Path.Combine(frameDirectory, frame.GetProperty("file").GetString() ?? $"{action}_p{i}.png"));
                captured++;
            }
            poses.WriteEndArray();
            poses.WriteEndObject();
            return (captured, times.Length);
        }
        finally
        {
            studio.Scene.Remove(heroObject);
            heroObject.Dispose();
        }
    }

    private static void Advance(Animator animator, float deltaTime)
    {
        animator.Update(deltaTime);
        ApplyRootMotion.Invoke(animator, new object[] { true });
    }

    private static Transform? FindNode(GameObject root, string path)
    {
        GameObject current = root;
        if (path.Length == 0) return current.Transform;   // Unity's path for the prefab root itself
        foreach (string segment in path.Split('/'))
        {
            GameObject? next = null;
            foreach (GameObject child in current.Children)
            {
                if (child.Name == segment)
                {
                    next = child;
                    break;
                }
            }
            if (next == null) return null;
            current = next;
        }
        return current.Transform;
    }

    // [position xyz, rotation xyzw, scale xyz]: the layout of Unity's samples.
    private static void WritePose(Utf8JsonWriter poses, Transform transform)
    {
        poses.WriteStartArray();
        Float3 position = transform.LocalPosition;
        poses.WriteStartArray();
        poses.WriteNumberValue(position.X); poses.WriteNumberValue(position.Y); poses.WriteNumberValue(position.Z);
        poses.WriteEndArray();
        Quaternion rotation = transform.LocalRotation;
        poses.WriteStartArray();
        poses.WriteNumberValue(rotation.X); poses.WriteNumberValue(rotation.Y); poses.WriteNumberValue(rotation.Z); poses.WriteNumberValue(rotation.W);
        poses.WriteEndArray();
        Float3 scale = transform.LocalScale;
        poses.WriteStartArray();
        poses.WriteNumberValue(scale.X); poses.WriteNumberValue(scale.Y); poses.WriteNumberValue(scale.Z);
        poses.WriteEndArray();
        poses.WriteEndArray();
    }
}
