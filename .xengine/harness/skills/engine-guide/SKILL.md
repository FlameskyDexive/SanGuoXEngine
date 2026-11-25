---
name: engine-guide
description: Build and verify an XEngine game project through the AI pipeline.
---

# XEngine engine guide

XEngine projects keep source assets under `Assets/`, project settings under `ProjectSettings/`,
and the AI brief under `.xengine/game.json`. Read the brief before authoring scenes or scripts.

Use `game-init` to create the brief, `game-assemble` to generate/import mock assets and scaffold a
vertical slice, and `build-project`
before running a player. Headless runs accept `--fixed-delta`, `--input-script`, `--seed`, and
`--stop-on-error` for repeatable gameplay checks.

The canonical acceptance sequence is `game-verify`, followed by `game-package`. Evidence is written
under `.xengine/evidence/<run-id>/` and includes the manifest, gate results, and journal state.
