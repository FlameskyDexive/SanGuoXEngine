# -*- coding: utf-8 -*-
"""clip_manifest.py — which battle clips of a hero the port takes."""

from unity_export import clip_manifest as cm

HERO = "Hero_LvBu"


def complete(extra=()):
    """The files of a hero that has every clip of the manifest (one per wildcard), in name order."""
    names = [HERO + "_" + p for p in cm.BATTLE_CLIPS if "*" not in p]
    names += [HERO + "_NormalAttack_1", HERO + "_SkillAttack_1", HERO + "_SA_1"]
    return sorted(names + list(extra))


def test_select_takes_every_manifest_clip():
    chosen = cm.select(complete([HERO + "_SA_cam", HERO + "_SA_2", HERO + "_NormalAttack_2"]), HERO)
    assert len(chosen) == len([p for p in cm.BATTLE_CLIPS if "*" not in p]) + 5


def test_select_keeps_the_manifest_order_and_the_hero_prefix():
    chosen = cm.select(complete([HERO + "_NormalAttack_2", "Hero_ZhaoYun_idle_loop"]), HERO)
    assert chosen[:2] == [HERO + "_idle_loop", HERO + "_run_loop"]
    assert "Hero_ZhaoYun_idle_loop" not in chosen


def test_select_sorts_the_matches_of_a_pattern_by_name():
    chosen = cm.select(complete([HERO + "_NormalAttack_3", HERO + "_NormalAttack_2"]), HERO)
    first = chosen.index(HERO + "_NormalAttack_1")
    assert chosen[first:first + 3] == [HERO + "_NormalAttack_1", HERO + "_NormalAttack_2", HERO + "_NormalAttack_3"]


def test_select_leaves_the_camera_clip_behind():
    assert HERO + "_SA_cam" not in cm.select(complete([HERO + "_SA_cam"]), HERO)


def test_select_leaves_plot_riding_and_boss_skin_clips_behind():
    chosen = cm.select(complete([HERO + "_NPC_Plot_angry", HERO + "_ride_idle_loop", HERO + "_Boss_NormalAttack_1",
                                 HERO + "_Boss_SkillAttack_1"]), HERO)
    assert not [c for c in chosen if "NPC_Plot" in c or "_ride_" in c or "_Boss_" in c]


def test_select_returns_each_clip_once():
    chosen = cm.select(complete(), HERO)
    assert len(chosen) == len(set(chosen))


def test_missing_lists_exact_names_without_a_file():
    names = [n for n in complete() if n != HERO + "_die"]
    assert cm.missing(names, HERO) == ["die"]


def test_unmatched_patterns_includes_wildcards():
    names = [n for n in complete() if n != HERO + "_hurt" and not n.startswith(HERO + "_SA_")]
    assert cm.unmatched_patterns(names, HERO) == ["SA_*", "hurt"]


def test_unmatched_ignores_an_excluded_clip():
    # SA_* is matched by SA_cam only, and that one is excluded: the pattern counts as unmatched.
    assert "SA_*" in cm.unmatched_patterns(complete([HERO + "_SA_cam"])[:1] + [HERO + "_SA_cam"], HERO)


def test_the_manifest_names_no_clip_twice_and_no_excluded_name():
    assert len(cm.BATTLE_CLIPS) == len(set(cm.BATTLE_CLIPS))
    assert not [p for p in cm.BATTLE_CLIPS if "*" not in p and p in cm.EXCLUDED]
