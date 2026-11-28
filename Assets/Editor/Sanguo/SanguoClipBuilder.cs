using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using XEngine.Animation;
using XEngine.Editor;
using XEngine.Editor.Projects;
using XEngine.Runtime;
using XEngine.Vector;
// XEngine.Vector has an AnimationCurve of its own; every curve here is the engine's.
using AnimationCurve = XEngine.Runtime.AnimationCurve;

namespace SanGuoXEngine.Editor;

/// <summary>
/// Turns the Unity clip export (<c>Tools/unity_export/scripts/export_clips.csx</c> →
/// <c>Export/clips/Hero_*.json</c>) into native animation clips and one animator controller per
/// hero, under <c>Assets/Heroes/Animation/</c>.
/// </summary>
/// <remarks>
/// Unity does not play a clip's curves the way they are stored, so the keys are converted the way
/// Unity's Mecanim does it. Every rule below was measured against Unity's own sampled poses of the
/// 124 exported clips (docs/evidence/m2):
/// <list type="bullet">
/// <item>A float curve with more than a fifth as many keys as the clip has frames, all of them on
/// frame times, is <em>dense</em>: Unity evaluates it at every frame of the clip (30 Hz) and
/// interpolates linearly in between. Such a curve is resampled here the same way, with linear
/// segments. Playing its keys with plain Hermite interpolation instead differs from Unity by up to
/// 14° on a fast-moving bone.</item>
/// <item>Any other curve is <em>streamed</em>: cubic Hermite through its keys, as stored.</item>
/// <item>A curve whose keys never move collapses to one key.</item>
/// <item>Root motion: the root-motion node's (<c>Bip001</c>) travel is stored again as the
/// Animator's <c>RootT</c>/<c>RootQ</c> curves. Unity evaluates those at frame granularity, moves
/// the actor by their increments and keeps the node at "its own curve minus the trajectory". The
/// trajectory becomes the clip's model-root track, which the engine's Animator applies the same
/// way (by increments, in actor space).</item>
/// </list>
/// Run from the menu (Sanguo/Import/3 Generate Animation Clips) or from a live editor through
/// <c>runtime_eval</c> (see <c>Tools/xengine_live/evals/generate_clips.cs</c>).
/// </remarks>
public static class SanguoClipBuilder
{
    /// <summary>The export documents, relative to the project root.</summary>
    public const string ExportRelativePath = "Export/clips";

    /// <summary>Where the clips and controllers go, relative to the Assets folder.</summary>
    public const string AnimationRelativePath = "Heroes/Animation";

    /// <summary>A dense curve has more keys than a fifth of the clip's frames.</summary>
    public const int FramesPerDenseKey = 5;

    /// <summary>... and every key this close to a frame time, in frames. A curve whose keys sit
    /// between frames (张飞's <c>node_fixed</c>) stays streamed however many keys it has.</summary>
    public const float FrameGridTolerance = 0.03f;

    /// <summary>... and no two keys closer together than this, in frames: a curve that carries two
    /// keys inside one frame stays streamed too.</summary>
    public const float MinimumKeySpacing = 0.5f;

    /// <summary>Keys closer than this in value (and flat) make a constant curve.</summary>
    public const float ConstantTolerance = 1e-6f;

    public enum CurveKind { Constant, Streamed, Dense }

    /// <summary>One curve of the export, grouped by what the builder did with it.</summary>
    public sealed class BuildResult
    {
        public required AnimationClip Clip { get; init; }
        public required int SourceKeys { get; init; }
        public required int NativeKeys { get; init; }
        public required int ConstantCurves { get; init; }
        public required int StreamedCurves { get; init; }
        public required int DenseCurves { get; init; }
        /// <summary>The node whose travel Unity extracts as root motion, or null when the clip has none.</summary>
        public string? MotionNode { get; init; }
        /// <summary>Rotation tracks whose consecutive keys fall on opposite sides of the quaternion
        /// sphere. Component-wise interpolation is meaningless there, so these must stay empty.</summary>
        public required List<string> HemisphereFlips { get; init; }
    }

    private static readonly string[] PositionAxes = { "x", "y", "z" };

    private readonly record struct UKey(float Time, float Value, float In, float Out, float InWeight, float OutWeight, AnimationWeightedMode Mode)
    {
        public static readonly Comparer<UKey> ByTime = Comparer<UKey>.Create((a, b) => a.Time.CompareTo(b.Time));
    }

    // ------------------------------------------------------------------ curves (no engine state)

    /// <summary>True when a curve with <paramref name="keyCount"/> keys over <paramref name="frames"/>
    /// frames is resampled by Unity (and here) at every frame.</summary>
    public static bool IsDense(int keyCount, float frames, float maxOffGrid, float minSpacing)
        => keyCount * FramesPerDenseKey > frames && maxOffGrid <= FrameGridTolerance && minSpacing >= MinimumKeySpacing;

    /// <summary>How close, in frames, the two closest keys are.</summary>
    public static float MinSpacing(IReadOnlyList<float[]> keys, float sampleRate)
    {
        float closest = float.MaxValue;
        for (int i = 1; i < keys.Count; i++)
            closest = MathF.Min(closest, (keys[i][0] - keys[i - 1][0]) * sampleRate);
        return closest;
    }

    /// <summary>How far, in frames, the key furthest from a frame time is.</summary>
    public static float MaxOffGrid(IReadOnlyList<float[]> keys, float sampleRate)
    {
        float worst = 0f;
        foreach (float[] key in keys)
        {
            float frame = key[0] * sampleRate;
            worst = MathF.Max(worst, MathF.Abs(frame - MathF.Round(frame)));
        }
        return worst;
    }

    /// <summary>True when every key holds the same value and no slope leaves it.</summary>
    public static bool IsConstant(IReadOnlyList<float[]> keys)
    {
        if (keys.Count <= 1) return true;
        float value = keys[0][1];
        foreach (float[] key in keys)
        {
            if (MathF.Abs(key[1] - value) > ConstantTolerance || MathF.Abs(key[2]) > ConstantTolerance || MathF.Abs(key[3]) > ConstantTolerance)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Converts one exported curve (<c>[time, value, inSlope, outSlope, inWeight, outWeight,
    /// weightedMode]</c> per key) the way Unity plays it. <paramref name="frames"/> is the clip's
    /// length times its sample rate.
    /// </summary>
    public static AnimationCurve ConvertCurve(IReadOnlyList<float[]> keys, float frames, float sampleRate)
        => ConvertCurve(keys, frames, sampleRate, false, out _);

    /// <param name="forceDense">Resample per frame whatever the key count; Unity does this for the
    /// root-motion curves.</param>
    public static AnimationCurve ConvertCurve(IReadOnlyList<float[]> keys, float frames, float sampleRate, bool forceDense, out CurveKind kind)
    {
        List<UKey> clean = Clean(keys);
        AnimationCurve source = StreamedCurve(clean);
        kind = CurveKind.Constant;
        if (clean.Count == 0 || IsConstant(keys)) return source;
        if (!forceDense && !IsDense(clean.Count, frames, MaxOffGrid(keys, sampleRate), MinSpacing(keys, sampleRate)))
        {
            kind = CurveKind.Streamed;
            return source;
        }
        kind = CurveKind.Dense;
        return ResamplePerFrame(source, clean[0].Time, clean[^1].Time, sampleRate);
    }

    /// <summary>Keys with strictly increasing times: a clip can carry two keys at the same time.</summary>
    private static List<UKey> Clean(IReadOnlyList<float[]> keys)
    {
        List<UKey> result = new(keys.Count);
        foreach (float[] key in keys)
        {
            AnimationWeightedMode mode = key.Length > 6 ? (AnimationWeightedMode)(int)key[6] : AnimationWeightedMode.None;
            float inWeight = key.Length > 4 ? key[4] : KeyFrame.DefaultWeight;
            float outWeight = key.Length > 5 ? key[5] : KeyFrame.DefaultWeight;
            result.Add(new UKey(key[0], key[1], key[2], key[3], inWeight, outWeight, mode));
        }
        result.Sort(UKey.ByTime);
        for (int i = result.Count - 1; i > 0; i--)
        {
            if (result[i].Time <= result[i - 1].Time) result.RemoveAt(i - 1);   // the later key wins
        }
        return result;
    }

    /// <summary>The keys as a Hermite curve. A constant curve keeps one key.</summary>
    private static AnimationCurve StreamedCurve(List<UKey> keys)
    {
        AnimationCurve curve = new();
        curve.Keys.Clear();
        if (keys.Count == 0)
        {
            curve.Keys.Add(new KeyFrame(0f, 0f));
            return curve;
        }
        bool constant = MathF.Abs(keys[0].In) <= ConstantTolerance && MathF.Abs(keys[0].Out) <= ConstantTolerance;
        for (int i = 1; i < keys.Count && constant; i++)
        {
            if (MathF.Abs(keys[i].Value - keys[0].Value) > ConstantTolerance || MathF.Abs(keys[i].In) > ConstantTolerance
                || MathF.Abs(keys[i].Out) > ConstantTolerance)
                constant = false;
        }
        int count = constant ? 1 : keys.Count;
        for (int i = 0; i < count; i++)
        {
            UKey key = keys[i];
            // The engine's tangents are the value change over a whole segment, Unity's are per second.
            float dtIn = i > 0 ? key.Time - keys[i - 1].Time : 0f;
            float dtOut = i + 1 < keys.Count ? keys[i + 1].Time - key.Time : 0f;
            curve.Keys.Add(new KeyFrame(key.Time, key.Value, key.In * dtIn, key.Out * dtOut)
            {
                WeightedMode = key.Mode,
                InWeight = key.InWeight,
                OutWeight = key.OutWeight,
            });
        }
        return curve;
    }

    /// <summary>
    /// One key per frame time from <paramref name="firstTime"/> to <paramref name="lastTime"/>
    /// (inclusive), each holding the source curve's value there; linear in between. Outside its
    /// keys the source holds its end values, so the first and last frame carry them.
    /// </summary>
    public static AnimationCurve ResamplePerFrame(AnimationCurve source, float firstTime, float lastTime, float sampleRate)
    {
        int first = (int)MathF.Floor(firstTime * sampleRate + 1e-3f);
        int last = (int)MathF.Ceiling(lastTime * sampleRate - 1e-3f);
        if (last < first) last = first;
        List<float> times = new(last - first + 1);
        List<float> values = new(last - first + 1);
        for (int frame = first; frame <= last; frame++)
        {
            float time = frame / sampleRate;
            times.Add(time);
            values.Add(source.Evaluate(time));
        }
        return LinearCurve(times, values);
    }

    /// <summary>
    /// "Node curve minus trajectory" of the root-motion node, sampled per frame. The frame samples
    /// keep the difference's own slope, because the node's curve is smooth inside a frame while the
    /// trajectory is linear there.
    /// </summary>
    public static AnimationCurve PinnedCurve(AnimationCurve node, AnimationCurve trajectory, float sampleRate)
    {
        int first = (int)MathF.Floor(MathF.Min(FirstTime(node), FirstTime(trajectory)) * sampleRate + 1e-3f);
        int last = (int)MathF.Ceiling(MathF.Max(LastTime(node), LastTime(trajectory)) * sampleRate - 1e-3f);
        if (last < first) last = first;
        float Difference(float time) => node.Evaluate(time) - trajectory.Evaluate(time);

        List<float> times = new(last - first + 1);
        List<float> values = new(last - first + 1);
        List<float> slopesIn = new(last - first + 1);
        List<float> slopesOut = new(last - first + 1);
        float step = 1e-3f / sampleRate;
        for (int frame = first; frame <= last; frame++)
        {
            float time = frame / sampleRate;
            float value = Difference(time);
            times.Add(time);
            values.Add(value);
            slopesIn.Add((value - Difference(time - step)) * sampleRate * 1e3f);
            slopesOut.Add((Difference(time + step) - value) * sampleRate * 1e3f);
        }

        AnimationCurve curve = new();
        curve.Keys.Clear();
        // The difference vanishes at every frame (node and trajectory agree there) and only lives
        // in between, so a constant one has to stay flat at the half frames too.
        bool constant = IsConstant(values);
        for (int frame = first; constant && frame < last; frame++)
            constant = MathF.Abs(Difference((frame + 0.5f) / sampleRate)) <= ConstantTolerance;
        if (constant)
        {
            curve.Keys.Add(new KeyFrame(times[0], values[0]));
            return curve;
        }
        for (int i = 0; i < times.Count; i++)
        {
            float dtIn = i > 0 ? times[i] - times[i - 1] : 0f;
            float dtOut = i + 1 < times.Count ? times[i + 1] - times[i] : 0f;
            curve.Keys.Add(new KeyFrame(times[i], values[i], slopesIn[i] * dtIn, slopesOut[i] * dtOut));
        }
        return curve;
    }

    private static bool IsConstant(List<float> values)
    {
        for (int i = 1; i < values.Count; i++)
        {
            if (MathF.Abs(values[i] - values[0]) > ConstantTolerance) return false;
        }
        return true;
    }

    private static AnimationCurve LinearCurve(List<float> times, List<float> values)
    {
        AnimationCurve curve = new();
        curve.Keys.Clear();
        for (int i = 0; i < times.Count; i++)
        {
            float tangentIn = i > 0 ? values[i] - values[i - 1] : 0f;
            float tangentOut = i + 1 < values.Count ? values[i + 1] - values[i] : tangentIn;
            if (i == 0 && values.Count == 1) tangentIn = tangentOut = 0f;
            curve.Keys.Add(new KeyFrame(times[i], values[i], tangentIn, tangentOut));
        }
        return curve;
    }

    private static float FirstTime(AnimationCurve curve) => curve.Keys.Count > 0 ? curve.Keys[0].Position : 0f;
    private static float LastTime(AnimationCurve curve) => curve.Keys.Count > 0 ? curve.Keys[^1].Position : 0f;

    // ------------------------------------------------------------------ clips (no engine state)

    /// <summary>A node counts as the root-motion node when its position stays this close to the
    /// trajectory everywhere (Unity keeps the node at its own curve minus the travel).</summary>
    public const float MotionNodeTolerance = 0.1f;

    /// <summary>Builds the native clip of one exported clip document.</summary>
    public static AnimationClip BuildClip(JsonElement clip) => Build(clip).Clip;

    public static BuildResult Build(JsonElement clip)
    {
        string action = RequiredString(clip, "action");
        float length = clip.GetProperty("length").GetSingle();
        float sampleRate = clip.GetProperty("sampleRate").GetSingle();
        float frames = length * sampleRate;
        JsonElement settings = clip.GetProperty("settings");
        float startTime = settings.GetProperty("startTime").GetSingle();

        // Every curve of the clip, converted: by the path of the node it animates, then by property.
        // The Animator's own curves (empty path) are the root-motion curves; Unity evaluates those
        // at frame granularity whatever their key count.
        Dictionary<string, Dictionary<string, AnimationCurve>> curves = new(StringComparer.Ordinal);
        int sourceKeys = 0, constant = 0, streamed = 0, dense = 0;
        foreach (JsonElement curve in clip.GetProperty("curves").EnumerateArray())
        {
            string path = curve.GetProperty("path").GetString() ?? "";
            string property = curve.GetProperty("property").GetString() ?? "";
            List<float[]> curveKeys = ReadKeys(curve.GetProperty("keys"));
            AnimationCurve converted = ConvertCurve(curveKeys, frames, sampleRate, path.Length == 0, out CurveKind kind);
            if (kind == CurveKind.Constant) constant++;
            else if (kind == CurveKind.Streamed) streamed++;
            else dense++;
            if (!curves.TryGetValue(path, out Dictionary<string, AnimationCurve>? properties))
            {
                curves[path] = properties = new(StringComparer.Ordinal);
            }
            properties[property] = converted;
            sourceKeys += curveKeys.Count;
        }

        AnimationClip native = new()
        {
            Name = action,
            Duration = length,
            DurationInTicks = length,
            TicksPerSecond = 1f,
            FrameRate = sampleRate,
            Wrap = settings.GetProperty("loopTime").GetBoolean() ? AnimationWrapMode.Loop : AnimationWrapMode.Once,
        };

        curves.TryGetValue("", out Dictionary<string, AnimationCurve>? root);
        Dictionary<string, AnimationCurve> trajectory = Channel(root, "RootT.", "xyz");
        string? motionNode = FindMotionNode(curves, trajectory, frames, sampleRate);

        foreach (KeyValuePair<string, Dictionary<string, AnimationCurve>> node in curves)
        {
            string path = node.Key;
            if (path.Length == 0) continue;
            AnimationClip.AnimBone bone = new() { BoneName = path };
            bool animated = false;

            Dictionary<string, AnimationCurve> position = Channel(node.Value, "m_LocalPosition.", "xyz");
            if (position.Count > 0)
            {
                // Unity keeps the root-motion node at its own curve minus the extracted travel.
                if (path == motionNode)
                {
                    foreach (string axis in PositionAxes) position[axis] = PinnedCurve(position[axis], trajectory[axis], sampleRate);
                }
                bone.PosX = position["x"]; bone.PosY = position["y"]; bone.PosZ = position["z"];
                animated = true;
            }

            Dictionary<string, AnimationCurve> rotation = Channel(node.Value, "m_LocalRotation.", "xyzw");
            if (rotation.Count > 0)
            {
                bone.RotX = rotation["x"]; bone.RotY = rotation["y"]; bone.RotZ = rotation["z"]; bone.RotW = rotation["w"];
                animated = true;
            }

            Dictionary<string, AnimationCurve> scale = Channel(node.Value, "m_LocalScale.", "xyz");
            if (scale.Count > 0)
            {
                bone.ScaleX = scale["x"]; bone.ScaleY = scale["y"]; bone.ScaleZ = scale["z"];
                animated = true;
            }

            if (animated) native.AddBone(bone);
        }

        if (trajectory.Count > 0)
        {
            // The trajectory is not a bone: its empty path and the explicit "." root selection make
            // the Animator apply it as root motion instead of writing it anywhere.
            AnimationClip.AnimBone rootBone = new() { BoneName = "" };
            rootBone.PosX = trajectory["x"]; rootBone.PosY = trajectory["y"]; rootBone.PosZ = trajectory["z"];
            Dictionary<string, AnimationCurve> orientation = Channel(root, "RootQ.", "xyzw");
            if (orientation.Count > 0)
            {
                rootBone.RotX = orientation["x"]; rootBone.RotY = orientation["y"]; rootBone.RotZ = orientation["z"]; rootBone.RotW = orientation["w"];
            }
            native.AddBone(rootBone);
            native.RootBonePath = ".";
            native.RootMotionPolicy = ClipRootMotionPolicy.ExplicitBone;
        }

        if (clip.TryGetProperty("events", out JsonElement events))
        {
            foreach (JsonElement ev in events.EnumerateArray())
            {
                native.Events.Add(new AnimationClipEvent
                {
                    Time = ev.GetProperty("time").GetSingle() - startTime,
                    FunctionName = ev.GetProperty("functionName").GetString() ?? "",
                    FloatParam = ev.GetProperty("floatParameter").GetSingle(),
                    IntParam = ev.GetProperty("intParameter").GetInt32(),
                    StringParam = ev.GetProperty("stringParameter").GetString() ?? "",
                });
            }
        }

        int nativeKeys = 0;
        foreach (AnimationClip.AnimBone bone in native.Bones)
        {
            foreach (AnimationCurve? curve in new[] { bone.PosX, bone.PosY, bone.PosZ, bone.RotX, bone.RotY, bone.RotZ, bone.RotW, bone.ScaleX, bone.ScaleY, bone.ScaleZ })
            {
                if (curve != null) nativeKeys += curve.Keys.Count;
            }
        }

        return new BuildResult
        {
            Clip = native,
            SourceKeys = sourceKeys,
            NativeKeys = nativeKeys,
            ConstantCurves = constant,
            StreamedCurves = streamed,
            DenseCurves = dense,
            MotionNode = motionNode,
            HemisphereFlips = FindHemisphereFlips(native),
        };
    }

    private static List<float[]> ReadKeys(JsonElement keys)
    {
        List<float[]> result = new(keys.GetArrayLength());
        foreach (JsonElement key in keys.EnumerateArray())
        {
            float[] values = new float[key.GetArrayLength()];
            for (int i = 0; i < values.Length; i++) values[i] = key[i].GetSingle();
            result.Add(values);
        }
        return result;
    }

    /// <summary>
    /// The curves of one channel, keyed by component letter. A channel Unity animates only partly
    /// (a position with x and y but no z) is not expected and rejected, because the engine writes a
    /// channel as a whole. An absent channel gives an empty result.
    /// </summary>
    private static Dictionary<string, AnimationCurve> Channel(Dictionary<string, AnimationCurve>? properties, string prefix, string components)
    {
        Dictionary<string, AnimationCurve> result = new();
        if (properties == null) return result;
        int present = 0;
        foreach (char component in components)
        {
            if (properties.ContainsKey(prefix + component)) present++;
        }
        if (present == 0) return result;
        if (present != components.Length)
            throw new InvalidDataException($"a node animates only {present} of {components.Length} components of '{prefix}'.");
        foreach (char component in components) result[component.ToString()] = properties[prefix + component];
        return result;
    }

    /// <summary>
    /// The node whose travel Unity extracts as root motion: the shallowest node whose position
    /// curves follow the trajectory everywhere. Unity keeps that node at "own curve minus
    /// trajectory", which is centimetres at most.
    /// </summary>
    private static string? FindMotionNode(Dictionary<string, Dictionary<string, AnimationCurve>> curves,
        Dictionary<string, AnimationCurve> trajectory, float frames, float sampleRate)
    {
        if (trajectory.Count == 0) return null;
        List<float> times = new() { 0f, frames / sampleRate };
        foreach (KeyFrame key in trajectory["z"].Keys) times.Add(key.Position);

        string? best = null;
        int bestDepth = int.MaxValue;
        foreach (KeyValuePair<string, Dictionary<string, AnimationCurve>> node in curves)
        {
            string path = node.Key;
            if (path.Length == 0) continue;
            Dictionary<string, AnimationCurve> position = Channel(node.Value, "m_LocalPosition.", "xyz");
            if (position.Count == 0) continue;
            float worst = 0f;
            foreach (float time in times)
            {
                foreach (string axis in PositionAxes)
                {
                    worst = MathF.Max(worst, MathF.Abs(position[axis].Evaluate(time) - trajectory[axis].Evaluate(time)));
                }
            }
            if (worst > MotionNodeTolerance) continue;
            int depth = 0;
            foreach (char c in path) if (c == '/') depth++;
            if (depth < bestDepth)
            {
                bestDepth = depth;
                best = path;
            }
        }
        return best;
    }

    /// <summary>
    /// Rotation tracks with two consecutive keys on opposite sides of the quaternion sphere
    /// (negative dot). Between such keys a per-component interpolation passes through zero.
    /// </summary>
    public static List<string> FindHemisphereFlips(AnimationClip clip)
    {
        List<string> result = new();
        foreach (AnimationClip.AnimBone bone in clip.Bones)
        {
            if (bone.RotX == null || bone.RotY == null || bone.RotZ == null || bone.RotW == null) continue;
            // The four components may have different keys; check every key time of any of them.
            List<float> times = new();
            foreach (AnimationCurve curve in new[] { bone.RotX, bone.RotY, bone.RotZ, bone.RotW })
            {
                foreach (KeyFrame key in curve.Keys) times.Add(key.Position);
            }
            times.Sort();
            Quaternion previous = default;
            for (int i = 0; i < times.Count; i++)
            {
                if (i > 0 && times[i] - times[i - 1] < 1e-6f) continue;
                float t = times[i];
                Quaternion current = new(bone.RotX.Evaluate(t), bone.RotY.Evaluate(t), bone.RotZ.Evaluate(t), bone.RotW.Evaluate(t));
                if (i > 0 && Dot(previous, current) < 0f)
                {
                    result.Add($"{bone.BoneName} at {t:0.####}s");
                    break;
                }
                previous = current;
            }
        }
        return result;
    }

    private static float Dot(Quaternion a, Quaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    private static string RequiredString(JsonElement element, string name)
        => element.GetProperty(name).GetString() ?? throw new InvalidDataException($"the clip has no '{name}'.");

    // ------------------------------------------------------------------ generation (needs the project)

    /// <summary>The export folder inside the open project.</summary>
    public static string ExportDirectory()
        => Path.Combine(Project.Current?.RootPath ?? throw new InvalidOperationException("No project is open."), ExportRelativePath);

    /// <summary>Writes every clip and controller of every exported actor document in
    /// <paramref name="exportDirectory"/>. Hero documents keep their historical
    /// <c>Hero_*.json</c> name; monster documents use <c>Monster_*.json</c> and follow the same
    /// curve conversion path.</summary>
    public static string Generate(string exportDirectory)
    {
        string assets = Project.Current?.AssetsPath ?? throw new InvalidOperationException("No project is open.");
        string[] documents = Directory.GetFiles(exportDirectory, "Hero_*.json")
            .Concat(Directory.GetFiles(exportDirectory, "Monster_*.json"))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (documents.Length == 0) throw new InvalidOperationException($"no Hero_*.json under {exportDirectory}; run the clip export first.");

        int clips = 0, sourceKeys = 0, nativeKeys = 0, constant = 0, streamed = 0, dense = 0;
        List<string> warnings = new();
        List<string> lines = new();
        foreach (string document in documents)
        {
            string hero = Path.GetFileNameWithoutExtension(document);
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(document));
            string folder = $"{AnimationRelativePath}/{hero}";
            string heroFolder = Path.Combine(assets, folder);
            Directory.CreateDirectory(heroFolder);
            MetaFile.EnsureMeta(Path.Combine(assets, AnimationRelativePath), "DefaultImporter");
            MetaFile.EnsureMeta(heroFolder, "DefaultImporter");

            // One state per clip and no transitions, like the hero's Unity controller: the game
            // plays states by name. Every state's speed follows one parameter, so SanguoAnimator
            // can slow or freeze the running motion (hit-stop, slow motion).
            AnimatorController controller = new() { Name = hero };
            controller.Parameters.Add(new AnimatorController.Parameter
            {
                Name = Animation.SanguoAnimator.SpeedParameter,
                Type = AnimatorParameterType.Float,
                DefaultFloat = 1f,
            });
            int heroClips = 0, heroKeys = 0;
            foreach (JsonElement clip in json.RootElement.GetProperty("clips").EnumerateArray())
            {
                string action = clip.GetProperty("action").GetString() ?? "clip";
                try
                {
                    BuildResult built = Build(clip);
                    if (built.HemisphereFlips.Count > 0)
                        warnings.Add($"{hero}/{action}: rotation keys on opposite sides of the sphere: {string.Join("; ", built.HemisphereFlips)}.");
                    Guid guid = StableGuid("unity-guid", clip.GetProperty("guid").GetString() ?? action);
                    SanguoHeroGenerator.WriteNative(built.Clip, $"{folder}/{action}.anim", guid, "ScriptableObjectImporter");

                    int index = controller.States.Count;
                    controller.States.Add(new AnimatorController.State
                    {
                        Name = action,
                        IsLooping = built.Clip.Wrap == AnimationWrapMode.Loop,
                        Motion = new AssetRef<AnimationClip>(guid),
                        SpeedMultiplierParameter = Animation.SanguoAnimator.SpeedParameter,
                        SpeedParameterActive = true,
                        PosX = 260f * (index / 6),
                        PosY = 200f * (index % 6),
                    });
                    clips++; heroClips++;
                    sourceKeys += built.SourceKeys; nativeKeys += built.NativeKeys; heroKeys += built.NativeKeys;
                    constant += built.ConstantCurves; streamed += built.StreamedCurves; dense += built.DenseCurves;
                }
                catch (Exception ex)
                {
                    warnings.Add($"{hero}/{action}: {ex.Message}");
                }
            }
            foreach (AnimatorController.State state in controller.States)
            {
                if (state.Name == "idle_loop") controller.DefaultStateName = state.Name;
            }
            if (controller.DefaultStateName.Length == 0 && controller.States.Count > 0)
                controller.DefaultStateName = controller.States[0].Name;
            SanguoHeroGenerator.WriteNative(controller, $"{folder}/{hero}.controller", StableGuid("hero-controller", hero), "ScriptableObjectImporter");
            lines.Add($"{hero}: {heroClips} clips, {heroKeys} keys, default state '{controller.DefaultStateName}'");
        }

        lines.Insert(0, $"{clips} clip(s), {sourceKeys} Unity keys → {nativeKeys} native keys "
            + $"({constant} constant, {streamed} streamed, {dense} dense curves)");
        if (warnings.Count > 0) lines.Add($"{warnings.Count} warning(s):");
        foreach (string warning in warnings) lines.Add("  ! " + warning);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The same deterministic GUID as <c>Tools/sanguo_import/engine_meta.py::stable_guid</c>:
    /// re-running a generation never forks a reference.</summary>
    public static Guid StableGuid(params string[] parts)
    {
        byte[] digest = MD5.HashData(Encoding.UTF8.GetBytes("xengine:sanguo:" + string.Join(':', parts)));
        return new Guid(digest);
    }
}
