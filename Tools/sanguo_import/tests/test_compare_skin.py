import json

import numpy as np
import pytest
from PIL import Image

from sanguo_import import compare_skin as cs


def translation(x, y, z):
    matrix = np.eye(4)
    matrix[:3, 3] = (x, y, z)
    return matrix


def test_matrix_layouts_read_the_same_block_their_own_way():
    # Unity lists rows; the engine lists columns. One matrix that is a pure 90-degree turn and a
    # move, so that confusing the two would show.
    unity = [0, 1, 0, 10, -1, 0, 0, 20, 0, 0, 1, 30, 0, 0, 0, 1]
    engine = [0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 10, 20, 30, 1]
    np.testing.assert_allclose(cs.matrices(unity, "rows")[0], cs.matrices(engine, "columns")[0])


def test_skin_blends_and_reads_the_first_influence():
    matrices_ = np.stack([translation(1, 0, 0), translation(0, 2, 0)])
    raw = np.zeros((4, 3))
    indices = np.array([[0, 1, 0, 0]] * 4)
    weights = np.array([[1.0, 0.0, 0.0, 0.0], [0.25, 0.75, 0.0, 0.0], [0.0, 1.0, 0.0, 0.0], [0.0, 0.0, 0.0, 0.0]])

    blend, first = cs.skin(raw, indices, weights, matrices_)

    np.testing.assert_allclose(blend, [(1, 0, 0), (0.25, 1.5, 0), (0, 2, 0), (0, 0, 0)])
    # The first influence is the first slot with a weight, not the heaviest one.
    np.testing.assert_allclose(first, [(1, 0, 0), (1, 0, 0), (0, 2, 0), (0, 0, 0)])


def test_skin_ignores_an_influence_that_names_no_bone():
    matrices_ = np.stack([translation(1, 0, 0)])
    indices = np.array([[0, 5, 0, 0]])  # bone 5 does not exist
    weights = np.array([[0.5, 0.5, 0.0, 0.0]])

    blend, _ = cs.skin(np.zeros((1, 3)), indices, weights, matrices_)

    np.testing.assert_allclose(blend, [(0.5, 0.0, 0.0)])


def test_skin_matrices_follow_the_documented_formula():
    renderer_to_world = translation(0, 0, 5)
    bone_world = translation(3, 0, 8)
    bind_pose = translation(1, 0, 0)

    matrix = cs.skin_matrices(renderer_to_world, [bone_world], [bind_pose])[0]

    # (0,0,-5) * (3,0,8) * (1,0,0): the bone's move since it was bound, seen from the renderer.
    np.testing.assert_allclose(matrix, translation(4, 0, 3))


def test_nearest_is_zero_for_identical_sets_and_the_gap_for_one():
    a = np.array([[0.0, 0, 0], [1, 0, 0]])
    np.testing.assert_allclose(cs.nearest(a, a.copy()), [0, 0])
    np.testing.assert_allclose(cs.nearest(a, a + (0, 0, 0.5)), [0.5, 0.5])


def test_weights_compare_by_bone_name_and_three_decimals():
    names = ["Hand", "Forearm"]
    unity = {"raw": np.zeros((2, 3)), "indices": np.array([[0, 1, 0, 0]] * 2),
             "weights": np.array([[0.5, 0.5, 0, 0], [0.9, 0.1, 0, 0]]), "names": names}
    engine = {"raw": np.zeros((2, 3)), "indices": np.array([[0, 1, 0, 0]] * 2),
              "weights": np.array([[0.4839, 0.5161, 0, 0], [0.9, 0.1, 0, 0]]), "names": names}

    assert cs.compare_weights(unity, engine) == (2, 1)


def test_on_grid_measures_how_quantised_weights_are():
    # Zero weights are not counted; 1.0 is on every grid, 0.5 and 0.25 are not multiples of 1/31.
    assert cs.on_grid(np.array([1.0, 0.5, 0.25, 0.0]), 31) == pytest.approx(1 / 3)
    assert cs.on_grid(np.array([15 / 31, 16 / 31]), 31) == 1.0
    assert cs.on_grid(np.array([0.0, 0.0]), 31) == 1.0


def test_project_puts_a_point_in_front_of_the_camera_in_the_centre():
    rotation = cs.quaternion_matrix((0, 0, 0, 1))
    centre = cs.project(np.array([[0.0, 0.0, 3.0]]), np.zeros(3), rotation, 60.0, 2 / 3)
    above = cs.project(np.array([[0.0, 0.3, 3.0]]), np.zeros(3), rotation, 60.0, 2 / 3)
    np.testing.assert_allclose(centre, [(0.5, 0.5)])
    assert above[0, 0] == pytest.approx(0.5) and above[0, 1] < 0.5  # image y grows downwards


def test_world_matrices_chain_a_child_through_its_parent():
    transforms = [
        {"path": "Root", "parent": None, "position": [1, 0, 0], "rotation": [0, 0, 0, 1], "scale": [2, 2, 2]},
        {"path": "Root/Child", "parent": "Root", "position": [0, 1, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1]},
    ]
    np.testing.assert_allclose(cs.world_matrices(transforms)["Root/Child"][:3, 3], [1, 2, 0])


def test_silhouette_mask_and_overlap(tmp_path):
    image = Image.new("RGB", (4, 4), (51, 51, 51))
    image.putpixel((1, 1), (255, 0, 255))
    image.putpixel((2, 2), (255, 0, 255))
    path = tmp_path / "frame.png"
    image.save(path)
    mask = cs.silhouette_mask(path)
    assert mask.sum() == 2 and mask[1, 1] and mask[2, 2]

    other = np.zeros((4, 4), dtype=bool)
    other[1, 1] = other[3, 3] = True
    assert cs.overlap(mask, other) == pytest.approx(1 / 3)
    assert cs.overlap(np.zeros((4, 4), bool), np.zeros((4, 4), bool)) == 1.0


def test_raster_keeps_one_winding_only():
    # Image y grows downwards. (0.25,0.25) -> (0.75,0.25) -> (0.5,0.75) has a positive signed area.
    points = np.array([[0.25, 0.25], [0.75, 0.25], [0.5, 0.75]])
    triangles = np.array([[0, 1, 2]])
    size, height = 40, 40
    positive = cs.raster(points, triangles, size, height, True)
    negative = cs.raster(points, triangles, size, height, False)

    assert positive.any() and not negative.any()
    assert positive[17, 20] and not positive[2, 2]      # inside the triangle, far outside it
    assert not positive[0].any() and not positive[:, 0].any()


def side(raw, indices, weights, names, matrices_, to_world=None):
    to_world = np.eye(4) if to_world is None else to_world
    blend, first = cs.skin(raw, indices, weights, matrices_)
    return {"mesh": "Hero_Test_Body", "raw": raw, "indices": indices, "weights": weights, "names": names,
            "skin": matrices_, "blend": blend, "first": first, "toWorld": to_world}


def test_compare_renderer_reports_matching_meshes_as_matching():
    raw = np.array([[0.0, 0, 0], [1, 0, 0]])
    indices = np.array([[0, 1, 0, 0]] * 2)
    weights = np.array([[1.0, 0, 0, 0], [0.5, 0.5, 0, 0]])
    matrices_ = np.stack([translation(0, -0.089, 0.001), translation(0.2, 0, 0)])
    unity = side(raw, indices, weights, ["A", "B"], matrices_)
    unity["baked"] = unity["first"]
    engine = side(raw, indices, weights, ["A", "B"], matrices_)

    report = cs.compare_renderer(unity, engine)

    assert report["raw"] == pytest.approx(0.0) and report["formula"] == pytest.approx(0.0)
    assert report["skinMatrix"] == pytest.approx(0.0)
    assert (report["paired"], report["weightsDiffer"]) == (2, 0)
    assert report["bakeVsFirst"] == pytest.approx(0.0) and report["bakeVsBlend"] > 0


def test_fbx_agreement_sees_through_a_mesh_offset_and_tells_the_mirror():
    skin = {"vertices": [0.0, 0.0, 1.008, 1.0, 0.0, 1.008], "weights": {0: [("A", 1.0)], 1: [("B", 0.5), ("A", 0.5)]}}
    # The engine mirrored X and dropped the mesh node's 8 mm offset.
    raw = np.array([[0.0, 0.0, 1.0], [-1.0, 0.0, 1.0]])
    engine = {"raw": raw, "indices": np.array([[0, 0, 0, 0], [1, 0, 0, 0]]),
              "weights": np.array([[1.0, 0, 0, 0], [0.5, 0.5, 0, 0]]), "names": ["A", "B"]}

    assert cs.fbx_agreement(skin, engine) == (2, 2, -1.0)

    engine["weights"][1] = [0.25, 0.75, 0, 0]
    assert cs.fbx_agreement(skin, engine) == (2, 1, -1.0)


def test_silhouette_report_names_the_reading_each_frame_shows():
    # Bone A only moves the triangle; bone B also turns it. Blending B in therefore turns the
    # drawn triangle, which is what tells the two readings apart; they still partly overlap.
    raw = np.array([[0.0, 0.0, 0.0], [0.4, 0.0, 0.0], [0.0, 0.4, 0.0]])
    indices = np.array([[0, 1, 0, 0]] * 3)
    weights = np.array([[0.5, 0.5, 0, 0]] * 3)
    turn = np.eye(4)
    turn[:3, :3] = [[0, -1, 0], [1, 0, 0], [0, 0, 1]]
    matrices_ = np.stack([translation(0, 0, 4), translation(0.4, 0, 4) @ turn])
    unity = side(raw, indices, weights, ["A", "B"], matrices_)
    engine = side(raw, indices, weights, ["A", "B"], matrices_)
    unity["triangles"] = engine["triangles"] = np.array([[0, 1, 2]])
    shot = {"cameraPosition": [0, 0, 0], "cameraRotation": [0, 0, 0, 1]}
    width, height, fov = 64, 64, 60.0

    def drawn(reading):
        world = unity[reading] @ unity["toWorld"][:3, :3].T + unity["toWorld"][:3, 3]
        pixels = cs.project(world, np.zeros(3), cs.quaternion_matrix(shot["cameraRotation"]), fov, 1.0)
        return [cs.raster(pixels, unity["triangles"], width, height, sign) for sign in (True, False)]

    # Both readings keep the triangle's orientation, so the front face is the same winding.
    frames = {reading: drawn(reading) for reading in ("blend", "first")}
    positive = frames["first"][0].any()
    front_index, back_index = (0, 1) if positive else (1, 0)
    assert not frames["first"][back_index].any()
    front = [frames["first"][front_index], frames["blend"][front_index]]
    assert front[0].sum() > 0 and front[1].sum() > 0
    assert cs.overlap(front[0], front[1]) < 0.9
    unity["mask"], engine["mask"] = front[0], front[1]  # Unity drew "first", the engine drew "blend"

    report = cs.silhouette_report(unity, engine, shot, width, height, fov)

    assert report["winding"] == ("positive" if positive else "negative")
    assert report["unityReading"] == "first" and report["engineReading"] == "blend"
    assert 0.0 < report["gpu"] < 0.9 and report["readingsApart"] > 0.1


def test_a_report_goes_through_json_as_it_is():
    raw = np.zeros((1, 3))
    one = side(raw, np.array([[0, 0, 0, 0]]), np.array([[1.0, 0, 0, 0]]), ["A"], np.stack([np.eye(4)]))
    one["mesh"], one["baked"] = "Hero_Test_Body", one["blend"]

    report = json.loads(json.dumps({"hero": "Hero_Test", **cs.compare_renderer(one, one)}))

    assert report["hero"] == "Hero_Test" and report["mesh"] == "Hero_Test_Body"
    assert report["vertices"] == [1, 1] and report["formula"] == 0.0
