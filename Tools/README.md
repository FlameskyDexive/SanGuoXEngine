# Tools — offline converters for the SanGuoXEngine port

Python lives here and nowhere else. These tools read the source project at `F:\jws2\client`
(read-only), convert what the port needs, and write native assets plus a report. Nothing in this
folder ships with the game or is referenced by runtime code.

Run everything with `py -3` from the repository root. The tools' tests: `py -3 -m pytest Tools -q`.

| Folder | Purpose |
|---|---|
| `unity_export/` | AIBridge driver + `.csx` scripts run inside the jws2 Unity editor: exporters, reference captures, probes |
| `sanguo_import/` | import the heroes (FBX, textures, metas, import manifest), and compare models, skinning and animation with Unity |
| `xengine_live/` | MCP stdio client for a live XEngine editor + `.cs` scripts it evaluates there |
| `wwise2ogg/` | (M6) Wwise banks → OGG Vorbis + event mapping table |

Downloaded third-party binaries go to `Tools/bin/` (git-ignored). Intermediate JSON and reference
frames go to `Export/` at the repository root (git-ignored); what is kept as evidence is copied to
`docs/evidence/`.

## unity_export

The exporter runs C# scripts **inside the jws2 editor** through the project's AIBridge CLI, so the
data it produces is already resolved by Unity (prefab instances, mesh references, animator
controllers). It never touches anything under `F:\jws2\client\Assets`: scripts are staged into
jws2's own `.aibridge/code/` cache folder and outputs are written under `Export/`.

```powershell
py -3 Tools/unity_export/run_export.py status          # is the jws2 editor reachable?
py -3 Tools/unity_export/run_export.py run probe_env   # run one script
py -3 Tools/unity_export/run_export.py run --all       # run every script in scripts/ order
py -3 Tools/unity_export/run_export.py run --all --launch   # start the jws2 editor first
```

Requirements: the jws2 editor (Unity 2022.3.62f3, project `F:\jws2\client`) must be open and idle.
Do not use that editor while an export is running.

| Script | Writes | Used by |
|---|---|---|
| `export_heroes` | `Export/heroes/heroes.json`: every hero's anatomy, models, materials with all properties, textures with import settings | `import_heroes.py` |
| `capture_heroes`, `capture_heroes_standard` | reference frames of the five heroes with the game's shaders, and with Unity's Standard shader | the side-by-side sheets |
| `export_skin` | per mesh: raw vertices, bone weights, bind poses, `BakeMesh` output | `compare_skin.py` |
| `capture_silhouettes` | every mesh alone as a flat silhouette | `compare_skin.py --silhouettes` |
| `export_clips` | `Export/clips/Hero_*.json`: the battle clips of one hero (`--filter Hero_LvBu`) — every curve with its keys and tangents, the clip settings, Unity's sampled pose of every node at 11 fixed times, and a reference frame at 3 of them | `SanguoClipBuilder` (in the editor), `compare_anim.py` |
| `probe_*` | one-off audits (environment, material, hero, clip, scene, API surface, skinning) | recorded in `docs/evidence/` |

Each capture script takes its own output folder: `--out Export/unity-refs/heroes`,
`Export/unity-refs/heroes-standard`, `Export/heroes/skin`, `Export/unity-refs/silhouettes`.

**Outside play mode Unity skins a skinned mesh on its own schedule.** A camera rendered right after
posing a hero can show the previous skin. `export_clips` switches every `SkinnedMeshRenderer` off and
on before each render; a capture that poses a character must do the same, or its frames are stale.

**The quality level decides how many bones skin a vertex**, and jws2's active level ("Fastest") skins
with one bone. The engine skins with four, so the capture scripts switch to the lowest level that
does too ("Simple"), record it in `shots.json`, and put the editor's level back afterwards. Nothing
is saved to the jws2 project.

## sanguo_import

| Module | Purpose |
|---|---|
| `import_heroes.py` | reads `heroes.json` and writes `Assets/Heroes/`: the FBX files, the textures (PNG, at the size Unity imported them), the derived `_SFX` surface maps and `_EMI` maps, every `.meta`, and the manifest `Export/import/heroes.json` that the editor-side generators read |
| `texture_math.py` | the channel arithmetic: `_M_Alpha` → `_SurfaceTex`, emission mask × colour → `_EmissionTex` |
| `material_mapping.py` | Unity material record → engine shader and properties |
| `engine_meta.py` | engine `.meta` files out of Unity's importer settings |
| `compare_models.py` | node sets, positions and scales of the imported models against Unity's transform tree |
| `compare_skin.py` | skinning of every mesh against Unity: vertices, skin matrices, the formula, the weights, and (with `--silhouettes`) what each renderer drew |
| `fbx_skin_weights.py` | the weights the FBX file itself stores, per mesh and per control point |
| `compare_anim.py` | the engine's played poses (from `capture_poses.cs`) against Unity's sampled poses, bone by bone and time by time; also the rendered hero's extent in every Unity | engine frame pair, and with `--sheets` contact sheets |
| `upgrade_legacy_fbx.py` | converts the four legacy FBX 6100 meshes the engine rejects (`t_shanshi04_01`, `i_ly_muche01_01_lod2`, the two ASCII `i_zx_xiaodiaodeng01` lantern LODs) into binary FBX 7400 in place. Reads both 6100 layouts (binary: mesh data inside the Model node; ASCII: bare-value arrays, `*N { a: … }` wrappers, and the brace miscounts of 2015-era Max exports), fans quads into triangles and remaps the per-corner normals and UV indices by corner position, then writes a minimal 7400 (Model + Geometry + scalar-long OO connections + W-flipped PolygonVertexIndex) |

```powershell
py -3 Tools/sanguo_import/import_heroes.py
py -3 Tools/sanguo_import/compare_models.py <mcp_live.py --out file of inspect_models.cs>
py -3 Tools/sanguo_import/compare_skin.py --silhouettes --report Export/skin-report.json
py -3 Tools/sanguo_import/fbx_skin_weights.py Assets/Heroes/Hero_GuanYu/Hero_GuanYu.fbx --mesh Hero_GuanYu_Hair
py -3 Tools/unity_export/clip_manifest.py Hero_LvBu
py -3 Tools/unity_export/run_export.py run export_clips --filter Hero_LvBu --out Export/clips --timeout-ms 900000
py -3 Tools/sanguo_import/compare_anim.py --sheets docs/evidence/m2/sheets
py -3 Tools/sanguo_import/upgrade_legacy_fbx.py <source.FBX> <target.FBX>   # or --info <file>
```

`export_clips` runs once per hero. `compare_anim.py` exits with status 1 when a rotation is more than
0.5°, a bone position more than 5 mm or a root-motion position more than 30 mm away from Unity's
sample; add `--strict-frames` to fail on frame pairs whose hero extent differs by more than 24 px.

`compare_skin.py` exits with status 1 when a mesh's vertices or its CPU skinning are more than
`--tolerance-mm` (default 2) away from Unity's.

## xengine_live

`mcp_live.py` starts `XEngine.Editor --serve --project <root>` and talks MCP over stdio to it.

```powershell
py -3 Tools/xengine_live/mcp_live.py --wait-compile 300 --eval-file Tools/xengine_live/evals/capture_heroes.cs
py -3 Tools/xengine_live/mcp_live.py --graphics d3d12 --eval-file Tools/xengine_live/evals/capture_heroes.cs
py -3 Tools/xengine_live/mcp_live.py --inline '[{"tool":"runtime_screenshot","args":{"path":"shot.png"}}]'
```

The editor is closed when the commands are done unless `--keep` is given. `SANGUO_CAPTURE_OUT=<folder>`
redirects the frames of `capture_heroes.cs`, `SANGUO_POSE_OUT` those of `capture_poses.cs`, e.g. per
backend. The scripts under `evals/`:

| Script | Does |
|---|---|
| `generate_heroes.cs` | runs `SanguoHeroGenerator` (materials, prefabs) in the editor; then refresh assets to import them |
| `capture_heroes.cs` | runs `SanguoCaptureStudio.CaptureHeroes`: the heroes under the Unity studio setup |
| `render_silhouettes.cs` | runs `SanguoCaptureStudio.CaptureSilhouettes` |
| `generate_clips.cs` | runs `SanguoClipBuilder.Generate`: native clips and one controller per hero out of `Export/clips`; then refresh assets to import them |
| `capture_poses.cs` | runs `SanguoCaptureStudio.CapturePoses`: plays every exported clip on the Animator, dumps the nodes' transforms at Unity's sample times (`poses.json`) and renders Unity's frames |
| `inspect_models.cs` | dumps the imported hierarchies for `compare_models.py` |
| `dump_skin.cs` | dumps the engine's skinning data for `compare_skin.py` |

Project types are not referenceable by name in an eval (scripts load in their own collectible
context), so the scripts reach them by reflection.
