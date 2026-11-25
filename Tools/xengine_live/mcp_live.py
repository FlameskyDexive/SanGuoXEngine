# -*- coding: utf-8 -*-
"""mcp_live.py — drive a LIVE XEngine editor (`XEngine.Editor --serve`) over MCP stdio.

Usage (from the SanGuoXEngine repository root):

    py -3 Tools/xengine_live/mcp_live.py --inline '[{"tool":"runtime_screenshot","args":{}}]'
    py -3 Tools/xengine_live/mcp_live.py --cmds cmds.json [--out result.json] [--keep]
    py -3 Tools/xengine_live/mcp_live.py --screenshot out.png        # shortcut
    py -3 Tools/xengine_live/mcp_live.py --eval 'return IsPlaying;'   # shortcut

A command file is a JSON array. Each entry is one of
    {"tool": "<mcp tool>", "args": {...}, "timeout": 120, "tag": "label"}
    {"delay": 2.5}                       wait seconds
    {"waitCompile": 300}                 poll until the editor finished compiling scripts
    {"until": {"tool": ..., "args": {...}, "contains": "text"}, "timeout": 120}
                                         repeat a tool call until its text contains `contains`
Results are printed as `RESULT <tag> <text>` / `ERROR <tag> <error>` lines and, with `--out`,
written to a JSON file. The editor is terminated when the commands are done unless `--keep` is given.

This is an offline development tool: it never ships and is not referenced by the game.
"""

import argparse
import json
import subprocess
import sys
import threading
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
DEFAULT_EDITOR = r"D:\Engine\XEngine\Build\Editor\Debug\net10.0\XEngine.Editor.exe"
PROJECT = REPO_ROOT

# The editor compiles project scripts asynchronously after it opens; play mode or eval against a
# half-compiled project runs stale assemblies, so callers gate on this expression.
COMPILE_PROBE = "return XEngine.Editor.Projects.Scripting.ScriptAssemblyManager.IsCompiling;"


class LiveEditor:
    def __init__(self, editor, project):
        args = [editor, "--project", str(project), "--serve"]
        self.proc = subprocess.Popen(
            args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="replace", bufsize=1, cwd=str(project))
        self._next_id = 1
        self._lock = threading.Lock()
        self.messages = []
        self.other = []
        threading.Thread(target=self._reader, daemon=True).start()

    def _reader(self):
        for line in self.proc.stdout:
            line = line.strip()
            if not line:
                continue
            try:
                message = json.loads(line)
            except json.JSONDecodeError:
                self.other.append(line)
                continue
            if isinstance(message, dict) and ("id" in message or "method" in message):
                with self._lock:
                    self.messages.append(message)
            else:
                self.other.append(line)

    def _send(self, obj):
        self.proc.stdin.write(json.dumps(obj) + "\n")
        self.proc.stdin.flush()

    def call(self, method, params, timeout=120):
        with self._lock:
            request_id = self._next_id
            self._next_id += 1
        self._send({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params})
        deadline = time.time() + timeout
        while time.time() < deadline:
            with self._lock:
                for index, message in enumerate(self.messages):
                    if message.get("id") == request_id and ("result" in message or "error" in message):
                        return self.messages.pop(index)
            if self.proc.poll() is not None:
                raise ConnectionError(f"editor exited with code {self.proc.returncode}")
            time.sleep(0.1)
        raise TimeoutError(f"{method} timed out after {timeout}s")

    def initialize(self, attempts=90):
        last = None
        for attempt in range(attempts):
            if self.proc.poll() is not None:
                raise ConnectionError(f"editor exited with code {self.proc.returncode}")
            try:
                return self.call("initialize", {
                    "protocolVersion": "2024-11-05", "capabilities": {},
                    "clientInfo": {"name": "sanguo-live", "version": "1.0"}}, timeout=5)
            except TimeoutError as error:
                last = error
            time.sleep(2)
        raise TimeoutError(f"editor MCP server never initialized ({last})")

    def tool(self, name, arguments=None, timeout=120):
        """Returns the concatenated text content; raises RuntimeError on a protocol error."""
        response = self.call("tools/call", {"name": name, "arguments": arguments or {}}, timeout)
        if "error" in response:
            raise RuntimeError(json.dumps(response["error"], ensure_ascii=False))
        content = response.get("result", {}).get("content", [])
        return "".join(c.get("text", "") for c in content if c.get("type") == "text")

    def close(self):
        if self.proc.poll() is None:
            try:
                self.proc.terminate()
                self.proc.wait(timeout=15)
            except Exception:
                if hasattr(subprocess, "CREATE_NEW_PROCESS_GROUP"):
                    self.proc.kill()
                try:
                    self.proc.wait(timeout=5)
                except Exception:
                    pass


def wait_compile(editor, timeout):
    """True once the compile flag reads False twice in a row (it flips at the start of a rebuild)."""
    deadline = time.time() + timeout
    calm = 0
    last = None
    errors = 0
    while time.time() < deadline:
        try:
            # runtime_eval answers with a structured object: {"succeeded":true,"result":"False",...}
            text = editor.tool("runtime_eval", {"code": COMPILE_PROBE}, timeout=30).strip()
            value = str(json.loads(text).get("result"))
        except (RuntimeError, TimeoutError, json.JSONDecodeError, AttributeError) as error:
            value = f"error: {error}"
        if value != last:
            print(f"[waitCompile] {value}", flush=True)
            last = value
        if value.startswith("error:"):
            # A probe that keeps failing (e.g. a type that does not exist in this editor build)
            # will never report False; fail fast instead of burning the whole timeout.
            errors += 1
            if errors >= 5:
                raise RuntimeError(f"compile probe failed 5 times: {value[:300]}")
        else:
            errors = 0
        calm = calm + 1 if value.lower() == "false" else 0
        if calm >= 2:
            return True
        time.sleep(2)
    return False


def run_commands(editor, commands):
    results = []
    for command in commands:
        tag = command.get("tag") or command.get("tool") or ("delay" if "delay" in command else "?")
        try:
            if "delay" in command:
                time.sleep(float(command["delay"]))
                continue
            if "waitCompile" in command:
                try:
                    ok = wait_compile(editor, float(command["waitCompile"]))
                except RuntimeError as error:
                    ok = False
                    print(f"ERROR waitCompile {error}", flush=True)
                results.append({"tag": "waitCompile", "ok": ok})
                print(f"{'RESULT' if ok else 'ERROR'} waitCompile compiled={ok}", flush=True)
                continue
            if "until" in command:
                spec = command["until"]
                deadline = time.time() + float(command.get("timeout", 120))
                text = ""
                while time.time() < deadline:
                    text = editor.tool(spec["tool"], spec.get("args"), timeout=60)
                    if spec.get("contains", "") in text:
                        break
                    time.sleep(float(spec.get("every", 2)))
                results.append({"tag": tag, "ok": spec.get("contains", "") in text, "text": text})
                print(f"{'RESULT' if spec.get('contains', '') in text else 'ERROR'} {tag} {text[:1500]}", flush=True)
                continue
            text = editor.tool(command["tool"], command.get("args"), float(command.get("timeout", 120)))
            results.append({"tag": tag, "ok": True, "text": text})
            print(f"RESULT {tag} {text[:1500]}", flush=True)
        except (RuntimeError, TimeoutError, ConnectionError) as error:
            results.append({"tag": tag, "ok": False, "error": str(error)})
            print(f"ERROR {tag} {str(error)[:800]}", flush=True)
    return results


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--cmds", help="JSON file with a command array")
    parser.add_argument("--inline", help="JSON array of commands on the command line")
    parser.add_argument("--out", help="write results to this JSON file")
    parser.add_argument("--editor", default=DEFAULT_EDITOR)
    parser.add_argument("--project", default=str(PROJECT))
    parser.add_argument("--keep", action="store_true", help="leave the editor running")
    parser.add_argument("--wait-compile", type=int, default=None,
                        help="seconds to wait for the first script compile before the commands")
    parser.add_argument("--screenshot", help="shortcut: one runtime_screenshot")
    parser.add_argument("--eval", help="shortcut: one runtime_eval")
    parser.add_argument("--tail", type=int, default=25,
                        help="on failure, print this many non-MCP editor output lines")
    args = parser.parse_args(argv)

    commands = []
    if args.cmds:
        commands = json.loads(Path(args.cmds).read_text(encoding="utf-8"))
    if args.inline:
        commands += json.loads(args.inline)
    if args.screenshot:
        commands += [{"tool": "runtime_screenshot", "args": {"path": args.screenshot}, "tag": "screenshot"}]
    if args.eval:
        commands += [{"tool": "runtime_eval", "args": {"code": args.eval}, "tag": "eval"}]

    editor = LiveEditor(args.editor, args.project)
    try:
        init = editor.initialize()
        editor._send({"jsonrpc": "2.0", "method": "notifications/initialized"})
        print("INIT", json.dumps(init.get("result", {}).get("serverInfo", {})), flush=True)
        if args.wait_compile:
            if not wait_compile(editor, args.wait_compile):
                print("ERROR scripts did not finish compiling", flush=True)
        results = run_commands(editor, commands)
        if args.out:
            Path(args.out).parent.mkdir(parents=True, exist_ok=True)
            Path(args.out).write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
        failed = sum(1 for r in results if not r.get("ok"))
        if failed:
            print(f"[done] {failed}/{len(results)} command(s) failed", flush=True)
            for line in editor.other[-args.tail:]:
                print("  | " + line[:300], flush=True)
            return 1
        return 0
    finally:
        if not args.keep:
            editor.close()


if __name__ == "__main__":
    sys.exit(main())
