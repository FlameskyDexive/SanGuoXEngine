---
name: runtime-qa
description: Run deterministic XEngine gameplay checks with input replay and fixed simulation time.
---

# runtime-qa

Use a JSON input script with frame-indexed events, for example:

```json
{"events":[{"frame":0,"action":"press","key":"D"},{"frame":30,"action":"release","key":"D"}]}
```

Run it with:

```bash
xengine-cli run-headless --project <root> --player <player> --max-frames 60 \
  --fixed-delta 0.0166667 --input-script .xengine/input.json --stop-on-error
```

Use the live editor runtime tools for interactive checks. Reset injected input after a run when
driving a long-lived editor session.
