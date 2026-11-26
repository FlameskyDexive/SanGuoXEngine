# -*- coding: utf-8 -*-
"""clip_manifest.py — which animation clips the port takes from the Unity project.

jws2 stores every hero's clips next to the hero's FBX under
``Assets/Actor/AnimationSplit/Animation_L/<Hero>/``, named ``<Hero>_<action>.anim``. Only the
clips the 桃园吕布 Boss 战 needs are exported; the plot (``NPC_Plot_*``), riding (``ride_*``),
city (``*_city_loop``) and boss-skin (``Boss_*``) clips are not.

This module is offline tooling: the Unity exporter (``run_export.py run export_clips``) resolves
the patterns against the clip files on disk and hands the concrete list to the script that runs
inside the editor.

    py -3 Tools/unity_export/clip_manifest.py Hero_LvBu            # list the clips of one hero
    py -3 Tools/unity_export/clip_manifest.py --all
"""

import fnmatch
import sys

HEROES = ["Hero_ZhaoYun", "Hero_GuanYu", "Hero_LiuBei", "Hero_ZhangFei", "Hero_LvBu"]

# Clip names after the hero's own prefix ("<Hero>_"), in the order the port groups them.
# A trailing "*" matches any remainder.
BATTLE_CLIPS = [
    # locomotion
    "idle_loop", "run_loop", "run_stop", "dodge", "RunAttack",
    # normal attacks, skills, the ultimate ("SA")
    "NormalAttack_*", "SkillAttack_*", "SA_*",
    # hit reactions
    "hurt", "bighurt", "knockback", "hitdown", "fly", "standup", "dizzy_loop",
    # death, victory, entering and leaving the arena
    "die", "win", "getin", "getout",
]

# Matched by a pattern above but not wanted: the camera's own animation of the ultimate.
EXCLUDED = ["SA_cam"]


def matches(name, prefix, patterns):
    """True when ``name`` is ``prefix`` + one of ``patterns`` (``*`` = any remainder)."""
    if not name.startswith(prefix):
        return False
    return any(fnmatch.fnmatchcase(name[len(prefix):], pattern) for pattern in patterns)


def select(names, hero):
    """The clips of ``hero`` the port takes, in manifest order, from the available ``names``."""
    prefix = hero + "_"
    available = set(names)
    result = []
    for pattern in BATTLE_CLIPS:
        found = sorted(n for n in names if matches(n, prefix, [pattern]) and n[len(prefix):] not in EXCLUDED)
        # Exact names keep their order; a pattern's matches sort by name.
        result.extend(found)
    return [n for n in result if n in available]


def missing(names, hero):
    """Exact names (no ``*``) in the manifest that have no clip file for ``hero``."""
    prefix = hero + "_"
    return [p for p in BATTLE_CLIPS if "*" not in p and prefix + p not in set(names)]


def unmatched_patterns(names, hero):
    """Manifest patterns (``*`` ones included) that match no clip file of ``hero``; ``SA_*`` counts
    as matched by its non-excluded clips."""
    prefix = hero + "_"
    return [p for p in BATTLE_CLIPS
            if not any(matches(n, prefix, [p]) and n[len(prefix):] not in EXCLUDED for n in names)]


def clip_names(hero, anim_root):
    """The names of every clip file of ``hero`` under the Unity project's animation folder."""
    folder = anim_root / hero
    return sorted(p.stem for p in folder.glob("*.anim")) if folder.is_dir() else []


def main(argv):
    import json
    from pathlib import Path

    if not argv or argv == ["--all"]:
        heroes = HEROES
    else:
        heroes = argv
    client = Path(json.loads((Path(__file__).with_name("config.json")).read_text(encoding="utf-8"))["jws2_client"])
    anim_root = client / "Assets/Actor/AnimationSplit/Animation_L"
    for hero in heroes:
        names = clip_names(hero, anim_root)
        chosen = select(names, hero)
        print(f"{hero}: {len(chosen)} clips; missing: {missing(names, hero)}")
        for name in chosen:
            print("   " + name)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
