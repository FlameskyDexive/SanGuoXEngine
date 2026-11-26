# -*- coding: utf-8 -*-
"""Unity material to XEngine `Default/Standard` / `Default/StandardAnisotropic` property set.

Input is one material record as `Tools/unity_export/scripts/export_heroes.csx` writes it. The
result is data only: which engine shader, which engine properties with which values (textures named
by their Unity guid; the import resolves them to files), and an emission description. Texture
properties that Unity samples but XEngine's Standard has no slot for are listed in `notes`, so the
differences are recorded rather than silently dropped.

Formulas and why they hold are in `docs/evidence/m1/heroes.md`:
  _SurfaceTex  R = 1 - AO(_M_Alpha.G)   G = 1 - smoothness(_M_Alpha.A)   B = metallic(_M_Alpha.R)
  _MainColor   linear(_Color)              XEngine colours are linear by default, Unity's are sRGB-authored
  emission     linear(_EmissionColor) * _EmissionScale * mask  (see texture_math.emission_recipe)
"""

from __future__ import annotations

from sanguo_import import texture_math

# jws2 shader -> XEngine built-in shader that stands in for it.
SHADER_MAP = {
    "Standards/S_Standard_Hero": "Standard",
    "Standards/S_Standard_Skin": "Standard",
    "Standards/S_Standard_Monster": "Standard",
    "Standards/S_Standard_Hair": "StandardAnisotropic",
}

_WHITE = (1.0, 1.0, 1.0)


def _properties_by_name(material):
    """{name: value} out of either property shape: export_heroes.csx writes a list of
    {name, type, value} records, export_scene_assets.csx a {name: {type, value}} object."""
    properties = material.get("properties", {})
    if isinstance(properties, dict):
        # Scalar records carry "value"; TexEnv records are {type, texture, texturePath, scale,
        # offset} — the same keys the hero export puts inside "value", so pass the record itself.
        return {name: record.get("value", record) if isinstance(record, dict) else record
                for name, record in properties.items() if record is not None}
    return {p["name"]: p["value"] for p in properties if p.get("value") is not None}


def _texture_guid(props, name):
    """The Unity guid bound to a texture property, or None when no texture is bound."""
    value = props.get(name)
    return value.get("texture") if value else None


def _is_white(color):
    return all(abs(component - 1.0) < 1e-5 for component in color[:3])


def _enabled(props, keywords, toggle_name, keyword):
    return bool(props.get(toggle_name, 0.0)) or keyword in keywords


def map_material(material):
    """Maps one exported Unity material record; see the module docstring for the result's shape."""
    shader_name = material.get("shader")
    props = _properties_by_name(material)
    keywords = set(material.get("keywords", []))
    notes = []

    engine_shader = SHADER_MAP.get(shader_name)
    if engine_shader is None:
        engine_shader = "Standard"
        notes.append(f"shader {shader_name!r} has no mapping; Default/Standard stands in")

    properties = {}

    base = props.get("_MainTex")
    if base:
        if base.get("texture"):
            properties["_MainTex"] = base["texture"]
        if tuple(base.get("scale", (1.0, 1.0))) != (1.0, 1.0):
            properties["_Tiling"] = list(base["scale"])
        if tuple(base.get("offset", (0.0, 0.0))) != (0.0, 0.0):
            properties["_Offset"] = list(base["offset"])

    color = props.get("_Color")
    if color is not None and not _is_white(color):
        linear = texture_math.srgb_to_linear(color[:3])
        properties["_MainColor"] = [float(linear[0]), float(linear[1]), float(linear[2]), float(color[3])]

    normal = _texture_guid(props, "_BumpMap")
    if normal:
        properties["_NormalTex"] = normal

    surface = _texture_guid(props, "_MetallicGlossMap")
    if surface:
        properties["_SurfaceTex"] = surface
    else:
        # Every jws2 character shader samples _MetallicGlossMap unconditionally; unbound, Unity
        # hands it the "white" default: metallic 1, smoothness 1, no occlusion.
        notes.append("no _MetallicGlossMap bound: Unity samples the white default (fully metallic, fully smooth)")
        properties["_SurfaceTex"] = None

    # jws2's character shaders only cut holes when _ALPHATEST_ON is set; the hair shader always does.
    alpha_test = "_ALPHATEST_ON" in keywords or engine_shader == "StandardAnisotropic"
    if alpha_test:
        properties["_AlphaCutoff"] = float(props.get("_Cutoff", 0.5))
        if engine_shader == "Standard":
            properties["_AlphaClip"] = 1.0
    elif engine_shader == "Standard":
        # Default/Standard clips below the alpha cutoff unless told not to.
        properties["_AlphaClip"] = 0.0

    if "_Cull" in props:
        properties["_Cull"] = int(props["_Cull"])
    elif engine_shader == "StandardAnisotropic":
        # The hair shader draws its cards from both sides (an opaque front-face pass, then
        # transparent back-face and front-face passes). One two-sided opaque pass stands in for
        # all three; with the engine's default (back faces culled) every card seen from behind
        # vanished — 吕布's left plume disappeared in most poses.
        properties["_Cull"] = 0

    emission = None
    recipe = texture_math.emission_recipe(props.get("_EmissionColor", (0.0, 0.0, 0.0, 1.0)), props.get("_EmissionScale", 0.0))
    if recipe is not None:
        emission = {"maskTexture": surface, "recipe": recipe}
        properties["_EmissionIntensity"] = recipe["intensity"]
        notes.append("emission pulse replaced by its mean")

    if shader_name == "Standards/S_Standard_Skin":
        notes.append(f"fake-skin shading is not reproduced (_SSSLUT ramp, _SkinLightScale {props.get('_SkinLightScale', 1.0)})")
    if shader_name == "Standards/S_Standard_Hair":
        notes.append("strand specular replaced by GGX anisotropy; the transparent front-face and back-face "
                     "passes are not reproduced (one two-sided alpha-tested opaque pass, cull off)")
    if _enabled(props, keywords, "_RimLight", "_RIMLIGHT_ON"):
        notes.append("rim light is not reproduced")
    if _enabled(props, keywords, "_EffectToggle", "_EFFECTTOGGLE_ON"):
        notes.append("flow-light layer is not reproduced")
    if float(props.get("_PassColor", 0.0) or 0.0) != 0.0:
        notes.append("dye recolouring is not reproduced")
    if _texture_guid(props, "_Unity_SpecCube"):
        notes.append("per-material reflection cubemap replaced by the scene's image-based lighting")

    return {"shader": engine_shader, "properties": properties, "emission": emission, "notes": notes}


# jws2 桃园 scene shader -> XEngine built-in shader that stands in for it. The Scene family shares
# the hero shaders' texture slots (_MainTex / _BumpMap / _MetallicGlossMap / _EmissionMap), so the
# same property mapping applies; wind, wetness and snow layers are recorded as degraded.
SCENE_SHADER_MAP = {
    "Standards/S_Standard_Scene": "Standard",
    "Standards/S_Standard_Scene_Lod1": "Standard",
    "Standards/S_Standard_Scene_Lod2": "Standard",
    "Standards/S_Standard_Scene_Monster": "Standard",
    # A flipbook of distant soldiers (598 instances): the port shows the first frame, static.
    "Standards/S_Standard_Sequence": "Standard",
    "Standards/S_Standard_Scene_Grass": "Standard",
    "Standards/S_Standard_Scene_Plant": "Standard",
    "Standards/S_Standard_Scene_PBR_Plant": "Standard",
    # The terrain splat atlas (index + blend textures) degrades to its composited diffuse.
    "Standards/S_Standard_TerrainAtla": "Standard",
    # Billboard cards keep their alpha clip but stand still (no RuntimeBillBoardItem rotation).
    "Unlit/BillBoard": "Standard",
    # The water plane loses its refraction / foam / flow; one static wave texture stands in.
    "FX/TKWater": "Standard",
}


def map_scene_material(material):
    """Maps one 桃园 scene material (as export_scene_assets.csx writes it) to engine properties.

    Returns {"shader", "properties", "emission", "notes", "sequence"} where `shader` is "Standard"
    or "Skybox" (the builder treats the sky node specially) and `sequence` describes the flipbook
    frame crop for the two Sequence materials (None otherwise).
    """
    shader_name = material.get("shader")
    engine_shader = SCENE_SHADER_MAP.get(shader_name)
    if engine_shader is None:
        raise ValueError(f"scene shader {shader_name!r} has no mapping (effects belong to M5)")

    props = _properties_by_name(material)
    keywords = set(material.get("keywords", []))
    notes = []

    if engine_shader == "Skybox":
        texture = _texture_guid(props, "_Tex")
        if texture is None:
            raise ValueError("the skybox material binds no _Tex")
        notes.append(f"exposure {_Exposure(props)} and rotation {_Rotation(props)} are not reproduced "
                     "(the engine samples the panorama directly)")
        return {"shader": "Skybox", "properties": {"_MainTex": texture, "_MainColor": _Tint(props)},
                "emission": None, "notes": notes, "sequence": None}

    sequence = None
    if shader_name == "Standards/S_Standard_Sequence":
        # Frame 0 of the W×H flipbook: Unity's UV origin is the bottom-left cell. The port shows
        # that one frame statically, alpha-clipped, instead of the blended animation.
        width = int(float(props.get("_Width", 1.0)))
        height = int(float(props.get("_Height", 1.0)))
        sequence = {"columns": width, "rows": height, "frame": 0}
        notes.append(f"flipbook ({width}x{height}, {int(float(props.get('_EndIndex', 0.0)))} frames) "
                     "replaced by its first frame, alpha-clipped and static")

    properties = {}
    cutoff_name = "_CutOff" if shader_name == "Unlit/BillBoard" else "_Cutoff"
    alpha_test = "_ALPHATEST_ON" in keywords or shader_name in (
        "Unlit/BillBoard", "Standards/S_Standard_Sequence",
        "Standards/S_Standard_Scene_Grass", "Standards/S_Standard_Scene_Plant",
        "Standards/S_Standard_Scene_PBR_Plant")
    if alpha_test:
        properties["_AlphaClip"] = 1.0
        properties["_AlphaCutoff"] = float(props.get(cutoff_name, props.get("_Cutoff", 0.5)))

    main = _texture_guid(props, "_MainTex")
    if main:
        properties["_MainTex"] = main
    normal = _texture_guid(props, "_BumpMap")
    if normal:
        properties["_NormalTex"] = normal
    packed = _texture_guid(props, "_MetallicGlossMap")
    if packed:
        properties["_SurfaceTex"] = packed
    color = props.get("_Color")
    if color is not None and not _is_white(color):
        linear = texture_math.srgb_to_linear(color[:3])
        properties["_MainColor"] = [float(linear[0]), float(linear[1]), float(linear[2]), float(color[3])]

    emission = None
    recipe = texture_math.emission_recipe(props.get("_EmissionColor", (0.0, 0.0, 0.0, 1.0)), props.get("_EmissionScale", 0.0))
    if recipe is not None and _texture_guid(props, "_EmissionMap") is None:
        notes.append("flat emission colour is not reproduced (the engine bakes emission from a mask)")
    elif recipe is not None:
        emission = {"maskTexture": packed, "recipe": recipe}
        properties["_EmissionIntensity"] = recipe["intensity"]

    if shader_name in ("Standards/S_Standard_Scene_Grass", "Standards/S_Standard_Scene_Plant",
                       "Standards/S_Standard_Scene_PBR_Plant"):
        notes.append("wind animation (_Speed/_Rigidness/_SwayMax) is not reproduced")
    if shader_name == "Standards/S_Standard_TerrainAtla":
        notes.append("terrain splat (_IndexTex/_BlendTex) degrades to the composited diffuse map; "
                     "wetness and ripple layers are not reproduced")
    if shader_name == "Standards/S_Standard_Scene":
        notes.append("wetness / snow / ripple layers are not reproduced")
    if shader_name == "Unlit/BillBoard":
        properties["_Cull"] = 0    # a single quad seen from both sides
        notes.append("the card stands still (RuntimeBillBoardItem camera-facing is not reproduced)")
    if shader_name == "FX/TKWater":
        notes.append("the water shader degrades to a static wave texture "
                     "(no refraction, foam, SSR or flow)")

    return {"shader": "Standard", "properties": properties, "emission": emission,
            "notes": notes, "sequence": sequence}


def _Exposure(props):
    return props.get("_Exposure", 1.0)


def _Rotation(props):
    return props.get("_Rotation", 0.0)


def _Tint(props):
    tint = props.get("_Tint", (1.0, 1.0, 1.0, 1.0))
    linear = texture_math.srgb_to_linear(tint[:3])
    return [float(linear[0]), float(linear[1]), float(linear[2]), float(tint[3])]
