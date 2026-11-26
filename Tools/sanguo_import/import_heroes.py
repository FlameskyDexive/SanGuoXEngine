# -*- coding: utf-8 -*-
"""Imports the five heroes from the AIBridge export into this XEngine project.

    py -3 Tools/unity_export/run_export.py run export_heroes --out Export/heroes
    py -3 Tools/sanguo_import/import_heroes.py

Source files are only read from the Unity project (never modified). The export names them by Unity
asset path (`Assets/...`), resolved against the Unity project folder. What this writes:

  Assets/Heroes/<Hero>/<Hero>.fbx (+ .meta)      the model as-is, meta carrying the engine import settings
  Assets/Heroes/Textures/<name>.png (+ .meta)    albedo and normal textures at the size Unity imported them
  Assets/Heroes/Textures/<name>_SFX.png          derived surface map: R = 1-AO, G = 1-smoothness, B = metallic
  Assets/Heroes/Textures/<name>_EMI.png          derived emission map, only for materials that emit
  Export/import/heroes.json                      the manifest the C# generators read
  Export/import/heroes-report.json               what was written, and every approximation per material

Native materials and prefabs are NOT written here: they are engine objects, so the Editor-side C#
generator (Assets/Editor/Sanguo/SanguoHeroGenerator.cs) creates them from the manifest.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import sys
from pathlib import Path

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import numpy as np
from PIL import Image

from sanguo_import import engine_meta, material_mapping, texture_math

REPO_ROOT = Path(__file__).resolve().parents[2]
CONFIG = Path(__file__).with_name("config.json")
PACKED_SUFFIX = re.compile(r"_M_Alpha$", re.IGNORECASE)


def load_config():
    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    override = os.environ.get("SANGUO_JWS2_CLIENT")
    if override:
        config["jws2_client"] = override
    return config


def load_as_unity_imported(source, texture):
    """A source texture as Unity imported it: the export's width/height (after Unity's NPOT and
    max-size rules) and an RGB(A) uint8 array. Normal maps are re-normalized when resized."""
    with Image.open(source) as image:
        image.load()
        if image.mode in ("P", "LA", "PA"):
            image = image.convert("RGBA")
        elif image.mode not in ("RGB", "RGBA"):
            image = image.convert("RGB")
        size = (int(texture["width"]), int(texture["height"]))
        resized = image.size != size
        if resized:
            image = image.resize(size, Image.LANCZOS)
        array = np.asarray(image, dtype=np.uint8).copy()
    if array.ndim != 3 or array.shape[2] < 3:
        raise ValueError(f"{source}: expected an RGB(A) image, got shape {array.shape}")
    if texture.get("importer", {}).get("textureType") == "NormalMap":
        array[..., :3] = texture_math.renormalize_normal_map(array[..., :3])
    return array, resized


def save_png(array, path):
    """Writes an RGB(A) uint8 array as PNG; an all-opaque alpha channel is dropped."""
    if array.shape[2] == 4 and bool((array[..., 3] != 255).any()):
        channels = array
    else:
        channels = array[..., :3]
    Image.fromarray(np.ascontiguousarray(channels)).save(path, format="PNG", optimize=True)


def emission_key(recipe):
    """Identifies an emission recipe, so materials that share a mask and a recipe share a map."""
    tint = ",".join(f"{component:.6g}" for component in recipe["tint"])
    return f"{recipe['intensity']:.6g}:{tint}"


class HeroImport:
    def __init__(self, export, source_project, assets_root, root):
        self.export = export
        self.source_project = Path(source_project)   # Unity project folder; export paths start with Assets/
        self.assets_root = Path(assets_root)
        self.manifest = {"root": root, "models": {}, "textures": {}, "materials": {}, "heroes": []}
        self.report = {
            "models": 0, "textures": 0, "texturesResizedToUnitySize": 0,
            "textureBytesSource": 0, "textureBytesPng": 0, "derivedMaps": 0,
            "materials": 0, "materialNotes": [],
        }
        self.textures = {}   # Unity guid -> decoded array as Unity imported it
        self.derived = {}    # Unity guid of the packed texture -> {"stem", "surface", "emission": {key: path}}

    # ------------------------------------------------------------ models
    def import_models(self):
        for unity_path, model in self.export.get("models", {}).items():
            source = self.source_project / unity_path
            if not source.is_file():
                raise FileNotFoundError(f"model not found in the Unity project: {source}")
            relative = Path(self.manifest["root"]) / source.stem / source.name
            destination = self.assets_root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
            guid = engine_meta.unity_guid(model["guid"])
            engine_meta.write_meta(destination, guid, engine_meta.MODEL_IMPORTER, engine_meta.model_settings(model.get("importer")))
            self.manifest["models"][unity_path] = {"enginePath": relative.as_posix(), "guid": guid}
            self.report["models"] += 1

    # ------------------------------------------------------------ textures
    def referenced_texture_guids(self):
        """The Unity guids the mapped materials actually read. jws2 materials bind more textures
        than Default/Standard has slots for (flow-light layers, skin ramps); those are not imported."""
        guids = set()
        for material in self.used_materials():
            properties = material_mapping.map_material(material)["properties"]
            for key in ("_MainTex", "_NormalTex", "_SurfaceTex"):
                if properties.get(key):
                    guids.add(properties[key])
        return guids

    def used_materials(self):
        """The exported material records of every slot a hero sub-mesh draws with, each once."""
        seen = set()
        for hero in self.export.get("heroes", []):
            for smr in hero.get("skinnedMeshes", []):
                for slot in smr.get("materials", []):
                    if slot is None or not slot.get("usedBySubMesh", True) or slot["path"] in seen:
                        continue
                    seen.add(slot["path"])
                    yield self.export["materials"][slot["path"]]

    def import_textures(self):
        needed = self.referenced_texture_guids()
        self.report["texturesSkippedUnused"] = len(self.export.get("textures", {})) - len(needed)
        for guid, texture in self.export.get("textures", {}).items():
            if guid not in needed:
                continue
            source = self.source_project / texture["path"]
            if not source.is_file():
                raise FileNotFoundError(f"texture not found in the Unity project: {source}")
            array, resized = load_as_unity_imported(source, texture)
            destination = self._texture_destination(source.stem + ".png")
            save_png(array, destination)
            importer = texture.get("importer", {})
            max_size = max(int(texture["width"]), int(texture["height"]))
            engine_meta.write_meta(destination, engine_meta.unity_guid(guid), engine_meta.TEXTURE_IMPORTER,
                                   engine_meta.texture_settings(importer, max_size))
            self.manifest["textures"][guid] = {"enginePath": self._relative(destination).as_posix()}
            self.textures[guid] = array
            self.report["textures"] += 1
            self.report["texturesResizedToUnitySize"] += int(resized)
            self.report["textureBytesSource"] += source.stat().st_size
            self.report["textureBytesPng"] += destination.stat().st_size

    def _texture_destination(self, file_name):
        destination = self.assets_root / self.manifest["root"] / "Textures" / file_name
        destination.parent.mkdir(parents=True, exist_ok=True)
        return destination

    def _relative(self, path):
        return path.relative_to(self.assets_root)

    def _packed_entry(self, guid):
        entry = self.derived.get(guid)
        if entry is None:
            stem = Path(self.export["textures"][guid]["path"]).stem
            entry = self.derived[guid] = {"stem": stem, "surface": None, "emission": {}}
        return entry

    def _packed_array(self, guid):
        array = self.textures.get(guid)
        if array is None:
            raise KeyError(f"packed surface texture {guid} was not exported")
        if array.shape[2] < 4:
            raise ValueError(f"packed surface texture {guid} has no alpha channel, so smoothness is missing")
        return array

    def surface_map(self, guid):
        """Writes (once per packed texture) and returns the engine path of the derived surface map."""
        entry = self._packed_entry(guid)
        if entry["surface"] is None:
            packed = self._packed_array(guid)
            array = texture_math.repack_surface(
                texture_math.channel_to_unit(packed[..., 0]),
                texture_math.channel_to_unit(packed[..., 1]),
                texture_math.channel_to_unit(packed[..., 3]))
            destination = self._texture_destination(PACKED_SUFFIX.sub("", entry["stem"]) + "_SFX.png")
            save_png(array, destination)
            engine_meta.write_meta(destination, engine_meta.stable_guid("derived", guid, "surface"), engine_meta.TEXTURE_IMPORTER,
                                   engine_meta.surface_texture_settings(max(array.shape[:2])))
            entry["surface"] = self._relative(destination).as_posix()
            self.report["derivedMaps"] += 1
        return entry["surface"]

    def emission_map(self, guid, recipe):
        """Writes (once per packed texture and recipe) and returns the engine path of the emission map."""
        entry = self._packed_entry(guid)
        key = emission_key(recipe)
        if key not in entry["emission"]:
            packed = self._packed_array(guid)
            array = texture_math.bake_emission(texture_math.channel_to_unit(packed[..., 2]), recipe)
            stem = PACKED_SUFFIX.sub("", entry["stem"]) + "_EMI"
            if entry["emission"]:
                stem += f"_{len(entry['emission']) + 1}"
            destination = self._texture_destination(stem + ".png")
            save_png(array, destination)
            engine_meta.write_meta(destination, engine_meta.stable_guid("derived", guid, "emission", key), engine_meta.TEXTURE_IMPORTER,
                                   engine_meta.emission_texture_settings(max(array.shape[:2])))
            entry["emission"][key] = self._relative(destination).as_posix()
            self.report["derivedMaps"] += 1
        return entry["emission"][key]

    # ------------------------------------------------------------ materials
    def import_materials(self):
        for material in self.used_materials():
            self.import_material(material["path"])

    def import_material(self, unity_path):
        if unity_path in self.manifest["materials"]:
            return
        material = self.export["materials"][unity_path]
        mapped = material_mapping.map_material(material)
        name = material["name"]

        def engine_path(guid):
            entry = self.manifest["textures"].get(guid)
            if entry is None:
                raise KeyError(f"material {name!r} references texture guid {guid}, which the export did not describe")
            return entry["enginePath"]

        properties = {}
        emission = mapped["emission"]
        for key, value in mapped["properties"].items():
            if key == "_SurfaceTex":
                if value is None:
                    properties[key] = None     # keep the shader's default surface map
                    continue
                properties[key] = self.surface_map(value)
            elif key == "_EmissionTex":
                continue
            elif key in ("_MainTex", "_NormalTex"):
                properties[key] = engine_path(value)
            else:
                properties[key] = value
        if emission is not None:
            if emission["maskTexture"] is None:
                raise ValueError(f"material {name!r} emits but binds no _MetallicGlossMap; not handled")
            properties["_EmissionTex"] = self.emission_map(emission["maskTexture"], emission["recipe"])

        destination = self.assets_root / self.manifest["root"] / "Materials" / (name + ".mat")
        self.manifest["materials"][unity_path] = {
            "name": name,
            "unityShader": material["shader"],
            "enginePath": self._relative(destination).as_posix(),
            "guid": engine_meta.unity_guid(material["guid"]),
            "shader": mapped["shader"],
            "properties": properties,
            "notes": mapped["notes"],
        }
        self.report["materials"] += 1
        self.report["materialNotes"].append({"material": name, "shader": mapped["shader"], "notes": mapped["notes"]})

    # ------------------------------------------------------------ heroes
    def import_heroes(self):
        for hero in self.export.get("heroes", []):
            if not hero.get("found"):
                continue
            name = hero["name"]
            if hero.get("meshRendererCount"):
                raise ValueError(f"hero {name!r} has {hero['meshRendererCount']} plain MeshRenderer(s), "
                                 "which the prefab generator does not bind")
            model = None
            skinned = []
            for smr in hero.get("skinnedMeshes", []):
                mesh = smr.get("mesh") or {}
                if model is None:
                    model = self.manifest["models"].get(mesh.get("path"))
                node = smr["node"]
                if node.startswith(name + "/"):
                    node = node[len(name) + 1:]
                skinned.append({
                    "node": node,
                    "mesh": mesh.get("name"),
                    "materials": [self.manifest["materials"][slot["path"]]["enginePath"]
                                  for slot in smr.get("materials", []) if slot and slot.get("usedBySubMesh", True)],
                })
            if model is None:
                raise KeyError(f"hero {name!r}: its FBX was not exported")
            controller = (hero.get("animator") or {}).get("controller") or {}
            self.manifest["heroes"].append({
                "name": name,
                "prefabPath": f"{self.manifest['root']}/Prefabs/{name}.prefab",
                "guid": engine_meta.unity_guid(hero["prefabGuid"]),
                "model": model["enginePath"],
                "skinnedMeshes": skinned,
                "unityRootSpaceBounds": hero.get("rootSpaceBounds"),
                "unityAnimator": {"controllerPath": controller.get("path"), "controllerGuid": controller.get("guid")},
            })

    # ------------------------------------------------------------ entry
    def run(self):
        self.import_models()
        self.import_textures()
        self.import_materials()
        self.import_heroes()
        return self


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--export", help="heroes.json from Tools/unity_export (default: config)")
    parser.add_argument("--out", help="folder under Assets/ to write to (default: config)")
    args = parser.parse_args(argv)
    config = load_config()
    export_path = REPO_ROOT / (args.export or config["export"])
    export = json.loads(export_path.read_text(encoding="utf-8"))
    source_project = Path(config["jws2_client"])

    importer = HeroImport(export, source_project, REPO_ROOT / "Assets", args.out or config["output_root"]).run()
    importer.manifest["source"] = export_path.relative_to(REPO_ROOT).as_posix()

    manifest_path = REPO_ROOT / config["manifest"]
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(importer.manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    report_path = REPO_ROOT / config["report"]
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(importer.report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")

    report = importer.report
    print(f"models    {report['models']}")
    print(f"textures  {report['textures']} ({report['texturesResizedToUnitySize']} resized to Unity's imported size; "
          f"{report['textureBytesSource'] / 1048576:.1f} MB source -> {report['textureBytesPng'] / 1048576:.1f} MB PNG)")
    print(f"derived   {report['derivedMaps']} surface / emission maps")
    print(f"materials {report['materials']} mapped, {sum(1 for m in report['materialNotes'] if m['notes'])} with recorded approximations")
    print(f"heroes    {len(importer.manifest['heroes'])}")
    print(f"manifest  {manifest_path.relative_to(REPO_ROOT).as_posix()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
