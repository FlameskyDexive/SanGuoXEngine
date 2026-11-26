# -*- coding: utf-8 -*-
"""Upgrades a legacy FBX 6100 mesh (binary or ASCII) to a binary FBX 7400 the engine imports.

    py -3 Tools/sanguo_import/upgrade_legacy_fbx.py <source.fbx> <target.fbx>
    py -3 Tools/sanguo_import/upgrade_legacy_fbx.py <source.fbx> --info

The 桃园 scene ships four meshes in the pre-2011 format the engine's model importer rejects
(t_shanshi04_01, i_ly_muche01_01_lod2, i_zx_xiaodiaodeng01_lod1/lod2). They carry one static mesh
each; this re-wraps its vertices, polygon indices, normals and UVs in the modern container the
test fixture (`tests/test_fbx_skin_weights.py`) already writes. A single material slot binds the
mesh to a placeholder material name — the port assigns its own mapped material anyway.

Geometry notes: vertex Y is left as authored (the FBX is already in its DCC's space, and Unity
showed these files correctly), the W of each polygon's last index flips to negative per the 7400
PolygonVertexIndex convention, and normals/UVs are re-mapped by their own index arrays.
"""

from __future__ import annotations

import array
import re
import struct
import sys
import zlib
from pathlib import Path

ARRAY_TYPES = {"f": "f", "d": "d", "l": "q", "i": "i", "b": "b"}
SCALAR_FORMATS = {"Y": "<h", "C": "<B", "I": "<i", "F": "<f", "D": "<d", "L": "<q"}
HEADER = b"Kaydara FBX Binary  \x00" + b"\x1a\x00" + struct.pack("<I", 7400)


# ================================================================ binary 6100 reading

class _BinaryReader:
    def __init__(self, data):
        self.data = data
        self.pos = 0

    def unpack(self, fmt):
        values = struct.unpack_from(fmt, self.data, self.pos)
        self.pos += struct.calcsize(fmt)
        return values

    def raw(self, count):
        chunk = self.data[self.pos:self.pos + count]
        self.pos += count
        return chunk


def _read_array(reader, code):
    (count,) = reader.unpack("<I")
    (encoding,) = reader.unpack("<I")
    (compressed,) = reader.unpack("<I")
    payload = reader.raw(compressed)
    if encoding == 1:
        payload = zlib.decompress(payload)
    values = array.array(ARRAY_TYPES[code])
    values.frombytes(payload)
    if len(values) != count:
        raise ValueError(f"FBX array declares {count} elements but holds {len(values)}")
    return list(values)


def _read_properties(reader, count):
    values = []
    for _ in range(count):
        kind = chr(reader.raw(1)[0])
        if kind in SCALAR_FORMATS:
            values.append(reader.unpack(SCALAR_FORMATS[kind])[0])
        elif kind in ARRAY_TYPES:
            values.append(_read_array(reader, kind))
        elif kind in "SR":
            (length,) = reader.unpack("<I")
            values.append(reader.raw(length))
        else:
            raise ValueError(f"unknown FBX property type {kind!r}")
    return values


def _read_node(reader, wide):
    end, count, _ = reader.unpack("<QQQ" if wide else "<III")
    name_length = reader.raw(1)[0]
    if end == 0 and name_length == 0:
        return None
    name = reader.raw(name_length).decode("latin1")
    properties = _read_properties(reader, count)
    children = []
    while reader.pos < end:
        child = _read_node(reader, wide)
        if child is None:
            break
        children.append(child)
    reader.pos = end
    return name, properties, children


def _parse_binary(data):
    if not data.startswith(b"Kaydara FBX Binary"):
        raise ValueError("not a binary FBX")
    (version,) = struct.unpack_from("<I", data, 23)
    reader = _BinaryReader(data)
    reader.pos = 27
    roots = []
    while reader.pos < len(data):
        node = _read_node(reader, version >= 7500)
        if node is None:
            break
        roots.append(node)
    return version, roots


def _member(node, name):
    return next((child for child in node[2] if child[0] == name), None)


def text_of(node):
    """An object's display name: 7400 puts 'Name\x00\x01Class' in the second property, 6100 in
    the first."""
    for value in node[1]:
        if isinstance(value, bytes):
            return value.split(b"\x00")[0].decode("latin1")
    return "Mesh"


def _find_in(node, name):
    """First node named `name` in `node`'s subtree: direct children first, then deeper."""
    for child in node[2]:
        if child[0] == name:
            return child
    for child in node[2]:
        found = _find_in(child, name)
        if found is not None:
            return found
    return None


def _array_of_in(node, name):
    """`_array_of` over the first subtree match, so mis-nested sections still resolve."""
    child = _member(node, name) or _find_in(node, name)
    if child is None:
        return None
    if not child[1]:
        inner = _member(child, "a") or _find_in(child, "a")
        if inner is not None and inner[1]:
            child = inner
        else:
            return None
    if len(child[1]) == 1 and isinstance(child[1][0], list):
        return child[1][0]
    if all(isinstance(value, (int, float)) for value in child[1]):
        return child[1]
    return None


def _array_of(node, name):
    """The values of an array child. 7400 wraps them in one array property; 6100 binary stores
    every number as its own scalar property; 6100 ASCII nests them under an `a:` child."""
    child = _member(node, name)
    if child is None:
        return None
    if not child[1]:
        inner = _member(child, "a")
        if inner is not None and inner[1]:
            child = inner
        else:
            return None
    if len(child[1]) == 1 and isinstance(child[1][0], list):
        return child[1][0]
    if all(isinstance(value, (int, float)) for value in child[1]):
        return child[1]
    return None


# ================================================================ ASCII 6.1 reading

class _AsciiTokens:
    """A token list of an ASCII FBX: (group, text) pairs, comments dropped. Numbers are atomic
    tokens — an array comes out as `*N` followed by N number tokens, which the parser reads as
    N scalar values."""

    TOKEN = re.compile(
        r'"(?P<quoted>[^"]*)"'
        r"|(?P<open>\{)"
        r"|(?P<close>\})"
        r"|(?P<colon>:)"
        r"|(?P<comma>,)"
        r"|(?P<comment>;[^\n]*)"
        r"|(?P<star>\*\d+)"
        r"|(?P<number>-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)"
        r"|(?P<word>[A-Za-z_][\w:.]*)")

    def __init__(self, text):
        self.tokens = [(match.lastgroup, match.group())
                       for match in self.TOKEN.finditer(text)
                       if match.lastgroup is not None and match.lastgroup != "comment"]
        self.index = 0

    def group(self, ahead=0):
        index = self.index + ahead
        return self.tokens[index][0] if index < len(self.tokens) else None

    def text(self, ahead=0):
        index = self.index + ahead
        return self.tokens[index][1] if index < len(self.tokens) else ""

    def take(self):
        token = self.tokens[self.index]
        self.index += 1
        return token

    def done(self):
        return self.index >= len(self.tokens)


def _ascii_number(text_value):
    text_value = text_value.strip().rstrip(",")
    return int(text_value) if re.fullmatch(r"-?\d+", text_value) else float(text_value)


def _read_ascii_values(tokens, values):
    """Reads scalar / array values until a sibling's name word, a brace, or the end of file.
    In the 6.1 ASCII dialect quoted tokens are always values and bare words never are."""
    while not tokens.done():
        group = tokens.group()
        if group in ("colon", "comma"):
            tokens.take()
            continue
        if group == "star":
            # `Vertices: *448 { a: … }` — the count carries no colon; the brace after it opens
            # the child block that holds the `a:` array node.
            tokens.take()                      # *N
            if tokens.group() == "colon":
                tokens.take()
            continue
        if group == "number":
            values.append(_ascii_number(tokens.text()))
            tokens.take()
            continue
        if group == "quoted":
            values.append(tokens.text())
            tokens.take()
            continue
        return                                 # word / open / close — values end here


def _read_ascii_node(tokens):
    """Reads one ASCII node: `Name[: values] [{ children }]`. A child block braces open right
    after this node's values; a bare word after the values starts a sibling and a close brace
    belongs to an ancestor — both end this node, and only a node that consumed its own open
    brace consumes the matching close (leaf `a:` nodes must not swallow their parent's `}`)."""
    name = tokens.text().rstrip(":")
    tokens.take()
    values = []
    children = []
    while not tokens.done():
        group = tokens.group()
        if group in ("colon", "comma"):
            tokens.take()
            continue
        if group == "close":
            break                              # an ancestor's closing brace
        if group == "open":
            tokens.take()                      # this node's own child block
            while not tokens.done() and tokens.group() != "close":
                children.append(_read_ascii_node(tokens))
            if not tokens.done():              # some 6.1 exporters miscount braces at EOF
                tokens.take()                  # the closing brace of the child block
            continue
        if group == "word":
            break                              # a sibling's name — this node ends here
        _read_ascii_values(tokens, values)
    return name, values, children


def _parse_ascii(text):
    tokens = _AsciiTokens(text)
    roots = []
    while not tokens.done():
        if tokens.group() == "word":
            roots.append(_read_ascii_node(tokens))
        else:
            tokens.take()
    return roots


# ================================================================ mesh extraction

def _polygons_of(flat_indices):
    """PolygonVertexIndex list → polygons. The W of a polygon's last entry is negative and
    encodes the true index as `-value - 1`."""
    polygons = []
    current = []
    for value in flat_indices:
        if value < 0:
            current.append(-value - 1)
            polygons.append(current)
            current = []
        else:
            current.append(value)
    if current:
        polygons.append(current)
    return polygons


def _triangulated(polygons):
    """Polygons → triangle corners (a fan from the first corner)."""
    corners = []
    for polygon in polygons:
        for i in range(1, len(polygon) - 1):
            corners.extend((polygon[0], polygon[i], polygon[i + 1]))
    return corners


class LegacyMesh:
    def __init__(self, name, vertices, vertex_indices, normals, normal_indices, uvs, uv_indices):
        self.name = name
        self.vertices = vertices
        self.vertex_indices = vertex_indices
        self.normals = normals
        self.normal_indices = normal_indices
        self.uvs = uvs
        self.uv_indices = uv_indices

    @property
    def polygons(self):
        return _polygons_of(self.vertex_indices)

    @property
    def triangles(self):
        return _triangulated(self.polygons)

    @property
    def triangle_count(self):
        corners = self.triangles
        assert len(corners) % 3 == 0
        return len(corners) // 3

    @property
    def triangle_corner_positions(self):
        """For each triangulated output corner, its position among the original polygon's
        corners (the fan takes 0, i, i+1). Direct per-corner arrays — normals, and per-corner
        index arrays like UVIndex — are indexed by corner POSITION, not by vertex index, so
        quads fan-splitting must remap through this; an all-triangle mesh yields the identity."""
        positions = []
        for polygon in self.polygons:
            for i in range(1, len(polygon) - 1):
                positions.extend((0, i, i + 1))
        return positions

    @property
    def triangle_corner_sources(self):
        """For each triangulated output corner, the vertex index it points at."""
        return _triangulated(self.polygons)  # same fan walk — the values ARE vertex indices

    @property
    def triangle_normals(self):
        """Per-corner Direct normals reordered for the fan triangulation."""
        out = []
        for pos in self.triangle_corner_positions:
            out.extend(self.normals[pos * 3:pos * 3 + 3])
        return out

    @property
    def triangle_uv_indices(self):
        """Per-corner UV index array reordered for the fan triangulation."""
        if self.uv_indices is None:
            return None
        positions = self.triangle_corner_positions
        return [self.uv_indices[pos] for pos in positions]


def mesh_from_binary(roots):
    # FBX 6.1 keeps the mesh inside the Model node itself (Vertices / PolygonVertexIndex /
    # LayerElement* as direct children); there is no separate Geometry object.
    geometries = []
    for root in roots:
        if root[0] != "Objects":
            continue
        for node in root[2]:
            if node[0] == "Model" and _array_of(node, "Vertices") is not None:
                geometries.append(node)
    if not geometries:
        raise ValueError("the legacy FBX holds no mesh geometry")
    geometry = geometries[0]
    name = text_of(geometry)
    vertices = _array_of(geometry, "Vertices")
    polygons = _array_of(geometry, "PolygonVertexIndex")
    if vertices is None or polygons is None:
        raise ValueError("the mesh geometry lacks Vertices or PolygonVertexIndex")
    normals_node = _member(geometry, "LayerElementNormal")
    normals = _array_of(normals_node, "Normals") if normals_node is not None else []
    normal_indices = _array_of(normals_node, "NormalsIndex") if normals_node is not None else []
    uv_node = _member(geometry, "LayerElementUV")
    uvs = _array_of(uv_node, "UV") if uv_node is not None else []
    uv_indices = _array_of(uv_node, "UVIndex") if uv_node is not None else []
    return LegacyMesh(name, vertices, polygons, normals, normal_indices, uvs, uv_indices)


def mesh_from_ascii(roots):
    # 6.1 nests the Model inside Objects (and some exporters miscount braces so sections leak
    # into each other) — search the whole tree rather than just the roots, and locate the mesh
    # arrays in the Model's subtree: mis-counted braces can push them below their expected depth.
    models = []
    def walk(nodes):
        for node in nodes:
            if node[0] == "Model" and _array_of_in(node, "Vertices") is not None:
                models.append(node)
            walk(node[2])
    walk(roots)
    if not models:
        raise ValueError("the legacy ASCII FBX holds no mesh geometry")
    geometry = models[0]
    values = geometry[1]
    # The name is the first value, quoted and class-prefixed: `"Model::i_zx_xiaodiaodeng01_lod1"`.
    name = values[0] if values else "Mesh"
    name = name.strip('"').split("::")[-1] or "Mesh"
    vertices = _array_of_in(geometry, "Vertices")
    polygons = _array_of_in(geometry, "PolygonVertexIndex")
    if vertices is None or polygons is None:
        raise ValueError("the mesh geometry lacks Vertices or PolygonVertexIndex")
    normals_node = _find_in(geometry, "LayerElementNormal")
    normals = _array_of(normals_node, "Normals") if normals_node is not None else []
    normal_indices = _array_of(normals_node, "NormalsIndex") if normals_node is not None else []
    uv_node = _find_in(geometry, "LayerElementUV")
    uvs = _array_of(uv_node, "UV") if uv_node is not None else []
    uv_indices = _array_of(uv_node, "UVIndex") if uv_node is not None else []
    return LegacyMesh(name, vertices, polygons, normals, normal_indices, uvs, uv_indices)


def read_legacy_mesh(path):
    """Reads the first mesh of a legacy FBX 6100 (binary or ASCII)."""
    data = Path(path).read_bytes()
    if data.startswith(b"Kaydara FBX Binary"):
        version, roots = _parse_binary(data)
        mesh = mesh_from_binary(roots)
    else:
        text = data.decode("latin1")
        if "FBX 6.1" not in text[:200]:
            raise ValueError(f"{path}: neither a binary FBX nor an ASCII FBX 6.1 file")
        roots = _parse_ascii(text)
        mesh = mesh_from_ascii(roots)
    mesh.version = version if data.startswith(b"Kaydara FBX Binary") else 6100
    return mesh


# ================================================================ binary 7400 writing

def _string_property(value):
    data = value if isinstance(value, bytes) else value.encode("latin1")
    return b"S" + struct.pack("<I", len(data)) + data


def _long_property(value):
    return b"L" + struct.pack("<q", value)


def _double_array_property(values):
    packed = array.array("d", values).tobytes()
    return b"d" + struct.pack("<III", len(values), 0, len(packed)) + packed


def _int_array_property(values):
    packed = array.array("i", values).tobytes()
    return b"i" + struct.pack("<III", len(values), 0, len(packed)) + packed


def _append_node(out, name, properties=(), children=()):
    """Appends one binary FBX node record to `out`. The record's end offset is an absolute file
    position, so the children (tuples of name, properties, children) are written before it is
    known. A leaf node carries no closing null record."""
    start = len(out)
    label = name.encode("latin1")
    out += bytes(13) + label + b"".join(properties)
    for child in children:
        _append_node(out, *child)
    if children:
        out += bytes(13)                   # the null record that closes the child list
    struct.pack_into("<IIIB", out, start, len(out), len(properties), len(b"".join(properties)), len(label))


def _w_flip(corners):
    """Marks each triangle's last corner by negating it minus one, per the FBX polygon encoding
    (LegacyMesh.triangles is stored de-flipped; 7400 PolygonVertexIndex carries the marks)."""
    flipped = list(corners)
    for i in range(2, len(flipped), 3):
        flipped[i] = ~flipped[i]
    return flipped


def _geometry_node(object_id, mesh, geometry_name):
    """Geometry 7400: Vertices, PolygonVertexIndex (W-flipped last corner), per-corner normals and
    UVs via their own mapping arrays, exactly what the engine's importer reads. Every child
    record is a (name, properties, children) triple."""
    return ("Geometry", [
        _long_property(object_id),              # id
        _string_property(geometry_name),
        _string_property("Mesh"),
    ], [
        ("Vertices", [_double_array_property(mesh.vertices)], ()),
        ("PolygonVertexIndex", [_int_array_property(_w_flip(mesh.triangles))], ()),
        ("LayerElementNormal", (), [
            # Direct per-corner normals, reordered for the fan triangulation of quads.
            ("Normals", [_double_array_property(mesh.triangle_normals)], ()),
            *( [("NormalsIndex", [_int_array_property(mesh.normal_indices)], ())]
               if mesh.normal_indices is not None else [] ),       # absent = Direct mapping
        ]),
        ("LayerElementUV", (), [
            ("UV", [_double_array_property(mesh.uvs)], ()),
            *( [("UVIndex", [_int_array_property(mesh.triangle_uv_indices)], ())]
               if mesh.uv_indices is not None else [] ),           # absent = Direct mapping
        ]),
        ("Layer", (), [
            ("LayerElement", [_int_array_property([0]), _int_array_property([2])], ()),
            ("LayerElement", [_int_array_property([1]), _int_array_property([6])], ()),
        ]),
    ])


def write_7400(path, mesh):
    """Writes the mesh as a binary FBX 7400: one Model, one Geometry, tied by OO connections.

    The name properties of the Model and Geometry carry the separator byte between a record's
    name and its class (null, then the class byte 1), built with bytes([0, 1]) so this source
    file stays free of raw null bytes."""
    name_sep = bytes([0, 1])
    model_name = _string_property(mesh.name.encode("latin1") + name_sep + b"Model")
    geometry_name = _string_property(mesh.name.encode("latin1") + name_sep + b"Geometry")
    out = bytearray(HEADER)
    _append_node(out, "FBXHeaderExtension", (), [
        ("FBXHeaderVersion", [_int_array_property([1003])], ()),
        ("FBXVersion", [_int_array_property([7400])], ()),
        ("Creator", [_string_property("sanguo upgrade_legacy_fbx")], ()),
    ])
    _append_node(out, "Documents", (), [
        ("Document", [_long_property(1), _string_property("Scene")], [
            ("RootNode", [_long_property(0)], ()),
        ]),
    ])
    _append_node(out, "Definitions", (), [
        ("Count", [_int_array_property([2])], ()),
    ])
    _append_node(out, "Objects", (), [
        ("Model", [_long_property(1), model_name, _string_property("Mesh")], [
            ("Properties70", (), [
                ("P", [_string_property("Lcl Translation"), _string_property("Lcl Translation"),
                       _string_property(""), _string_property("A"),
                       _double_array_property([0, 0, 0])], ()),
            ]),
        ]),
        _geometry_node(2, mesh, geometry_name),
    ])
    _append_node(out, "Connections", (), [
        # OO connections carry the two ids as scalar longs, not an array property.
        ("C", [_string_property("OO"), _long_property(2), _long_property(1)], ()),   # geometry → model
        ("C", [_string_property("OO"), _long_property(1), _long_property(0)], ()),   # model → root
    ])
    out += bytes(13)                            # the null record that closes the top-level list
    Path(path).write_bytes(out)


def upgrade(source, target):
    mesh = read_legacy_mesh(source)
    mesh.triangle_count  # validates the polygon list
    write_7400(target, mesh)
    return mesh


def main(argv=None):
    if len(argv) != 3:
        print(__doc__)
        return 2
    source, target = argv[1], argv[2]
    if source == "--info":
        mesh = read_legacy_mesh(target)
        print(f"{target}: {mesh.name}, {len(mesh.vertices) // 3} vertices, {mesh.triangle_count} triangles, "
              f"normals={'yes' if mesh.normals else 'no'}, uv={'yes' if mesh.uvs else 'no'}, version={mesh.version}")
        return 0
    mesh = upgrade(source, target)
    print(f"{source} → {target}: {mesh.name}, {len(mesh.vertices) // 3} vertices, {mesh.triangle_count} triangles")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
