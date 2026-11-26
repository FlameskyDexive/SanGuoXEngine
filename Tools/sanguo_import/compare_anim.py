# -*- coding: utf-8 -*-
"""compare_anim.py — the engine's played poses against Unity's sampled poses, clip by clip.

Unity's side is the clip export (``Tools/unity_export/scripts/export_clips.csx`` → ``Export/clips/
Hero_*.json``): for every battle clip, the local transform of every node at fixed times, sampled by
Unity's own clip evaluation. The engine's side is ``SanguoCaptureStudio.CapturePoses`` →
``poses.json``: the same nodes at the same times, read back from a hero whose Animator played the
generated clip (root motion included).

    py -3 Tools/sanguo_import/compare_anim.py                  # Export/clips vs Export/xengine-shots/clips
    py -3 Tools/sanguo_import/compare_anim.py --engine <dir with poses.json> --out <report.json>
    py -3 Tools/sanguo_import/compare_anim.py --sheets <dir>   # also write Unity | engine contact sheets

The rendered frames are compared too: the extent (top, bottom, left, right) of the rendered hero in
every Unity | engine frame pair, a check on the whole pipeline — camera, root motion, skinning — that
does not depend on shading (the engine's materials are darker than the game's, see M1). Pairs beyond
FRAME_TOLERANCE_PX are listed; with --strict-frames they fail the run.

Exit code 1 when a deviation is over tolerance. Python is an offline tool: nothing here ships.
"""

import argparse
import json
import math
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:  # pragma: no cover - the frame checks and --sheets need Pillow
    Image = None

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
UNITY_DEFAULT = REPO_ROOT / "Export" / "clips"
ENGINE_DEFAULT = REPO_ROOT / "Export" / "xengine-shots" / "clips"

# Unity stores its keys with four decimals; the rotation error is float rounding. The root-motion
# node and the actor root carry Unity's own frame-granular residual.
ROTATION_TOLERANCE_DEG = 0.5
POSITION_TOLERANCE_MM = 5.0
ROOT_TOLERANCE_MM = 30.0
FRAME_TOLERANCE_PX = 24


def angle_degrees(a, b):
    """Angle between two quaternions given as [x, y, z, w], whatever their sign or length."""
    dot = abs(sum(x * y for x, y in zip(a, b)))
    na = math.sqrt(sum(x * x for x in a))
    nb = math.sqrt(sum(x * x for x in b))
    if na == 0 or nb == 0:
        return 0.0
    return math.degrees(2 * math.acos(min(1.0, dot / (na * nb))))


def distance(a, b):
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def compare_document(unity, engine):
    """One hero. Returns a dict with the worst deviations and where they occur."""
    result = {"clips": 0, "rotations": 0, "positions": 0, "roots": 0, "missing": [],
              "rotation": 0.0, "position": 0.0, "root": 0.0, "rotationAt": None, "positionAt": None, "rootAt": None,
              "perClip": {}}
    # Unity's export describes every node; the engine dump lists their paths in the same order.
    nodes = [node["path"] for node in unity["nodes"]]
    if engine.get("nodes") != nodes:
        result["missing"].append("the engine dumped different nodes than Unity sampled")
        return result
    for clip in unity["clips"]:
        action = clip["action"]
        played = engine.get("clips", {}).get(action)
        if played is None:
            result["missing"].append(action)
            continue
        times = clip["samples"]["times"]
        if len(played["poses"]) != len(times) or max(abs(a - b) for a, b in zip(played["times"], times)) > 1e-4:
            result["missing"].append(f"{action} (sampled at other times)")
            continue
        clip_stat = {"rotation": 0.0, "position": 0.0, "root": 0.0}
        result["clips"] += 1
        for ti in range(1, len(times)):
            unity_poses = clip["samples"]["poses"][ti]
            engine_poses = played["poses"][ti]
            for n, path in enumerate(nodes):
                up, ep = unity_poses[n], engine_poses[n]
                where = f"{action} '{path or '<root>'}' t={times[ti]:.4f}"
                if path:
                    error = angle_degrees(up[1], ep[1])
                    result["rotations"] += 1
                    clip_stat["rotation"] = max(clip_stat["rotation"], error)
                    if error > result["rotation"]:
                        result["rotation"], result["rotationAt"] = error, where
                error = distance(up[0], ep[0]) * 1000
                if path in ("", "Bip001"):
                    # The actor root and the root-motion node carry Unity's frame-granular residual.
                    result["roots"] += 1
                    clip_stat["root"] = max(clip_stat["root"], error)
                    if error > result["root"]:
                        result["root"], result["rootAt"] = error, where
                else:
                    result["positions"] += 1
                    clip_stat["position"] = max(clip_stat["position"], error)
                    if error > result["position"]:
                        result["position"], result["positionAt"] = error, where
        result["perClip"][action] = clip_stat
    return result


def within(stat):
    return stat["rotation"] <= ROTATION_TOLERANCE_DEG and stat["position"] <= POSITION_TOLERANCE_MM \
        and stat["root"] <= ROOT_TOLERANCE_MM and not stat["missing"]


def silhouette(path, threshold=24):
    """Boolean mask of the hero in a studio frame: every pixel that differs from the backdrop
    (read from the frame's corner, so Unity's and the engine's encoding of the same grey both work)."""
    import numpy as np
    pixels = np.asarray(Image.open(path).convert("RGB")).astype(int)
    return np.abs(pixels - pixels[2, 2]).sum(axis=2) > threshold


def extent(mask):
    """(top, bottom, left, right) of a mask in pixels, or None when it is empty."""
    import numpy as np
    rows = np.where(mask.any(axis=1))[0]
    columns = np.where(mask.any(axis=0))[0]
    if not len(rows) or not len(columns):
        return None
    return int(rows.min()), int(rows.max()), int(columns.min()), int(columns.max())


def compare_frames(unity, frame_dir_unity, frame_dir_engine):
    """The rendered hero's extent in every frame pair of a hero: (worst, mean, count, outliers),
    the first two in pixels, the outliers the pairs beyond FRAME_TOLERANCE_PX."""
    import numpy as np
    deviations, outliers = [], []
    for clip in unity["clips"]:
        for frame in clip.get("frames", []):
            left = Path(frame_dir_unity) / frame["file"]
            right = Path(frame_dir_engine) / frame["file"]
            if not (left.exists() and right.exists()):
                continue
            a, b = extent(silhouette(left)), extent(silhouette(right))
            if a is None or b is None:
                continue
            deviation = int(np.abs(np.array(a) - np.array(b)).max())
            deviations.append(deviation)
            if deviation > FRAME_TOLERANCE_PX:
                outliers.append(f"{frame['file']} {deviation}px")
    if not deviations:
        return None
    return max(deviations), sum(deviations) / len(deviations), len(deviations), outliers


def contact_sheets(unity_dir, engine_dir, out_dir, cell=128, rows=13):
    """Unity | engine, the three key poses of every clip, `rows` clips per PNG and one PNG set
    per hero. The label column names the clip and the sampled times of its three poses."""
    written = []
    label = 150
    for engine_file in sorted(Path(engine_dir).glob("*/")):
        hero = engine_file.name
        export = Path(unity_dir) / f"{hero}.json"
        if not export.exists():
            continue
        clips = load(export)["clips"]
        height = round(cell * 768 / 512)
        for page in range(0, len(clips), rows):
            batch = clips[page:page + rows]
            sheet = Image.new("RGB", (6 * (cell + 4) + label, len(batch) * (height + 4)), (32, 32, 32))
            draw = ImageDraw.Draw(sheet)
            for r, clip in enumerate(batch):
                y = r * (height + 4)
                times = clip["samples"]["times"]
                frames = sorted((f["sample"], f["file"]) for f in clip.get("frames", []))
                draw.text((4, y + 4), clip["action"], fill=(230, 230, 230))
                draw.text((4, y + 22), "Unity | engine", fill=(150, 150, 150))
                draw.text((4, y + 38), "\n".join(f"{times[sample]:.2f} s" for sample, _ in frames), fill=(200, 200, 200))
                for c, (sample, name) in enumerate(frames):
                    for side, folder in enumerate((Path(unity_dir) / hero, engine_file)):
                        path = folder / name
                        if not path.exists():
                            continue
                        with Image.open(path) as image:
                            image = image.convert("RGB").resize((cell, height), Image.LANCZOS)
                            sheet.paste(image, (label + (c * 2 + side) * (cell + 4), y))
            target = Path(out_dir) / f"{hero}-{page // rows + 1}.png"
            target.parent.mkdir(parents=True, exist_ok=True)
            sheet.save(target, optimize=True)
            written.append(target)
    return written


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--unity", default=str(UNITY_DEFAULT), help="folder with the Unity clip export")
    parser.add_argument("--engine", default=str(ENGINE_DEFAULT), help="folder with poses.json")
    parser.add_argument("--out", help="write the report to this JSON file")
    parser.add_argument("--sheets", help="also write Unity | engine contact sheets to this folder")
    parser.add_argument("--cell", type=int, default=128, help="width of one frame in a contact sheet")
    parser.add_argument("--rows", type=int, default=13, help="clips per contact sheet")
    parser.add_argument("--strict-frames", action="store_true",
                        help=f"fail when a frame pair's hero extent differs by more than {FRAME_TOLERANCE_PX} px")
    args = parser.parse_args(argv)

    engine_path = Path(args.engine) / "poses.json"
    if not engine_path.exists():
        sys.exit(f"[error] no {engine_path}; run Tools/xengine_live/evals/capture_poses.cs first")
    engine = load(engine_path)
    report = {"unity": args.unity, "engine": args.engine, "tolerances": {
        "rotationDeg": ROTATION_TOLERANCE_DEG, "positionMm": POSITION_TOLERANCE_MM, "rootMm": ROOT_TOLERANCE_MM,
        "frameExtentPx": FRAME_TOLERANCE_PX, "strictFrames": args.strict_frames}, "heroes": {}}
    failed = False
    for export in sorted(Path(args.unity).glob("Hero_*.json")):
        hero = export.stem
        unity = load(export)
        stat = compare_document(unity, engine.get(hero, {}))
        if Image:
            stat["frames"] = compare_frames(unity, Path(args.unity) / hero, Path(args.engine) / hero)
        report["heroes"][hero] = stat
        failed |= not within(stat)
        if stat["frames"] and stat["frames"][3] and args.strict_frames:
            failed = True
        frames = ""
        if stat["frames"]:
            frames = f"; {stat['frames'][2]} frame pairs, hero extent within {stat['frames'][0]} px (mean {stat['frames'][1]:.1f})"
            if stat["frames"][3]:
                frames += f"; BEYOND {FRAME_TOLERANCE_PX} px: {', '.join(stat['frames'][3])}"
        print(f"{hero}: {stat['clips']} clips — worst rotation {stat['rotation']:.4f}° ({stat['rotationAt']}), "
              f"worst bone position {stat['position']:.3f} mm ({stat['positionAt']}), worst root-motion position "
              f"{stat['root']:.1f} mm ({stat['rootAt']}); {stat['rotations']} rotations, {stat['positions']} positions, "
              f"{stat['roots']} roots compared" + frames + (f"; MISSING {stat['missing']}" if stat["missing"] else ""))
    if args.out:
        Path(args.out).parent.mkdir(parents=True, exist_ok=True)
        Path(args.out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    if args.sheets:
        if Image is None:
            sys.exit("[error] --sheets needs Pillow")
        for path in contact_sheets(args.unity, args.engine, args.sheets, args.cell, args.rows):
            print(f"sheet {path}")
    print("PASS" if not failed else "FAIL: a deviation is over tolerance")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
