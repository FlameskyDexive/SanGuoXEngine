# -*- coding: utf-8 -*-
"""run_export.py — drive the jws2 Unity editor through AIBridge `code execute`.

The scripts in scripts/*.csx are C# snippets that run INSIDE the jws2 editor and write JSON into the
export folder. The data is therefore already resolved by Unity (prefab instances, mesh references,
animator controllers). Nothing under F:\\jws2\\client\\Assets is touched: snippets are staged into
jws2's own .aibridge\\code\\ cache folder with a `sanguo_` prefix, and every script opens scenes
without saving.

Usage (from the SanGuoXEngine repository root):

    py -3 Tools/unity_export/run_export.py status
    py -3 Tools/unity_export/run_export.py run probe_env probe_material
    py -3 Tools/unity_export/run_export.py run --all [--launch] [--timeout-ms 120000]
    py -3 Tools/unity_export/run_export.py clean        # remove staged snippets from jws2

Python is an offline converter only: its output feeds the C# generators under
Assets/Editor/Sanguo and is never read by the game at runtime.
"""

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parent.parent
PRELUDE = HERE / "_prelude.csx"
SCRIPTS = HERE / "scripts"
STAGE_PREFIX = "sanguo_"

ENV_OVERRIDES = {
    "jws2_client": "SANGUO_JWS2_CLIENT",
    "unity_editor": "SANGUO_UNITY_EDITOR",
    "out_dir": "SANGUO_EXPORT_DIR",
}

# Scripts run in this order when `run --all` is used; each one is independent.
DEFAULT_ORDER = ["probe_env", "probe_material", "probe_hero", "probe_clip", "probe_scene"]

# Longest we wait for a freshly launched editor to become reachable and idle.
LAUNCH_TIMEOUT_S = 20 * 60


def load_config():
    cfg = json.loads((HERE / "config.json").read_text(encoding="utf-8"))
    for key, env in ENV_OVERRIDES.items():
        value = os.environ.get(env)
        if value:
            cfg[key] = value
    return cfg


def resolve_out_dir(cfg, value=None):
    """The export folder is always inside this repository and always git-ignored."""
    rel = value or cfg.get("out_dir", "Export")
    out = Path(rel)
    if not out.is_absolute():
        out = REPO_ROOT / rel
    return out.resolve()


class Bridge:
    """Thin wrapper over the project-local AIBridgeCLI.exe of the jws2 Unity project."""

    def __init__(self, client):
        self.client = Path(client)
        self.cli = self.client / ".aibridge" / "cli" / "AIBridgeCLI.exe"
        self.code_dir = self.client / ".aibridge" / "code"

    def run(self, *args, timeout_s=30):
        if not self.cli.exists():
            sys.exit(f"AIBridge CLI not found: {self.cli}")
        cmd = [str(self.cli)] + [str(a) for a in args]
        try:
            proc = subprocess.run(
                cmd, capture_output=True, text=True, encoding="utf-8", errors="replace",
                timeout=timeout_s, cwd=str(self.client))
        except subprocess.TimeoutExpired:
            return {"success": False, "error": f"CLI timed out after {timeout_s}s"}
        out = (proc.stdout or "").strip()
        if not out:
            return {"success": False, "error": (proc.stderr or "empty output").strip()[-2000:]}
        try:
            return json.loads(out)
        except json.JSONDecodeError:
            return {"success": False, "error": "non-JSON CLI output", "raw": out[-2000:]}

    def state(self):
        return self.run("editor", "get_state")

    def is_ready(self):
        """True when the editor answers and is neither compiling nor in play mode."""
        st = self.state()
        if not st.get("success"):
            return False, st.get("error", "unknown")
        data = st.get("data") or {}
        if data.get("isCompiling"):
            return False, "compiling"
        if data.get("isPlaying"):
            return False, "play mode"
        return True, data

    def stage(self, name, out_dir, name_filter=""):
        body = (SCRIPTS / f"{name}.csx").read_text(encoding="utf-8")
        text = PRELUDE.read_text(encoding="utf-8") + "\n" + body
        # Forward slashes keep the path safe inside a C# string literal.
        text = text.replace("__OUT_DIR__", out_dir.as_posix())
        text = text.replace("__NAME_FILTER__", name_filter)
        self.code_dir.mkdir(parents=True, exist_ok=True)
        staged = self.code_dir / f"{STAGE_PREFIX}{name}.csx"
        staged.write_text(text, encoding="utf-8")
        return staged

    def execute(self, name, out_dir, timeout_ms, name_filter=""):
        staged = self.stage(name, out_dir, name_filter)
        res = self.run("code", "execute", "--file", staged, "--timeout", int(timeout_ms),
                       timeout_s=timeout_ms / 1000.0 + 60.0)
        data = res.get("data") or {}
        # A compile failure reports success=false with `compileErrors` (strings) and
        # `diagnostics` (structured); check it before the generic failure path.
        if data.get("status") == "compile_failed" or not res.get("success"):
            diagnostics = data.get("diagnostics") or []
            compile_errors = [d for d in diagnostics if d.get("severity") == "error"]
            if data.get("status") == "compile_failed" or compile_errors:
                return {"ok": False, "stage": "compile",
                        "error": "; ".join(data.get("compileErrors") or []) or "code compilation failed",
                        "errors": compile_errors,
                        "warnings": [d.get("raw") for d in diagnostics if d.get("severity") == "warning"],
                        "logs": [], "raw": None}
            return {"ok": False, "stage": "execute", "error": res.get("error", "unknown"),
                    "raw": res.get("raw")}
        value = data.get("returnValue")
        logs = data.get("logs") or []
        errors = data.get("errors") or []
        parsed = value
        if isinstance(value, str):
            try:
                parsed = json.loads(value)
            except json.JSONDecodeError:
                parsed = None
        ok = parsed is not None
        return {"ok": ok, "stage": "execute" if ok else "returnValue", "value": parsed,
                "raw": value if not ok else None, "logs": logs, "errors": errors}

    def clean(self):
        removed = 0
        if self.code_dir.is_dir():
            for f in self.code_dir.glob(f"{STAGE_PREFIX}*.csx"):
                f.unlink()
                removed += 1
        return removed


def launch_editor(cfg):
    editor = Path(cfg["unity_editor"])
    if not editor.exists():
        sys.exit(f"Unity editor not found: {editor}")
    flags = 0
    if os.name == "nt":
        flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
    log = Path(cfg["jws2_client"]) / "Logs" / "sanguo_unity_editor.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    subprocess.Popen([str(editor), "-projectPath", str(cfg["jws2_client"]), "-logFile", str(log)],
                     creationflags=flags, close_fds=True)
    print(f"[launch] started {editor} on {cfg['jws2_client']} (log: {log})")


def wait_ready(bridge, timeout_s, quiet=False):
    deadline = time.time() + timeout_s
    last = ""
    while time.time() < deadline:
        ready, info = bridge.is_ready()
        if ready:
            return info
        state = info if isinstance(info, str) else json.dumps(info, ensure_ascii=False)
        if state != last and not quiet:
            print(f"[wait] editor not ready: {state}")
            last = state
        time.sleep(5)
    sys.exit(f"[error] the jws2 editor did not become ready within {timeout_s}s (last: {last})")


def cmd_status(bridge):
    ready, info = bridge.is_ready()
    if ready:
        print("jws2 editor: READY")
        print(json.dumps(info, ensure_ascii=False, indent=2))
        return 0
    print(f"jws2 editor: NOT READY ({info})")
    return 1


def validate_json(path):
    """Returns an error string when the file is missing or is not valid JSON, else None."""
    if not path.exists():
        return "file was not written"
    try:
        json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        text = path.read_text(encoding="utf-8")
        context = text[max(0, e.pos - 60):e.pos + 60].replace("\n", "\\n")
        return f"{e.msg} at char {e.pos}: ...{context}..."
    return None


def cmd_run(bridge, cfg, names, out_dir, timeout_ms, name_filter=""):
    out_dir.mkdir(parents=True, exist_ok=True)
    summary = []
    failed = 0
    for name in names:
        print(f"[run] {name} ...")
        started = time.time()
        result = bridge.execute(name, out_dir, timeout_ms, name_filter)
        elapsed = time.time() - started
        entry = {"script": name, "ok": result["ok"], "stage": result["stage"],
                 "elapsedS": round(elapsed, 2)}
        if result["ok"]:
            doc = result["value"]
            target = out_dir / f"{name}.json"
            target.write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding="utf-8")
            entry["file"] = target.name
            # Scripts that dump a large document to disk only return a summary naming that file;
            # the document itself must parse, or downstream generators get silently broken input.
            document = doc.get("file") if isinstance(doc, dict) else None
            if document:
                entry["document"] = document
                problem = validate_json(out_dir / document)
                if problem:
                    failed += 1
                    entry["ok"] = False
                    entry["stage"] = "document"
                    entry["error"] = problem
                    print(f"[FAIL] {name}: {document} is not valid JSON ({problem})")
                    summary.append(entry)
                    continue
            print(f"[ok]   {name} -> {target.name} ({elapsed:.1f}s)")
        else:
            failed += 1
            entry["error"] = result.get("error")
            entry["raw"] = result.get("raw")
            entry["errors"] = result.get("errors")
            entry["logs"] = result.get("logs")
            print(f"[FAIL] {name} at stage '{result['stage']}' ({elapsed:.1f}s)")
            if result.get("error"):
                print(f"       error: {result['error']}")
            for err in (result.get("errors") or [])[:20]:
                if isinstance(err, dict):
                    print(f"       {err.get('line')}:{err.get('column')} {err.get('code')} {err.get('message')}")
                else:
                    print(f"       {err}")
            for line in (result.get("logs") or [])[:12]:
                print(f"       log: {json.dumps(line, ensure_ascii=False)}")
            if result.get("raw"):
                print(f"       raw: {str(result['raw'])[:800]}")
        summary.append(entry)
    (out_dir / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[done] {len(names) - failed}/{len(names)} succeeded; summary: {out_dir / 'summary.json'}")
    return 1 if failed else 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("status", help="check whether the jws2 editor is reachable and idle")
    sub.add_parser("clean", help="remove staged sanguo_*.csx snippets from the jws2 cache")

    run = sub.add_parser("run", help="stage and execute exporter scripts")
    run.add_argument("scripts", nargs="*", help="script names without .csx")
    run.add_argument("--all", action="store_true", help=f"run the default order: {DEFAULT_ORDER}")
    run.add_argument("--launch", action="store_true",
                     help="start the jws2 editor when it is not reachable, then wait for it")
    run.add_argument("--out", help="export folder (default: config out_dir)")
    run.add_argument("--timeout-ms", type=int, default=None, help="per-script timeout")
    run.add_argument("--filter", default="", help="method-name substring for probe_api")

    args = parser.parse_args(argv)
    cfg = load_config()
    bridge = Bridge(cfg["jws2_client"])

    if args.command == "status":
        return cmd_status(bridge)
    if args.command == "clean":
        print(f"removed {bridge.clean()} staged snippet(s) from {bridge.code_dir}")
        return 0

    names = list(args.scripts)
    if args.all:
        for n in DEFAULT_ORDER:
            if n not in names:
                names.append(n)
    if not names:
        run.error("no scripts given (use --all or list script names)")
    for n in names:
        if not (SCRIPTS / f"{n}.csx").exists():
            sys.exit(f"unknown script: {n} (no {SCRIPTS / (n + '.csx')})")

    ready, why = bridge.is_ready()
    if not ready and args.launch:
        launch_editor(cfg)
        wait_ready(bridge, LAUNCH_TIMEOUT_S)
    elif not ready:
        sys.exit(f"[error] the jws2 editor is not reachable ({why}); "
                 "open it on F:\\jws2\\client or pass --launch")

    # Default destination is <out_dir>/probe (the M0 gate); `--out` selects another export folder.
    out_dir = resolve_out_dir(cfg, args.out)
    if not args.out:
        out_dir = out_dir / "probe"
    timeout_ms = args.timeout_ms or int(cfg.get("default_timeout_ms", 120000))
    return cmd_run(bridge, cfg, names, out_dir, timeout_ms, args.filter)


if __name__ == "__main__":
    sys.exit(main())
