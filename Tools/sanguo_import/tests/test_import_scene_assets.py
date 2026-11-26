# -*- coding: utf-8 -*-
"""End-to-end tests for Tools/sanguo_import/import_scene_assets.py on a synthetic mini project."""

import json

import numpy as np
import pytest
from PIL import Image

from sanguo_import import engine_meta, material_mapping, texture_math
from sanguo_import.import_scene_assets import SceneAssetImport

FBX = "Assets/GameLevels/Raw/Fbx/b_zx_shitai04_01.FBX"
MESH_KEY = FBX + "|b_zx_shitai04_01"
MAT = "Assets/GameLevels/Raw/Materials/b_zx_shitai04_01.mat"
SEQ_MAT = "Assets/GameLevels/Raw/Materials/i_luoyang_xiaobingyuanjing_2.mat"
BB_MAT = "Assets/GameLevels/Raw/BillBoard/shu01/p_com_zhuzi01/p_com_zhuzi01_BillBoardMat.mat"
ALBEDO, NORMAL, PACKED, STRIP = "a" * 32, "b" * 32, "c" * 32, "d" * 32
SCENE_SHADER = "Standards/S_Standard_Scene"
SIZE = 4
RNG = np.random.default_rng(2027)


def _write_tga(path, array):
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(array).save(path, format="TGA")


def _texture(guid, file_name, importer=None, width=SIZE, height=SIZE):
    doc = {"name": file_name.rsplit(".", 1)[0], "path": f"Assets/GameLevels/Raw/Textures/{file_name}",
           "width": width, "height": height, "format": "DXT5"}
    if importer is not None:
        doc["importer"] = importer
    return doc


def _color_importer(**overrides):
    settings = {"sRGB": True, "textureType": "Default", "alphaSource": "FromInput", "mipmaps": True,
                "filterMode": "Bilinear", "wrapMode": "Repeat", "maxTextureSize": 2048,
                "npotScale": "ToNearest", "compression": "Compressed"}
    settings.update(overrides)
    return settings


def _scene_shader(guids):
    """A S_Standard_Scene material with the three texture slots bound."""
    properties = {
        "_MainTex": {"type": "TexEnv", "texture": guids["albedo"], "texturePath": "x.tga",
                     "scale": [1.0, 1.0], "offset": [0.0, 0.0]},
        "_Cutoff": {"type": "Range", "value": 0.5},
    }
    if guids.get("normal"):
        properties["_BumpMap"] = {"type": "TexEnv", "texture": guids["normal"], "texturePath": "n.tga",
                                  "scale": [1.0, 1.0], "offset": [0.0, 0.0]}
    if guids.get("packed"):
        properties["_MetallicGlossMap"] = {"type": "TexEnv", "texture": guids["packed"], "texturePath": "m.tga",
                                           "scale": [1.0, 1.0], "offset": [0.0, 0.0]}
    return {"name": "b_zx_shitai04_01", "path": MAT, "guid": "m" * 32, "shader": SCENE_SHADER,
            "renderQueue": 2001, "keywords": ["_NORMALMAP"] if guids.get("normal") else [],
            "properties": properties}


@pytest.fixture
def project(tmp_path):
    project = tmp_path / "unity"
    albedo = RNG.integers(0, 256, (SIZE, SIZE, 3), dtype=np.uint8)
    normals = RNG.normal(size=(SIZE, SIZE, 3))
    normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
    packed = RNG.integers(0, 256, (SIZE, SIZE, 4), dtype=np.uint8)
    strip = RNG.integers(0, 256, (SIZE * 5, SIZE * 5, 4), dtype=np.uint8)   # 5×5 flipbook
    _write_tga(project / "Assets/GameLevels/Raw/Textures/b_zx_shitai04_01_d.tga", albedo)
    _write_tga(project / "Assets/GameLevels/Raw/Textures/b_zx_shitai04_01_n.tga",
               np.rint(normals * 127.5 + 127.5).astype(np.uint8))
    _write_tga(project / "Assets/GameLevels/Raw/Textures/b_zx_shitai04_01_m.tga", packed)
    _write_tga(project / "Assets/GameLevels/Raw/Textures/Tex_luoyang_xiaobingyuanjing_2_Body_D.png", strip)
    (project / FBX).parent.mkdir(parents=True, exist_ok=True)
    (project / FBX).write_bytes(b"not-a-real-fbx")

    # The scene exporter writes material properties as {name: {type, texture/value, ...}}.
    scene_material = _scene_shader({"albedo": ALBEDO, "normal": NORMAL, "packed": PACKED})
    scene_material["properties"]["_MainTex"]["texturePath"] = \
        "Assets/GameLevels/Raw/Textures/b_zx_shitai04_01_d.tga"

    def record(properties):
        return {"name": "record", "guid": "r" * 32, "shader": SCENE_SHADER, "renderQueue": 2001,
                "keywords": [], "properties": properties}

    export = {
        "meshes": {MESH_KEY: {"file": FBX, "guid": "f" * 32, "meshName": "b_zx_shitai04_01",
                              "vertexCount": 12, "hasUV2": True, "subMeshCount": 1,
                              "bounds": {"center": [0, 0, 0], "extents": [0.5, 0.5, 0.5]}}},
        "models": {FBX: {"guid": "f" * 32, "importer": {
            "globalScale": 1, "useFileScale": True, "importAnimation": False, "animationType": "None",
            "importNormals": "Import", "importTangents": "CalculateMikk",
            "isReadable": False, "weldVertices": True}}},
        "materials": {
            MAT: scene_material,
            SEQ_MAT: {"name": "i_luoyang_xiaobingyuanjing_2", "guid": "s" * 32,
                      "shader": "Standards/S_Standard_Sequence", "renderQueue": 3001, "keywords": [],
                      "properties": {
                          "_MainTex": {"type": "TexEnv", "texture": STRIP, "texturePath": "strip.png",
                                       "scale": [1.0, 1.0], "offset": [0.0, 0.0]},
                          "_Width": {"type": "Float", "value": 5.0},
                          "_Height": {"type": "Float", "value": 5.0},
                          "_EndIndex": {"type": "Float", "value": 24.0},
                      }},
            BB_MAT: {"name": "p_com_zhuzi01_BillBoardMat", "guid": "b" * 32,
                     "shader": "Unlit/BillBoard", "renderQueue": 2450, "keywords": [],
                     "properties": {
                         "_MainTex": {"type": "TexEnv", "texture": ALBEDO, "texturePath": "bb.png",
                                      "scale": [1.0, 1.0], "offset": [0.0, 0.0]},
                         "_CutOff": {"type": "Float", "value": 0.4},
                     }},
        },
        "textures": {
            ALBEDO: _texture(ALBEDO, "b_zx_shitai04_01_d.tga", _color_importer()),
            NORMAL: _texture(NORMAL, "b_zx_shitai04_01_n.tga", _color_importer(sRGB=False, textureType="NormalMap")),
            PACKED: _texture(PACKED, "b_zx_shitai04_01_m.tga", _color_importer(sRGB=False)),
            STRIP: _texture(STRIP, "Tex_luoyang_xiaobingyuanjing_2_Body_D.png", _color_importer(), SIZE * 5, SIZE * 5),
        },
    }
    scene = {
        "nodes": [
            {"path": "Root/StaticObject/b_zx_shitai04_01", "active": True, "layer": 0,
             "localPosition": [0, 0, 0], "localRotation": [0, 0, 0, 1], "localScale": [1, 1, 1],
             "mesh": {"path": FBX, "name": "b_zx_shitai04_01"},
             "materials": [{"path": MAT, "name": "b_zx_shitai04_01", "shader": SCENE_SHADER}]},
            {"path": "Root/Hid_lod1/i_luoyang_xiaobingyuanjing_1", "active": True, "layer": 0,
             "localPosition": [1, 0, 0], "localRotation": [0, 0, 0, 1], "localScale": [1, 1, 1],
             "mesh": {"path": FBX, "name": "b_zx_shitai04_01"},
             "materials": [{"path": SEQ_MAT, "name": "i_luoyang_xiaobingyuanjing_2",
                            "shader": "Standards/S_Standard_Sequence"}]},
            {"path": "Root/StaticObject/plant/p_com_zhuzi01_BillBoard", "active": True, "layer": 0,
             "localPosition": [2, 0, 0], "localRotation": [0, 0, 0, 1], "localScale": [1, 1, 1],
             "mesh": {"path": "Library/unity default resources", "name": "Quad"},
             "materials": [{"path": BB_MAT, "name": "p_com_zhuzi01_BillBoardMat",
                            "shader": "Unlit/BillBoard"}]},
        ],
        "lightmaps": [{"color": "Assets/.../Lightmap-0_comp_light.exr", "dir": "d.png", "shadowMask": "s.png"}],
        "renderSettings": {"customReflection": "Assets/.../Tex_hdr_the_sky_dusk.hdr", "sun": "Root/Directional Light"},
        "lodBias": 0.4,
    }
    return tmp_path, project, export, scene, {"albedo": albedo, "strip": strip}


def _run(tmp_path, unity, export, scene):
    xengine = tmp_path / "xengine"
    importer = SceneAssetImport(scene, export, unity, xengine / "Assets", "Scene").run()
    return xengine / "Assets" / "Scene", importer


def _png(path):
    with Image.open(path) as image:
        return np.asarray(image.convert("RGBA"), dtype=np.uint8)


def _meta(path):
    return json.loads((path.parent / (path.name + ".meta")).read_text(encoding="utf-8"))


def test_import_writes_models_textures_and_the_manifest(project):
    tmp_path, unity, export, scene, images = project
    scene_root, importer = _run(tmp_path, unity, export, scene)

    fbx = scene_root / "Fbx" / "GameLevels/Raw/Fbx/b_zx_shitai04_01.FBX"
    assert fbx.read_bytes() == b"not-a-real-fbx"
    meta = _meta(fbx)
    assert meta["importer"] == "EditorModelImporter"
    assert meta["guid"] == engine_meta.unity_guid("f" * 32)
    assert meta["settings"]["animationType"] == 0          # Unity "None" → engine Generic

    albedo = scene_root / "Textures" / "GameLevels/Raw/Textures/b_zx_shitai04_01_d.png"
    assert (_png(albedo)[:, :, :3] == images["albedo"]).all()

    manifest = importer.manifest
    assert manifest["meshes"][MESH_KEY]["fbx"] == "Scene/Fbx/GameLevels/Raw/Fbx/b_zx_shitai04_01.FBX"
    assert manifest["meshes"][MESH_KEY]["meshName"] == "b_zx_shitai04_01"
    material = manifest["materials"][MAT]
    assert material["shader"] == "Standard"
    assert material["properties"]["_MainTex"] == manifest["textures"][ALBEDO]["enginePath"]
    assert material["properties"]["_NormalTex"] == manifest["textures"][NORMAL]["enginePath"]
    assert "_SFX" in material["properties"]["_SurfaceTex"]     # the packed map is repacked


def test_sequence_material_becomes_alpha_clipped_first_frame(project):
    tmp_path, unity, export, scene, images = project
    scene_root, importer = _run(tmp_path, unity, export, scene)

    material = importer.manifest["materials"][SEQ_MAT]
    crop = material["properties"]["_MainTex"]
    assert "_SEQ0" in crop
    frame = _png(scene_root.parent / crop)          # scene_root is Assets/Scene
    assert frame.shape == (SIZE, SIZE, 4)
    # Frame 0 is the bottom-left cell of the strip: its last-row band and first-column band.
    assert (frame[:, :, :3] == images["strip"][SIZE * 4:, 0:SIZE, :3]).all()
    assert material["properties"]["_AlphaClip"] == 1.0


def test_billboard_material_is_two_sided_and_alpha_clipped(project):
    tmp_path, unity, export, scene, _ = project
    _, importer = _run(tmp_path, unity, export, scene)

    material = importer.manifest["materials"][BB_MAT]
    assert material["properties"]["_Cull"] == 0
    assert material["properties"]["_AlphaClip"] == 1.0
    assert material["properties"]["_AlphaCutoff"] == pytest.approx(0.4)


def test_lightmaps_and_the_sky_are_recorded_for_the_builder(project):
    tmp_path, unity, export, scene, _ = project
    _, importer = _run(tmp_path, unity, export, scene)

    assert importer.manifest["lightmaps"] == [{"index": 0, "color": scene["lightmaps"][0]["color"],
                                               "dir": "d.png", "shadowMask": "s.png"}]
    assert importer.manifest["sky"]["customReflection"] == scene["renderSettings"]["customReflection"]
    assert importer.manifest["sky"]["lodBias"] == 0.4


def test_scene_material_mapping_degrades_with_notes():
    mapped = material_mapping.map_scene_material({
        "name": "Mat_t_taoyuan01_terrain", "shader": "Standards/S_Standard_TerrainAtla",
        "keywords": [], "properties": {
            "_MainTex": {"type": "TexEnv", "texture": "t" * 32, "scale": [1, 1], "offset": [0, 0]},
            "_IndexTex": {"type": "TexEnv", "texture": "i" * 32, "scale": [1, 1], "offset": [0, 0]},
        }})
    assert mapped["shader"] == "Standard"
    assert mapped["properties"]["_MainTex"] == "t" * 32
    assert "_IndexTex" not in mapped["properties"]
    assert any("splat" in note for note in mapped["notes"])

    with pytest.raises(ValueError, match="no mapping"):
        material_mapping.map_scene_material({"name": "fx", "shader": "Effects/S_Cj_Fire_01",
                                             "keywords": [], "properties": {}})


def test_grass_alpha_clip_comes_from_its_keyword():
    mapped = material_mapping.map_scene_material({
        "name": "p_zx_grass01_02", "shader": "Standards/S_Standard_Scene_Grass",
        "keywords": ["_ALPHATEST_ON"],
        "properties": {
            "_MainTex": {"type": "TexEnv", "texture": "g" * 32, "scale": [1, 1], "offset": [0, 0]},
            "_Cutoff": {"type": "Range", "value": 0.25},
            "_EmissionColor": {"type": "Color", "value": [0.972549, 1, 0.8235295, 1]},
        }})
    assert mapped["properties"]["_AlphaClip"] == 1.0
    assert mapped["properties"]["_AlphaCutoff"] == pytest.approx(0.25)
    # The flat emission colour has no scale behind it in jws2, so it stays off here too.
    assert mapped["emission"] is None
    assert any("wind" in note for note in mapped["notes"])
