# -*- coding: utf-8 -*-
"""Unit tests for the Unity material to Default/Standard mapping in Tools/sanguo_import/material_mapping.py."""

import pytest

from sanguo_import import material_mapping, texture_math


def material(shader, **values):
    """A material record in the shape export_heroes.csx writes: properties keyed by type."""
    types = {
        "_MainTex": "TexEnv", "_MetallicGlossMap": "TexEnv", "_BumpMap": "TexEnv", "_Unity_SpecCube": "TexEnv",
        "_Color": "Color", "_EmissionColor": "Color",
        "_Cutoff": "Range", "_Cull": "Float", "_EmissionScale": "Range", "_RimLight": "Float",
        "_EffectToggle": "Float", "_PassColor": "Float", "_SkinLightScale": "Range",
    }
    return {
        "name": values.pop("name", "TestMaterial"),
        "shader": shader,
        "keywords": values.pop("keywords", []),
        "properties": [{"name": key, "type": types.get(key, "Float"), "value": value} for key, value in values.items()],
    }


def texenv(guid, scale=(1.0, 1.0), offset=(0.0, 0.0)):
    return {"texture": guid, "scale": list(scale), "offset": list(offset)}


HERO = "Standards/S_Standard_Hero"
SKIN = "Standards/S_Standard_Skin"
HAIR = "Standards/S_Standard_Hair"


def test_body_material_maps_the_three_textures_and_disables_alpha_clipping():
    mapped = material_mapping.map_material(material(
        HERO, _MainTex=texenv("albedo"), _MetallicGlossMap=texenv("packed"), _BumpMap=texenv("normal"),
        _Color=[1, 1, 1, 1], _Cutoff=0.5, _Cull=2, _Unity_SpecCube=texenv("cube")))

    assert mapped["shader"] == "Standard"
    assert mapped["properties"]["_MainTex"] == "albedo"
    assert mapped["properties"]["_NormalTex"] == "normal"
    # Named by the packed texture's guid: the import swaps in the derived surface map.
    assert mapped["properties"]["_SurfaceTex"] == "packed"
    # Opaque in Unity, so Default/Standard must not cut holes below its alpha cutoff.
    assert mapped["properties"]["_AlphaClip"] == 0.0
    assert "_AlphaCutoff" not in mapped["properties"]
    assert "_MainColor" not in mapped["properties"]      # a white _Color is the default
    assert mapped["properties"]["_Cull"] == 2
    assert mapped["emission"] is None
    assert mapped["notes"] == ["per-material reflection cubemap replaced by the scene's image-based lighting"]


def test_alpha_tested_material_keeps_unitys_cutoff():
    mapped = material_mapping.map_material(material(
        HERO, _MainTex=texenv("albedo"), _MetallicGlossMap=texenv("packed"), _Cutoff=0.35, keywords=["_ALPHATEST_ON"]))

    assert mapped["properties"]["_AlphaClip"] == 1.0
    assert mapped["properties"]["_AlphaCutoff"] == 0.35


def test_emission_is_folded_into_a_texture_and_an_intensity():
    mapped = material_mapping.map_material(material(
        HERO, _MainTex=texenv("albedo"), _MetallicGlossMap=texenv("packed"),
        _EmissionColor=[1.0, 0.5082709, 0.0, 1.0], _EmissionScale=3.8))

    recipe = texture_math.emission_recipe((1.0, 0.5082709, 0.0, 1.0), 3.8)
    assert mapped["emission"] == {"maskTexture": "packed", "recipe": recipe}
    assert mapped["properties"]["_EmissionIntensity"] == pytest.approx(3.8)
    assert "emission pulse replaced by its mean" in mapped["notes"]


def test_zero_emission_scale_means_no_emission():
    mapped = material_mapping.map_material(material(
        HERO, _MainTex=texenv("albedo"), _MetallicGlossMap=texenv("packed"),
        _EmissionColor=[1.5, 1.5, 1.5, 1], _EmissionScale=0, keywords=["_EMISSION"]))

    assert mapped["emission"] is None
    assert "_EmissionIntensity" not in mapped["properties"]


def test_skin_tint_is_decoded_from_srgb():
    mapped = material_mapping.map_material(material(
        SKIN, _MainTex=texenv("skin"), _MetallicGlossMap=texenv("packed"), _Color=[0.83, 0.83, 0.83, 1], _SkinLightScale=0.8))

    expected = texture_math.srgb_to_linear([0.83, 0.83, 0.83])
    assert mapped["properties"]["_MainColor"] == pytest.approx([expected[0], expected[1], expected[2], 1.0])
    assert any("fake-skin shading" in note for note in mapped["notes"])


def test_hair_uses_the_anisotropic_shader_and_always_alpha_tests():
    mapped = material_mapping.map_material(material(
        HAIR, _MainTex=texenv("hair"), _MetallicGlossMap=texenv("packed"), _Color=[0.77, 0.70, 0.63, 1], _Cutoff=0.944))

    assert mapped["shader"] == "StandardAnisotropic"
    assert mapped["properties"]["_AlphaCutoff"] == 0.944
    assert "_AlphaClip" not in mapped["properties"]       # that shader has no clipping toggle
    assert any("strand specular" in note for note in mapped["notes"])


def test_tiling_and_offset_are_carried_only_when_not_default():
    default = material_mapping.map_material(material(HERO, _MainTex=texenv("a", (1, 1), (0, 0)), _MetallicGlossMap=texenv("p")))
    tiled = material_mapping.map_material(material(HERO, _MainTex=texenv("a", (2, 3), (0.25, 0)), _MetallicGlossMap=texenv("p")))

    assert "_Tiling" not in default["properties"] and "_Offset" not in default["properties"]
    assert tiled["properties"]["_Tiling"] == [2, 3]
    assert tiled["properties"]["_Offset"] == [0.25, 0]


def test_unbound_surface_map_is_reported_and_left_at_the_shader_default():
    mapped = material_mapping.map_material(material(HERO, _MainTex=texenv("a")))

    assert mapped["properties"]["_SurfaceTex"] is None
    assert any("white default" in note for note in mapped["notes"])


def test_unmapped_shader_falls_back_to_standard_and_says_so():
    mapped = material_mapping.map_material(material("Custom/Unknown", _MainTex=texenv("a"), _MetallicGlossMap=texenv("p")))

    assert mapped["shader"] == "Standard"
    assert any("has no mapping" in note for note in mapped["notes"])


def test_unreproduced_effects_are_listed():
    mapped = material_mapping.map_material(material(
        HERO, _MainTex=texenv("a"), _MetallicGlossMap=texenv("p"),
        _RimLight=1, _EffectToggle=1, _PassColor=1))

    joined = "\n".join(mapped["notes"])
    assert "rim light is not reproduced" in joined
    assert "flow-light layer is not reproduced" in joined
    assert "dye recolouring is not reproduced" in joined
