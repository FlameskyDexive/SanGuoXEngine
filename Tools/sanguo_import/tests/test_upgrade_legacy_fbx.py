# -*- coding: utf-8 -*-
"""upgrade_legacy_fbx.py — reading legacy FBX 6100 (binary + ASCII) and writing binary FBX 7400.

All fixtures are synthetic: jws2 art files never enter the test suite."""

import array
import struct

import pytest

from sanguo_import import upgrade_legacy_fbx as u

HEADER_6100 = b"Kaydara FBX Binary  \x00" + b"\x1a\x00" + struct.pack("<I", 6100)

# One quad on z=0: corners (0,0) (1,0) (1,1) (0,1), one normal per corner, one uv per corner.
VERTICES = [0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0, 0.0]
PVI = [0, 1, 2, -4]                       # the quad: last corner W-flipped
NORMALS = [0.0, 0.0, 1.0] * 4
UVS = [0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0]
UV_INDEX = [0, 1, 2, 3]
TRIANGLES = [0, 1, 2, 0, 2, 3]            # the fan triangulation of the quad
FLIPPED = [0, 1, -3, 0, 2, -4]            # the same triangles in 7400 PolygonVertexIndex form


def identifier(value):
    return b"L" + struct.pack("<q", value)


def string(value):
    data = value if isinstance(value, bytes) else value.encode("latin1")
    return b"S" + struct.pack("<I", len(data)) + data


def doubles(values):
    data = array.array("d", values).tobytes()
    return b"d" + struct.pack("<III", len(values), 0, len(data)) + data


def ints(values):
    data = array.array("i", values).tobytes()
    return b"i" + struct.pack("<III", len(values), 0, len(data)) + data


def node(out, name, properties=(), children=()):
    """Appends one binary FBX node record; 6100 and 7400 share the 13-byte header shape."""
    start = len(out)
    label = name.encode("latin1")
    out += bytes(13) + label + b"".join(properties)
    for child in children:
        node(out, *child)
    if children:
        out += bytes(13)
    struct.pack_into("<IIIB", out, start, len(out), len(properties), len(b"".join(properties)), len(label))


def build_binary_6100():
    out = bytearray(HEADER_6100)
    node(out, "FBXHeaderExtension", (), [
        ("FBXVersion", [ints([6100])], ()),
    ])
    node(out, "Objects", (), [
        ("Model", [string(b"b_test\x00Model"), string("Mesh")], [
            ("Vertices", [doubles(VERTICES)], ()),
            ("PolygonVertexIndex", [ints(PVI)]),
            ("LayerElementNormal", (), [
                ("Normals", [doubles(NORMALS)], ()),
            ]),
            ("LayerElementUV", (), [
                ("UV", [doubles(UVS)], ()),
                ("UVIndex", [ints(UV_INDEX)], ()),
            ]),
        ]),
    ])
    out += bytes(13)
    return bytes(out)


ASCII_6100 = """\
; FBX 6.1.0 project file
FBXHeaderExtension:  {
\tFBXHeaderVersion: 1003
\tFBXVersion: 6100
}
Objects:  {
\tModel: "Model::b_test", "Mesh" {
\t\tVersion: 232
\t\tProperties60:  {
\t\t\tProperty: "Lcl Translation", "Lcl Translation", "", "A",0,0,0
\t\t}
\t\tVertices: 0,0,0,1,0,0,1,1,0,0,1,0
\t\tPolygonVertexIndex: 0,1,2,-4
\t\tLayerElementNormal: 0 {
\t\t\tNormals: 0,0,1,0,0,1,0,0,1,0,0,1
\t\t}
\t\tLayerElementUV: 0 {
\t\t\tUV: 0,0,1,0,1,1,0,1
\t\t\tUVIndex: 0,1,2,3
\t\t}
\t}
}
"""


def test_binary_6100_mesh_extraction(tmp_path):
    source = tmp_path / "quad.FBX"
    source.write_bytes(build_binary_6100())
    mesh = u.read_legacy_mesh(str(source))
    assert mesh.name == "b_test"
    assert mesh.version == 6100
    assert mesh.vertices == VERTICES
    assert mesh.triangles == TRIANGLES
    assert mesh.triangle_count == 2
    assert mesh.normals == NORMALS                      # per original quad corner
    assert mesh.uv_indices == UV_INDEX


def test_ascii_6100_mesh_extraction(tmp_path):
    source = tmp_path / "quad_ascii.FBX"
    source.write_bytes(ASCII_6100.encode("latin1"))
    mesh = u.read_legacy_mesh(str(source))
    assert mesh.name == "b_test"                        # quotes and "Model::" prefix stripped
    assert mesh.version == 6100
    assert mesh.triangles == TRIANGLES
    assert mesh.uv_indices == UV_INDEX


def test_ascii_6100_star_array_variant(tmp_path):
    # Later 6.1 exporters wrap arrays as `Name: *N { a: … }`.
    text = ASCII_6100.replace(
        "\t\tVertices: 0,0,0,1,0,0,1,1,0,0,1,0",
        "\t\tVertices: *12 {\n\t\t\ta: 0,0,0,1,0,0,1,1,0,0,1,0\n\t\t}").replace(
        "\t\t\tNormals: 0,0,1,0,0,1,0,0,1,0,0,1",
        "\t\t\tNormals: *12 {\n\t\t\t\ta: 0,0,1,0,0,1,0,0,1,0,0,1\n\t\t\t}")
    source = tmp_path / "quad_ascii_star.FBX"
    source.write_bytes(text.encode("latin1"))
    mesh = u.read_legacy_mesh(str(source))
    assert mesh.triangles == TRIANGLES
    assert mesh.triangle_normals == [0.0, 0.0, 1.0] * 6   # the fan's 6 corners


def test_ascii_6100_tolerates_brace_miscount(tmp_path):
    # Some 2015-era exporters close Version5 one brace short at EOF; the parser must still
    # finish and hand back every section.
    text = ASCII_6100 + "Version5:  {\n\tSettings:  {\n\t\tFrameRate: \"30\"\n\t}\n"
    tokens = u._AsciiTokens(text)
    assert sum(1 for kind, _ in tokens.tokens if kind == "open") == \
        sum(1 for kind, _ in tokens.tokens if kind == "close") + 1
    roots = u._parse_ascii(text)
    assert [r[0] for r in roots][-1] == "Version5"


def test_quad_fan_triangulation_remaps_direct_arrays(tmp_path):
    source = tmp_path / "quad.FBX"
    source.write_bytes(build_binary_6100())
    mesh = u.read_legacy_mesh(str(source))
    # 4 original corners fan out to 6: (0,1,2) then (0,2,3).
    assert mesh.triangle_corner_sources == [0, 1, 2, 0, 2, 3]
    assert mesh.triangle_normals == [0.0, 0.0, 1.0] * 6
    assert mesh.triangle_uv_indices == [0, 1, 2, 0, 2, 3]


def test_upgrade_writes_parseable_7400(tmp_path):
    source = tmp_path / "quad.FBX"
    target = tmp_path / "quad_7400.FBX"
    source.write_bytes(build_binary_6100())
    mesh = u.upgrade(str(source), str(target))
    assert mesh.triangle_count == 2

    version, roots = u._parse_binary(target.read_bytes())
    assert version == 7400
    names = [n[0] for n in roots]
    assert names == ["FBXHeaderExtension", "Documents", "Definitions", "Objects", "Connections"]

    objects = roots[3]
    assert objects[0] == "Objects"
    kinds = [child[0] for child in objects[2]]
    assert kinds == ["Model", "Geometry"]

    model, geometry = objects[2]
    assert model[1][2] in ("Mesh", b"Mesh")
    assert model[1][1].endswith(b"\x00\x01Model")
    assert geometry[1][1].endswith(b"\x00\x01Geometry")

    members = {child[0]: child for child in geometry[2]}
    assert members["Vertices"][1][0] == VERTICES         # decoded by the binary reader
    pvi = members["PolygonVertexIndex"][1][0]
    assert list(pvi) == FLIPPED
    normals = next(c for c in members["LayerElementNormal"][2] if c[0] == "Normals")[1][0]
    assert list(normals) == [0.0, 0.0, 1.0] * 6          # remapped to the fan's corners
    uv_index = next(c for c in members["LayerElementUV"][2] if c[0] == "UVIndex")[1][0]
    assert list(uv_index) == [0, 1, 2, 0, 2, 3]


def test_connections_carry_scalar_long_ids(tmp_path):
    # Clay's connection reader skips records with fewer than three properties, so the ids
    # must be scalar longs, not an array property.
    source = tmp_path / "quad.FBX"
    target = tmp_path / "quad_7400.FBX"
    source.write_bytes(build_binary_6100())
    u.upgrade(str(source), str(target))
    _, roots = u._parse_binary(target.read_bytes())
    connections = next(n for n in roots if n[0] == "Connections")
    records = [c for c in connections[2] if c[0] == "C"]
    assert len(records) == 2
    for record in records:
        assert len(record[1]) == 3
        assert record[1][0] in ("OO", b"OO")
        assert all(isinstance(value, int) for value in record[1][1:])
    assert [record[1][1] for record in records] == [2, 1]   # geometry→model, model→root
    assert [record[1][2] for record in records] == [1, 0]


def test_w_flip_round_trip(tmp_path):
    source = tmp_path / "quad.FBX"
    target = tmp_path / "quad_7400.FBX"
    source.write_bytes(build_binary_6100())
    mesh = u.upgrade(str(source), str(target))
    flipped = u._w_flip(mesh.triangles)
    assert flipped == FLIPPED
    assert u._polygons_of(flipped) == [[0, 1, 2], [0, 2, 3]]
