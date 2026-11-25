# Tools — offline converters for the SanGuoXEngine port

Python lives here and nowhere else. These tools read the source project at `F:\jws2\client`
(read-only), convert what the port needs, and write native assets plus a report. Nothing in this
folder ships with the game or is referenced by runtime code.

Run everything with `py -3` from the repository root.

| Folder | Purpose |
|---|---|
| `unity_export/` | AIBridge driver + `.csx` exporter scripts run inside the jws2 Unity editor |
| `sanguo_import/` | (M1+) copy FBX/textures, repack `_SurfaceTex` channels, extract Lua tables to JSON |
| `wwise2ogg/` | (M6) Wwise banks → OGG Vorbis + event mapping table |

Downloaded third-party binaries go to `Tools/bin/` (git-ignored). Intermediate JSON goes to
`Export/` at the repository root (git-ignored).

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
