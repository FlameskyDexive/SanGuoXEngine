# -*- coding: utf-8 -*-
"""Compares the skinning of every hero mesh between Unity and the engine.

    py -3 Tools/unity_export/run_export.py run export_skin --out Export/heroes/skin
    py -3 Tools/xengine_live/mcp_live.py --wait-compile 300 --eval-file Tools/xengine_live/evals/dump_skin.cs
    py -3 Tools/sanguo_import/compare_skin.py                      # the data
    py -3 Tools/unity_export/run_export.py run capture_silhouettes --out Export/unity-refs/silhouettes
    py -3 Tools/xengine_live/mcp_live.py --eval-file Tools/xengine_live/evals/render_silhouettes.cs
    py -3 Tools/sanguo_import/compare_skin.py --silhouettes        # the data and what each GPU drew

Two readings of a vertex's weights are evaluated for every mesh, in renderer space:

  blend   sum of weight * (renderer.worldToLocal * bone.localToWorld * bindpose) * vertex, the
          skinning formula both engines document
  first   the first influence alone (Unity's boneIndex0) with weight 1

Unity's SkinnedMeshRenderer.BakeMesh lands on "first" for every vertex whose influences do not
share one skin matrix (probe_bake_weights.csx), so a bake cannot serve as the blend reference.
Both readings are computed from each side's own data instead, and, when the silhouettes are given,
set against what each engine's renderer really drew.

Per mesh, maxima over vertices:
  raw          nearest-neighbour distance between the two raw vertex sets (the engine reorders and
               splits vertices, so they are compared as sets)
  bind         largest difference between Unity's and the engine's skin matrix of the same bone
  formula      engine CPU blend against Unity CPU blend: whether the engine evaluates the formula
               like Unity (nearest neighbour, since weights may differ; see "weights")
  weights      paired vertices (same raw position) whose (bone, weight) sets differ between the sides
  bake/blend   Unity's BakeMesh against Unity's own blend, and bake/first against its first-only
               reading: which of the two the bake produced
With --silhouettes, the overlap (intersection over union) of:
  GPU/GPU      Unity's rendered silhouette against the engine's
  U blend ...  each rendered silhouette against the projection of its own side's blend and first
The projections are rasterised front faces only (both captures cull back faces), with the winding
that reproduces the engine's own frame.

Exit status 1 when "raw" or "formula" exceeds --tolerance-mm (default 2 mm).
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import numpy as np
from PIL import Image, ImageDraw

from sanguo_import import fbx_skin_weights as fbx

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULTS = {
    "export": REPO_ROOT / "Export" / "heroes" / "heroes.json",
    "unity_skin": REPO_ROOT / "Export" / "heroes" / "skin",
    "engine_skin": REPO_ROOT / "Export" / "xengine-shots" / "skin",
    "unity_shots": REPO_ROOT / "Export" / "unity-refs" / "heroes" / "shots.json",
    "unity_silhouettes": REPO_ROOT / "Export" / "unity-refs" / "silhouettes",
    "engine_silhouettes": REPO_ROOT / "Export" / "xengine-shots" / "silhouettes",
}
INFLUENCES = 4
NEAREST_CHUNK = 512


# ---------------------------------------------------------------- matrices

def matrices(flat, layout):
    """Row-indexed 4x4 matrices (column vectors: p' = M @ p) out of flat 16-float blocks.

    layout "rows" is Unity's (m00, m01, ..., m33); layout "columns" is the engine's Float4x4
    (c0, c1, c2, c3, each x, y, z, w)."""
    blocks = np.asarray(flat, dtype=np.float64).reshape(-1, 4, 4)
    return blocks if layout == "rows" else blocks.transpose(0, 2, 1)


def quaternion_matrix(q):
    """Rotation matrix of a quaternion (x, y, z, w), the same convention in both exports."""
    x, y, z, w = q
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ])


def trs(position, rotation, scale):
    """A node's local matrix from its TRS."""
    result = np.eye(4)
    result[:3, :3] = quaternion_matrix(rotation) @ np.diag(scale)
    result[:3, 3] = position
    return result


def world_matrices(transforms):
    """Path -> root-space matrix for Unity's transform list (parent chains)."""
    by_path = {node["path"]: node for node in transforms}
    cache = {}

    def world(path):
        if path not in cache:
            node = by_path[path]
            local = trs(node["position"], node["rotation"], node["scale"])
            cache[path] = world(node["parent"]) @ local if node.get("parent") else local
        return cache[path]

    return {path: world(path) for path in by_path}


# ---------------------------------------------------------------- skinning

def skin_matrices(renderer_to_world, bone_worlds, bind_poses):
    """One skin matrix per bone, in renderer space: worldToLocal * boneWorld * bindpose."""
    to_renderer = np.linalg.inv(renderer_to_world)
    return np.stack([to_renderer @ bone @ bind for bone, bind in zip(bone_worlds, bind_poses)])


def skin(raw, indices, weights, matrices_):
    """Both readings of every vertex, (n, 3) each: the blended position and the first influence
    alone with weight 1 (the first slot with a weight above zero; Unity's boneIndex0, which is not
    always the heaviest). Indices are 0-based; an unweighted vertex stays where it is."""
    blended = np.zeros_like(raw)
    has_weight = (weights > 0).any(axis=1)
    lead = np.argmax(weights > 0, axis=1)  # 0 for an unweighted row
    rows = np.arange(len(raw))
    lead_bone = indices[rows, lead]
    alone = np.einsum("nij,nj->ni", matrices_[lead_bone, :3, :3], raw) + matrices_[lead_bone, :3, 3]
    for k in range(INFLUENCES):
        valid = (weights[:, k] > 0) & (indices[:, k] >= 0) & (indices[:, k] < len(matrices_))
        chosen = matrices_[np.where(valid, indices[:, k], 0)]
        moved = np.einsum("nij,nj->ni", chosen[:, :3, :3], raw) + chosen[:, :3, 3]
        blended += np.where(valid[:, None], weights[:, k:k + 1] * moved, 0.0)
    return blended, np.where(has_weight[:, None], alone, raw)


def nearest(a, b):
    """For every point of a, the distance to the nearest point of b (chunked brute force)."""
    out = np.empty(len(a))
    for start in range(0, len(a), NEAREST_CHUNK):
        block = a[start:start + NEAREST_CHUNK]
        out[start:start + NEAREST_CHUNK] = np.linalg.norm(block[:, None, :] - b[None, :, :], axis=2).min(axis=1)
    return out


def weight_set(names, indices, weights):
    """The (bone name, weight rounded to 1e-3) pairs of one vertex's influences above zero."""
    return frozenset((names[int(bone)], round(float(weight), 3))
                     for bone, weight in zip(indices, weights) if weight > 1e-4)


def on_grid(weights, quantum):
    """The share of the weights that lie within 1e-4 of a multiple of 1/quantum."""
    values = weights[weights > 1e-6]
    if values.size == 0:
        return 1.0
    return float((np.abs(values * quantum - np.round(values * quantum)) < 1e-4).mean())


def fbx_agreement(skin, engine):
    """(weighted control points, matching, mirror X): how many control points of the FBX skin have
    an engine vertex at the same position with the same weights.

    The FBX is right-handed and both importers mirror it on import, so an engine vertex sits at the
    control point's position with X negated; the other reading is tried as well. A mesh node can
    also carry an offset the control points do not include (FBX "GeometricTranslation": ZhaoYun's
    body sits 8 mm off), so when both point clouds have the same extents the difference of their
    bounding boxes is applied first. The reading that matches more control points is reported."""
    by_position = {}
    for v, raw in enumerate(engine["raw"]):
        by_position.setdefault(tuple(np.round(raw, 3)), []).append(v)
    engine_sets = [weight_set(engine["names"], indices, weights)
                   for indices, weights in zip(engine["indices"], engine["weights"])]
    vertices = np.asarray(skin["vertices"], dtype=np.float64).reshape(-1, 3)
    engine_min, engine_size = engine["raw"].min(axis=0), np.ptp(engine["raw"], axis=0)
    best = (0, 0, -1.0)
    for sign in (-1.0, 1.0):
        mirrored = vertices * (sign, 1.0, 1.0)
        offset = engine_min - mirrored.min(axis=0)
        if np.abs(np.ptp(mirrored, axis=0) - engine_size).max() > 2e-3:
            offset = np.zeros(3)  # not the same point cloud up to a translation
        matching = 0
        for index, influences in skin["weights"].items():
            expected = frozenset((bone, round(weight, 3)) for bone, weight in influences if weight > 1e-4)
            key = tuple(np.round(mirrored[index] + offset, 3))
            if any(engine_sets[c] == expected for c in by_position.get(key, [])):
                matching += 1
        if matching > best[1]:
            best = (len(skin["weights"]), matching, sign)
    return best


def compare_weights(unity, engine):
    """(paired, different): Unity vertices that have an engine vertex at the same raw position, and
    of those how many have none with the same weights."""
    by_position = {}
    for v, raw in enumerate(engine["raw"]):
        by_position.setdefault(tuple(np.round(raw, 4)), []).append(v)
    engine_sets = [weight_set(engine["names"], indices, weights)
                   for indices, weights in zip(engine["indices"], engine["weights"])]
    paired = different = 0
    for v, raw in enumerate(unity["raw"]):
        candidates = by_position.get(tuple(np.round(raw, 4)))
        if not candidates:
            continue
        paired += 1
        expected = weight_set(unity["names"], unity["indices"][v], unity["weights"][v])
        if all(engine_sets[c] != expected for c in candidates):
            different += 1
    return paired, different


def compare_renderer(unity, engine):
    """The data metrics of one mesh (millimetres)."""
    report = {"mesh": engine["mesh"], "vertices": [len(unity["raw"]), len(engine["raw"])]}
    report["raw"] = float(nearest(engine["raw"], unity["raw"]).max() * 1000)
    shared = [i for i, name in enumerate(unity["names"]) if i < len(engine["names"]) and engine["names"][i] == name]
    report["skinMatrix"] = float(max(np.abs(unity["skin"][i] - engine["skin"][i]).max() for i in shared)) if shared else None
    report["formula"] = float(nearest(engine["blend"], unity["blend"]).max() * 1000)
    report["paired"], report["weightsDiffer"] = compare_weights(unity, engine)
    report["unityOn31stGrid"] = on_grid(unity["weights"], 31)
    report["engineOn31stGrid"] = on_grid(engine["weights"], 31)
    if "baked" in unity:
        report["bakeVsBlend"] = float(nearest(unity["baked"], unity["blend"]).max() * 1000)
        report["bakeVsFirst"] = float(nearest(unity["baked"], unity["first"]).max() * 1000)
    return report


# ---------------------------------------------------------------- silhouettes

def silhouette_mask(path):
    """True where a captured frame shows the flat magenta mesh."""
    image = np.asarray(Image.open(path).convert("RGB"), dtype=np.int16)
    return (image[..., 0] > 128) & (image[..., 2] > 128) & (image[..., 1] < 128)


def overlap(a, b):
    """Intersection over union of two masks (1 when both are empty)."""
    union = int((a | b).sum())
    return float((a & b).sum()) / union if union else 1.0


def project(points, camera, rotation, vertical_fov, aspect):
    """World points to normalised image coordinates (0..1, y down) through a Unity camera:
    it looks down its +Z with +Y up. `rotation` is the camera's rotation matrix (rows = axes)."""
    view = (points - camera) @ rotation
    depth = np.maximum(view[:, 2], 1e-6)
    half = math.tan(math.radians(vertical_fov) / 2)
    return np.stack([(view[:, 0] / depth / (half * aspect) + 1) / 2,
                     (1 - view[:, 1] / depth / half) / 2], axis=1)


def raster(points, triangles, width, height, positive_area, supersample=4):
    """A mask of the triangles whose projected signed area (y down) has the given sign. It is drawn
    at `supersample` x and reduced by half coverage, which is closer to what a GPU rasteriser fills
    than a one-sample polygon fill: with one sample the overlaps sit about 0.02 lower on hair cards."""
    image = Image.new("1", (width * supersample, height * supersample), 0)
    draw = ImageDraw.Draw(image)
    for a, b, c in triangles:
        p, q, r = points[a], points[b], points[c]
        area = (q[0] - p[0]) * (r[1] - p[1]) - (r[0] - p[0]) * (q[1] - p[1])
        if (area > 0) != positive_area:
            continue
        k = width * supersample, height * supersample
        draw.polygon([(p[0] * k[0], p[1] * k[1]), (q[0] * k[0], q[1] * k[1]),
                      (r[0] * k[0], r[1] * k[1])], fill=1)
    coverage = np.asarray(image, dtype=bool).reshape(height, supersample, width, supersample)
    return coverage.mean(axis=(1, 3)) >= 0.5


def silhouette_report(unity, engine, shot, width, height, vertical_fov, separable=0.995):
    """Overlaps of the two rendered silhouettes with each other and with the projections, and which
    reading each renderer drew. The reading is only named when the two projections differ (their
    overlap below `separable`); otherwise no rendered frame can tell them apart."""
    camera = np.asarray(shot["cameraPosition"], dtype=np.float64)
    rotation = quaternion_matrix(shot["cameraRotation"])
    masks = {}
    for name, side in (("unity", unity), ("engine", engine)):
        for reading in ("blend", "first"):
            world = side[reading] @ side["toWorld"][:3, :3].T + side["toWorld"][:3, 3]
            pixels = project(world, camera, rotation, vertical_fov, width / height)
            masks[name, reading] = [raster(pixels, side["triangles"], width, height, sign)
                                    for sign in (True, False)]
    # Both engines draw the same index buffer; the winding that reproduces the engine's own frame
    # is the front-face winding for every projection.
    use_positive = overlap(engine["mask"], masks["engine", "blend"][0]) >= overlap(engine["mask"], masks["engine", "blend"][1])
    index = 0 if use_positive else 1
    result = {"winding": "positive" if use_positive else "negative",
              "unityPixels": int(unity["mask"].sum()), "enginePixels": int(engine["mask"].sum()),
              "gpu": overlap(unity["mask"], engine["mask"])}
    separations = [1 - overlap(masks[name, "blend"][index], masks[name, "first"][index])
                   for name in ("unity", "engine")]
    result["readingsApart"] = max(separations)
    for name, side in (("unity", unity), ("engine", engine)):
        for reading in ("blend", "first"):
            result[name + reading.capitalize()] = overlap(side["mask"], masks[name, reading][index])
        blend, first = result[name + "Blend"], result[name + "First"]
        if result["readingsApart"] < 1 - separable:
            result[name + "Reading"] = "not separable"
        else:
            result[name + "Reading"] = "blend" if blend >= first else "first"
        result[name + "Margin"] = abs(blend - first)
    return result


# ---------------------------------------------------------------- loading

def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def as_vertices(flat):
    return np.asarray(flat, dtype=np.float64).reshape(-1, 3)


def leaf_names(paths):
    return [path.rsplit("/", 1)[-1] for path in paths]


def engine_side(renderer):
    raw = as_vertices(renderer["raw"])
    indices = np.asarray(renderer["boneIndices"], dtype=np.float64).reshape(-1, INFLUENCES).astype(np.int64) - 1
    weights = np.asarray(renderer["boneWeights"], dtype=np.float64).reshape(-1, INFLUENCES)
    to_world = np.linalg.inv(matrices(renderer["worldToLocal"], "columns")[0])
    skin_matrices_ = engine_skin(renderer)
    blend, first = skin(raw, indices, weights, skin_matrices_)
    return {"mesh": renderer["mesh"], "node": renderer["node"], "raw": raw, "indices": indices, "weights": weights,
            "names": leaf_names(renderer["bones"]), "skin": skin_matrices_, "blend": blend, "first": first,
            "toWorld": to_world, "triangles": np.asarray(renderer["triangles"], dtype=np.int64).reshape(-1, 3)}


def engine_skin(renderer):
    return np.einsum("ij,njk,nkl->nil", matrices(renderer["worldToLocal"], "columns")[0],
                     matrices(renderer["boneWorld"], "columns"), matrices(renderer["bindposes"], "columns"))


def unity_side(renderer, transforms):
    raw = as_vertices(renderer["raw"])
    # Unity rows are (bone index x4, weight x4) per vertex.
    rows = np.asarray(renderer["weights"], dtype=np.float64).reshape(-1, 8)
    indices, weights = rows[:, :INFLUENCES].astype(np.int64), rows[:, INFLUENCES:]
    worlds = world_matrices(transforms)
    matrices_ = skin_matrices(matrices(renderer["smrToWorld"], "rows")[0],
                              [worlds[path] for path in renderer["bones"]],
                              matrices(renderer["bindposes"], "rows"))
    blend, first = skin(raw, indices, weights, matrices_)
    return {"mesh": renderer["mesh"], "raw": raw, "indices": indices, "weights": weights,
            "names": leaf_names(renderer["bones"]), "skin": matrices_, "blend": blend, "first": first,
            "baked": as_vertices(renderer["baked"]), "toWorld": matrices(renderer["smrToWorld"], "rows")[0],
            "triangles": np.asarray(renderer["triangles"], dtype=np.int64).reshape(-1, 3)}


def compare(export, unity_skin, engine_skin, silhouettes=None, models=None):
    """One report per mesh present on both sides. `models` maps a hero to its FBX file; when the
    file can be read, every control point's weights are also compared with the engine's."""
    heroes = {hero["name"]: hero for hero in export["heroes"]}
    reports = []
    for name in sorted(heroes):
        if name not in unity_skin or name not in engine_skin:
            continue
        by_mesh = {renderer["mesh"]: renderer for renderer in unity_skin[name]["skinnedMeshes"]}
        file_skins = {}
        if models and models.get(name):
            _, roots = fbx.parse(Path(models[name]).read_bytes())
            file_skins = {skin["mesh"]: skin for skin in fbx.skins(roots)}
        for engine_renderer in engine_skin[name]["renderers"]:
            unity_renderer = by_mesh.get(engine_renderer["mesh"])
            if unity_renderer is None:
                continue
            engine, unity = engine_side(engine_renderer), unity_side(unity_renderer, heroes[name]["transforms"])
            report = {"hero": name, **compare_renderer(unity, engine)}
            if engine_renderer["mesh"] in file_skins:
                weighted, matching, sign = fbx_agreement(file_skins[engine_renderer["mesh"]], engine)
                report["fbx"] = {"weightedControlPoints": weighted, "sameWeightsInEngine": matching,
                                 "engineX": "-x" if sign < 0 else "x"}
            if silhouettes:
                unity_path = silhouettes["unity"] / name / (engine_renderer["mesh"] + ".png")
                engine_path = silhouettes["engine"] / name / (engine_renderer["node"] + ".png")
                if unity_path.exists() and engine_path.exists():
                    unity["mask"], engine["mask"] = silhouette_mask(unity_path), silhouette_mask(engine_path)
                    report["silhouette"] = silhouette_report(
                        unity, engine, silhouettes["shots"][name], silhouettes["width"],
                        silhouettes["height"], silhouettes["verticalFov"])
            reports.append(report)
    return reports


def print_report(reports, with_silhouettes):
    header = (f"{'hero / mesh':28s} {'raw':>7s} {'formula':>8s} {'weights':>11s} {'fbx=eng':>10s} "
              f"{'U 1/31':>6s} {'b/blend':>8s} {'b/first':>8s}")
    if with_silhouettes:
        header += f" {'GPU/GPU':>8s} {'U blend':>8s} {'U first':>8s} {'E blend':>8s} {'E first':>8s}"
    print(header)
    for r in reports:
        name = f"{r['hero'][5:]}/{r['mesh'].split('_', 2)[-1]}"
        line = f"{name:28s} {r['raw']:5.2f}mm {r['formula']:6.2f}mm {r['weightsDiffer']:5d}/{r['paired']:<5d}"
        if "fbx" in r:
            line += f" {r['fbx']['sameWeightsInEngine']:4d}/{r['fbx']['weightedControlPoints']:<5d}"
        else:
            line += f" {'-':>10s}"
        line += f" {r['unityOn31stGrid'] * 100:5.0f}%"
        if "bakeVsBlend" in r:
            line += f" {r['bakeVsBlend']:6.1f}mm {r['bakeVsFirst']:6.1f}mm"
        if "silhouette" in r:
            s = r["silhouette"]
            line += (f" {s['gpu']:8.3f} {s['unityBlend']:8.3f} {s['unityFirst']:8.3f}"
                     f" {s['engineBlend']:8.3f} {s['engineFirst']:8.3f}")
        print(line)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--export", type=Path, default=DEFAULTS["export"], help="Unity's heroes.json")
    parser.add_argument("--unity-skin", type=Path, default=DEFAULTS["unity_skin"])
    parser.add_argument("--engine-skin", type=Path, default=DEFAULTS["engine_skin"])
    parser.add_argument("--tolerance-mm", type=float, default=2.0, help="allowed maximum of the raw and formula checks")
    parser.add_argument("--silhouettes", action="store_true", help="also compare the rendered silhouettes")
    parser.add_argument("--unity-silhouettes", type=Path, default=DEFAULTS["unity_silhouettes"])
    parser.add_argument("--engine-silhouettes", type=Path, default=DEFAULTS["engine_silhouettes"])
    parser.add_argument("--report", type=Path, help="write every metric to this JSON file")
    parser.add_argument("--no-fbx", action="store_true", help="skip reading the hero FBX files")
    args = parser.parse_args(argv)

    export = load(args.export)
    unity_skin = {path.stem[len("skin_"):]: load(path) for path in sorted(args.unity_skin.glob("skin_*.json"))}
    engine_skin = {path.stem[len("skin_"):]: load(path) for path in sorted(args.engine_skin.glob("skin_*.json"))}
    models = None if args.no_fbx else {
        hero["name"]: path for hero in export["heroes"]
        if (path := REPO_ROOT / "Assets" / "Heroes" / hero["name"] / f"{hero['name']}.fbx").exists()}
    silhouettes = None
    if args.silhouettes:
        shots = load(DEFAULTS["unity_shots"])
        silhouettes = {
            "unity": args.unity_silhouettes, "engine": args.engine_silhouettes,
            "width": shots["width"], "height": shots["height"], "verticalFov": shots["verticalFov"],
            "shots": {shot["name"]: shot for shot in shots["shots"]},
        }
    reports = compare(export, unity_skin, engine_skin, silhouettes, models)
    if not reports:
        print("no mesh is present on both sides; run the exports named at the top of this file")
        return 2
    print_report(reports, args.silhouettes)
    if args.silhouettes:
        print("\nWhat each renderer drew (the reading with the larger overlap; 'not separable' when the")
        print("two readings project to the same silhouette):")
        for r in reports:
            if "silhouette" in r:
                s = r["silhouette"]
                print(f"  {r['hero']}/{r['mesh']}: Unity {s['unityReading']} (margin {s['unityMargin']:.3f}), "
                      f"engine {s['engineReading']} (margin {s['engineMargin']:.3f}), readings {s['readingsApart'] * 100:.1f}% apart")
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(reports, indent=2) + "\n", encoding="utf-8")
        print(f"\nwrote {args.report}")
    failed = [f"{r['hero']}/{r['mesh']}" for r in reports if r["raw"] > args.tolerance_mm or r["formula"] > args.tolerance_mm]
    if failed:
        print(f"\nFAILED (over {args.tolerance_mm:g} mm): " + ", ".join(failed))
        return 1
    print(f"\nOK: every mesh within {args.tolerance_mm:g} mm")
    return 0


if __name__ == "__main__":
    sys.exit(main())
