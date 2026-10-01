"""Optional stdio MCP bridge to the application's existing evaluator; no main session launch."""
from __future__ import annotations

import argparse
import contextlib
import json
import sys
import tempfile
from pathlib import Path
from typing import Any

import router

PROTOCOL = "2025-11-25"
MAX_MESSAGE = 1024 * 1024
TOOL = {
    "name": "evaluate_task",
    "description": "Evaluate the task and explicitly supplied chat context with the app Task Router. "
                   "Uses a short Codex classification call and live model catalog; returns a provisional "
                   "plan, evidence, weighted scores, safety floor and suggested read-only subagents. "
                   "Does not start a main chat, spawn agents or change configuration.",
    "inputSchema": {
        "type": "object", "required": ["task"], "additionalProperties": False,
        "properties": {
            "task": {"type": "string", "minLength": 1, "maxLength": router.MAX_INPUT},
            "chat_context": {"type": "string", "maxLength": router.MAX_INPUT},
            "max_subagents": {"type": "integer", "minimum": 0, "maximum": 2},
        },
    },
    "annotations": {"readOnlyHint": True, "destructiveHint": False,
                    "idempotentHint": False, "openWorldHint": True},
}


def evaluate(arguments: dict[str, Any], codex: str | None = None) -> dict[str, Any]:
    router.validate(arguments, TOOL["inputSchema"])
    task = arguments["task"]
    context = arguments.get("chat_context", "")
    if context:
        task += "\n\nAusdrücklich beigefügter Chat-Kontext:\n" + context
    if not task.strip() or len(task) > router.MAX_INPUT:
        raise router.RouterError("Auftrag und Kontext müssen zusammen 1 bis 60000 Zeichen umfassen.")
    before = router.credential_fingerprint()
    prefix = router.resolve_codex(codex)
    policy = router.load_policy(router.ROOT / "routing_policy.json")
    catalog = router.normalize_catalog(router.discover_models(prefix))
    with tempfile.TemporaryDirectory(prefix="task-router-mcp-") as directory:
        run_dir = Path(directory)
        assessment = router.assess(prefix, catalog, policy, task, [], run_dir, 150)
        plan = router.decide(assessment, catalog, policy, cap=arguments.get("max_subagents", 2))
        plan["intake"] = router.load_json(run_dir / "intake.json")
    if before != router.credential_fingerprint():
        raise router.RouterError("Konto während der Bewertung geändert. Ergebnis verworfen; erneut bewerten.")
    plan["chat_execution"] = {
        "parent": "current_chat", "main_model": "recommendation_only_no_switch_performed",
        "agents": "proposed_not_started", "tools": "client_capabilities_not_yet_checked",
        "skills_and_plugins": "existing_chat_configuration_unchanged",
        "context": "only_explicitly_supplied_text_no_automatic_history_access",
        "verification": "not_yet_performed",
    }
    return plan


class Server:
    def __init__(self, codex: str | None = None):
        self.codex = codex
        self.initialized = False
        self.ready = False

    def handle(self, request: Any) -> dict[str, Any] | None:
        ident = request.get("id") if isinstance(request, dict) else None
        def error(code: int, message: str) -> dict[str, Any]:
            return {"jsonrpc": "2.0", "id": ident, "error": {"code": code, "message": message}}
        if (not isinstance(request, dict) or request.get("jsonrpc") != "2.0"
                or not isinstance(request.get("method"), str)
                or ("id" in request and (type(ident) not in (str, int)))):
            return error(-32600, "Invalid Request")
        method = request["method"]
        params = request.get("params", {})
        if not isinstance(params, dict):
            return None if "id" not in request else error(-32602, "Invalid params")
        if "id" not in request:
            if method == "notifications/initialized" and self.initialized:
                self.ready = True
            return None
        if method == "initialize":
            if self.initialized or not isinstance(params.get("protocolVersion"), str):
                return error(-32602, "Invalid initialization")
            self.initialized = True
            result = {"protocolVersion": PROTOCOL, "capabilities": {"tools": {}},
                      "serverInfo": {"name": "simple-accounts-task-router", "version": "1.6.0"}}
        elif method == "ping":
            result = {}
        elif method not in ("tools/list", "tools/call"):
            # Also immediately answers newer discovery probes, allowing legacy negotiation.
            return error(-32601, "Method not found")
        elif not self.ready:
            return error(-32600, "Initialize the server first")
        elif method == "tools/list":
            result = {"tools": [TOOL]}
        else:
            if params.get("name") != TOOL["name"]:
                return error(-32602, "Unknown tool")
            arguments = params.get("arguments", {})
            try:
                # Keep stdout exclusively for protocol messages, including future router diagnostics.
                with contextlib.redirect_stdout(sys.stderr):
                    plan = evaluate(arguments, self.codex)
                result = {"content": [{"type": "text", "text": json.dumps(plan, ensure_ascii=False)}],
                          "structuredContent": plan, "isError": False}
            except router.RouterError:
                # Child errors can contain account diagnostics; never leak those into the chat.
                result = {"content": [{"type": "text", "text":
                          "App-Bewertung fehlgeschlagen. Eingaben, Codex-Anmeldung und Modellverfügbarkeit "
                          "prüfen; kein Ersatzplan erstellt."}], "isError": True}
            except Exception:
                result = {"content": [{"type": "text", "text":
                          "App-Bewertung intern fehlgeschlagen; kein Ersatzplan erstellt."}], "isError": True}
        return {"jsonrpc": "2.0", "id": ident, "result": result}


def serve(source, destination, codex: str | None = None) -> None:
    server = Server(codex)
    while True:
        line = source.readline(MAX_MESSAGE + 1)
        if not line:
            return
        if len(line) > MAX_MESSAGE:
            # Close rather than consume an unbounded input stream.
            response = {"jsonrpc": "2.0", "id": None,
                        "error": {"code": -32600, "message": "Message too large"}}
            destination.write(json.dumps(response) + "\n")
            destination.flush()
            return
        try:
            response = server.handle(json.loads(line))
        except (ValueError, RecursionError):
            response = {"jsonrpc": "2.0", "id": None,
                        "error": {"code": -32700, "message": "Parse error"}}
        if response is not None:
            destination.write(json.dumps(response, ensure_ascii=False) + "\n")
            destination.flush()


def install(project: Path) -> None:
    """Explicit, project-only setup; preserve unrelated skills and settings."""
    import os
    import shutil
    import tomllib
    project = project.expanduser().resolve()
    if not project.is_dir():
        raise router.RouterError("Projektordner existiert nicht.")
    target = project / ".agents/skills/gg"
    config = project / ".codex/config.toml"
    original = config.read_text(encoding="utf-8") if config.exists() else ""
    parsed = tomllib.loads(original)
    if target.exists() or "simple_accounts_router" in parsed.get("mcp_servers", {}):
        raise router.RouterError("Integration bereits vorhanden; nichts überschrieben.")
    snippet = ("\n[mcp_servers.simple_accounts_router]\ncommand = " + json.dumps(sys.executable)
               + "\nargs = " + json.dumps([str(Path(__file__).resolve())])
               + '\ntool_timeout_sec = 210\nenabled_tools = ["evaluate_task"]\n')
    tomllib.loads(original + snippet)
    config.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(router.ROOT / "gg", target)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=config.parent,
                                         delete=False) as output:
            temporary = Path(output.name)
            output.write(original + snippet)
        current = config.read_text(encoding="utf-8") if config.exists() else ""
        if current != original:
            raise router.RouterError("Konfiguration gleichzeitig geändert; erneut einrichten.")
        os.replace(temporary, config)
    except Exception:
        shutil.rmtree(target)
        raise
    finally:
        if temporary and temporary.exists():
            temporary.unlink()
    print("Integration im Projekt eingerichtet. Projekt in Codex vertrauen und MCP-Verfügbarkeit prüfen. "
          "gg im Skill-Menü wählen oder $gg verwenden. App-Dateien müssen an diesem Ort bleiben.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--codex", help="Trusted startup configuration only; unavailable to tool callers")
    parser.add_argument("--install-project", type=Path, help="Explizite Einrichtung nur in diesem Projekt")
    args = parser.parse_args()
    if args.install_project:
        if args.codex:
            parser.error("--codex wird bei der Einrichtung nicht gespeichert; Codex muss im PATH liegen")
        install(args.install_project)
        sys.exit(0)
    if hasattr(sys.stdin, "reconfigure"):
        sys.stdin.reconfigure(encoding="utf-8")
        sys.stdout.reconfigure(encoding="utf-8")
    serve(sys.stdin, sys.stdout, args.codex)
