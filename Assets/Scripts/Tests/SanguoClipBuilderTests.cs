using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

using SanGuoXEngine.Editor;

using Xunit;
using Xunit.Abstractions;

using XEngine.Vector;

namespace SanGuoXEngine.Tests;

/// <summary>
/// The curve rules of <see cref="SanguoClipBuilder"/>: how Unity's keys become engine keys.
/// <see cref="ExportedClips_ReproduceUnitySamples"/> is the numeric acceptance of M2 — it replays
/// every exported clip against Unity's own sampled poses when the export is on disk.
/// </summary>
public sealed class SanguoClipBuilderTests
{
    private const float Rate = 30f;

    private static float[][] Keys(params float[][] keys) => keys;

    private static float[] Key(float time, float value, float inSlope = 0f, float outSlope = 0f)
        => new[] { time, value, inSlope, outSlope, 1f / 3f, 1f / 3f, 0f };

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Curve(string path, string property, params float[][] keys)
    {
        StringBuilder text = new StringBuilder("{\"path\":\"").Append(path).Append("\",\"property\":\"").Append(property).Append("\",\"keys\":[");
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0) text.Append(',');
            text.Append('[');
            for (int k = 0; k < keys[i].Length; k++)
            {
                if (k > 0) text.Append(',');
                text.Append(Number(keys[i][k]));
            }
            text.Append(']');
        }
        return text.Append("]}").ToString();
    }

    /// <summary>A one-second clip with the given curves and no events.</summary>
    private static JsonElement Clip(params string[] curves)
    {
        StringBuilder text = new StringBuilder("{\"name\":\"Hero_Test_x\",\"action\":\"x\",\"guid\":\"0123456789abcdef0123456789abcdef\",")
            .Append("\"sampleRate\":").Append(Number(Rate)).Append(",\"length\":1,\"settings\":{\"startTime\":0,\"stopTime\":1,\"loopTime\":false},")
            .Append("\"events\":[],\"curves\":[");
        for (int i = 0; i < curves.Length; i++)
        {
            if (i > 0) text.Append(',');
            text.Append(curves[i]);
        }
        return Parse(text.Append("]}").ToString());
    }

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static float Angle(Quaternion a, Quaternion b)
    {
        float dot = MathF.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W);
        return MathF.Acos(MathF.Min(1f, dot / MathF.Sqrt(Length(a) * Length(b)))) * 2f * 180f / MathF.PI;
    }

    private static float Length(Quaternion q) => q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;

    // ------------------------------------------------------------------ curve rules

    [Fact]
    public void DenseNeedsMoreThanOneKeyPerFiveFrames()
    {
        Assert.False(SanguoClipBuilder.IsDense(4, 23f, 0f, 1f));
        Assert.True(SanguoClipBuilder.IsDense(5, 23f, 0f, 1f));
        // exactly one key per five frames is still streamed
        Assert.False(SanguoClipBuilder.IsDense(23, 115f, 0f, 1f));
        Assert.True(SanguoClipBuilder.IsDense(24, 115f, 0f, 1f));
    }

    [Fact]
    public void KeysBetweenFramesStayStreamed()
    {
        // Keys a third of the way into their frames: the curve is played by Hermite however many
        // keys it has.
        float[][] keys = Keys(Key(0f, 0f), Key(1.3f / Rate, 0.2f), Key(2.3f / Rate, 0.9f), Key(3f / Rate, 1f));
        Assert.Equal(0.3f, SanguoClipBuilder.MaxOffGrid(keys, Rate), 4);
        XEngine.Runtime.AnimationCurve curve = SanguoClipBuilder.ConvertCurve(keys, 3f, Rate, false, out SanguoClipBuilder.CurveKind kind);
        Assert.Equal(SanguoClipBuilder.CurveKind.Streamed, kind);
        Assert.Equal(4, curve.Keys.Count);
    }

    [Fact]
    public void TwoKeysInOneFrameStayStreamed()
    {
        // A key on frame 2 and another one a tenth of a frame later.
        float[][] keys = Keys(Key(0f, 0f), Key(1f / Rate, 0.1f), Key(2f / Rate, 0.8f), Key(2.1f / Rate, 0.9f), Key(3f / Rate, 1f));
        Assert.Equal(0.1f, SanguoClipBuilder.MinSpacing(keys, Rate), 4);
        XEngine.Runtime.AnimationCurve curve = SanguoClipBuilder.ConvertCurve(keys, 3f, Rate, false, out SanguoClipBuilder.CurveKind kind);
        Assert.Equal(SanguoClipBuilder.CurveKind.Streamed, kind);
        Assert.Equal(5, curve.Keys.Count);
    }

    [Fact]
    public void ConstantCurveKeepsOneKey()
    {
        XEngine.Runtime.AnimationCurve curve = SanguoClipBuilder.ConvertCurve(
            Keys(Key(0f, 0.5f), Key(1f, 0.5f), Key(2f, 0.5f)), 60f, Rate, false, out SanguoClipBuilder.CurveKind kind);

        Assert.Equal(SanguoClipBuilder.CurveKind.Constant, kind);
        Assert.Equal(1, curve.Keys.Count);
        Assert.Equal(0.5f, curve.Evaluate(1.5f));
    }

    [Fact]
    public void StreamedCurveKeepsItsKeysWithTangentsPerSegment()
    {
        // A rise from 0 to 1 whose start leaves at one unit per second.
        XEngine.Runtime.AnimationCurve curve = SanguoClipBuilder.ConvertCurve(
            Keys(Key(0f, 0f, 0f, 1f), Key(1f, 1f, 0f, 0f)), 60f, Rate, false, out SanguoClipBuilder.CurveKind kind);

        Assert.Equal(SanguoClipBuilder.CurveKind.Streamed, kind);
        Assert.Equal(2, curve.Keys.Count);
        // The engine's tangent is the value change over the whole segment: 1/s over 1 s.
        Assert.Equal(1f, curve.Keys[0].TangentOut, 5);
        Assert.Equal(0f, curve.Keys[1].TangentIn, 5);
        // Cubic Hermite: h10(½)·1 + h01(½)·1 = 0.125 + 0.5.
        Assert.Equal(0.625f, curve.Evaluate(0.5f), 5);
        Assert.Equal(1f, curve.Evaluate(5f), 5);   // holds its last value
    }

    [Fact]
    public void DenseCurveIsLinearBetweenFrameSamples()
    {
        // Four keys, one per frame. The second key leaves at 60 units per second, so plain Hermite
        // through the keys bulges; Unity interpolates linearly between the frame values instead.
        float[][] keys = Keys(Key(0f, 0f), Key(1f / Rate, 0f, 0f, 60f), Key(2f / Rate, 1f, 0f, 0f), Key(3f / Rate, 1f));
        XEngine.Runtime.AnimationCurve dense = SanguoClipBuilder.ConvertCurve(keys, 3f, Rate, false, out SanguoClipBuilder.CurveKind kind);
        XEngine.Runtime.AnimationCurve streamed = SanguoClipBuilder.ConvertCurve(keys, 300f, Rate, false, out _);

        Assert.Equal(SanguoClipBuilder.CurveKind.Dense, kind);
        Assert.Equal(4, dense.Keys.Count);
        float middle = 1.5f / Rate;
        Assert.Equal(0.5f, dense.Evaluate(middle), 1e-4f);
        Assert.Equal(0.75f, streamed.Evaluate(middle), 1e-4f);
    }

    [Fact]
    public void DenseCurveEvaluatesTheSourceCurveAtFrameTimes()
    {
        // Three keys over ten frames are dense; the first two keys are six frames apart, so the
        // frame samples in between come from the Hermite curve through the keys.
        float[][] keys = Keys(Key(0f, 0f), Key(0.2f, 1f), Key(10f / Rate, 1f));
        XEngine.Runtime.AnimationCurve curve = SanguoClipBuilder.ConvertCurve(keys, 10f, Rate, false, out SanguoClipBuilder.CurveKind kind);

        Assert.Equal(SanguoClipBuilder.CurveKind.Dense, kind);
        Assert.Equal(11, curve.Keys.Count);
        // Frame 4 lies at t = 4/30, two thirds along the first segment.
        Assert.Equal(4f / Rate, curve.Keys[4].Position, 5);
        Assert.Equal(-2f * (2f / 3f) * (2f / 3f) * (2f / 3f) + 3f * (2f / 3f) * (2f / 3f), curve.Keys[4].Value, 1e-5f);
        // And in between two frame samples the curve is their chord.
        Assert.Equal((curve.Keys[3].Value + curve.Keys[4].Value) * 0.5f, curve.Evaluate(3.5f / Rate), 1e-5f);
    }

    [Fact]
    public void SameTimeKeysKeepTheLastOne()
    {
        XEngine.Runtime.AnimationCurve curve = SanguoClipBuilder.ConvertCurve(
            Keys(Key(0f, 0f), Key(1f, 3f), Key(1f, 1f)), 60f, Rate, false, out _);

        Assert.Equal(2, curve.Keys.Count);
        Assert.Equal(1f, curve.Keys[1].Value);
    }

    // ------------------------------------------------------------------ whole clips

    [Fact]
    public void BuiltClipCarriesMetadataBonesEventsAndTheTrajectory()
    {
        // The root-motion node's travel is stored twice: on the node and on the Animator (RootT).
        float[] travel = { 0f, 0.5f, 0f, 0f, 1f / 3f, 1f / 3f, 0f };
        float[] travelEnd = { 1f, 2.5f, 2f, 2f, 1f / 3f, 1f / 3f, 0f };
        float[][] flat = Keys(Key(0f, 0f), Key(1f, 0f));
        float[][] turn = Keys(Key(0f, 0f, 1f, 1f), Key(1f, 0.5f));
        JsonElement clip = Parse("{\"name\":\"Hero_Test_x\",\"action\":\"x\",\"guid\":\"0123456789abcdef0123456789abcdef\",\"sampleRate\":30,\"length\":1,"
            + "\"settings\":{\"startTime\":0,\"stopTime\":1,\"loopTime\":true},"
            + "\"events\":[{\"time\":0.25,\"functionName\":\"Hit\",\"floatParameter\":1.5,\"intParameter\":2,\"stringParameter\":\"head\"}],"
            + "\"curves\":["
            + Curve("", "RootT.x", flat) + "," + Curve("", "RootT.y", flat) + "," + Curve("", "RootT.z", travel, travelEnd)
            + "," + Curve("", "RootQ.x", flat) + "," + Curve("", "RootQ.y", flat) + "," + Curve("", "RootQ.z", flat)
            + "," + Curve("", "RootQ.w", Keys(Key(0f, 1f), Key(1f, 1f)))
            + "," + Curve("Bip001", "m_LocalPosition.x", flat) + "," + Curve("Bip001", "m_LocalPosition.y", flat)
            + "," + Curve("Bip001", "m_LocalPosition.z", travel, travelEnd)
            + "," + Curve("Bip001", "m_LocalRotation.x", Keys(Key(0f, -0.7071f), Key(1f, -0.7071f)))
            + "," + Curve("Bip001", "m_LocalRotation.y", flat) + "," + Curve("Bip001", "m_LocalRotation.z", flat)
            + "," + Curve("Bip001", "m_LocalRotation.w", Keys(Key(0f, 0.7071f), Key(1f, 0.7071f)))
            + "," + Curve("Bip001/Bip00A", "m_LocalPosition.x", flat) + "," + Curve("Bip001/Bip00A", "m_LocalPosition.y", flat)
            + "," + Curve("Bip001/Bip00A", "m_LocalPosition.z", Keys(Key(0f, 1.2f), Key(1f, 1.2f)))
            + "," + Curve("Bip001/Bip00A", "m_LocalRotation.x", turn)
            + "," + Curve("Bip001/Bip00A", "m_LocalRotation.y", flat) + "," + Curve("Bip001/Bip00A", "m_LocalRotation.z", flat)
            + "," + Curve("Bip001/Bip00A", "m_LocalRotation.w", Keys(Key(0f, 1f), Key(1f, 0.866f)))
            + "]}");

        SanguoClipBuilder.BuildResult built = SanguoClipBuilder.Build(clip);
        XEngine.Runtime.AnimationClip native = built.Clip;

        Assert.Equal("x", native.Name);
        Assert.Equal(1f, native.Duration, 5);
        Assert.Equal(Rate, native.FrameRate);
        Assert.Equal(XEngine.Runtime.AnimationWrapMode.Loop, native.Wrap);
        Assert.Equal("Bip001", built.MotionNode);
        Assert.Empty(built.HemisphereFlips);
        Assert.Equal(42, built.SourceKeys);   // 21 curves with two keys each
        Assert.Equal(3, native.Bones.Count);

        // The trajectory is a track without a node, selected as the clip's root.
        XEngine.Runtime.AnimationClip.AnimBone? root = native.GetBone("");
        Assert.NotNull(root);
        // Dense: the frame sample at 0.5 s is the Hermite curve through the keys there
        // (flat start, arriving at 2 units per second): 0.25·0.5 + 0.5·2.5 − 0.125·2.
        Assert.Equal(1.25f, root!.PosZ.Evaluate(0.5f), 1e-4f);
        Assert.Equal(1f, root.RotW.Evaluate(0.3f), 5);
        Assert.Equal(".", native.RootBonePath);
        Assert.Equal(XEngine.Runtime.ClipRootMotionPolicy.ExplicitBone, native.RootMotionPolicy);

        // The root-motion node stays where its own curve and the trajectory agree: at the origin.
        XEngine.Runtime.AnimationClip.AnimBone? motionNode = native.GetBone("Bip001");
        Assert.NotNull(motionNode);
        Assert.Equal(0f, motionNode!.PosZ.Evaluate(0.5f), 1e-3f);
        Assert.Equal(-0.7071f, motionNode.RotX.Evaluate(0.3f), 4);

        // An ordinary bone keeps its curves; rotations come out normalized.
        XEngine.Runtime.AnimationClip.AnimBone? bone = native.GetBone("Bip001/Bip00A");
        Assert.NotNull(bone);
        Assert.Equal(1.2f, bone!.PosZ.Evaluate(0.7f), 5);
        Assert.Equal(1f, Length(bone.EvaluateRotationAt(0.5f)), 2e-3f);

        XEngine.Animation.AnimationClipEvent ev = Assert.Single(native.Events);
        Assert.Equal(0.25f, ev.Time, 5);
        Assert.Equal("Hit", ev.FunctionName);
        Assert.Equal(1.5f, ev.FloatParam);
        Assert.Equal(2, ev.IntParam);
        Assert.Equal("head", ev.StringParam);
    }

    [Fact]
    public void WrapModeFollowsTheLoopFlag()
    {
        string[] rotation =
        {
            Curve("A", "m_LocalRotation.x", Keys(Key(0f, 0f), Key(1f, 0f))),
            Curve("A", "m_LocalRotation.y", Keys(Key(0f, 0f), Key(1f, 0f))),
            Curve("A", "m_LocalRotation.z", Keys(Key(0f, 0f), Key(1f, 0f))),
            Curve("A", "m_LocalRotation.w", Keys(Key(0f, 1f), Key(1f, 1f))),
        };
        Assert.Equal(XEngine.Runtime.AnimationWrapMode.Once, SanguoClipBuilder.BuildClip(Clip(rotation)).Wrap);
    }

    [Fact]
    public void PartlyAnimatedChannelIsRejected()
    {
        JsonElement clip = Clip(Curve("A", "m_LocalRotation.w", Keys(Key(0f, 1f), Key(1f, 1f))));
        Assert.Throws<InvalidDataException>(() => SanguoClipBuilder.BuildClip(clip));
    }

    [Fact]
    public void PinnedNodeIsTheNodeCurveMinusTheTrajectory()
    {
        // The node's curve is smooth inside a frame, the trajectory is linear there, so the
        // difference is a real (centimetre) curve that must survive the resampling.
        float[][] keys = Keys(Key(0f, 0f, 0f, 30f), Key(0.2f, 1f), Key(1f, 1f));
        XEngine.Runtime.AnimationCurve node = SanguoClipBuilder.ConvertCurve(keys, 30f, Rate, false, out _);
        XEngine.Runtime.AnimationCurve trajectory = SanguoClipBuilder.ConvertCurve(keys, 30f, Rate, true, out _);
        Assert.Equal(1.026f, node.Evaluate(0.18f), 3);
        Assert.Equal(1.039f, trajectory.Evaluate(0.18f), 3);

        XEngine.Runtime.AnimationCurve pinned = SanguoClipBuilder.PinnedCurve(node, trajectory, Rate);
        for (float time = 0.02f; time < 1f; time += 0.0137f)
            Assert.Equal(node.Evaluate(time) - trajectory.Evaluate(time), pinned.Evaluate(time), 1e-4f);
    }

    [Fact]
    public void HemisphereFlipIsReported()
    {
        // (0,0,0,1) to (1,0,0,-1) is the same rotation as to (-1,0,0,1); per-component
        // interpolation between the stored keys passes through zero.
        JsonElement clip = Clip(
            Curve("A", "m_LocalRotation.x", Keys(Key(0f, 0f), Key(1f, 1f))),
            Curve("A", "m_LocalRotation.y", Keys(Key(0f, 0f), Key(1f, 0f))),
            Curve("A", "m_LocalRotation.z", Keys(Key(0f, 0f), Key(1f, 0f))),
            Curve("A", "m_LocalRotation.w", Keys(Key(0f, 1f), Key(1f, -1f))));

        Assert.Single(SanguoClipBuilder.FindHemisphereFlips(SanguoClipBuilder.BuildClip(clip)));
    }

    // ------------------------------------------------------------------ acceptance against Unity's samples

    /// <summary>Rotation error is float rounding (Unity stores its keys with four decimals); the
    /// root-motion node and the actor root carry Unity's own frame-granular residual.</summary>
    private const float RotationToleranceDegrees = 0.5f;
    private const float PositionToleranceMetres = 0.005f;
    private const float RootToleranceMetres = 0.03f;

    private readonly ITestOutputHelper _output;

    public SanguoClipBuilderTests(ITestOutputHelper output) => _output = output;

    /// <summary>The worst deviations of one hero's clips from Unity's samples.</summary>
    private sealed class Parity
    {
        public int Clips, Rotations, Positions, Roots;
        public float Rotation, Position, Root;
        public string? RotationAt, PositionAt;

        public void NoteRotation(float error, string where)
        {
            Rotations++;
            if (error <= Rotation) return;
            Rotation = error;
            RotationAt = where;
        }

        public void NotePosition(float error, string where)
        {
            Positions++;
            if (error <= Position) return;
            Position = error;
            PositionAt = where;
        }

        public void NoteRoot(float error)
        {
            Roots++;
            Root = MathF.Max(Root, error);
        }

        public bool WithinTolerance => Rotation <= RotationToleranceDegrees && Position <= PositionToleranceMetres && Root <= RootToleranceMetres;

        public string Line(string hero)
            => $"{hero}: {Clips} clips — worst rotation {Rotation:0.0000}° ({RotationAt}), worst bone position {Position * 1000:0.000} mm ({PositionAt}), "
                + $"worst root-motion position {Root * 1000:0.0} mm; {Rotations} rotations, {Positions} positions, {Roots} roots compared";
    }

    [Fact]
    public void ExportedClips_ReproduceUnitySamples()
    {
        string? repository = FindRepository();
        string export = Path.Combine(repository ?? AppContext.BaseDirectory, SanguoClipBuilder.ExportRelativePath);
        string[] documents = Directory.Exists(export) ? Directory.GetFiles(export, "Hero_*.json") : Array.Empty<string>();
        if (documents.Length == 0) return;   // the export needs the jws2 editor; see docs/evidence/m2

        List<Parity> parities = new();
        foreach (string document in documents)
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(document));
            JsonElement rootElement = json.RootElement;
            List<string> paths = new();
            foreach (JsonElement node in rootElement.GetProperty("nodes").EnumerateArray()) paths.Add(node.GetProperty("path").GetString() ?? "");
            float[] rest = ReadFloats(rootElement.GetProperty("rootRest").GetProperty("position"), 3);
            string hero = Path.GetFileNameWithoutExtension(document);
            Parity parity = new();
            parities.Add(parity);

            foreach (JsonElement clip in rootElement.GetProperty("clips").EnumerateArray())
            {
                SanguoClipBuilder.BuildResult built = SanguoClipBuilder.Build(clip);
                string action = clip.GetProperty("action").GetString() ?? "?";
                Assert.Empty(built.HemisphereFlips);
                Assert.Equal("Bip001", built.MotionNode);   // every exported clip has root motion

                JsonElement samples = clip.GetProperty("samples");
                float[] times = ReadFloats(samples.GetProperty("times"), samples.GetProperty("times").GetArrayLength());
                XEngine.Runtime.AnimationClip.AnimBone? trajectory = built.Clip.GetBone("");
                Assert.NotNull(trajectory);
                Float3 start = trajectory!.EvaluatePositionAt(times[0]);
                parity.Clips++;

                int timeIndex = 0;
                foreach (JsonElement poses in samples.GetProperty("poses").EnumerateArray())
                {
                    float time = times[timeIndex];
                    timeIndex++;
                    if (time == 0f) continue;
                    for (int n = 0; n < paths.Count && n < poses.GetArrayLength(); n++)
                    {
                        XEngine.Runtime.AnimationClip.AnimBone? bone = built.Clip.GetBone(paths[n]);
                        if (bone == null) continue;
                        JsonElement pose = poses[n];

                        if (bone.RotX != null)
                        {
                            JsonElement unity = pose[1];
                            Quaternion expected = new(unity[0].GetSingle(), unity[1].GetSingle(), unity[2].GetSingle(), unity[3].GetSingle());
                            parity.NoteRotation(Angle(expected, bone.EvaluateRotationAt(time)), $"{action} '{paths[n]}' t={time:0.####}");
                        }
                        if (bone.PosX == null) continue;
                        JsonElement unityPosition = pose[0];
                        Float3 expectedPosition = new(unityPosition[0].GetSingle(), unityPosition[1].GetSingle(), unityPosition[2].GetSingle());

                        if (paths[n].Length == 0)
                        {
                            // The actor root: its rest position plus the trajectory's increments.
                            Float3 travelled = trajectory.EvaluatePositionAt(time) - start;
                            parity.NoteRoot(Distance(expectedPosition, new Float3(rest[0], rest[1], rest[2]) + travelled));
                        }
                        else if (paths[n] == built.MotionNode)
                        {
                            parity.NoteRoot(Distance(expectedPosition, bone.EvaluatePositionAt(time)));
                        }
                        else
                        {
                            parity.NotePosition(Distance(expectedPosition, bone.EvaluatePositionAt(time)), $"{action} '{paths[n]}' t={time:0.####}");
                        }
                    }
                }
            }
            _output.WriteLine(parity.Line(hero));
        }

        StringBuilder text = new();
        foreach (Parity p in parities)
        {
            if (text.Length > 0) text.Append(Environment.NewLine).Append("  ");
            text.Append(p.Line("!"));
        }
        string report = text.ToString();
        Assert.All(parities, p => Assert.True(p.WithinTolerance, report));
    }

    private static string? FindRepository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SanGuoXEngine.Tests.csproj"))) return directory.FullName;
        }
        return null;
    }

    private static float[] ReadFloats(JsonElement array, int count)
    {
        float[] result = new float[count];
        for (int i = 0; i < result.Length; i++) result[i] = array[i].GetSingle();
        return result;
    }

    private static float Distance(Float3 a, Float3 b)
    {
        float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return MathF.Sqrt(x * x + y * y + z * z);
    }
}
