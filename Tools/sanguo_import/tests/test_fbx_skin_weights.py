import array
import struct
import zlib

import pytest

from sanguo_import import fbx_skin_weights as fbx

HEADER = b"Kaydara FBX Binary  \x00" + b"\x1a\x00" + struct.pack("<I", 7400)  # 27 bytes; version 7400: 32-bit offsets
MESH, GEOMETRY, HAND, FOREARM, SKIN, CLUSTER = 1, 2, 3, 4, 5, 6


def identifier(value):
    return b"L" + struct.pack("<q", value)


def string(value):
    data = value if isinstance(value, bytes) else value.encode("latin1")
    return b"S" + struct.pack("<I", len(data)) + data


def doubles(values):
    packed = zlib.compress(array.array("d", values).tobytes())
    return b"d" + struct.pack("<III", len(values), 1, len(packed)) + packed


def ints(values):
    data = array.array("i", values).tobytes()
    return b"i" + struct.pack("<III", len(values), 0, len(data)) + data


def node(out, name, properties=(), children=()):
    """Appends one binary FBX node record to `out`. The record's end offset is a position in the
    file, so the children (tuples of name, properties, children) are written before it is known."""
    start = len(out)
    label = name.encode("latin1")
    out += bytes(13) + label + b"".join(properties)
    for child in children:
        node(out, *child)
    if children:
        out += bytes(13)  # the null record that closes the child list
    struct.pack_into("<IIIB", out, start, len(out), len(properties), len(b"".join(properties)), len(label))


def model(name, kind, value):
    return "Model", [identifier(value), string(name + b"\x00\x01Model"), string(kind)], ()


def cluster(value, indexes, weights):
    return "Deformer", [identifier(value), string(b"\x00\x01Cluster"), string(b"Cluster")], [
        ("Indexes", [ints(indexes)], ()), ("Weights", [doubles(weights)], ())]


def link(child, parent):
    return "C", [string("OO"), identifier(child), identifier(parent)], ()


def file(objects, links):
    out = bytearray(HEADER)
    node(out, "Objects", (), objects)
    node(out, "Connections", (), links)
    out += bytes(13)
    return bytes(out)


def small_file(extra_objects=(), extra_links=()):
    geometry = "Geometry", [identifier(GEOMETRY), string(b"\x00\x01Geometry"), string(b"Mesh")], [
        ("Vertices", [doubles([0.0, 0.0, 0.0, 1.0, 0.0, 0.0])], ())]
    skin = "Deformer", [identifier(SKIN), string(b"\x00\x01Deformer"), string(b"Skin")], ()
    objects = [
        model(b"Body", b"Mesh", MESH), geometry, model(b"Hand", b"LimbNode", HAND),
        model(b"Forearm", b"LimbNode", FOREARM), skin, cluster(CLUSTER, [0, 1], [0.75, 1.0]),
    ]
    links = [link(GEOMETRY, MESH), link(CLUSTER, SKIN), link(SKIN, GEOMETRY), link(HAND, CLUSTER)]
    return file(objects + list(extra_objects), links + list(extra_links))


def test_reads_the_skin_of_a_small_file():
    version, roots = fbx.parse(small_file())
    skins = fbx.skins(roots)

    assert version == 7400
    assert [skin["mesh"] for skin in skins] == ["Body"]  # the geometry is named by the model that owns it
    assert skins[0]["vertices"] == [0.0, 0.0, 0.0, 1.0, 0.0, 0.0]
    assert skins[0]["weights"] == {0: [("Hand", 0.75)], 1: [("Hand", 1.0)]}


def test_influences_are_listed_largest_first_and_a_bone_comes_from_its_cluster_link():
    other = 7
    data = small_file(extra_objects=[cluster(other, [0], [0.25])],
                      extra_links=[link(other, SKIN), link(FOREARM, other)])

    skins = fbx.skins(fbx.parse(data)[1])

    assert len(skins) == 1
    assert skins[0]["weights"][0] == [("Hand", 0.75), ("Forearm", 0.25)]
    assert skins[0]["weights"][1] == [("Hand", 1.0)]


def test_an_array_whose_count_does_not_match_its_data_is_an_error():
    packed = zlib.compress(array.array("d", [0.5]).tobytes())
    lying = ("Weights", [b"d" + struct.pack("<III", 3, 1, len(packed)) + packed], ())
    data = file([("Deformer", [identifier(SKIN)], [lying])], [])

    with pytest.raises(ValueError, match="declares 3 elements but holds 1"):
        fbx.parse(data)


def test_a_file_that_is_not_a_binary_fbx_is_rejected():
    with pytest.raises(ValueError, match="not a binary FBX"):
        fbx.parse(b"PK\x03\x04 not a model")


def test_text_keeps_the_name_half_of_a_name_and_class_pair():
    assert fbx.text(b"Hand\x00\x01Model") == "Hand"
    assert fbx.text(b"\x00\x01Geometry") == ""
    assert fbx.text("plain") == "plain"
