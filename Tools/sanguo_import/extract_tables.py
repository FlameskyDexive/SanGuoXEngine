# -*- coding: utf-8 -*-
"""Extracts the 桃园 battle scope's Lua data tables into JSON intermediates for M4.

    py -3 Tools/sanguo_import/extract_tables.py

Reads ONLY the Unity project's Lua data (`StreamingAssets/LuaScripts/Data/`, plus the level layout
for stage 1011) through the shared `lua_parse.py` parser and writes:

  Export/data/skills.json            SkillData, all rows (61 named columns)
  Export/data/call-bullets.json      CallBulletData (the things skill events spawn)
  Export/data/missiles.json          MissileData (hit shapes, movement, lifetimes)
  Export/data/monsters.json          MonsterData (黄巾小兵 / 吕布 boss …)
  Export/data/monster-models.json    MonsterModelData (model paths, shove diameters, scales)
  Export/data/monster-values.json    MonsterValueData (per-group attributes)
  Export/data/anim-sounds.json       AnimSound (animation → sound events)
  Export/data/level-1011.json        LevelLayoutData1011 (the stage's spawn layout, named fields)
  Export/data/extract-report.json    counts + schema notes

The JSON files are intermediate sources for the editor-side SanguoDataGenerator and are
gitignored; the runtime never reads them. Zero Lua reaches the repository or the runtime.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from sanguo_import import lua_parse
from sanguo_import.import_heroes import load_config

REPO_ROOT = Path(__file__).resolve().parents[2]

TABLES = [
    # (lua sheet name, output stem)
    ("SkillData", "skills"),
    ("CallBulletData", "call-bullets"),
    ("MissileData", "missiles"),
    ("MonsterData", "monsters"),
    ("MonsterModelData", "monster-models"),
    ("MonsterValueData", "monster-values"),
    ("AnimSound", "anim-sounds"),
]


def _strip_fixed_point(text):
    """Rewrites `FP.newV2(a, b)` / `FP.newV1(v)` fixed-point wrappers as plain Lua table
    constructors so the standard parser can read the level layout; vectors become arrays."""
    text = re.sub(r"FP\.newV2\(([^()]*?)\)", r"{\1}", text)
    text = re.sub(r"FP\.newV1\(([^()]*?)\)", r"{\1}", text)
    return text


def parse_level_layout(path):
    """LevelLayoutData1011.lua: named fields (`LevelID`, `LevelItemList` of item dicts with
    `ItemPos`/`ItemDir` fixed-point vectors, nested `WeightMonsterIDList`)."""
    text = Path(path).read_text(encoding="utf-8", errors="replace")
    block = lua_parse.extract_table_block(_strip_fixed_point(text), "_layoutTable")
    if block is None:
        raise ValueError(f"no _layoutTable in {path}")
    return lua_parse.parse_lua_table(block)


def extract(data_dir, out_dir):
    out_dir.mkdir(parents=True, exist_ok=True)
    report = {"tables": {}, "level": {}}
    for sheet_name, stem in TABLES:
        try:
            sheet = lua_parse.load_sheet(str(data_dir), sheet_name)
        except FileNotFoundError:
            report["tables"][stem] = {"rows": 0, "fields": 0, "skipped": "source file missing"}
            print(f"{stem:<16} skipped (source file missing)")
            continue
        rows = [{k: v for k, v in row.items() if k != "_raw"} for row in sheet.rows]
        out = out_dir / f"{stem}.json"
        out.write_text(json.dumps(rows, ensure_ascii=False), encoding="utf-8", newline="\n")
        report["tables"][stem] = {"rows": len(rows), "fields": len(sheet.property_table)}
        print(f"{stem:<16} {len(rows):>6} rows, {len(sheet.property_table)} fields -> {out.name}")

    layout_path = Path(data_dir).parent / "Battle" / "Logic" / "Level" / "LevelItemAndEventData" / "LevelLayoutData1011.lua"
    layout = parse_level_layout(layout_path)
    out = out_dir / "level-1011.json"
    out.write_text(json.dumps(layout, ensure_ascii=False), encoding="utf-8", newline="\n")
    items = layout.get("LevelItemList") or []
    monster_ids = sorted({m["MonsterID"]
                          for item in items
                          for m in (item.get("WeightMonsterIDList") or [])
                          if isinstance(m, dict) and "MonsterID" in m})
    report["level"] = {"levelID": layout.get("LevelID"), "items": len(items), "monsterIDs": monster_ids}
    print(f"{'level-1011':<16} {len(items):>6} items, monsters {monster_ids} -> level-1011.json")

    report_path = out_dir / "extract-report.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return report


def main(argv=None):
    config = load_config()
    data_dir = Path(config["jws2_client"]) / "Assets/StreamingAssets/LuaScripts/Data"
    out_dir = REPO_ROOT / config.get("data_extract", "Export/data")
    report = extract(data_dir, out_dir)
    total = sum(t["rows"] for t in report["tables"].values())
    print(f"total {total} rows -> {out_dir.relative_to(REPO_ROOT).as_posix()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
