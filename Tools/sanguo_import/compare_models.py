# -*- coding: utf-8 -*-
"""Compares the engine's imported hero models with the Unity export, node by node.

    py -3 Tools/xengine_live/mcp_live.py --out <dump.json> --eval-file Tools/xengine_live/evals/inspect_models.cs
    py -3 Tools/sanguo_import/compare_models.py <dump.json> [--export Export/heroes/heroes.json]

The dump holds the hierarchy the engine's FBX importer produced (see inspect_models.cs); the export
holds Unity's transform tree with local TRS. For every hero this reports:

  - which nodes exist on one side only,
  - the largest difference of root-space (world) position and of local scale over shared nodes,
  - per skinned mesh: bone count and material slot count on both sides. The engine drops bones no
    vertex is weighted to (stripBones), so its count can be lower than Unity's.

Unity composes a node's root-space position here from its local TRS chain, which is what Unity's
own Transform does. A scale or axis mistake in the import shows up immediately as a position
difference that grows with distance from the root.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
POSITION_TOLERANCE = 0.005   # metres
SCALE_TOLERANCE = 0.001


def quat_rotate(q, v):
    """Rotates v by quaternion q = (x, y, z, w), without numpy."""
    qx, qy, qz, qw = q
    vx, vy, vz = v
    # t = 2 * cross(q.xyz, v)
    tx = 2 * (qy * vz - qz * vy)
    ty = 2 * (qz * vx - qx * vz)
    tz = 2 * (qx * vy - qy * vx)
    return (
        vx + qw * tx + (qy * tz - qz * ty),
        vy + qw * ty + (qz * tx - qx * tz),
        vz + qw * tz + (qx * ty - qy * tx),
    )


def quat_multiply(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def unity_world_transforms(transforms):
    """Path -> root-space position for Unity's transform list."""
    by_path = {t["path"]: t for t in transforms}
    cache = {}

    def world(path):
        if path in cache:
            return cache[path]
        node = by_path[path]
        parent = node.get("parent")
        position, rotation, scale = node["position"], node["rotation"], list(node["scale"])
        if parent:
            parent_position, parent_rotation, parent_scale = world(parent)
            scaled = tuple(position[i] * parent_scale[i] for i in range(3))
            offset = quat_rotate(parent_rotation, scaled)
            position = tuple(parent_position[i] + offset[i] for i in range(3))
            rotation = quat_multiply(parent_rotation, rotation)
            scale = tuple(scale[i] * parent_scale[i] for i in range(3))
        cache[path] = (position, rotation, scale)
        return cache[path]

    return {path: world(path)[0] for path in by_path}


def parse_dump(text):
    """Hero name -> {"nodes": {path -> dict}, "header": str} from inspect_models.cs output."""
    heroes = {}
    current = None
    for line in text.splitlines():
        if line.startswith("## "):
            header = line[3:]
            current = {"header": header, "nodes": {}}
            heroes[header.split("|")[0].strip()] = current
            continue
        if not line.strip() or current is None:
            continue
        if line.lstrip().startswith("sub: "):
            continue  # a sub-asset of the model (mesh or avatar), not a node
        parts = line.split("|")
        components, _, skinned = parts[4].partition("skinned: ")
        node = {
            "local": tuple(float(v) for v in parts[1].split(",")),
            "world": tuple(float(v) for v in parts[2].split(",")),
            "scale": tuple(float(v) for v in parts[3].split(",")),
            "components": [c for c in components.split(",") if c],
        }
        if skinned:
            fields = [f.strip() for f in skinned.split(",")]
            node["skinned"] = {"mesh": fields[0]}
            for field in fields[1:]:
                key, _, value = field.partition("=")
                node["skinned"][key] = int(value)
        current["nodes"][parts[0]] = node
    return heroes


def compare(export, dump_text):
    report = []
    dumps = parse_dump(dump_text)
    for hero in export["heroes"]:
        name = hero["name"]
        dump = next((d for key, d in dumps.items() if Path(key).stem == name), None)
        entry = {"hero": name, "found": dump is not None}
        report.append(entry)
        if dump is None or not dump["nodes"]:
            continue
        unity = unity_world_transforms(hero["transforms"])
        unity_local_scale = {t["path"]: t["scale"] for t in hero["transforms"]}
        engine = dump["nodes"]

        unity_paths, engine_paths = set(unity), set(engine)
        entry["unityNodes"], entry["engineNodes"] = len(unity_paths), len(engine_paths)
        entry["missingInEngine"] = sorted(unity_paths - engine_paths)
        entry["extraInEngine"] = sorted(engine_paths - unity_paths)

        worst_position, worst_scale = (0.0, None), (0.0, None)
        for path in sorted(unity_paths & engine_paths):
            u_position, u_scale = unity[path], unity_local_scale[path]
            e_position, e_scale = engine[path]["world"], engine[path]["scale"]
            distance = math.dist(u_position, e_position)
            if distance > worst_position[0]:
                worst_position = (distance, path)
            scale_gap = max(abs(u_scale[i] - e_scale[i]) for i in range(3))
            if scale_gap > worst_scale[0]:
                worst_scale = (scale_gap, path)
        entry["sharedNodes"] = len(unity_paths & engine_paths)
        entry["maxPositionDelta"] = {"metres": worst_position[0], "node": worst_position[1]}
        entry["maxLocalScaleDelta"] = {"value": worst_scale[0], "node": worst_scale[1]}

        skinned = []
        for smr in hero["skinnedMeshes"]:
            path = smr["node"]
            node = engine.get(path)
            skinned.append({
                "node": path,
                "unityVertices": smr.get("vertexCount"),
                "engineVertices": node["skinned"].get("v") if node and "skinned" in node else None,
                "unityBones": len(smr["bones"]),
                "engineBones": node["skinned"].get("bones") if node and "skinned" in node else None,
                "unityMaterials": sum(1 for s in (smr.get("materials") or []) if s and s.get("usedBySubMesh", True)),
                "engineMaterials": node["skinned"].get("mats") if node and "skinned" in node else None,
            })
        entry["skinnedMeshes"] = skinned
        entry["engineAnimator"] = any("Animator" in node["components"] for node in engine.values())
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("dump", help="--out file of mcp_live.py after running inspect_models.cs")
    parser.add_argument("--export", default="Export/heroes/heroes.json")
    args = parser.parse_args(argv)

    results = json.loads(Path(args.dump).read_text(encoding="utf-8"))
    text = ""
    for result in results:
        if result.get("ok") and result.get("text", "").startswith("{"):
            try:
                text += json.loads(result["text"]).get("result") or ""
            except json.JSONDecodeError:
                pass
    export = json.loads((REPO_ROOT / args.export).read_text(encoding="utf-8"))
    report = compare(export, text)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    # Every node on both sides, positions within 5 mm (the FBX float precision of a long bone
    # chain) and identical local scales.
    ok = all(r.get("found") and not r.get("missingInEngine") and not r.get("extraInEngine")
             and r.get("maxPositionDelta", {}).get("metres", 1) < POSITION_TOLERANCE
             and r.get("maxLocalScaleDelta", {}).get("value", 1) < SCALE_TOLERANCE for r in report)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
