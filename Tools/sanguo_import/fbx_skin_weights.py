# -*- coding: utf-8 -*-
"""Reads the skin weights a binary FBX really stores, per mesh and control point.

    py -3 Tools/sanguo_import/fbx_skin_weights.py <model.fbx>
    py -3 Tools/sanguo_import/fbx_skin_weights.py <model.fbx> --mesh <geometry name>
    py -3 Tools/sanguo_import/fbx_skin_weights.py <model.fbx> --mesh <geometry name> --at X Y Z

Unity and the engine import different weights for many vertices of the jws2 heroes although both
read the same file. This reads the file itself, so it can say which side altered what:

  no option       file-wide statistics: clusters, weights, distinct values, multiples of 1/64
  --mesh M        that geometry's influences per control point: histogram, sums that are not 1,
                  and the control points with more than four influences
  --at X Y Z      the control points at a position (Unity negates X on import, so both signs are
                  tried) with every influence the file holds for them, largest first

Binary FBX keeps every object flat under Objects and ties clusters to their skin, mesh and bone by
connections (OO records: child id, parent id), so that is what is followed here.
"""

import array
import struct
import sys
import zlib
from collections import Counter, defaultdict
from pathlib import Path

ARRAY_TYPES = {"f": "f", "d": "d", "l": "q", "i": "i", "b": "b"}
SCALAR_FORMATS = {"Y": "<h", "C": "<B", "I": "<i", "F": "<f", "D": "<d", "L": "<q"}
HEADER = 27  # "Kaydara FBX Binary  " + two zero bytes + the 4-byte version


# ---------------------------------------------------------------- binary records

class Reader:
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


def text(value):
    """An FBX string property as text. A "Name" + NUL + "Class" pair keeps only the name."""
    if not isinstance(value, bytes):
        return str(value)
    return value.split(b"\x00")[0].decode("latin1")


def read_array(reader, code):
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


def read_properties(reader, count):
    values = []
    for _ in range(count):
        kind = chr(reader.raw(1)[0])
        if kind in SCALAR_FORMATS:
            values.append(reader.unpack(SCALAR_FORMATS[kind])[0])
        elif kind in ARRAY_TYPES:
            values.append(read_array(reader, kind))
        elif kind in "SR":
            (length,) = reader.unpack("<I")
            values.append(reader.raw(length))
        else:
            raise ValueError(f"unknown FBX property type {kind!r} at byte {reader.pos - 1}")
    return values


def read_node(reader, wide):
    end, count, _ = reader.unpack("<QQQ" if wide else "<III")
    name_length = reader.raw(1)[0]
    if end == 0 and name_length == 0:
        return None  # the null record that closes a list
    name = reader.raw(name_length).decode("latin1")
    properties = read_properties(reader, count)
    children = []
    while reader.pos < end:
        child = read_node(reader, wide)
        if child is None:
            break
        children.append(child)
    reader.pos = end
    return name, properties, children


def parse(data):
    """Top-level nodes of a binary FBX file image."""
    if not data.startswith(b"Kaydara FBX Binary"):
        raise ValueError("not a binary FBX")
    (version,) = struct.unpack_from("<I", data, 23)
    reader = Reader(data)
    reader.pos = HEADER
    roots = []
    while reader.pos < len(data):
        node = read_node(reader, version >= 7500)
        if node is None:
            break  # top-level list closed; a footer follows
        roots.append(node)
    return version, roots


# ---------------------------------------------------------------- the skin in the file

def find(node, name):
    if node[0] == name:
        yield node
    for child in node[2]:
        yield from find(child, name)


def member(node, name):
    return next((child for child in node[2] if child[0] == name), None)


def object_table(roots):
    """Object id -> node, for every node under Objects."""
    table = {}
    for root in roots:
        for objects in find(root, "Objects"):
            for node in objects[2]:
                if node[1] and isinstance(node[1][0], int):
                    table[node[1][0]] = node
    return table


def parents_by_child(roots):
    """Object id -> the ids it is connected to (the parents of its OO connections)."""
    parents = defaultdict(list)
    for root in roots:
        for connections in find(root, "Connections"):
            for record in connections[2]:
                if record[0] == "C" and len(record[1]) >= 3 and text(record[1][0]) == "OO":
                    parents[record[1][1]].append(record[1][2])
    return parents


def class_of(node):
    return text(node[1][2]) if len(node[1]) >= 3 else ""


def name_of(node):
    """An object's name without its "Class::" prefix."""
    return text(node[1][1]).split("::")[-1] if len(node[1]) >= 2 else ""


def skins(roots):
    """One dict per skin deformer: name of its mesh, its control points, and per control point the
    list of (bone name, weight) the file holds, largest first.

    Geometry and deformer objects carry no name in this file format; a mesh is named by the Model
    that owns its Geometry, a bone by the Model that a cluster's link points at (a connection from
    the bone to the cluster)."""
    objects = object_table(roots)
    parents = parents_by_child(roots)
    children = defaultdict(list)
    for child, its_parents in parents.items():
        for parent in its_parents:
            children[parent].append(child)

    def owner(node):
        """The Model (mesh or bone) an object belongs to by its connections."""
        linked = parents.get(node[1][0], []) + children.get(node[1][0], [])
        return next((name_of(objects[other]) for other in linked
                     if other in objects and objects[other][0] == "Model"), "?")

    result = []
    for identifier, node in objects.items():
        if class_of(node) != "Skin":
            continue
        geometry = next((objects[p] for p in parents[identifier]
                         if p in objects and objects[p][0] == "Geometry"), None)
        if geometry is None or member(geometry, "Vertices") is None:
            continue
        weights = defaultdict(list)
        for child_id, cluster in objects.items():
            if class_of(cluster) != "Cluster" or identifier not in parents.get(child_id, []):
                continue
            indexes_node, weights_node = member(cluster, "Indexes"), member(cluster, "Weights")
            if indexes_node is None or weights_node is None:
                continue
            bone = owner(cluster)
            for index, weight in zip(indexes_node[1][0], weights_node[1][0]):
                weights[int(index)].append((bone, float(weight)))
        result.append({"mesh": owner(geometry), "vertices": member(geometry, "Vertices")[1][0],
                       "weights": {index: sorted(influences, key=lambda pair: -pair[1])
                                   for index, influences in weights.items()}})
    return sorted(result, key=lambda skin: skin["mesh"])


# ---------------------------------------------------------------- reports

def report_file(path, version, skins_):
    clusters = sum(len({bone for influences in skin["weights"].values() for bone, _ in influences}) for skin in skins_)
    weights = [weight for skin in skins_ for influences in skin["weights"].values() for _, weight in influences]
    counter = Counter(round(w, 4) for w in weights)
    multiple_of_64th = sum(1 for w in weights if abs(w * 64 - round(w * 64)) < 1e-4)
    print(f"{path.name}: FBX version {version}, {len(skins_)} skinned meshes, {clusters} bone clusters, "
          f"{len(weights)} weights, {len(counter)} distinct values, {multiple_of_64th / len(weights) * 100:.1f}% a multiple of 1/64")
    print("  meshes:", ", ".join(skin["mesh"] for skin in skins_))
    print("  most common weights:", ", ".join(f"{value:g} x{count}" for value, count in counter.most_common(8)))


def report_mesh(skin):
    weights = skin["weights"]
    histogram = Counter(len(influences) for influences in weights.values())
    off = [index for index, influences in weights.items() if abs(sum(w for _, w in influences) - 1.0) > 1e-3]
    many = [index for index, influences in weights.items() if len(influences) > 4]
    print(f"{skin['mesh']}: {len(skin['vertices']) // 3} control points, {len(weights)} of them weighted")
    print("  influences per control point:", ", ".join(f"{count}: {histogram[count]}" for count in sorted(histogram)))
    print(f"  weights not summing to 1: {len(off)}; more than four influences: {len(many)}")
    for index in many[:5]:
        influences = weights[index]
        kept = influences[:4]
        renormalised = [round(w / sum(k for _, k in kept), 4) for _, w in kept]
        print(f"    control point {index}: {[(bone, round(w, 4)) for bone, w in influences]}"
              f"  -> strongest four renormalised {renormalised}")


def report_position(skin, position, tolerance=1e-3):
    vertices = skin["vertices"]
    hits = []
    for index in range(0, len(vertices), 3):
        point = vertices[index:index + 3]
        for sign in (1.0, -1.0):  # Unity negates X when it imports a right-handed Z-up FBX
            if abs(point[0] * sign - position[0]) < tolerance and abs(point[1] - position[1]) < tolerance \
                    and abs(point[2] - position[2]) < tolerance:
                hits.append(index // 3)
    if not hits:
        print(f"no control point of {skin['mesh']} at {position}")
        return
    for index in hits:
        point = [round(v, 4) for v in vertices[index * 3:index * 3 + 3]]
        influences = skin["weights"].get(index, [])
        print(f"control point {index} at {point}: sum {sum(w for _, w in influences):.4f}, {len(influences)} influences")
        for bone, weight in influences:
            print(f"    {weight:.4f}  {bone}")


def main(argv):
    if len(argv) < 2:
        raise SystemExit(__doc__)
    path = Path(argv[1])
    version, roots = parse(path.read_bytes())
    skins_ = skins(roots)
    if not skins_:
        raise SystemExit(f"{path.name}: no skin")
    report_file(path, version, skins_)
    if "--mesh" in argv:
        wanted = argv[argv.index("--mesh") + 1]
        skin = next((s for s in skins_ if s["mesh"] == wanted), None)
        if skin is None:
            raise SystemExit(f"no skinned mesh named {wanted}; try one of: " + ", ".join(s["mesh"] for s in skins_))
        print()
        report_mesh(skin)
        if "--at" in argv:
            at = argv.index("--at") + 1
            print()
            report_position(skin, [float(v) for v in argv[at:at + 3]])
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
