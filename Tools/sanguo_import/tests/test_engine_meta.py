# -*- coding: utf-8 -*-
"""Unit tests for the engine meta translation in Tools/sanguo_import/engine_meta.py."""

import json
import uuid

import pytest

from sanguo_import import engine_meta


def test_stable_guid_is_deterministic_and_well_formed():
    again = engine_meta.stable_guid("unity-guid", "abc")
    assert again == engine_meta.stable_guid("unity-guid", "abc")
    uuid.UUID(again)                                   # parses as a GUID
    assert again != engine_meta.stable_guid("unity-guid", "abd")
    assert again != engine_meta.stable_guid("derived", "abc")


def test_texture_settings_translate_unity_names_to_engine_values():
    settings = engine_meta.texture_settings(
        {"sRGB": True, "textureType": "Default", "alphaSource": "FromInput", "mipmaps": True,
         "filterMode": "Bilinear", "wrapMode": "Repeat"}, 512)

    assert settings == {
        "textureType": 0, "maxTextureSize": 512, "generateMipmaps": True, "sRGB": True, "sRGBSampling": False,
        "alphaSource": 1, "minFilter": 3, "magFilter": 1, "wrapMode": 0, "npotScale": 0,
    }


@pytest.mark.parametrize("filter_mode, mipmaps, expected_min", [
    ("Point", False, 0), ("Bilinear", False, 1), ("Trilinear", False, 1),
    ("Point", True, 2), ("Bilinear", True, 3), ("Trilinear", True, 5),
])
def test_filter_modes_follow_the_engines_texturemin_order(filter_mode, mipmaps, expected_min):
    settings = engine_meta.texture_settings({"filterMode": filter_mode, "mipmaps": mipmaps}, 64)

    assert settings["minFilter"] == expected_min
    assert settings["magFilter"] == (0 if filter_mode == "Point" else 1)


@pytest.mark.parametrize("wrap, expected", [("Repeat", 0), ("Clamp", 2), ("Mirror", 3), ("MirrorOnce", 2)])
def test_wrap_modes_follow_the_engines_texturewrap_order(wrap, expected):
    """TextureWrap is Repeat 0, ClampToBorder 1, ClampToEdge 2, MirroredRepeat 3, which is not the
    graphics-API order; Unity's Clamp must land on ClampToEdge (2), not on 1."""
    assert engine_meta.texture_settings({"wrapMode": wrap}, 64)["wrapMode"] == expected


def test_normal_maps_are_linear_and_keep_their_type():
    settings = engine_meta.texture_settings({"sRGB": False, "textureType": "NormalMap"}, 1024)

    assert settings["sRGB"] is False
    assert settings["textureType"] == 1


def test_a_texture_setting_without_an_engine_value_is_rejected():
    with pytest.raises(ValueError):
        engine_meta.texture_settings({"textureType": "Cookie"}, 64)


def test_model_settings_follow_unitys_rig_choices():
    settings = engine_meta.model_settings({
        "globalScale": 1, "useFileScale": True, "importAnimation": False, "animationType": "Generic",
        "avatarSetup": "CreateFromThisModel", "importNormals": "Import", "importTangents": "CalculateMikk",
        "skinWeights": "Standard", "isReadable": False, "bakeAxisConversion": False,
    })

    assert settings["animationType"] == 0            # Generic
    assert settings["avatarDefinition"] == 0        # create the avatar from this model
    assert settings["importAnimation"] is False
    assert settings["normalImportMode"] == 0        # import the authored normals
    assert settings["tangentImportMode"] == 3       # calculate (MikkTSpace)
    assert settings["calculateTangents"] is True
    assert settings["skinBoneCount"] == 4
    assert settings["convertFileUnits"] is True
    # Only integers and booleans: the meta stays plain JSON with no float formatting to get wrong.
    assert all(isinstance(value, (bool, int)) for value in settings.values())


def test_model_settings_default_to_no_avatar_and_no_animation():
    settings = engine_meta.model_settings(None)

    assert settings["avatarDefinition"] == 2
    assert settings["importAnimation"] is False


@pytest.mark.parametrize("field, value", [
    ("animationType", "Humanoid"), ("skinWeights", "Custom"), ("globalScale", 100.0), ("avatarSetup", "Bogus"),
])
def test_model_settings_this_import_does_not_handle_are_rejected(field, value):
    with pytest.raises(ValueError):
        engine_meta.model_settings({field: value})


def test_derived_maps_are_data_textures_and_only_emission_is_srgb():
    surface = engine_meta.surface_texture_settings(256)
    emission = engine_meta.emission_texture_settings(256)

    assert surface["sRGB"] is False and surface["textureType"] == 0
    assert emission["sRGB"] is True and emission["textureType"] == 0
    assert surface["maxTextureSize"] == 256


def test_write_meta_writes_the_layout_the_editor_reads(tmp_path):
    asset = tmp_path / "Tex.png"
    asset.write_bytes(b"\x89PNG\r\n")
    guid = engine_meta.stable_guid("unity-guid", "x")

    meta_path = engine_meta.write_meta(asset, guid, engine_meta.TEXTURE_IMPORTER, {"sRGB": False})

    assert meta_path == tmp_path / "Tex.png.meta"
    assert json.loads(meta_path.read_text(encoding="utf-8")) == {
        "guid": guid, "importer": "TextureImporter", "importerVersion": 7, "settings": {"sRGB": False},
    }
