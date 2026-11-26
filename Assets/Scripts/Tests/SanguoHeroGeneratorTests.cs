using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using SanGuoXEngine.Editor;

using Xunit;

namespace SanGuoXEngine.Tests;

/// <summary>
/// The manifest-to-property reading of <see cref="SanguoHeroGenerator"/>: the part that decides
/// which material slot each manifest value lands in. Everything else in the generator needs a
/// running editor and is covered by the M1 evidence (docs/evidence/m1/heroes.md).
/// </summary>
public sealed class SanguoHeroGeneratorTests
{
    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static void AssertTexture(SanguoHeroGenerator.MaterialProperty property, string name, string path)
    {
        Assert.Equal(name, property.Name);
        Assert.Equal(SanguoHeroGenerator.PropertyKind.Texture, property.Kind);
        Assert.Equal(path, property.TexturePath);
        Assert.Empty(property.Values);
    }

    private static void AssertValues(SanguoHeroGenerator.MaterialProperty property, string name,
        SanguoHeroGenerator.PropertyKind kind, params float[] values)
    {
        Assert.Equal(name, property.Name);
        Assert.Equal(kind, property.Kind);
        Assert.Null(property.TexturePath);
        Assert.Equal(values, property.Values);
    }

    [Fact]
    public void Properties_Are_Read_With_Their_Kind_And_Value()
    {
        JsonElement properties = Parse("""
            {
              "_MainTex": "Heroes/Textures/Body_D.png",
              "_NormalTex": "Heroes/Textures/Body_N_NRM.png",
              "_SurfaceTex": "Heroes/Textures/Body_SFX.png",
              "_EmissionTex": "Heroes/Textures/Body_EMI.png",
              "_Tiling": [2.0, 3.0],
              "_Offset": [0.25, 0.0],
              "_MainColor": [0.66, 0.5, 0.25, 1.0],
              "_AlphaClip": 1.0,
              "_AlphaCutoff": 0.35,
              "_Cull": 2.0
            }
            """);

        List<SanguoHeroGenerator.MaterialProperty> read = SanguoHeroGenerator.ReadMaterialProperties(properties);

        Assert.Equal(10, read.Count);
        AssertTexture(read[0], "_MainTex", "Heroes/Textures/Body_D.png");
        AssertTexture(read[1], "_NormalTex", "Heroes/Textures/Body_N_NRM.png");
        AssertTexture(read[2], "_SurfaceTex", "Heroes/Textures/Body_SFX.png");
        AssertTexture(read[3], "_EmissionTex", "Heroes/Textures/Body_EMI.png");
        AssertValues(read[4], "_Tiling", SanguoHeroGenerator.PropertyKind.Vector2, 2.0f, 3.0f);
        AssertValues(read[5], "_Offset", SanguoHeroGenerator.PropertyKind.Vector2, 0.25f, 0.0f);
        AssertValues(read[6], "_MainColor", SanguoHeroGenerator.PropertyKind.Color, 0.66f, 0.5f, 0.25f, 1.0f);
        AssertValues(read[7], "_AlphaClip", SanguoHeroGenerator.PropertyKind.Float, 1.0f);
        AssertValues(read[8], "_AlphaCutoff", SanguoHeroGenerator.PropertyKind.Float, 0.35f);
        AssertValues(read[9], "_Cull", SanguoHeroGenerator.PropertyKind.Float, 2.0f);
    }

    [Fact]
    public void A_Null_Value_Keeps_The_Shader_Default()
    {
        // A Unity material that binds no surface map maps to null: Default/Standard keeps its own.
        List<SanguoHeroGenerator.MaterialProperty> read = SanguoHeroGenerator.ReadMaterialProperties(
            Parse(""" { "_MainTex": "Heroes/Textures/A.png", "_SurfaceTex": null } """));

        AssertTexture(Assert.Single(read), "_MainTex", "Heroes/Textures/A.png");
    }

    [Fact]
    public void A_Vector_With_The_Wrong_Length_Is_Rejected()
    {
        JsonElement properties = Parse(""" { "_Tiling": [1.0, 2.0, 3.0] } """);

        Assert.Throws<InvalidDataException>(() => SanguoHeroGenerator.ReadMaterialProperties(properties));
    }

    [Fact]
    public void A_Property_Name_Is_Matched_Exactly_Not_As_A_Fragment()
    {
        // "_TilingX" is not a manifest property. It reads as a float, so an array value is an error
        // instead of being written into the tiling slot by a substring match.
        JsonElement properties = Parse(""" { "_TilingX": [1.0, 2.0] } """);

        Assert.ThrowsAny<System.InvalidOperationException>(() => SanguoHeroGenerator.ReadMaterialProperties(properties));
    }
}
