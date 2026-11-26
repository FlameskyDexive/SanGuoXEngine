# -*- coding: utf-8 -*-
"""compare_anim.py — the engine's played poses against Unity's sampled poses."""

import json

import numpy as np
import pytest
from PIL import Image

from sanguo_import import compare_anim

TIMES = [0.0, 0.25, 0.5]
NODES = ["", "Bip001", "Bip001/Bip00A"]      # the actor root, the root-motion node, a bone


def pose(position=(0.0, 0.0, 0.0), rotation=(0.0, 0.0, 0.0, 1.0)):
    """One node at one time, in the layout both sides use: position, rotation xyzw, scale."""
    return [list(position), list(rotation), [1.0, 1.0, 1.0]]


def turn(degrees):
    half = np.radians(degrees) / 2
    return [0.0, np.sin(half), 0.0, np.cos(half)]


def frames(root, motion, bone):
    """Per time (after the first, which is never compared) the pose of root, motion node and bone."""
    return [[pose(), pose(), pose()]] + [[pose(*root), pose(*motion), pose(*bone)] for _ in TIMES[1:]]


def pair(unity_frames, engine_frames):
    unity = {"nodes": [{"path": path} for path in NODES],
             "clips": [{"action": "idle_loop", "samples": {"times": TIMES, "poses": unity_frames}}]}
    engine = {"nodes": NODES, "clips": {"idle_loop": {"times": list(TIMES), "poses": engine_frames}}}
    return unity, engine


def test_angle_ignores_sign_and_length():
    assert compare_anim.angle_degrees([0, 0, 0, 1], [0, 0, 0, -2]) == pytest.approx(0.0)
    assert compare_anim.angle_degrees([0, 0, 0, 1], turn(30)) == pytest.approx(30.0)
    assert compare_anim.angle_degrees(turn(30), turn(-30)) == pytest.approx(60.0)


def test_identical_poses_compare_clean():
    played = frames(((0, 0, 0.4), turn(0)), ((0, 0, 0), turn(10)), ((0, 0, 0), turn(20)))
    stat = compare_anim.compare_document(*pair(played, played))

    assert compare_anim.within(stat)
    # Two bones at two times; the bone's position at two times; root and motion node at two times.
    assert (stat["clips"], stat["rotations"], stat["positions"], stat["roots"]) == (1, 4, 2, 4)
    assert stat["rotation"] == 0.0 and stat["position"] == 0.0 and stat["root"] == 0.0
    assert stat["perClip"] == {"idle_loop": {"rotation": 0.0, "position": 0.0, "root": 0.0}}


def test_deviations_are_found_and_located():
    unity = frames(((1.0, 0.0, 0.0), turn(90)), ((0, 0, 0), turn(0)), ((1.0, 0.0, 0.0), turn(90)))
    engine = frames(((1.0, 0.0, 0.0), turn(90)), ((0, 0, 0), turn(30)), ((1.004, 0.0, 0.0), turn(90)))

    stat = compare_anim.compare_document(*pair(unity, engine))
    assert stat["rotation"] == pytest.approx(30.0)
    assert stat["rotationAt"] == "idle_loop 'Bip001' t=0.2500"
    assert stat["position"] == pytest.approx(4.0)       # millimetres
    assert stat["positionAt"] == "idle_loop 'Bip001/Bip00A' t=0.2500"
    assert not compare_anim.within(stat)                # 30° is over the 0.5° tolerance


def test_root_and_motion_node_are_held_to_the_root_tolerance():
    unity = frames(((0, 0, 0), turn(0)), ((0, 0, 0), turn(0)), ((0, 0, 0), turn(0)))
    engine = frames(((0, 0, 0.012), turn(0)), ((0, 0, 0.012), turn(0)), ((0, 0, 0), turn(0)))

    stat = compare_anim.compare_document(*pair(unity, engine))
    assert stat["root"] == pytest.approx(12.0)
    assert stat["rootAt"] == "idle_loop '<root>' t=0.2500"
    assert compare_anim.within(stat)                    # 12 mm is inside the 30 mm root tolerance


def test_missing_or_mistimed_clips_are_reported():
    rest = frames(((0, 0, 0), turn(0)), ((0, 0, 0), turn(0)), ((0, 0, 0), turn(0)))
    unity, engine = pair(rest, rest)
    no_clips = dict(engine, clips={})
    assert compare_anim.compare_document(unity, no_clips)["missing"] == ["idle_loop"]

    other_times = {**engine, "clips": {"idle_loop": {"times": [0.0, 0.3, 0.5], "poses": engine["clips"]["idle_loop"]["poses"]}}}
    assert "sampled at other times" in compare_anim.compare_document(unity, other_times)["missing"][0]

    other_nodes = dict(engine, nodes=["", "Bip001"])
    assert "different nodes" in compare_anim.compare_document(unity, other_nodes)["missing"][0]


def frame(path, box, background=(51, 51, 51)):
    image = Image.new("RGB", (64, 48), background)
    image.paste((200, 30, 30), box)
    image.save(path)


def test_rendered_hero_extent_is_compared_per_frame_pair(tmp_path):
    unity_dir, engine_dir = tmp_path / "unity" / "Hero_X", tmp_path / "engine" / "Hero_X"
    unity_dir.mkdir(parents=True)
    engine_dir.mkdir(parents=True)
    frame(unity_dir / "a_p1.png", (10, 5, 40, 45))
    frame(engine_dir / "a_p1.png", (12, 5, 40, 45))       # 2 px off on the left edge
    frame(unity_dir / "a_p4.png", (10, 5, 40, 45))
    frame(engine_dir / "a_p4.png", (10, 5, 40, 45))
    frame(unity_dir / "a_p7.png", (10, 5, 40, 45))
    frame(engine_dir / "a_p7.png", (40, 5, 60, 45))       # 30 px off: beyond the tolerance
    document = {"clips": [{"action": "a", "frames": [
        {"file": "a_p1.png"}, {"file": "a_p4.png"}, {"file": "a_p7.png"}, {"file": "a_p9.png"}]}]}

    assert compare_anim.compare_frames(document, unity_dir, engine_dir) == (30, 32 / 3, 3, ["a_p7.png 30px"])


def test_extent_of_an_empty_mask(tmp_path):
    empty = tmp_path / "empty.png"
    frame(empty, (0, 0, 0, 0))
    assert compare_anim.extent(compare_anim.silhouette(empty)) is None
    assert compare_anim.compare_frames({"clips": [{"frames": [{"file": "empty.png"}]}]}, tmp_path, tmp_path) is None


def write_sides(tmp_path, unity_frames, engine_frames):
    unity, engine = pair(unity_frames, engine_frames)
    unity_root, engine_root = tmp_path / "unity", tmp_path / "engine"
    (unity_root / "Hero_X").mkdir(parents=True)
    (engine_root / "Hero_X").mkdir(parents=True)
    (unity_root / "Hero_X.json").write_text(json.dumps(unity), encoding="utf-8")
    (engine_root / "poses.json").write_text(json.dumps({"Hero_X": engine}), encoding="utf-8")
    return ["--unity", str(unity_root), "--engine", str(engine_root)]


def test_main_passes_and_writes_the_report(tmp_path):
    played = frames(((0, 0, 0.4), turn(0)), ((0, 0, 0), turn(10)), ((0, 0, 0), turn(5)))
    report = tmp_path / "report.json"

    assert compare_anim.main(write_sides(tmp_path, played, played) + ["--out", str(report)]) == 0
    written = json.loads(report.read_text(encoding="utf-8"))
    assert written["heroes"]["Hero_X"]["clips"] == 1
    assert written["tolerances"]["rotationDeg"] == compare_anim.ROTATION_TOLERANCE_DEG


def test_main_fails_over_tolerance(tmp_path):
    unity = frames(((0, 0, 0.4), turn(0)), ((0, 0, 0), turn(0)), ((0, 0, 0), turn(0)))
    off = frames(((0, 0, 0.4), turn(0)), ((0, 0, 0), turn(40)), ((0, 0, 0), turn(0)))

    assert compare_anim.main(write_sides(tmp_path, unity, off)) == 1
