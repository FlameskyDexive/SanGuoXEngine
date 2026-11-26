# -*- coding: utf-8 -*-
"""Engine `.meta` files for the assets an import copies in.

XEngine keeps import settings beside each asset in `<file>.meta`: a JSON document holding the asset
guid, the importer's name and version, and the importer's settings
(XEngine.Editor/AssetsDatabase/MetaFile.cs). Imports write the same layout the editor writes, with
values translated from the Unity importer settings that the export recorded, so the editor opens the
files with the intended settings rather than with defaults.

Import settings are expressed with the engine's own enum values, which do not all match common
graphics APIs (for example `TextureWrap` is Repeat 0, ClampToBorder 1, ClampToEdge 2,
MirroredRepeat 3: XEngine.Runtime/Graphics/Primitives.cs).
"""

from __future__ import annotations

import hashlib
import json
import uuid
from pathlib import Path

TEXTURE_IMPORTER = "TextureImporter"
MODEL_IMPORTER = "EditorModelImporter"

# Importer versions of XEngine 1.1.0-a19. Ask the live editor when the engine is upgraded:
#   XEngine.Editor.EditorRegistries.CreateImporterByName(name).Version
# A meta that carries a lower version than the running editor is upgraded on open, so these may lag
# behind; the C# generator reports when they do.
IMPORTER_VERSIONS = {
    TEXTURE_IMPORTER: 7,
    MODEL_IMPORTER: 52,
}

# TextureImportType values (XEngine.Editor/AssetsDatabase/Importers/TextureImporter.cs).
TEXTURE_TYPE = {
    "Default": 0,
    "NormalMap": 1,
    "Cursor": 7,
    "Sprite": 8,
    "SingleChannel": 10,
    "Shadowmask": 11,
    "Lightmap": 6,
    "DirectionalLightmap": 12,
}
ALPHA_SOURCE = {"None": 0, "FromInput": 1, "FromGrayScale": 2}
# TextureMin, by (mipmaps generated, Unity filterMode).
MIN_FILTER = {
    (False, "Point"): 0,      # Nearest
    (False, "Bilinear"): 1,   # Linear
    (False, "Trilinear"): 1,  # Linear
    (True, "Point"): 2,       # NearestMipmapNearest
    (True, "Bilinear"): 3,    # LinearMipmapNearest
    (True, "Trilinear"): 5,   # LinearMipmapLinear
}
# TextureMag, by Unity filterMode.
MAG_FILTER = {"Point": 0, "Bilinear": 1, "Trilinear": 1}
# TextureWrap, by Unity TextureWrapMode. MirrorOnce has no engine equivalent and clamps.
WRAP_MODE = {"Repeat": 0, "Clamp": 2, "Mirror": 3, "MirrorOnce": 2}

NORMAL_IMPORT_MODE = {"Import": 0, "Calculate": 1, "None": 2}
TANGENT_IMPORT_MODE = {"Import": 0, "None": 2, "CalculateMikk": 3, "CalculateLegacy": 3, "CalculateLegacyWithSplitTangents": 3}
ANIMATION_TYPE_GENERIC = 0   # engine: 0 Generic, 1 Humanoid
AVATAR_DEFINITION = {"CreateFromThisModel": 0, "CopyFromOther": 1, "NoAvatar": 2}


def stable_guid(*parts):
    """A deterministic GUID for a stable key, so re-running an import never forks references."""
    digest = hashlib.md5(("xengine:sanguo:" + ":".join(parts)).encode("utf-8")).digest()
    return str(uuid.UUID(bytes=digest))


def unity_guid(unity_guid_text):
    """The engine guid used for an asset that exists in the Unity project (its Unity guid keyed)."""
    return stable_guid("unity-guid", unity_guid_text)


def write_meta(asset_path, guid, importer, settings=None):
    """Writes `<asset>.meta` next to `asset_path` and returns its path."""
    meta = {"guid": guid, "importer": importer, "importerVersion": IMPORTER_VERSIONS[importer]}
    if settings is not None:
        meta["settings"] = settings
    meta_path = asset_path.with_name(asset_path.name + ".meta")
    meta_path.write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")
    return meta_path


def texture_settings(unity_importer, max_size):
    """Engine TextureImporter settings translated from the export's Unity TextureImporter record.

    `max_size` is the longest side of the image as Unity imported it (after its own NPOT and
    max-size rules), which the import has already reproduced, so the engine needs no scaling.
    """
    texture_type = unity_importer.get("textureType", "Default")
    alpha_source = unity_importer.get("alphaSource", "FromInput")
    filter_mode = unity_importer.get("filterMode", "Bilinear")
    wrap_mode = unity_importer.get("wrapMode", "Repeat")
    for name, value, table in (
        ("textureType", texture_type, TEXTURE_TYPE), ("alphaSource", alpha_source, ALPHA_SOURCE),
        ("filterMode", filter_mode, MAG_FILTER), ("wrapMode", wrap_mode, WRAP_MODE),
    ):
        if value not in table:
            raise ValueError(f"texture importer setting {name}={value!r} has no engine value")

    mipmaps = bool(unity_importer.get("mipmaps", True))
    return {
        "textureType": TEXTURE_TYPE[texture_type],
        "maxTextureSize": int(max_size),
        "generateMipmaps": mipmaps,
        "sRGB": bool(unity_importer.get("sRGB", True)),
        "sRGBSampling": False,
        "alphaSource": ALPHA_SOURCE[alpha_source],
        "minFilter": MIN_FILTER[(mipmaps, filter_mode)],
        "magFilter": MAG_FILTER[filter_mode],
        "wrapMode": WRAP_MODE[wrap_mode],
        "npotScale": 0,
    }


def _data_texture_settings(max_size):
    """Settings for a texture the import derives itself: linear data, mipmaps, repeat."""
    return {
        "textureType": TEXTURE_TYPE["Default"],
        "maxTextureSize": int(max_size),
        "generateMipmaps": True,
        "sRGB": False,
        "sRGBSampling": False,
        "alphaSource": ALPHA_SOURCE["None"],
        "minFilter": MIN_FILTER[(True, "Trilinear")],
        "magFilter": MAG_FILTER["Bilinear"],
        "wrapMode": WRAP_MODE["Repeat"],
        "npotScale": 0,
    }


def surface_texture_settings(max_size):
    """Settings for a derived `_SurfaceTex` (occlusion / roughness / metallic: plain data)."""
    return _data_texture_settings(max_size)


def emission_texture_settings(max_size):
    """Settings for a derived `_EmissionTex`: authored colour, so sRGB."""
    settings = _data_texture_settings(max_size)
    settings["sRGB"] = True
    return settings


def model_settings(unity_importer):
    """Engine EditorModelImporter settings translated from the export's Unity ModelImporter record.

    Unity's per-model choices that change geometry or rigging are carried over; everything else
    keeps the engine default (omitted from the meta)."""
    unity_importer = unity_importer or {}
    animation_type = unity_importer.get("animationType", "Generic")
    if animation_type not in ("Generic", "None"):
        # Only generic rigs are handled: a humanoid rig needs the engine's muscle-space avatar path.
        # "None" (no rig — the 桃园 scene props) imports as Generic too: the engine attaches an
        # Animator only when the model has clips, so a clip-less prop gets none.
        raise ValueError(f"unsupported Unity animationType for a character model: {animation_type!r}")
    avatar_setup = unity_importer.get("avatarSetup", "NoAvatar")
    if avatar_setup not in AVATAR_DEFINITION:
        raise ValueError(f"unsupported Unity avatarSetup: {avatar_setup!r}")
    skin_weights = unity_importer.get("skinWeights", "Standard")
    if skin_weights != "Standard":
        raise ValueError(f"unsupported Unity skinWeights: {skin_weights!r} (only 4 bones per vertex is handled)")

    settings = {
        "normalImportMode": NORMAL_IMPORT_MODE[unity_importer.get("importNormals", "Import")],
        "tangentImportMode": TANGENT_IMPORT_MODE[unity_importer.get("importTangents", "CalculateMikk")],
        "calculateTangents": unity_importer.get("importTangents", "CalculateMikk") == "CalculateMikk",
        "convertFileUnits": bool(unity_importer.get("useFileScale", True)),
        "bakeAxisConversion": bool(unity_importer.get("bakeAxisConversion", False)),
        "importAnimation": bool(unity_importer.get("importAnimation", False)),
        "animationType": ANIMATION_TYPE_GENERIC,
        "avatarDefinition": AVATAR_DEFINITION[avatar_setup],
        "generateLightmapUVs": bool(unity_importer.get("generateSecondaryUV", False)),
        "skinBoneCount": 4,
        "stripBones": True,
        "isReadable": bool(unity_importer.get("isReadable", False)),
    }
    unit_scale = float(unity_importer.get("globalScale", 1.0))
    if unit_scale != 1.0:
        raise ValueError(f"Unity globalScale {unit_scale} is not handled (the heroes import at 1)")
    return settings
