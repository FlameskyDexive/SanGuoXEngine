# -*- coding: utf-8 -*-
"""End-to-end tests for Tools/sanguo_import/import_heroes.py on a synthetic mini "Unity project"."""

import json

import numpy as np
import pytest
from PIL import Image

from sanguo_import import engine_meta, texture_math
from sanguo_import.import_heroes import HeroImport, load_as_unity_imported

HERO_DIR = "Assets/Actor/Hero/Hero_Test"
MAT_DIR = HERO_DIR + "/BattleMat"
FBX = HERO_DIR + "/Hero_Test.fbx"
MAT = MAT_DIR + "/Hero_Test_Body.mat"
ALBEDO, NORMAL, PACKED = "a" * 32, "b" * 32, "c" * 32
RAMP = "d" * 32   # bound by the material, but Default/Standard has no slot for it
HERO_SHADER = "Standards/S_Standard_Hero"
COLOR, SCALE = [1.0, 0.5082709, 0.0, 1.0], 3.8

SIZE = 4
RNG = np.random.default_rng(2026)


def _write_tga(path, array):
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(array).save(path, format="TGA")


def _texture(name, file_name, importer):
    return {"name": name, "path": f"{MAT_DIR}/{file_name}", "width": SIZE, "height": SIZE,
            "format": "DXT5", "importer": importer}


def _color_importer(**overrides):
    settings = {"sRGB": True, "textureType": "Default", "alphaSource": "FromInput", "mipmaps": True,
                "filterMode": "Bilinear", "wrapMode": "Repeat", "maxTextureSize": 2048,
                "npotScale": "ToNearest", "compression": "Compressed"}
    settings.update(overrides)
    return settings


def _texenv(guid):
    return {"texture": guid, "scale": [1.0, 1.0], "offset": [0.0, 0.0]}


@pytest.fixture
def project(tmp_path):
    """A Unity project folder with three textures and an FBX, plus the matching export document."""
    project = tmp_path / "unity"
    albedo = RNG.integers(0, 256, (SIZE, SIZE // 2, 3), dtype=np.uint8)      # not square: Unity scales it
    normals = RNG.normal(size=(SIZE, SIZE, 3))
    normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
    packed = np.dstack([
        RNG.integers(0, 256, (SIZE, SIZE)),                        # metallic
        RNG.integers(0, 256, (SIZE, SIZE)),                        # occlusion
        RNG.integers(0, 256, (SIZE, SIZE)),                        # emission mask
        RNG.integers(0, 256, (SIZE, SIZE)),                        # smoothness
    ]).astype(np.uint8)
    _write_tga(project / MAT_DIR / "Tex_Test_Body_D.tga", albedo)
    _write_tga(project / MAT_DIR / "Tex_Test_Body_N_NRM.tga", np.rint(normals * 127.5 + 127.5).astype(np.uint8))
    _write_tga(project / MAT_DIR / "Tex_Test_Body_M_Alpha.tga", packed)
    (project / HERO_DIR).mkdir(parents=True, exist_ok=True)
    (project / FBX).write_bytes(b"not-a-real-fbx")

    def material(name, emission=True):
        properties = [
            {"name": "_MainTex", "type": "TexEnv", "value": _texenv(ALBEDO)},
            {"name": "_MetallicGlossMap", "type": "TexEnv", "value": _texenv(PACKED)},
            {"name": "_BumpMap", "type": "TexEnv", "value": _texenv(NORMAL)},
            {"name": "_Color", "type": "Color", "value": [1, 1, 1, 1]},
            {"name": "_Cutoff", "type": "Range", "value": 0.5},
            {"name": "_SSSLUT", "type": "TexEnv", "value": _texenv(RAMP)},
        ]
        if emission:
            properties += [
                {"name": "_EmissionColor", "type": "Color", "value": COLOR},
                {"name": "_EmissionScale", "type": "Range", "value": SCALE},
            ]
        return {"name": name, "path": f"{MAT_DIR}/{name}.mat", "guid": name, "shader": HERO_SHADER,
                "keywords": [], "properties": properties}

    export = {
        "heroes": [{
            "name": "Hero_Test", "found": True, "prefabGuid": "p" * 32, "meshRendererCount": 0,
            "rootSpaceBounds": {"min": [0, 0, 0], "max": [1, 2, 1]},
            "animator": {"controller": {"path": HERO_DIR + "/Hero_Test.controller", "guid": "q" * 32}},
            "skinnedMeshes": [{
                "node": "Hero_Test/Hero_Test_Body",
                "mesh": {"name": "Hero_Test_Body", "path": FBX},
                "materials": [{"path": MAT, "name": "Hero_Test_Body", "shader": HERO_SHADER, "usedBySubMesh": True}],
            }],
        }],
        "models": {FBX: {"guid": "f" * 32, "importer": {
            "globalScale": 1, "useFileScale": True, "importAnimation": False, "animationType": "Generic",
            "avatarSetup": "CreateFromThisModel", "importNormals": "Import", "importTangents": "CalculateMikk",
            "skinWeights": "Standard", "isReadable": False, "bakeAxisConversion": False}}},
        "materials": {MAT: material("Hero_Test_Body")},
        "textures": {
            ALBEDO: _texture("Tex_Test_Body_D", "Tex_Test_Body_D.tga", _color_importer()),
            NORMAL: _texture("Tex_Test_Body_N_NRM", "Tex_Test_Body_N_NRM.tga",
                             _color_importer(sRGB=False, textureType="NormalMap")),
            PACKED: _texture("Tex_Test_Body_M_Alpha", "Tex_Test_Body_M_Alpha.tga", _color_importer(sRGB=False)),
            RAMP: _texture("Tex_Test_Body_SSSLUT", "Tex_Test_Body_SSSLUT.tga", _color_importer()),
        },
    }
    return tmp_path, project, export, {"albedo": albedo, "packed": packed}


def _run(tmp_path, unity, export):
    xengine = tmp_path / "xengine"
    importer = HeroImport(export, unity, xengine / "Assets", "Heroes").run()
    return xengine / "Assets" / "Heroes", importer


def _png(path):
    with Image.open(path) as image:
        return np.asarray(image.convert("RGB"), dtype=np.uint8)


def _meta(path):
    return json.loads((path.parent / (path.name + ".meta")).read_text(encoding="utf-8"))


def test_import_writes_models_textures_and_the_manifest(project):
    tmp_path, unity, export, images = project
    heroes, importer = _run(tmp_path, unity, export)

    # Model: copied as-is, with the engine's import settings translated from Unity's.
    model = heroes / "Hero_Test" / "Hero_Test.fbx"
    assert model.read_bytes() == b"not-a-real-fbx"
    model_meta = _meta(model)
    assert model_meta["importer"] == "EditorModelImporter"
    assert model_meta["guid"] == engine_meta.unity_guid("f" * 32)
    assert model_meta["settings"]["animationType"] == 0
    assert model_meta["settings"]["avatarDefinition"] == 0

    # Albedo: brought to the size Unity imported it at, stored as sRGB colour.
    albedo = heroes / "Textures" / "Tex_Test_Body_D.png"
    assert Image.open(albedo).size == (SIZE, SIZE)
    assert _meta(albedo)["settings"]["sRGB"] is True
    assert _meta(albedo)["guid"] == engine_meta.unity_guid(ALBEDO)

    # Normal map: linear data, and the resize left the normals unit length.
    normal = heroes / "Textures" / "Tex_Test_Body_N_NRM.png"
    normal_meta = _meta(normal)["settings"]
    assert (normal_meta["sRGB"], normal_meta["textureType"]) == (False, 1)
    lengths = np.linalg.norm(_png(normal) / 127.5 - 1.0, axis=-1)
    assert lengths.min() > 0.95

    # Surface map: each jws2 channel in its engine slot, byte-exact for this power-of-two source.
    surface = _png(heroes / "Textures" / "Tex_Test_Body_SFX.png")
    packed = images["packed"]
    assert np.array_equal(surface[..., 0], 255 - packed[..., 1])   # R = 1 - occlusion
    assert np.array_equal(surface[..., 1], 255 - packed[..., 3])   # G = 1 - smoothness
    assert np.array_equal(surface[..., 2], packed[..., 0])         # B = metallic
    surface_meta = _meta(heroes / "Textures" / "Tex_Test_Body_SFX.png")["settings"]
    assert surface_meta["sRGB"] is False

    # Emission map: an sRGB map that, decoded and scaled by the intensity, is Unity's emission.
    emission = heroes / "Textures" / "Tex_Test_Body_EMI.png"
    assert _meta(emission)["settings"]["sRGB"] is True
    recipe = texture_math.emission_recipe(COLOR, SCALE)
    decoded = texture_math.srgb_to_linear(_png(emission) / 255.0) * recipe["intensity"]
    expected = (packed[..., 2:3] / 255.0) * texture_math.srgb_to_linear(COLOR[:3]) * SCALE
    assert np.allclose(decoded, expected, atol=0.02)

    # Manifest: what the C# generators read.
    mapped = importer.manifest["materials"][MAT]
    assert mapped["shader"] == "Standard"
    assert mapped["properties"]["_MainTex"] == "Heroes/Textures/Tex_Test_Body_D.png"
    assert mapped["properties"]["_NormalTex"] == "Heroes/Textures/Tex_Test_Body_N_NRM.png"
    assert mapped["properties"]["_SurfaceTex"] == "Heroes/Textures/Tex_Test_Body_SFX.png"
    assert mapped["properties"]["_EmissionTex"] == "Heroes/Textures/Tex_Test_Body_EMI.png"
    assert mapped["enginePath"] == "Heroes/Materials/Hero_Test_Body.mat"
    hero = importer.manifest["heroes"][0]
    assert hero["prefabPath"] == "Heroes/Prefabs/Hero_Test.prefab"
    assert hero["model"] == "Heroes/Hero_Test/Hero_Test.fbx"
    assert hero["skinnedMeshes"] == [{"node": "Hero_Test_Body", "mesh": "Hero_Test_Body",
                                      "materials": ["Heroes/Materials/Hero_Test_Body.mat"]}]
    assert hero["unityRootSpaceBounds"] == {"min": [0, 0, 0], "max": [1, 2, 1]}
    assert importer.report["texturesResizedToUnitySize"] == 1


def test_textures_without_an_engine_slot_are_not_imported(project):
    tmp_path, unity, export, _ = project
    (unity / MAT_DIR / "Tex_Test_Body_SSSLUT.tga").write_bytes(b"not even an image")

    heroes, importer = _run(tmp_path, unity, export)

    assert not (heroes / "Textures" / "Tex_Test_Body_SSSLUT.png").exists()
    assert importer.report["textures"] == 3
    assert importer.report["texturesSkippedUnused"] == 1


def test_running_the_import_again_changes_nothing(project):
    tmp_path, unity, export, _ = project
    heroes, first = _run(tmp_path, unity, export)
    snapshot = {p.relative_to(heroes).as_posix(): p.read_bytes() for p in heroes.rglob("*") if p.is_file()}
    assert len(snapshot) == 12   # 1 fbx + 5 png, each with its meta, and nothing else

    _, second = _run(tmp_path, unity, export)

    assert second.manifest == first.manifest
    assert {p.relative_to(heroes).as_posix(): p.read_bytes() for p in heroes.rglob("*") if p.is_file()} == snapshot


def _copy_material(export, source_path, new_name):
    copy = json.loads(json.dumps(export["materials"][source_path]))
    copy["name"] = new_name
    copy["path"] = f"{MAT_DIR}/{new_name}.mat"
    export["materials"][copy["path"]] = copy
    return copy


def test_materials_sharing_a_mask_and_a_recipe_share_the_derived_maps(project):
    tmp_path, unity, export, _ = project
    _copy_material(export, MAT, "Hero_Test_Leg")
    # A third material emits differently from the same mask: it gets its own emission map.
    cloth = _copy_material(export, MAT, "Hero_Test_Cloth")
    cloth["properties"][-2]["value"] = [0.0, 1.0, 0.0, 1.0]
    smr = export["heroes"][0]["skinnedMeshes"][0]
    for name in ("Hero_Test_Leg", "Hero_Test_Cloth"):
        smr["materials"].append({"path": f"{MAT_DIR}/{name}.mat", "name": name, "shader": HERO_SHADER, "usedBySubMesh": True})

    heroes, importer = _run(tmp_path, unity, export)

    textures = sorted(p.name for p in (heroes / "Textures").glob("*.png"))
    assert textures == [
        "Tex_Test_Body_D.png", "Tex_Test_Body_EMI.png", "Tex_Test_Body_EMI_2.png",
        "Tex_Test_Body_M_Alpha.png", "Tex_Test_Body_N_NRM.png", "Tex_Test_Body_SFX.png",
    ]
    paths = {name: importer.manifest["materials"][f"{MAT_DIR}/{name}.mat"]["properties"]
             for name in ("Hero_Test_Body", "Hero_Test_Leg", "Hero_Test_Cloth")}
    assert paths["Hero_Test_Leg"]["_EmissionTex"] == paths["Hero_Test_Body"]["_EmissionTex"] == "Heroes/Textures/Tex_Test_Body_EMI.png"
    assert paths["Hero_Test_Cloth"]["_EmissionTex"] == "Heroes/Textures/Tex_Test_Body_EMI_2.png"
    assert {p["_SurfaceTex"] for p in paths.values()} == {"Heroes/Textures/Tex_Test_Body_SFX.png"}


def test_a_packed_texture_without_an_alpha_channel_is_an_error(project):
    tmp_path, unity, export, _ = project
    source = unity / MAT_DIR / "Tex_Test_Body_M_Alpha.tga"
    with Image.open(source) as image:
        Image.fromarray(np.asarray(image.convert("RGB"))).save(source, format="TGA")

    with pytest.raises(ValueError, match="no alpha channel"):
        _run(tmp_path, unity, export)


def test_a_plain_mesh_renderer_on_a_hero_is_an_error(project):
    tmp_path, unity, export, _ = project
    export["heroes"][0]["meshRendererCount"] = 2

    with pytest.raises(ValueError, match="plain MeshRenderer"):
        _run(tmp_path, unity, export)


def test_load_as_unity_imported_resizes_to_unitys_size(tmp_path):
    source = tmp_path / "NPOT.tga"
    _write_tga(source, RNG.integers(0, 256, (6, 10, 3), dtype=np.uint8))

    array, resized = load_as_unity_imported(source, {"width": 8, "height": 8, "importer": {"textureType": "Default"}})

    assert resized is True
    assert array.shape == (8, 8, 3)
    _, not_resized = load_as_unity_imported(source, {"width": 10, "height": 6, "importer": {}})
    assert not_resized is False
