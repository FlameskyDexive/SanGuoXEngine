# -*- coding: utf-8 -*-
"""Imports the 桃园 scene's referenced assets from the AIBridge export into this XEngine project.

    py -3 Tools/unity_export/run_export.py run export_scene_assets --out Export/scene
    py -3 Tools/sanguo_import/import_scene_assets.py

Source files are only read from the Unity project (never modified). What this writes:

  Assets/Scene/Fbx/<source path minus Assets/>          the FBX files as-is (+ .meta)
  Assets/Scene/Textures/<source path minus Assets/>.png textures at the size Unity imported them
  Assets/Scene/Textures/<stem>_SEQ0.png                 flipbook first frame (2 Sequence materials)
  Assets/Scene/Textures/<stem>_SFX.png                  derived surface map (packed M/A → R/G/B)
  Assets/Scene/Textures/<stem>_EMI.png                  derived emission map (materials that emit)
  Export/import/scene-assets.json                       the manifest SanguoSceneBuilder reads
  Export/import/scene-assets-report.json                what was written, and every approximation

Effects materials (fire, smoke, waterfall, distort — the 21 `Effects/*` and `Particles/*` shaders)
and particle renderers are M5 and are skipped here, counted in the report. The one SpeedTree
`.asset` mesh (16 instances, a Unity-serialized mesh the engine cannot import yet) is recorded as
unimplemented. The scene document's node list itself is not imported here — SanguoSceneBuilder
(M3-3) reads it together with this manifest.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
from pathlib import Path

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import numpy as np

from sanguo_import import engine_meta, material_mapping, texture_math
from sanguo_import.import_heroes import load_as_unity_imported, load_config, save_png

REPO_ROOT = Path(__file__).resolve().parents[2]


class SceneAssetImport:
    def __init__(self, scene, export, source_project, assets_root, root):
        self.scene = scene            # scene.json: nodes, renderSettings, frames
        self.export = export          # scene-assets.json: meshes, models, materials, textures
        self.source_project = Path(source_project)
        self.assets_root = Path(assets_root)
        self.manifest = {"root": root, "models": {}, "meshes": {}, "textures": {}, "materials": {},
                         "lightmaps": [], "sky": None}
        self.derived = {}   # (kind, guid[, recipe]) -> engine path; referenced from material properties
        self.report = {
            "models": 0, "textures": 0, "texturesResizedToUnitySize": 0,
            "textureBytesSource": 0, "textureBytesPng": 0, "derivedMaps": 0,
            "materials": 0, "materialsSkippedEffects": 0, "materialNotes": [],
            "meshesUnimplemented": [],
        }
        self.textures = {}   # Unity guid -> decoded array as Unity imported it

    # ------------------------------------------------------------ models
    def import_models(self):
        for unity_path, model in self.export.get("models", {}).items():
            if not unity_path.lower().endswith(".fbx"):
                if unity_path.lower().endswith(".asset"):
                    for key, mesh in self.export["meshes"].items():
                        if mesh["file"] == unity_path:
                            self.report["meshesUnimplemented"].append(key)
                continue
            source = self.source_project / unity_path
            if not source.is_file():
                raise FileNotFoundError(f"model not found in the Unity project: {source}")
            relative = Path(self.manifest["root"]) / "Fbx" / _without_assets(unity_path)
            destination = self.assets_root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
            guid = engine_meta.unity_guid(model["guid"])
            engine_meta.write_meta(destination, guid, engine_meta.MODEL_IMPORTER,
                                   engine_meta.model_settings(model.get("importer")))
            self.manifest["models"][unity_path] = {"enginePath": relative.as_posix(), "guid": guid}
            self.report["models"] += 1
        for key in self.report["meshesUnimplemented"]:
            path, mesh_name = key.split("|", 1)
            self.manifest["meshes"][key] = {"fbx": None, "meshName": mesh_name,
                                            "reason": "unity .asset mesh import not implemented"}
        for key, mesh in self.export.get("meshes", {}).items():
            path, mesh_name = key.split("|", 1)
            model = self.manifest["models"].get(path)
            if model is None:
                if not path.lower().endswith(".fbx"):
                    continue        # built-in Quad / Cube: the builder creates these meshes itself
                continue            # the unimplemented .asset, already recorded
            self.manifest["meshes"][key] = {"fbx": model["enginePath"], "fbxGuid": model["guid"],
                                            "meshName": mesh_name}

    # ------------------------------------------------------------ textures
    def used_materials(self):
        """(unity path, exported record) of every material the scene's renderers draw with, each
        once, in scene order. Effects materials are skipped (M5) and counted."""
        for path in _used_material_paths(self.scene):
            material = self.export["materials"].get(path)
            if material is None:
                raise KeyError(f"the scene draws with {path!r}, which the asset export did not describe")
            if material["shader"] not in material_mapping.SCENE_SHADER_MAP:
                self.report["materialsSkippedEffects"] += 1
                continue
            yield path, material

    def referenced_texture_guids(self):
        guids = set()
        for _, material in self.used_materials():
            mapped = material_mapping.map_scene_material(material)
            for key in ("_MainTex", "_NormalTex", "_SurfaceTex"):
                if mapped["properties"].get(key):
                    guids.add(mapped["properties"][key])
            if mapped["emission"] and mapped["emission"]["maskTexture"]:
                guids.add(mapped["emission"]["maskTexture"])
            if mapped["sequence"]:
                guids.add(_texture_guid_of(material, "_MainTex"))
        return guids

    def import_textures(self):
        needed = self.referenced_texture_guids()
        for guid, texture in self.export.get("textures", {}).items():
            if guid not in needed:
                continue
            source = self.source_project / texture["path"]
            if not source.is_file():
                raise FileNotFoundError(f"texture not found in the Unity project: {source}")
            array, resized = load_as_unity_imported(source, texture)
            destination = self._texture_destination(_without_assets(texture["path"]), ".png")
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

    def _texture_destination(self, relative_name, suffix=None):
        destination = self.assets_root / self.manifest["root"] / "Textures" / Path(relative_name).with_suffix(suffix or ".png")
        destination.parent.mkdir(parents=True, exist_ok=True)
        return destination

    def _relative(self, path):
        return path.relative_to(self.assets_root)

    def _array(self, guid):
        array = self.textures.get(guid)
        if array is None:
            raise KeyError(f"texture {guid} was not imported")
        return array

    # ------------------------------------------------------------ derived maps
    def sequence_frame(self, guid, sequence):
        """Writes (once) and returns the engine path of the flipbook's first frame: Unity's UV
        origin makes frame 0 the bottom-left cell, which is the last row band of the pixels."""
        entry = self.derived.get(("sequence0", guid))
        if entry is not None:
            return entry
        array = self._array(guid)
        columns, rows = max(1, sequence["columns"]), max(1, sequence["rows"])
        cell_h = array.shape[0] // rows
        cell_w = array.shape[1] // columns
        frame = array[array.shape[0] - cell_h:, 0:cell_w].copy()
        stem = Path(self.export["textures"][guid]["path"]).stem
        destination = self._texture_destination(f"{Path(_without_assets(self.export['textures'][guid]['path'])).stem}_SEQ0", ".png")
        save_png(frame, destination)
        engine_meta.write_meta(destination, engine_meta.stable_guid("derived", guid, "sequence0"),
                               engine_meta.TEXTURE_IMPORTER, engine_meta.texture_settings({}, max(frame.shape[:2])))
        entry = self._relative(destination).as_posix()
        self.derived[("sequence0", guid)] = entry
        self.report["derivedMaps"] += 1
        return entry

    def surface_map(self, guid):
        entry = self.derived.get(("surface", guid))
        if entry is not None:
            return entry
        packed = self._array(guid)
        if packed.shape[2] < 4:
            raise ValueError(f"packed surface texture {guid} has no alpha channel, so smoothness is missing")
        array = texture_math.repack_surface(
            texture_math.channel_to_unit(packed[..., 0]),
            texture_math.channel_to_unit(packed[..., 1]),
            texture_math.channel_to_unit(packed[..., 3]))
        source_stem = Path(_without_assets(self.export["textures"][guid]["path"])).stem
        destination = self._texture_destination(f"{source_stem}_SFX", ".png")
        save_png(array, destination)
        engine_meta.write_meta(destination, engine_meta.stable_guid("derived", guid, "surface"),
                               engine_meta.TEXTURE_IMPORTER, engine_meta.surface_texture_settings(max(array.shape[:2])))
        entry = self._relative(destination).as_posix()
        self.derived[("surface", guid)] = entry
        self.report["derivedMaps"] += 1
        return entry

    def emission_map(self, guid, recipe):
        key = ("emission", guid, texture_math.emission_key(recipe))
        entry = self.derived.get(key)
        if entry is not None:
            return entry
        packed = self._array(guid)
        if packed.shape[2] < 4:
            raise ValueError(f"packed emission texture {guid} has no alpha channel")
        array = texture_math.bake_emission(texture_math.channel_to_unit(packed[..., 2]), recipe)
        source_stem = Path(_without_assets(self.export["textures"][guid]["path"])).stem
        destination = self._texture_destination(f"{source_stem}_EMI", ".png")
        save_png(array, destination)
        engine_meta.write_meta(destination, engine_meta.stable_guid("derived", guid, "emission", texture_math.emission_key(recipe)),
                               engine_meta.TEXTURE_IMPORTER, engine_meta.emission_texture_settings(max(array.shape[:2])))
        entry = self._relative(destination).as_posix()
        self.derived[key] = entry
        self.report["derivedMaps"] += 1
        return entry

    # ------------------------------------------------------------ materials
    def import_materials(self):
        for unity_path, material in self.used_materials():
            self.import_material(unity_path, material)

    def import_material(self, unity_path, material):
        if unity_path in self.manifest["materials"]:
            return
        mapped = material_mapping.map_scene_material(material)
        name = material["name"]

        def engine_path(guid):
            entry = self.manifest["textures"].get(guid)
            if entry is None:
                raise KeyError(f"material {name!r} references texture guid {guid}, which was not imported")
            return entry["enginePath"]

        properties = {}
        for key, value in mapped["properties"].items():
            if key in ("_MainTex", "_NormalTex"):
                properties[key] = engine_path(value)
            elif key == "_SurfaceTex":
                properties[key] = self.surface_map(value)
            elif key == "_EmissionTex":
                continue
            else:
                properties[key] = value
        if mapped["sequence"]:
            base_guid = _texture_guid_of(material, "_MainTex")
            if base_guid is None:
                raise ValueError(f"sequence material {name!r} binds no _MainTex")
            properties["_MainTex"] = self.sequence_frame(base_guid, mapped["sequence"])
        if mapped["emission"] is not None:
            if mapped["emission"]["maskTexture"] is None:
                raise ValueError(f"material {name!r} emits but binds no _MetallicGlossMap; not handled")
            properties["_EmissionTex"] = self.emission_map(mapped["emission"]["maskTexture"], mapped["emission"]["recipe"])

        destination = self.assets_root / self.manifest["root"] / "Materials" / _without_assets(unity_path)
        destination.parent.mkdir(parents=True, exist_ok=True)
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

    # ------------------------------------------------------------ lightmaps + sky
    def record_lightmaps_and_sky(self):
        """Records the lightmap atlases and the sky as builder instructions. The textures
        themselves are exported by Unity in M3-3's capture step (lightmap EXR / HDR need Unity's
        blit-to-RT path), so only the references live in the manifest here."""
        doc = self.scene.get("lightmaps") or []
        for i, data in enumerate(doc):
            self.manifest["lightmaps"].append({
                "index": i,
                "color": data.get("color"),
                "dir": data.get("dir"),
                "shadowMask": data.get("shadowMask"),
            })
        reflection = (self.scene.get("renderSettings") or {}).get("customReflection")
        skybox = next((m for m in self.manifest["materials"].values() if m["shader"] == "Skybox"), None)
        self.manifest["sky"] = {
            "material": skybox["enginePath"] if skybox else None,
            "customReflection": reflection,
            "sunPath": (self.scene.get("renderSettings") or {}).get("sun"),
            "lodBias": self.scene.get("lodBias"),
        }

    # ------------------------------------------------------------ entry
    def run(self):
        self.import_models()
        self.import_textures()
        self.import_materials()
        self.record_lightmaps_and_sky()
        return self


def _without_assets(unity_path):
    """`Assets/GameLevels/Raw/Fbx/x.FBX` → `GameLevels/Raw/Fbx/x.FBX` (a path under the import root)."""
    return unity_path[len("Assets/"):] if unity_path.startswith("Assets/") else unity_path


def _used_material_paths(scene):
    used = []
    seen = set()
    for node in scene.get("nodes", []):
        for slot in node.get("materials", []) or []:
            path = slot.get("path") if slot else None
            if not path or path in seen:
                continue
            seen.add(path)
            used.append(path)
    return used


def _texture_guid_of(material, name):
    """The guid bound to a texture slot, in either property shape (hero list / scene object)."""
    properties = material.get("properties", {})
    if isinstance(properties, dict):
        record = properties.get(name)
        return record.get("texture") if isinstance(record, dict) else None
    for prop in properties:
        if prop["name"] == name and prop.get("value"):
            return prop["value"].get("texture")
    return None


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--scene", help="scene.json from export_scene.csx (default: config)")
    parser.add_argument("--export", help="scene-assets.json from export_scene_assets.csx (default: config)")
    parser.add_argument("--out", help="folder under Assets/ to write to (default: config)")
    args = parser.parse_args(argv)
    config = load_config()

    scene_path = REPO_ROOT / (args.scene or config.get("scene_export_scene", "Export/scene/scene.json"))
    export_path = REPO_ROOT / (args.export or config.get("scene_export_assets", "Export/scene/scene-assets.json"))
    scene = json.loads(scene_path.read_text(encoding="utf-8"))
    export = json.loads(export_path.read_text(encoding="utf-8"))
    source_project = Path(config["jws2_client"])

    importer = SceneAssetImport(scene, export, source_project, REPO_ROOT / "Assets",
                                args.out or config.get("scene_output_root", "Scene")).run()
    importer.manifest["sceneDocument"] = scene_path.relative_to(REPO_ROOT).as_posix()
    importer.manifest["source"] = export_path.relative_to(REPO_ROOT).as_posix()

    manifest_path = REPO_ROOT / config.get("scene_manifest", "Export/import/scene-assets.json")
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(importer.manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    report_path = REPO_ROOT / config.get("scene_report", "Export/import/scene-assets-report.json")
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(importer.report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")

    report = importer.report
    print(f"models    {report['models']} FBX imported")
    print(f"textures  {report['textures']} ({report['texturesResizedToUnitySize']} resized; "
          f"{report['textureBytesSource'] / 1048576:.1f} MB source -> {report['textureBytesPng'] / 1048576:.1f} MB PNG)")
    print(f"derived   {report['derivedMaps']} sequence crops / surface / emission maps")
    print(f"materials {report['materials']} mapped, {report['materialsSkippedEffects']} effect materials skipped (M5), "
          f"{sum(1 for m in report['materialNotes'] if m['notes'])} with recorded approximations")
    if report["meshesUnimplemented"]:
        print(f"unimplemented meshes: {len(report['meshesUnimplemented'])} ({', '.join(report['meshesUnimplemented'])})")
    print(f"manifest  {manifest_path.relative_to(REPO_ROOT).as_posix()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
