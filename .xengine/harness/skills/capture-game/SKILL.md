---
name: capture-game
description: Capture deterministic XEngine gameplay evidence and package the resulting artifacts.
---

# capture-game

The pipeline records fixed-step frame sequences and writes their run manifest under
`.xengine/evidence/<run-id>/`. PNG frames are the portable evidence format. MP4 encoding is optional
when ffmpeg is available and becomes required only when the game manifest sets `requireVideo`.

Pair capture with `game-verify` so logs, input replay, performance gates, asset hashes, and journal
state are stored beside the visual evidence.
