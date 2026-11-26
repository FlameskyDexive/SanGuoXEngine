# -*- coding: utf-8 -*-
"""Channel arithmetic for converting jws2's packed textures to XEngine's Default/Standard layout.

Pure numpy, no file I/O, so every formula is unit-tested on synthetic arrays.

jws2's character shaders (`S_Standard_Hero`, `S_Standard_Skin`, `S_Standard_Hair`) all read one
packed surface texture, `*_M_Alpha`:

    R = metallic    G = ambient occlusion (1 = open)    B = emission mask    A = smoothness

XEngine's `Default/Standard` reads `_SurfaceTex` as (XEngine.Runtime/Assets/Defaults/StandardSurface.glsl):

    R = occlusion, applied as `ao = 1.0 - R`      G = perceptual roughness      B = metallic
"""

from __future__ import annotations

import numpy as np

_SRGB_BREAK = 0.04045
_LINEAR_BREAK = 0.0031308


def srgb_to_linear(color):
    """sRGB to linear for an array of any shape.

    Values above 1 follow Unity's extension for HDR material colours (c ** 2.2), the same curve the
    engine applies to sRGB-authored colours (docs/superpowers/specs/2026-10-29-material-color-encoding.md).
    """
    color = np.asarray(color, dtype=np.float64)
    low = color / 12.92
    high = np.where(color <= 1.0, ((color + 0.055) / 1.055) ** 2.4, color ** 2.2)
    return np.where(color <= _SRGB_BREAK, low, high)


def linear_to_srgb(color):
    """Inverse of :func:`srgb_to_linear` (also for the above-1 range)."""
    color = np.asarray(color, dtype=np.float64)
    low = color * 12.92
    high = np.where(color <= 1.0, 1.055 * np.power(np.maximum(color, 0.0), 1.0 / 2.4) - 0.055, color ** (1.0 / 2.2))
    return np.where(color <= _LINEAR_BREAK, low, high)


def channel_to_unit(channel):
    """A uint8 channel image to floats in [0, 1]."""
    return np.asarray(channel, dtype=np.float64) / 255.0


def repack_surface(metallic, occlusion, smoothness):
    """Builds an RGB uint8 `_SurfaceTex` from the three jws2 channels, each a float array in [0, 1].

        R = 1 - occlusion   G = 1 - smoothness   B = metallic

    The two complements are exact in 8 bits, so no precision is lost by the repack itself.
    """
    metallic = np.asarray(metallic, dtype=np.float64)
    occlusion = np.asarray(occlusion, dtype=np.float64)
    smoothness = np.asarray(smoothness, dtype=np.float64)
    if not (metallic.shape == occlusion.shape == smoothness.shape):
        raise ValueError("metallic, occlusion and smoothness must have the same shape")
    if metallic.ndim != 2:
        raise ValueError("channels must be single-channel images")

    red = np.rint((1.0 - occlusion) * 255.0)
    green = np.rint((1.0 - smoothness) * 255.0)
    blue = np.rint(metallic * 255.0)
    return np.clip(np.stack([red, green, blue], axis=-1), 0.0, 255.0).astype(np.uint8)


def unpack_surface(surface):
    """Inverse of :func:`repack_surface` for verification: an RGB uint8 image back to the three
    jws2 channels as float arrays in [0, 1]."""
    surface = np.asarray(surface, dtype=np.float64)
    if surface.ndim != 3 or surface.shape[2] < 3:
        raise ValueError("surface must be an RGB image")
    return channel_to_unit(surface[..., 2]), 1.0 - channel_to_unit(surface[..., 0]), 1.0 - channel_to_unit(surface[..., 1])


def emission_recipe(color_srgb, scale):
    """Describes jws2's emission for one material, or returns None when it is off.

    jws2 (all three character shaders): emission = mask.b * linear(_EmissionColor) * _EmissionScale
    * (sin pulse in [0.5, 1.5]). XEngine: emission = _EmissionTex * _EmissionIntensity. The pulse is
    replaced by its mean (1.0), and the colour is split so the product fits an 8-bit sRGB texture:
    the texture stores mask * colour / peak and the intensity carries scale * peak, where peak is
    the largest linear colour component. The returned intensity is what `_EmissionIntensity` takes.
    """
    color_srgb = np.asarray(color_srgb, dtype=np.float64)[:3]
    if float(scale) <= 0.0:
        return None
    color_linear = srgb_to_linear(color_srgb)
    peak = float(color_linear.max())
    if peak <= 0.0:
        return None
    return {
        "tint": (color_linear / peak).tolist(),
        "intensity": float(scale) * peak,
    }


def bake_emission(mask, recipe):
    """Bakes the emission texture for one material from the `_M_Alpha` blue channel.

    `mask` is a float array in [0, 1] and `recipe` the result of :func:`emission_recipe`. Returns an
    RGB uint8 image meant for an sRGB texture, or None when the recipe is None. Encoding to sRGB
    keeps the 8 bits where the eye sees them: a linear 8-bit store would band in the darks.
    """
    if recipe is None:
        return None
    mask = np.asarray(mask, dtype=np.float64)
    tint = np.asarray(recipe["tint"], dtype=np.float64)
    value = mask[..., None] * tint
    return np.clip(np.rint(linear_to_srgb(value) * 255.0), 0.0, 255.0).astype(np.uint8)


def renormalize_normal_map(rgb):
    """Re-establishes unit-length normals in an RGB uint8 normal map after it was resized.

    Z (blue) is kept and X/Y are scaled so the vector has length 1 again, which is what a resized
    normal map needs to keep its shading direction.
    """
    rgb = np.asarray(rgb, dtype=np.float64)
    x = rgb[..., 0] / 127.5 - 1.0
    y = rgb[..., 1] / 127.5 - 1.0
    z = rgb[..., 2] / 127.5 - 1.0
    radial = x * x + y * y
    scale = np.sqrt(np.maximum(1.0 - z * z, 0.0) / np.maximum(radial, 1e-12))
    scale = np.where(radial > 1e-12, scale, 0.0)
    x = x * scale
    y = y * scale
    out = np.stack([x, y, z], axis=-1) * 127.5 + 127.5
    return np.clip(np.rint(out), 0.0, 255.0).astype(np.uint8)
