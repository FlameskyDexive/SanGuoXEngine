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
    return {p["name"]: p["value"] for p in material.get("properties", []) if p.get("value") is not None}


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

    emission = None
    recipe = texture_math.emission_recipe(props.get("_EmissionColor", (0.0, 0.0, 0.0, 1.0)), props.get("_EmissionScale", 0.0))
    if recipe is not None:
        emission = {"maskTexture": surface, "recipe": recipe}
        properties["_EmissionIntensity"] = recipe["intensity"]
        notes.append("emission pulse replaced by its mean")

    if shader_name == "Standards/S_Standard_Skin":
        notes.append(f"fake-skin shading is not reproduced (_SSSLUT ramp, _SkinLightScale {props.get('_SkinLightScale', 1.0)})")
    if shader_name == "Standards/S_Standard_Hair":
        notes.append("strand specular replaced by GGX anisotropy; the extra front-face and back-face passes "
                     "and the transparent render queue are not reproduced (alpha-tested opaque)")
    if _enabled(props, keywords, "_RimLight", "_RIMLIGHT_ON"):
        notes.append("rim light is not reproduced")
    if _enabled(props, keywords, "_EffectToggle", "_EFFECTTOGGLE_ON"):
        notes.append("flow-light layer is not reproduced")
    if float(props.get("_PassColor", 0.0) or 0.0) != 0.0:
        notes.append("dye recolouring is not reproduced")
    if _texture_guid(props, "_Unity_SpecCube"):
        notes.append("per-material reflection cubemap replaced by the scene's image-based lighting")

    return {"shader": engine_shader, "properties": properties, "emission": emission, "notes": notes}
