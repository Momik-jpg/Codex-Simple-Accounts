"""Optional stdio MCP bridge to the application's existing evaluator; no main session launch."""
from __future__ import annotations

import argparse
import contextlib
import json
import os
import stat
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


class BridgeError(router.RouterError):
    """Only fixed, non-sensitive messages may be exposed to the chat."""
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


def fingerprint() -> str:
    try:
        return router.credential_fingerprint()
    except router.RouterError as exc:
        raise BridgeError("AUTH_UNAVAILABLE", "Keine lesbare Codex-Anmeldung. Gewünschtes Konto "
                          "anmelden/aktivieren und erneut bewerten; kein Ersatzplan erstellt.") from exc


def evaluate(arguments: dict[str, Any], codex: str | None = None) -> dict[str, Any]:
    try:
        router.validate(arguments, TOOL["inputSchema"])
    except router.RouterError as exc:
        raise BridgeError("INVALID_INPUT", "Auftrag als nicht-leeren Text, Kontext als Text und "
                          "max_subagents als ganze Zahl von 0 bis 2 übergeben; keine Zusatzfelder.") from exc
    task = arguments["task"]
    context = arguments.get("chat_context", "")
    if context:
        task += "\n\nAusdrücklich beigefügter Chat-Kontext:\n" + context
    if not task.strip() or len(task) > router.MAX_INPUT:
        raise BridgeError("INVALID_INPUT", "Auftrag und Kontext müssen zusammen 1 bis 60000 Zeichen umfassen.")
    before = fingerprint()
    prefix = router.resolve_codex(codex)
    policy = router.load_policy(router.ROOT / "routing_policy.json")
    catalog = router.normalize_catalog(router.discover_models(prefix))
    with tempfile.TemporaryDirectory(prefix="task-router-mcp-") as directory:
        run_dir = Path(directory)
        assessment = router.assess(prefix, catalog, policy, task, [], run_dir, 150)
        plan = router.decide(assessment, catalog, policy, cap=arguments.get("max_subagents", 2))
        plan["intake"] = router.load_json(run_dir / "intake.json")
    if before != fingerprint():
        raise BridgeError("ACCOUNT_CHANGED", "Konto während der Bewertung geändert. Ergebnis verworfen; erneut bewerten.")
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
        if type(ident) not in (str, int):
            ident = None
        def error(code: int, message: str) -> dict[str, Any]:
            return {"jsonrpc": "2.0", "id": ident, "error": {"code": code, "message": message}}
        if (not isinstance(request, dict) or request.get("jsonrpc") != "2.0"
                or not isinstance(request.get("method"), str)
                or ("id" in request and (type(request["id"]) not in (str, int)))):
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
            client = params.get("clientInfo")
            if (self.initialized or not isinstance(params.get("protocolVersion"), str)
                    or not params["protocolVersion"]
                    or not isinstance(params.get("capabilities"), dict)
                    or not isinstance(client, dict)
                    or not all(isinstance(client.get(key), str) and client[key] for key in ("name", "version"))):
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
            if not isinstance(arguments, dict):
                return error(-32602, "Tool arguments must be an object")
            try:
                # Keep stdout exclusively for protocol messages, including future router diagnostics.
                with open(os.devnull, "w", encoding="utf-8") as diagnostics, \
                        contextlib.redirect_stdout(diagnostics), contextlib.redirect_stderr(diagnostics):
                    plan = evaluate(arguments, self.codex)
                result = {"content": [{"type": "text", "text": json.dumps(plan, ensure_ascii=False)}],
                          "structuredContent": plan, "isError": False}
            except BridgeError as exc:
                result = {"content": [{"type": "text", "text": str(exc)}],
                          "isError": True, "_meta": {"error_code": exc.code}}
            except router.RouterError:
                # Child errors can contain account diagnostics; never leak those into the chat.
                result = {"content": [{"type": "text", "text":
                          "App-Bewertung fehlgeschlagen. Eingaben, Codex-Anmeldung und Modellverfügbarkeit "
                          "prüfen; kein Ersatzplan erstellt."}], "isError": True,
                          "_meta": {"error_code": "EVALUATION_FAILED"}}
            except Exception:
                result = {"content": [{"type": "text", "text":
                          "App-Bewertung intern fehlgeschlagen; kein Ersatzplan erstellt."}], "isError": True,
                          "_meta": {"error_code": "INTERNAL_ERROR"}}
        return {"jsonrpc": "2.0", "id": ident, "result": result}


def strict_json(line: str) -> Any:
    def invalid_constant(value: str) -> None:
        raise ValueError("Non-finite JSON number")
    def unique_object(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate JSON key")
            result[key] = value
        return result
    return json.loads(line, parse_constant=invalid_constant, object_pairs_hook=unique_object)


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
            response = server.handle(strict_json(line))
        except (ValueError, RecursionError):
            response = {"jsonrpc": "2.0", "id": None,
                        "error": {"code": -32700, "message": "Parse error"}}
        if response is not None:
            destination.write(json.dumps(response) + "\n")
            destination.flush()


def reject_redirected_paths(paths: tuple[Path, ...]) -> None:
    for path in paths:
        # Windows junctions are reparse points but Path.is_symlink() does not detect them.
        try:
            attributes = getattr(path.lstat(), "st_file_attributes", 0)
        except FileNotFoundError:
            attributes = 0
        if path.is_symlink() or attributes & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400):
            raise router.RouterError("Einrichtung über symbolische Links/Junctions nicht erlaubt; nichts geändert.")


def install(project: Path) -> None:
    """Explicit, project-only setup; stage the skill and serialize cooperating installers."""
    import shutil
    import tomllib
    project = project.expanduser().resolve()
    if not project.is_dir():
        raise router.RouterError("Projektordner existiert nicht.")
    target = project / ".agents/skills/gg"
    config = project / ".codex/config.toml"
    paths = (project / ".agents", target.parent, target, config.parent, config)
    reject_redirected_paths(paths)
    config.parent.mkdir(parents=True, exist_ok=True)
    lock = config.parent / ".simple-accounts-router-setup.lock"
    try:
        descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    except FileExistsError as exc:
        raise router.RouterError("Einrichtung bereits aktiv oder unterbrochen. Nach Ende des anderen "
                                 "Prozesses die Setup-Lockdatei prüfen; nichts überschrieben.") from exc
    os.close(descriptor)
    temporary = None
    installed = False
    try:
        original = config.read_text(encoding="utf-8-sig") if config.exists() else ""
        parsed = tomllib.loads(original)
        servers = parsed.get("mcp_servers", {})
        if not isinstance(servers, dict):
            raise router.RouterError("mcp_servers muss eine TOML-Tabelle sein; nichts geändert.")
        if target.exists() or "simple_accounts_router" in servers:
            raise router.RouterError("Integration bereits vorhanden; nichts überschrieben.")
        snippet = ("\n[mcp_servers.simple_accounts_router]\ncommand = " + json.dumps(sys.executable, ensure_ascii=False)
                   + "\nargs = " + json.dumps([str(Path(__file__).resolve())], ensure_ascii=False)
                   + '\ntool_timeout_sec = 210\nenabled_tools = ["evaluate_task"]\n')
        tomllib.loads(original + snippet)
        target.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix=".gg-setup-", dir=target.parent) as directory:
            staged = Path(directory) / "gg"
            shutil.copytree(router.ROOT / "gg", staged)
            with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=config.parent,
                                             delete=False) as output:
                temporary = Path(output.name)
                output.write(original + snippet)
            reject_redirected_paths(paths)
            current = config.read_text(encoding="utf-8-sig") if config.exists() else ""
            if current != original or target.exists():
                raise router.RouterError("Projekt-Einrichtung gleichzeitig geändert; erneut einrichten.")
            staged.rename(target)
            installed = True
            os.replace(temporary, config)
    except Exception:
        if installed:
            shutil.rmtree(target)
        raise
    finally:
        if temporary and temporary.exists():
            temporary.unlink()
        lock.unlink()
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
        try:
            install(args.install_project)
        except (router.RouterError, OSError, ValueError) as exc:
            print(f"Einrichtung fehlgeschlagen: {exc}", file=sys.stderr)
            sys.exit(2)
        sys.exit(0)
    if hasattr(sys.stdin, "reconfigure"):
        sys.stdin.reconfigure(encoding="utf-8")
        sys.stdout.reconfigure(encoding="utf-8")
    serve(sys.stdin, sys.stdout, args.codex)
