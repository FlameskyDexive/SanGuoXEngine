# -*- coding: utf-8 -*-
"""Unit tests for the channel arithmetic in Tools/sanguo_import/texture_math.py."""

import numpy as np
import pytest

from sanguo_import import texture_math


def test_srgb_curve_follows_the_formula_and_unitys_hdr_extension():
    assert texture_math.srgb_to_linear(0.0) == 0.0
    assert texture_math.srgb_to_linear(0.04045) == pytest.approx(0.04045 / 12.92)
    assert texture_math.srgb_to_linear(0.5) == pytest.approx(((0.5 + 0.055) / 1.055) ** 2.4)
    assert texture_math.srgb_to_linear(1.0) == pytest.approx(1.0)
    # Above 1 Unity (and the engine) keep going with c ** 2.2 instead of the sRGB curve.
    assert texture_math.srgb_to_linear(2.0) == pytest.approx(2.0 ** 2.2)
    assert texture_math.srgb_to_linear(2.0) != pytest.approx(((2.0 + 0.055) / 1.055) ** 2.4)


def test_srgb_conversions_invert_each_other():
    samples = np.linspace(0.0, 3.0, 301)
    assert np.allclose(texture_math.linear_to_srgb(texture_math.srgb_to_linear(samples)), samples)


def test_surface_repack_places_each_jws2_channel_in_its_engine_slot():
    metallic = np.array([[1.0, 0.0], [0.25, 0.5]])
    occlusion = np.array([[1.0, 0.5], [0.0, 1.0]])
    smoothness = np.array([[1.0, 0.0], [0.5, 0.75]])

    surface = texture_math.repack_surface(metallic, occlusion, smoothness)

    assert surface.dtype == np.uint8 and surface.shape == (2, 2, 3)
    # R = 1 - occlusion, G = 1 - smoothness (perceptual roughness), B = metallic.
    assert surface[0, 0].tolist() == [0, 0, 255]      # open, fully smooth, metal
    assert surface[0, 1].tolist() == [128, 255, 0]    # half occluded (0.5 * 255 rounds up), fully rough
    assert surface[1, 0].tolist() == [255, 128, 64]   # 0.25 * 255 = 63.75 rounds to 64
    assert surface[1, 1].tolist() == [0, 64, 128]


def test_surface_repack_is_lossless_for_byte_inputs():
    """The two complements are exact in 8 bits and the third channel is copied, so a byte-exact
    source round-trips through the engine map unchanged."""
    rng = np.random.default_rng(7)
    metallic = rng.integers(0, 256, (16, 16)).astype(np.uint8)
    occlusion = rng.integers(0, 256, (16, 16)).astype(np.uint8)
    smoothness = rng.integers(0, 256, (16, 16)).astype(np.uint8)

    surface = texture_math.repack_surface(
        metallic / 255.0, occlusion / 255.0, smoothness / 255.0)
    metallic_back, occlusion_back, smoothness_back = texture_math.unpack_surface(surface)

    assert np.array_equal(np.rint(metallic_back * 255).astype(np.uint8), metallic)
    assert np.array_equal(np.rint(occlusion_back * 255).astype(np.uint8), occlusion)
    assert np.array_equal(np.rint(smoothness_back * 255).astype(np.uint8), smoothness)


def test_surface_repack_rejects_mismatched_shapes():
    with pytest.raises(ValueError):
        texture_math.repack_surface(np.zeros((2, 2)), np.zeros((3, 3)), np.zeros((2, 2)))


def test_emission_is_off_for_a_black_colour_or_a_zero_scale():
    assert texture_math.emission_recipe((0.0, 0.0, 0.0, 1.0), 5.0) is None
    assert texture_math.emission_recipe((1.0, 0.5, 0.0, 1.0), 0.0) is None


def test_emission_recipe_splits_colour_into_unit_tint_and_intensity():
    recipe = texture_math.emission_recipe((1.0, 0.5, 0.0, 1.0), 4.0)
    # linear(0.5) = 0.214; the peak (red = 1) folds into the intensity.
    assert recipe["tint"] == pytest.approx([1.0, 0.21404114, 0.0])
    assert recipe["intensity"] == pytest.approx(4.0)


def test_emission_recipe_folds_an_hdr_peak_into_the_intensity():
    recipe = texture_math.emission_recipe((0.5, 2.0, 0.25, 1.0), 0.3)
    peak = 2.0 ** 2.2
    assert max(recipe["tint"]) == pytest.approx(1.0)
    assert recipe["intensity"] == pytest.approx(0.3 * peak)


def test_baked_emission_reproduces_unitys_product_in_linear_light():
    """Unity: mask * linear(colour) * scale. XEngine reads the sRGB map and multiplies by the
    intensity; decoded that way the baked map must give Unity's value."""
    rng = np.random.default_rng(3)
    mask = rng.random((8, 8))
    colour, scale = (1.0, 0.5082709, 0.0, 1.0), 3.8

    recipe = texture_math.emission_recipe(colour, scale)
    baked = texture_math.bake_emission(mask, recipe)

    decoded = texture_math.srgb_to_linear(baked[..., :3] / 255.0) * recipe["intensity"]
    expected = mask[..., None] * texture_math.srgb_to_linear(colour[:3]) * scale
    # One 8-bit sRGB step is at most ~1% of linear light near white, and the intensity scales the
    # stored value, so the error bound grows with both.
    tolerance = 0.011 * recipe["intensity"]
    assert np.abs(decoded - expected).max() <= tolerance


def test_baked_emission_is_none_without_a_recipe():
    assert texture_math.bake_emission(np.ones((4, 4)), None) is None


def test_resized_normal_map_gets_unit_length_normals_back():
    rng = np.random.default_rng(11)
    normals = rng.normal(size=(16, 16, 3))
    normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
    encoded = np.rint(normals * 127.5 + 127.5).astype(np.uint8)
    # A resize blurs the map; emulate it with a box blur that shortens the vectors.
    blurred = encoded.astype(np.float64)
    blurred[1:-1, 1:-1] = (blurred[1:-1, 1:-1] + blurred[:-2, 1:-1] + blurred[2:, 1:-1]) / 3.0
    blurred = np.clip(np.rint(blurred), 0, 255).astype(np.uint8)
    lengths_before = np.linalg.norm(blurred[..., :3] / 127.5 - 1.0, axis=-1)
    assert lengths_before.min() < 0.9

    renormalized = texture_math.renormalize_normal_map(blurred[..., :3])

    lengths_after = np.linalg.norm(renormalized / 127.5 - 1.0, axis=-1)
    assert lengths_after.min() > 0.98
    # Z is kept: only X/Y are rescaled.
    assert np.array_equal(renormalized[..., 2], blurred[..., 2])
