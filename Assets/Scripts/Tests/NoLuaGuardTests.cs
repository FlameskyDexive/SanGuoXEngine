using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace SanGuoXEngine.Tests;

/// <summary>
/// Zero-Lua guard for the SanGuoXEngine port.
/// </summary>
/// <remarks>
/// The game and editor code of this project is 100% C#: no Lua runtime may be referenced and no Lua
/// source or data may be committed. The original project's Lua tables are read once by the offline
/// converters in <c>Tools/</c> (Python) and become native data assets under <c>Assets/Data/</c>.
/// </remarks>
public sealed class NoLuaGuardTests
{
    private const int MaxReported = 20;

    // Identifiers that would pull a Lua interpreter into the project. Matched case-insensitively
    // against package names; the source scan uses the type-name form (see LuaRuntimeTypes).
    private static readonly string[] LuaRuntimeIdentifiers =
    {
        "tolua", "nlua", "keralua", "moonsharp", "xlua", "luainterface", "elua",
    };

    // Regenerable caches and export intermediates are not project sources.
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Library", "Temp", "Logs", "obj", "bin", "Export",
    };

    // `using NLua;`, `new LuaState()`, `MoonSharp.Registry`, ... The plain word "Lua" stays allowed
    // so comments can explain where the original logic came from.
    private static readonly Regex LuaRuntimeTypes = new(
        @"\b(ToLua|NLua|KeraLua|MoonSharp|XLua|LuaInterface|Elua)\w*",
        RegexOptions.Compiled);

    [Fact]
    public void ProjectFolders_Hold_No_Lua_Files()
    {
        string root = FindProjectRoot();
        List<string> offenders = new();
        string[] folders = { "Assets", "Packages", "ProjectSettings", "Tools" };
        for (int i = 0; i < folders.Length; i++)
        {
            CollectFiles(Path.Combine(root, folders[i]), IsLuaFile, offenders);
        }

        Assert.True(offenders.Count == 0, "Lua files must not be committed; convert them offline with "
            + "the Tools/ converters instead. Found:\n" + Join(offenders));
    }

    [Fact]
    public void Manifest_Declares_No_Lua_Package()
    {
        string manifestPath = Path.Combine(FindProjectRoot(), "Packages", "manifest.json");
        Assert.True(File.Exists(manifestPath), $"Missing package manifest: {manifestPath}");

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        List<string> names = new();
        ReadObjectPropertyNames(manifest.RootElement, "dependencies", names);
        ReadStringArray(manifest.RootElement, "testables", names);

        List<string> offenders = new();
        for (int i = 0; i < names.Count; i++)
        {
            if (ContainsLuaRuntime(names[i]))
                offenders.Add(names[i]);
        }

        Assert.True(offenders.Count == 0, "The manifest must not reference a Lua runtime package. Found:\n"
            + Join(offenders));
    }

    [Fact]
    public void DirectoryBuildProps_Reference_No_Lua_Package()
    {
        string propsPath = Path.Combine(FindProjectRoot(), "Directory.Build.props");
        Assert.True(File.Exists(propsPath), $"Missing {propsPath}");

        // Comments are stripped so an example inside one does not count.
        string text = Regex.Replace(File.ReadAllText(propsPath), @"<!--.*?-->", string.Empty,
            RegexOptions.Singleline);

        List<string> offenders = new();
        // \x22 is the double quote, so the pattern needs no quote escaping inside the verbatim string.
        foreach (Match match in Regex.Matches(text,
            @"<(?:PackageReference|ProjectReference)\b[^>]*\bInclude=\x22([^\x22]+)\x22"))
        {
            string include = match.Groups[1].Value;
            if (ContainsLuaRuntime(include))
                offenders.Add(include);
        }

        Assert.True(offenders.Count == 0, "Directory.Build.props must not reference a Lua runtime. Found:\n"
            + Join(offenders));
    }

    [Fact]
    public void RuntimeSources_Do_Not_Reference_Lua_Runtimes()
    {
        string assetsRoot = Path.Combine(FindProjectRoot(), "Assets");

        List<string> sources = new();
        CollectFiles(assetsRoot, IsRuntimeCSharpSource, sources);

        List<string> offenders = new();
        for (int i = 0; i < sources.Count && offenders.Count < MaxReported; i++)
        {
            foreach (Match match in LuaRuntimeTypes.Matches(File.ReadAllText(sources[i])))
            {
                offenders.Add(sources[i] + ": " + match.Value);
                if (offenders.Count >= MaxReported)
                    break;
            }
        }

        Assert.True(offenders.Count == 0, "Runtime sources must not use a Lua runtime. Found:\n" + Join(offenders));
    }

    /// <summary>Walks up from the test assembly's folder until it finds the project root.</summary>
    private static string FindProjectRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Packages", "manifest.json"))
                && Directory.Exists(Path.Combine(directory, "Assets")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException("Could not locate the SanGuoXEngine project root.");
    }

    private static void CollectFiles(string directory, Func<string, bool> predicate, List<string> results)
    {
        if (!Directory.Exists(directory))
            return;

        // Explicit stack instead of SearchOption.AllDirectories so skipped folders are pruned before
        // they are entered and a junction loop cannot make the walk unbounded.
        Stack<string> pending = new();
        pending.Push(directory);
        while (pending.Count > 0 && results.Count < MaxReported)
        {
            string current = pending.Pop();

            foreach (string file in Directory.EnumerateFiles(current))
            {
                if (!predicate(file))
                    continue;

                results.Add(file);
                if (results.Count >= MaxReported)
                    return;
            }

            foreach (string child in Directory.EnumerateDirectories(current))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child)))
                    pending.Push(child);
            }
        }
    }

    private static bool IsLuaFile(string path)
    {
        return path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".luac", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRuntimeCSharpSource(string path)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return false;

        // Test code may name the runtimes it forbids; runtime code may not.
        foreach (string segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Equals("Tests", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static void ReadObjectPropertyNames(JsonElement element, string propertyName, List<string> names)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out JsonElement obj)
            || obj.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in obj.EnumerateObject())
            names.Add(property.Name);
    }

    private static void ReadStringArray(JsonElement element, string propertyName, List<string> names)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                names.Add(item.GetString() ?? string.Empty);
        }
    }

    private static bool ContainsLuaRuntime(string name)
    {
        for (int i = 0; i < LuaRuntimeIdentifiers.Length; i++)
        {
            if (name.Contains(LuaRuntimeIdentifiers[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string Join(List<string> items)
    {
        StringBuilder builder = new();
        for (int i = 0; i < items.Count; i++)
        {
            builder.Append("- ").Append(items[i]).Append('\n');
        }

        return builder.ToString();
    }
}
