# -*- coding: utf-8 -*-
"""extract_tables.py — Lua 数据表抽取（合成夹具，不读 jws2 工程）。"""

import json
import sys
from pathlib import Path

import pytest

from sanguo_import import extract_tables, lua_parse

LAYOUT = """local _layoutTable =
{
\tLevelID = 1011,
\tLevelItemList =
\t{
\t\t{
\t\t\tType = 0,
\t\t\tCamAngle = 0,
\t\t\tLevelItemID = 1001,
\t\t\tLevelItemType = 1,
\t\t\tItemPos = FP.newV2(111.459,777.805),
\t\t\tItemDir = FP.newV2(0,-1),
\t\t\tActiveDefault = true,
\t\t},
\t\t{
\t\t\tWeightMonsterIDList =
\t\t\t{
\t\t\t\t{
\t\t\t\t\tMonsterID = 101103,
\t\t\t\t\tWeight = FP.newV1(100),
\t\t\t\t},
\t\t\t},
\t\t\tCount = 8,
\t\t\tLevelItemID = 2010,
\t\t\tLevelItemType = 2,
\t\t\tItemPos = FP.newV2(111.26,702.06),
\t\t\tItemDir = FP.newV2(0,1),
\t\t\tActiveDefault = false,
\t\t},
\t},
}
"""


def _skill_row(id_, belong, key, anim, resource, switch_time, events, buffs, switches):
    """A page row padded to the schema's 61 columns (the real pages carry every column).
    values[i] becomes column i+1 (events land in 52, switches in 60)."""
    values = [str(id_), str(belong), str(key), "0", "0", "0", "0", '""', "0", "0.0", "false", "0",
              "1", "false", '""', '"' + anim + '"', "0", "0", "false", '"' + resource + '"', '""',
              "false", "{" + ",".join(str(b) for b in buffs) + "}", "0", "-1", "0", "0.0", "0", "-1",
              "0", "0", "0", "-1", "0", "0.0", "0", "false", "0.0", "false", "false", '""', "0", "{}",
              "false", str(switch_time), "false", "false", "0", "0", "0", "0", events, "20", "5",
              "180", "180", '""', "1", "0", switches, "{}"]
    assert len(values) == 61, len(values)
    return "{" + ",".join(values) + "}"


ROW_N1 = _skill_row(100001, 100001, 1, "n1", "Skill/Hero_GuanYu/Attack_01", 500,
                    "{{EventTime=200,EventType=1,EventIntParam=10000102,EventParam=2}}",
                    [100031],
                    "{{SwitchStartTime=0,SwitchDuration=500,SwitchMinPlayTime=0,SwitchPutIn=8,SwitchPutOut=100009}}")
ROW_N2 = _skill_row(100002, 100001, 2, "n2", "Skill/Hero_GuanYu/Attack_02", 750, "{}",
                    [100031, 500002], "{}")

SKILL_PAGE1 = "local dataTable = {\n    " + ROW_N1 + ",\n    " + ROW_N2 + ",\n}\n"

SKILL_BASE = (
    "local propertyTable = {\n"
    "    ID = 1,BelongTo = 2,KeyPosition = 3,AnimName = 16,SkillResource = 20,SwitchAllTime = 45,"
    "SkillEvent_Table = 52,SkillSwitch_Table = 60,BuffIDs = 23,\n"
    "}\n\n"
    "local mainPropertyTable = {\n    ID = 1,PageIndex = 2,LineIndex = 3,\n}\n\n"
    "local dataTable = {\n"
    "\tsetmetatable({100001,100001,1},base),\n"
    "\tsetmetatable({100002,100001,2},base),\n"
    "}\n\n"
    "return {\n    dataTable = dataTable,\n    propertyTable = propertyTable,\n"
    "    primaryTable = primaryTable,\n}\n"
)


@pytest.fixture
def lua_data_dir(tmp_path):
    data = tmp_path / "Data"
    data.mkdir()
    (data / "SkillData.lua").write_text(SKILL_BASE, encoding="utf-8", newline="\n")
    (data / "SkillData-page1.lua").write_text(SKILL_PAGE1, encoding="utf-8", newline="\n")
    layout_dir = tmp_path / "Battle" / "Logic" / "Level" / "LevelItemAndEventData"
    layout_dir.mkdir(parents=True)
    (layout_dir / "LevelLayoutData1011.lua").write_text(LAYOUT, encoding="utf-8", newline="\n")
    return data


def test_load_sheet_maps_schema_over_pages(lua_data_dir):
    sheet = lua_parse.load_sheet(str(lua_data_dir), "SkillData")
    assert len(sheet.rows) == 2
    row = sheet.get(100001)
    assert row["AnimName"] == "n1"
    assert row["SkillResource"] == "Skill/Hero_GuanYu/Attack_01"
    assert row["SwitchAllTime"] == 500
    event = row["SkillEvent_Table"][0]
    assert event["EventTime"] == 200 and event["EventType"] == 1 and event["EventIntParam"] == 10000102
    assert row["SkillSwitch_Table"][0]["SwitchPutOut"] == 100009
    # the second row's empty tables stay empty, arrays keep multiple entries
    empty = sheet.get(100002)
    assert empty["SkillEvent_Table"] == []
    assert empty["BuffIDs"] == [100031, 500002]


def test_parse_level_layout_unwraps_fixed_point(lua_data_dir):
    layout = extract_tables.parse_level_layout(
        lua_data_dir.parent / "Battle" / "Logic" / "Level" / "LevelItemAndEventData" / "LevelLayoutData1011.lua")
    assert layout["LevelID"] == 1011
    items = layout["LevelItemList"]
    assert len(items) == 2
    assert items[0]["ItemPos"] == [111.459, 777.805]
    assert items[0]["ItemDir"] == [0, -1]
    monster = items[1]["WeightMonsterIDList"][0]
    assert monster["MonsterID"] == 101103 and monster["Weight"] == [100]


def test_extract_writes_tables_and_report(lua_data_dir, tmp_path):
    out = tmp_path / "out"
    report = extract_tables.extract(lua_data_dir, out)
    skills = json.loads((out / "skills.json").read_text(encoding="utf-8"))
    assert [s["ID"] for s in skills] == [100001, 100002]
    assert all("_raw" not in s for s in skills)      # raw columns stay out of the intermediates
    layout = json.loads((out / "level-1011.json").read_text(encoding="utf-8"))
    assert layout["LevelID"] == 1011
    assert report["tables"]["skills"]["rows"] == 2
    assert report["level"]["monsterIDs"] == [101103]
    assert (out / "extract-report.json").is_file()


def test_strip_fixed_point_handles_both_wrappers():
    text = "ItemPos = FP.newV2(1.5,-2),Weight = FP.newV1(100),X = 1"
    assert extract_tables._strip_fixed_point(text) == "ItemPos = {1.5,-2},Weight = {100},X = 1"
