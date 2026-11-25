---
name: game-pipeline
description: Drive the high-level XEngine AI game brief, assembly, verification and packaging flow.
---

# game-pipeline

完整流程见 [`docs/ai-game-development-pipeline.md`](../../../docs/ai-game-development-pipeline.md)。本技能只保留执行顺序和验收规则。

## 执行顺序

Create or update the project brief with:

```bash
xengine-cli game-init --project <root> --brief "Collect three crystals" --dimension 2d
```

When `<root>` does not exist, pass `--create-project --editor <XEngine.Editor.exe>` to route
creation through the editor's existing `--hub-command createProject` protocol before writing the
brief. `game-assemble` then generates mock assets through the normal import path, writes a replay
script, and creates a deterministic scene/controller scaffold when the entry scene is new.

Inspect and validate it with `game-inspect` and `game-assemble`. `game-verify` writes a structured
acceptance report. `game-package --output <zip>` includes the brief and available evidence.

Capture and headless runs use the manifest's fixed FPS, optional `capture.seed`, and replay hash.
PNG frames remain the required offline artifact. MP4 encoding is attempted only when requested and
is a gate only when `requireVideo` is true. Packages use sorted entries and stable timestamps so a
repeat of the same project state is reproducible.

The same commands are available through MCP as `game_init`, `game_inspect`, `game_assemble`,
`game_verify`, and `game_package`. The manifest is idempotent and stores asset prompts, controls,
build settings, capture settings, and performance budgets.

Starter briefs live under `cli/samples/game-briefs/2d-crystal-run.json` and
`cli/samples/game-briefs/3d-arena-interaction.json`. The `game-vertical-slice` workflow chains
assembly, verification, and packaging for either brief. The generated playable reference is
`Samples/AiCrystalRun`.

## Acceptance checklist

1. `game-inspect` reports the intended entry scene and replay path.
2. `game-assemble` returns no missing required assets.
3. Desktop build contains the manifest entry scene and its GUID in `ProjectSettings/Build.yaml`.
4. `run-headless` succeeds with a fixed delta, seed and replay script.
5. `game-capture` produces the requested PNG count and stable frame hashes.
6. `game-verify` passes log, journal and performance gates.
7. `game-package` is repeated once and its SHA-256 is compared.
