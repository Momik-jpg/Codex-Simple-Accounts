#!/usr/bin/env python3
"""Task Router: structured intake -> deterministic policy -> a new Codex CLI session.

Python 3.10+, standard library only. No API key handling, package installation,
recursive launch, automatic merges, or global Codex configuration writes.
See docs/AUTO_TASK_ROUTER.md for integration limits and security boundaries.
"""
from __future__ import annotations

import argparse
import copy
import json
import os
from pathlib import Path
import queue
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import threading
import time
from typing import Any
import uuid

ROOT = Path(__file__).resolve().parent
TIERS = ("fast", "balanced", "deep")
EFFORTS = ("minimal", "low", "medium", "high", "xhigh", "max", "ultra")
MAX_INPUT = 60000


class RouterError(Exception):
    """Actionable configuration, protocol, or validation error."""


def load_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        raise RouterError(f"JSON nicht lesbar: {path}: {exc}") from exc


def write_json(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def validate(value: Any, schema: dict[str, Any], at: str = "$ ") -> None:
    """Validate the limited JSON Schema vocabulary used by this package."""
    kind = schema.get("type")
    valid_type = {
        "object": isinstance(value, dict), "array": isinstance(value, list),
        "string": isinstance(value, str), "integer": type(value) is int,
        "boolean": type(value) is bool,
    }.get(kind, False)
    if not valid_type:
        raise RouterError(f"{at}: erwartet {kind}")
    if "enum" in schema and value not in schema["enum"]:
        raise RouterError(f"{at}: ungültiger Wert {value!r}")
    if kind == "object":
        props = schema.get("properties", {})
        if set(schema.get("required", [])) - value.keys():
            raise RouterError(f"{at}: Pflichtfelder fehlen")
        if not schema.get("additionalProperties", True) and value.keys() - props.keys():
            raise RouterError(f"{at}: unerlaubte Zusatzfelder")
        for key, item in value.items():
            if key in props:
                validate(item, props[key], f"{at}.{key}")
    elif kind == "array":
        if not schema.get("minItems", 0) <= len(value) <= schema.get("maxItems", 10000):
            raise RouterError(f"{at}: ungültige Anzahl Einträge")
        for i, item in enumerate(value):
            validate(item, schema["items"], f"{at}[{i}]")
    elif kind == "integer":
        if not schema.get("minimum", -10**12) <= value <= schema.get("maximum", 10**12):
            raise RouterError(f"{at}: Zahl ausserhalb des zulässigen Bereichs")
    elif kind == "string" and len(value) > 12000:
        raise RouterError(f"{at}: unerwartet langer Text")


def load_policy(path: Path) -> dict[str, Any]:
    p = load_json(path)
    try:
        valid = (
            all(isinstance(p["models"][t], list) and p["models"][t] for t in TIERS)
            and all(isinstance(n, str) and re.fullmatch(r"[A-Za-z0-9._:/-]+", n)
                    for t in TIERS for n in p["models"][t])
            and set(p["weights"]) == {"reasoning", "ambiguity", "coupling", "visual",
                                      "failure_impact", "verification_difficulty"}
            and all(type(x) is int and 0 < x <= 10 for x in p["weights"].values())
            and type(p["thresholds"]["fast_max"]) is int
            and type(p["thresholds"]["balanced_max"]) is int
            and 0 <= p["thresholds"]["fast_max"] < p["thresholds"]["balanced_max"]
            and type(p["max_subagents"]) is int and 0 <= p["max_subagents"] <= 2
            and type(p["max_stagnant_attempts"]) is int and p["max_stagnant_attempts"] >= 1
        )
    except (KeyError, TypeError) as exc:
        raise RouterError("routing_policy.json hat ungültige Felder/Werte.") from exc
    if not valid:
        raise RouterError("routing_policy.json hat ungültige Felder/Werte.")
    return p


def normalize_catalog(raw: Any) -> list[dict[str, Any]]:
    if isinstance(raw, dict):
        raw = raw.get("result", raw)
        raw = raw.get("data", []) if isinstance(raw, dict) else raw
    if not isinstance(raw, list) or not raw:
        raise RouterError("Kein nichtleerer model/list-Katalog verfügbar.")
    result: list[dict[str, Any]] = []
    for item in raw:
        if not isinstance(item, dict) or item.get("hidden", False):
            continue
        model_id = item.get("model")
        if not isinstance(model_id, str) or not re.fullmatch(r"[A-Za-z0-9._:/-]+", model_id):
            continue
        options = item.get("supportedReasoningEfforts", [])
        efforts = [o.get("reasoningEffort") for o in options if isinstance(o, dict)]
        efforts = [e for e in efforts if isinstance(e, str)]
        default = item.get("defaultReasoningEffort")
        modalities = item.get("inputModalities", ["text", "image"])
        if not isinstance(modalities, list):
            modalities = []
        result.append({"model": model_id, "efforts": efforts,
                       "default_effort": default, "modalities": modalities})
    if not result:
        raise RouterError("Der Katalog enthält keine verwendbaren sichtbaren Modelle.")
    return result


def choose_model(catalog: list[dict[str, Any]], policy: dict[str, Any], tier: str,
                 needs_images: bool = False) -> tuple[dict[str, Any], str]:
    by_id = {x["model"]: x for x in catalog}
    for actual in TIERS[TIERS.index(tier):]:
        for preferred in policy["models"][actual]:
            model = by_id.get(preferred)
            if model and (not needs_images or "image" in model["modalities"]):
                return model, actual
    names = ", ".join(by_id)
    raise RouterError(
        f"Kein freigegebenes Modell für Profil '{tier}'"
        f"{' mit Bildeingabe' if needs_images else ''}. Verfügbar: {names}. "
        "Modellzuordnung in routing_policy.json prüfen. Kein stilles Herabstufen."
    )


def choose_effort(model: dict[str, Any], desired: str) -> tuple[str | None, str | None]:
    options = model["efforts"]
    if desired in options:
        return desired, None
    # Never escalate automatically to max/ultra merely because an effort is absent.
    usable = [e for e in EFFORTS if e in options and EFFORTS.index(e) <= EFFORTS.index(desired)]
    if usable:
        chosen = usable[-1]
        return chosen, f"{desired} nicht angeboten; tatsächlich gewählt: {chosen}."
    default = model.get("default_effort")
    if default in options and default in EFFORTS and EFFORTS.index(default) <= EFFORTS.index("xhigh"):
        return default, f"Gewünschte Denkstufe nicht angeboten; Modellstandard {default}."
    raise RouterError(f"Keine geeignete bestätigte Denkstufe für {model['model']}; "
                      "weder unbelegte Werte noch automatisches max/ultra werden benutzt.")


def decide(a: dict[str, Any], catalog: list[dict[str, Any]], p: dict[str, Any],
           cap: int = 2, attached_images: bool = False) -> dict[str, Any]:
    validate(a, load_json(ROOT / "assessment.schema.json"))
    s = a["scores"]
    score = sum(s[k] * w for k, w in p["weights"].items())
    tier = ("fast" if score <= p["thresholds"]["fast_max"] else
            "balanced" if score <= p["thresholds"]["balanced_max"] else "deep")
    reasons = [f"Heuristik: {score}/{3 * sum(p['weights'].values())} Punkte; "
               f"Arbeitsumfang {a['workload']}, Einstufungssicherheit {a['confidence']}."]
    strong_floor = (a["inverse_visual_reconstruction"] or
                    (s["reasoning"] == 3 and s["ambiguity"] >= 2) or
                    (s["failure_impact"] == 3 and s["verification_difficulty"] >= 2) or
                    (a["stagnant_attempts"] >= 2 and a["stall_cause"] in ("reasoning", "method")))
    if strong_floor:
        tier = "deep"
        reasons.append("Qualitätsregel: anspruchsvolle Rekonstruktion/Analyse oder belegte methodische Stagnation.")
    elif (a["confidence"] == "low" or s["failure_impact"] >= 2) and tier == "fast":
        tier = "balanced"
        reasons.append("Unsichere Einstufung oder Fehlerfolgen: kein Routineprofil.")
    if a["stall_cause"] in ("input", "tool", "permission", "compute"):
        reasons.append("Ressourcen-/Werkzeugproblem ist für sich kein Grund für mehr Denkaufwand.")
    if a["blocking_reason"].strip():
        return {"status": "blocked", "goal": a["goal"], "score": score,
                "workload": a["workload"], "requested_tier": tier,
                "blocking_reason": a["blocking_reason"], "reasons": reasons,
                "agents": [], "max_concurrent_subagents": 0}
    needs_images = attached_images or a["requires_images"]
    model, actual_tier = choose_model(catalog, p, tier, needs_images)
    if actual_tier != tier:
        reasons.append(f"Profil {tier} nicht verfügbar: {actual_tier} verwendet, nicht still heruntergestuft.")
    desired = ("low" if tier == "fast" else
               "high" if tier == "balanced" and (s["reasoning"] >= 2 or score >= 11) else
               "medium" if tier == "balanced" else
               "xhigh" if a["inverse_visual_reconstruction"] or score >= 19 else "high")
    effort, note = choose_effort(model, desired)
    if note:
        reasons.append(note)
    agents: list[dict[str, Any]] = []
    limit = min(max(cap, 0), p["max_subagents"])
    for c in a["parallel_candidates"]:
        if len(agents) >= limit or a["workload"] in ("XS", "S"):
            break
        if not c["independent"] or c["shared_writes"] or c["benefit"] != "clear":
            continue
        if a["shared_mutable_artifact"]:
            if c["role"] != "review" or c["stage"] != "after_checkpoint" or agents:
                continue
        agent_tier = "fast" if c["role"] == "routine" else "balanced"
        try:
            m, _ = choose_model(catalog, p, agent_tier, needs_images and c["role"] == "review")
            e, n = choose_effort(m, "medium" if c["role"] == "routine" else "high")
        except RouterError as exc:
            reasons.append(f"Nebenaufgabe bleibt beim Hauptagenten: {exc}")
            continue
        agents.append({"name": f"router_{c['role']}_{len(agents) + 1}",
                       "role": c["role"], "objective": c["objective"], "stage": c["stage"],
                       "model": m["model"], "effort": e, "read_only": True,
                       "effort_note": n})
    return {"status": "ready", "goal": a["goal"], "score": score,
            "workload": a["workload"], "confidence": a["confidence"],
            "requested_tier": tier, "actual_tier": actual_tier,
            "model": model["model"], "effort": effort, "agents": agents,
            "max_concurrent_subagents": len(agents), "reasons": reasons,
            "acceptance_checks": a["acceptance_checks"], "evidence": a["evidence"],
            "max_stagnant_attempts": p["max_stagnant_attempts"],
            "read_only_task": a["read_only_task"], "provisional_until_project_inspection": True}


def resolve_codex(spec: str | None = None) -> list[str]:
    found = shutil.which(spec or "codex")
    if found is None and spec and Path(spec).is_file():
        found = str(Path(spec).resolve())
    if not found:
        raise RouterError("Codex CLI nicht gefunden. Codex installieren/anmelden oder --codex mit Pfad angeben.")
    path = Path(found).resolve()
    if path.suffix.lower() == ".js":
        node = shutil.which("node")
        if not node:
            raise RouterError("node wird für den Codex-JavaScript-Starter benötigt.")
        return [node, str(path)]
    if os.name == "nt" and path.suffix.lower() in (".cmd", ".bat", ".ps1"):
        # Do not route user-controlled prompts through cmd.exe / PowerShell parsing.
        js = path.parent / "node_modules" / "@openai" / "codex" / "bin" / "codex.js"
        if js.is_file() and shutil.which("node"):
            return [shutil.which("node"), str(js)]
        raise RouterError("Windows-Shell-Shim nicht sicher auflösbar. --codex auf codex.exe "
                          "oder @openai/codex/bin/codex.js setzen.")
    return [str(path)]


def child_options() -> dict[str, Any]:
    return ({"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt"
            else {"start_new_session": True})


def stop_tree(proc: subprocess.Popen[str]) -> None:
    if proc.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    else:
        try:
            os.killpg(proc.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
    try:
        proc.wait(timeout=3)
    except subprocess.TimeoutExpired:
        if os.name != "nt":
            try:
                os.killpg(proc.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
        else:
            proc.kill()
        proc.wait(timeout=3)


def captured(command: list[str], *, cwd: Path, input_text: str | None = None,
             timeout: int = 150) -> tuple[str, str]:
    proc = subprocess.Popen(command, cwd=cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace",
                            **child_options())
    try:
        out, err = proc.communicate(input_text, timeout=timeout)
    except subprocess.TimeoutExpired as exc:
        stop_tree(proc)
        raise RouterError(f"Zeitlimit der Eingangsprüfung ({timeout}s) erreicht; kein Auftrag gestartet.") from exc
    except BaseException:
        stop_tree(proc)
        raise
    finally:
        for stream in (proc.stdin, proc.stdout, proc.stderr):
            if stream and not stream.closed:
                stream.close()
    if proc.returncode:
        raise RouterError(f"Codex fehlgeschlagen (Exit {proc.returncode}): {err[-2000:] or out[-2000:]}")
    return out, err


def discover_models(prefix: list[str], timeout: int = 35) -> Any:
    """Use app-server JSON-RPC over stdio; never start an inference thread here."""
    events: queue.Queue[Any] = queue.Queue()
    with tempfile.TemporaryDirectory(prefix="task-router-catalog-") as td:
        with tempfile.TemporaryFile(mode="w+t", encoding="utf-8") as err:
            proc = subprocess.Popen(prefix + ["app-server"], cwd=td,
                                    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=err,
                                    text=True, encoding="utf-8", errors="replace", **child_options())
            def reader() -> None:
                assert proc.stdout is not None
                try:
                    for line in proc.stdout:
                        try:
                            events.put(json.loads(line))
                        except ValueError:
                            continue
                finally:
                    events.put(None)
            thread = threading.Thread(target=reader, daemon=True)
            thread.start()
            deadline = time.monotonic() + timeout
            def send(payload: dict[str, Any]) -> None:
                assert proc.stdin is not None
                proc.stdin.write(json.dumps(payload) + "\n")
                proc.stdin.flush()
            def request(ident: int, method: str, params: dict[str, Any]) -> Any:
                send({"id": ident, "method": method, "params": params})
                while True:
                    remaining = deadline - time.monotonic()
                    if remaining <= 0:
                        raise RouterError("model/list: Zeitlimit. Kein erfundener Ersatzkatalog.")
                    try:
                        event = events.get(timeout=remaining)
                    except queue.Empty as exc:
                        raise RouterError("model/list antwortet nicht.") from exc
                    if event is None:
                        err.seek(0)
                        raise RouterError("Codex app-server beendet: " + err.read()[-1800:])
                    if not isinstance(event, dict):
                        continue
                    if event.get("id") == ident and "method" not in event:
                        if "error" in event:
                            raise RouterError(f"{method}: {event['error']}")
                        return event.get("result")
                    if "id" in event and "method" in event:
                        send({"id": event["id"], "error": {"code": -32601,
                              "message": "Task Router model discovery does not grant actions."}})
            try:
                request(1, "initialize", {"clientInfo": {"name": "task_router",
                        "title": "Task Router", "version": "1.0.0"}})
                send({"method": "initialized", "params": {}})
                rows: list[Any] = []
                cursor = None
                seen: set[str] = set()
                for page in range(100):
                    params: dict[str, Any] = {"limit": 100, "includeHidden": False}
                    if cursor:
                        params["cursor"] = cursor
                    result = request(page + 2, "model/list", params)
                    if not isinstance(result, dict) or not isinstance(result.get("data"), list):
                        raise RouterError("model/list liefert ein unerwartetes Format.")
                    rows.extend(result["data"])
                    cursor = result.get("nextCursor")
                    if not cursor:
                        return {"data": rows, "nextCursor": None}
                    if not isinstance(cursor, str) or cursor in seen:
                        raise RouterError("Ungültige model/list-Paginierung.")
                    seen.add(cursor)
                raise RouterError("Ungewöhnlich grosser Modellkatalog; abgebrochen.")
            finally:
                stop_tree(proc)
                if proc.stdin:
                    proc.stdin.close()
                thread.join(timeout=2)
                if proc.stdout:
                    proc.stdout.close()


def cflag(key: str, value: Any) -> list[str]:
    # JSON booleans, strings and integer literals are valid TOML values here.
    return ["-c", f"{key}={json.dumps(value, ensure_ascii=False)}"]


def triage_command(prefix: list[str], model: str, effort: str | None,
                   schema: Path, output: Path, images: list[Path]) -> list[str]:
    cmd = prefix + ["--strict-config", "--ask-for-approval", "never", "exec",
                    "--ignore-user-config", "--ephemeral", "--skip-git-repo-check",
                    "--sandbox", "read-only", "--model", model, "--json",
                    "--output-schema", str(schema), "--output-last-message", str(output)]
    for key, value in {"agents.enabled": False, "features.multi_agent": False,
                       "features.shell_tool": False, "features.unified_exec": False,
                       "features.hooks": False, "features.apps": False,
                       "web_search": "disabled"}.items():
        cmd += cflag(key, value)
    if effort:
        cmd += cflag("model_reasoning_effort", effort)
    for path in images:
        cmd += ["--image", str(path)]
    return cmd + ["-"]


def assess(prefix: list[str], catalog: list[dict[str, Any]], p: dict[str, Any],
           task: str, images: list[Path], run_dir: Path, timeout: int) -> dict[str, Any]:
    # A short Sol-class intake prioritizes classification quality. It does not solve the task.
    model, _ = choose_model(catalog, p, "balanced", bool(images))
    effort, _ = choose_effort(model, "low")
    instructions = (ROOT / "prompts" / "triage.txt").read_text(encoding="utf-8")
    payload = instructions + "\n\nAUFTRAGSDATEN (JSON-kodiert):\n" + json.dumps(task, ensure_ascii=False)
    started = time.monotonic()
    with tempfile.TemporaryDirectory(prefix="task-router-triage-") as td:
        output = Path(td) / "assessment.json"
        cmd = triage_command(prefix, model["model"], effort, ROOT / "assessment.schema.json", output, images)
        out, _ = captured(cmd, cwd=Path(td), input_text=payload, timeout=timeout)
        value = load_json(output)
    validate(value, load_json(ROOT / "assessment.schema.json"))
    usage = None
    for line in out.splitlines():
        try:
            event = json.loads(line)
        except ValueError:
            continue
        if isinstance(event, dict) and event.get("type") == "turn.completed":
            usage = event.get("usage")
    write_json(run_dir / "intake.json", {"model": model["model"], "effort": effort,
               "elapsed_seconds": round(time.monotonic() - started, 2),
               "usage_if_reported": usage, "basis": "task_and_explicit_context_only"})
    return value


def prepare_execution(plan: dict[str, Any], task: str, project: Path,
                      run_dir: Path) -> Path:
    skills = (ROOT / "task-router" / "SKILL.md").read_text(encoding="utf-8")
    instructions = (
        "# Auftrag mit externer Startkonfiguration\n\n"
        "Der Starter hat Modell/Denkstufe für diese NEUE Sitzung angefordert. "
        "Behaupte keine darüber hinausgehende Modellumschaltung. "
        "Eine spätere Neubewertung ist eine Empfehlung, kein tatsächlicher Modellwechsel.\n\n"
        "## Routing-Ergebnis\n```json\n" + json.dumps(plan, ensure_ascii=False, indent=2) +
        "\n```\n\n## Arbeitsregeln\n" + skills +
        "\n\n## Originalauftrag und ausdrücklich beigefügter Kontext\n" + task + "\n"
    )
    target = run_dir / "auftrag.md"
    target.write_text(instructions, encoding="utf-8")
    for a in plan["agents"]:
        # Config-layer roles, not standalone custom-agent discovery. Each child is read-only
        # and cannot spawn additional agents through Codex multi-agent tools.
        text = ("model = " + json.dumps(a["model"]) + "\n" +
                ("model_reasoning_effort = " + json.dumps(a["effort"]) + "\n" if a["effort"] else "") +
                'sandbox_mode = "read-only"\n' +
                "developer_instructions = " + json.dumps(
                    "Du bist ein nicht schreibender Prüfer/Recherchehelfer. Aufgabe: " + a["objective"] +
                    ". Keine Änderungen, keine weiteren Subagenten. Nutze bereitgestellte Belege "
                    "und tatsächlich verfügbare Werkzeuge. Melde Befunde mit Pfaden/Quellen, "
                    "ausgeführte Prüfungen und Unsicherheiten. Keine Konfigurationsänderungen "
                    "oder neuen Codex-Prozesse. Warte auf einen festen Stand, falls vorgesehen.",
                    ensure_ascii=False) +
                "\n[agents]\nenabled = false\n[features]\nmulti_agent = false\n")
        (run_dir / f"{a['name']}.toml").write_text(text, encoding="utf-8")
    return target


def main_command(prefix: list[str], plan: dict[str, Any], project: Path,
                 run_dir: Path, prompt_file: Path, images: list[Path], write: bool) -> list[str]:
    sandbox = "workspace-write" if write and not plan["read_only_task"] else "read-only"
    cmd = prefix + ["--strict-config", "--model", plan["model"], "--cd", str(project),
                    "--sandbox", sandbox, "--ask-for-approval", "on-request"]
    if plan["effort"]:
        cmd += cflag("model_reasoning_effort", plan["effort"])
    enabled = bool(plan["agents"])
    cmd += cflag("agents.enabled", enabled) + cflag("features.multi_agent", enabled)
    if enabled:
        cmd += cflag("agents.max_concurrent_threads_per_session", len(plan["agents"]))
        first = plan["agents"][0]
        cmd += cflag("agents.default_subagent_model", first["model"])
        if first["effort"]:
            cmd += cflag("agents.default_subagent_reasoning_effort", first["effort"])
        for a in plan["agents"]:
            cmd += cflag(f"agents.{a['name']}.config_file", str(run_dir / f"{a['name']}.toml"))
            cmd += cflag(f"agents.{a['name']}.description",
                         f"{a['stage']}: {a['objective']} (nur lesen)")
    for path in images:
        cmd += ["--image", str(path)]
    cmd += [f"Lies den vollständigen Auftrag in {json.dumps(str(prompt_file), ensure_ascii=False)} "
            "und führe ihn im Projekt aus. Prüfe zuerst die relevanten Eingaben. "
            "Verwende die externe Startkonfiguration und nur die vorgesehenen Nebenrollen. "
            "Keine erneute Eingangsprüfung starten; keine rekursiven Router-/Codex-Prozesse."]
    return cmd


def show_plan(plan: dict[str, Any], simulated: bool = False) -> None:
    if simulated:
        print("SIMULATION — Testdaten, kein Modell aufgerufen oder Benchmark durchgeführt.")
    if plan["status"] == "blocked":
        print("BLOCKIERT: " + plan["blocking_reason"])
        return
    print(f"Routing: {plan['model']} / {plan['effort'] or 'Standard'} | "
          f"Aufwand {plan['workload']} | max. {plan['max_concurrent_subagents']} Subagent(en)")
    for a in plan["agents"]:
        print(f"  {a['name']}: {a['model']} / {a['effort']} — {a['stage']}: {a['objective']}")
    for reason in plan["reasons"]:
        print("  " + reason)


def cli() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("plan", "run"):
        sp = sub.add_parser(name, help="Einstufen" if name == "plan" else "Einstufen und Codex starten")
        task_group = sp.add_mutually_exclusive_group(required=True)
        task_group.add_argument("--task")
        task_group.add_argument("--task-file", type=Path)
        sp.add_argument("--project", type=Path, default=Path.cwd())
        sp.add_argument("--context-file", type=Path, action="append", default=[])
        sp.add_argument("--image", type=Path, action="append", default=[])
        sp.add_argument("--policy", type=Path, default=ROOT / "routing_policy.json")
        sp.add_argument("--max-subagents", type=int, choices=(0, 1, 2), default=2)
        sp.add_argument("--codex", help="Pfad zum Codex-Programm oder codex.js")
        sp.add_argument("--timeout", type=int, default=150, help="Sekunden für die Eingangsprüfung")
        sp.add_argument("--out", type=Path, help="Neuer Ordner für den Lauf; vorhandene Ordner werden nicht überschrieben")
        if name == "run":
            sp.add_argument("--write", action="store_true", help="Lokale Änderungen für Umsetzungsaufträge erlauben")
        else:
            sp.add_argument("--assessment-file", type=Path, help="Vorhandene Einstufung statt eines Modellaufrufs")
            sp.add_argument("--models-file", type=Path, help="Katalogdatei nur für Offline-Prüfungen, niemals Live-Verfügbarkeit")
    models = sub.add_parser("models", help="Live-Katalog abfragen, keine Modellinferenz")
    models.add_argument("--codex")
    demo = sub.add_parser("demo", help="Offline-Regeltests mit Beispielen, keine KI-Aufrufe")
    demo.add_argument("name", choices=("umr", "message", "research", "blocked"))
    install = sub.add_parser("install-skill", help="Skill lokal kopieren; keine globale Konfiguration ändern")
    install.add_argument("--project", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "install-skill":
        project = args.project.expanduser().resolve()
        if not project.is_dir():
            raise RouterError("Projektordner existiert nicht.")
        destination = project / ".agents" / "skills" / "task-router"
        if destination.exists():
            raise RouterError(f"Skill bereits vorhanden; nichts überschrieben: {destination}")
        shutil.copytree(ROOT / "task-router", destination)
        print(f"Skill kopiert: {destination}. Start per $task-router; Hauptmodellwechsel nur über den Starter.")
        return 0
    if args.command == "demo":
        p = load_policy(ROOT / "routing_policy.json")
        a = load_json(ROOT / "examples" / f"{args.name}.json")
        catalog = normalize_catalog(load_json(ROOT / "examples" / "models.fixture.json"))
        show_plan(decide(a, catalog, p), simulated=True)
        return 0
    if args.command == "models":
        print(json.dumps(discover_models(resolve_codex(args.codex)), ensure_ascii=False, indent=2))
        return 0
    if args.timeout < 10 or args.timeout > 900:
        raise RouterError("Eingangsprüfungs-Zeitlimit muss zwischen 10 und 900 Sekunden liegen.")
    p = load_policy(args.policy)
    project = args.project.expanduser().resolve()
    if not project.is_dir():
        raise RouterError(f"Projektverzeichnis fehlt: {project}")
    if args.task_file:
        task = args.task_file.read_text(encoding="utf-8-sig")
    else:
        task = args.task
    for path in args.context_file:
        if path.stat().st_size > MAX_INPUT * 4:
            raise RouterError("Kontextdatei zu gross; relevante Ausschnitte bereitstellen.")
        task += f"\n\nBeigefügter Kontext aus {path.name}:\n" + path.read_text(encoding="utf-8-sig")
    if not task.strip() or len(task) > MAX_INPUT:
        raise RouterError(f"Auftrag muss 1 bis {MAX_INPUT} Zeichen umfassen; Kontext gezielt begrenzen.")
    images = [x.expanduser().resolve() for x in args.image]
    if len(images) > 6:
        raise RouterError("Höchstens sechs ausdrücklich ausgewählte Referenzbilder pro Start.")
    for path in images:
        if not path.is_file() or path.suffix.lower() not in (".png", ".jpg", ".jpeg", ".webp"):
            raise RouterError(f"Kein unterstütztes vorhandenes Referenzbild: {path}")
    offline = bool(getattr(args, "models_file", None))
    prefix = None if offline and getattr(args, "assessment_file", None) else resolve_codex(args.codex)
    raw = load_json(args.models_file) if offline else discover_models(prefix)
    catalog = normalize_catalog(raw)
    if offline and not getattr(args, "assessment_file", None):
        raise RouterError("Offline-Katalog benötigt --assessment-file. Keine Live-Aufrufe mit Testkatalog.")
    root = args.out.expanduser().resolve() if args.out else (
        Path.home() / ".task-router" / "runs" / (time.strftime("%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:8]))
    root.mkdir(parents=True, exist_ok=False)
    write_json(root / "models.json", raw)
    write_json(root / "policy.json", p)
    if getattr(args, "assessment_file", None):
        assessment = load_json(args.assessment_file)
    else:
        print("Kurze Eingangsprüfung läuft; der Projektauftrag wurde noch nicht gestartet.", flush=True)
        assessment = assess(prefix, catalog, p, task, images, root, args.timeout)
    plan = decide(assessment, catalog, p, args.max_subagents, bool(images))
    write_json(root / "assessment.json", assessment)
    plan["catalog_source"] = "offline_unverified" if offline else "live_model_list"
    write_json(root / "plan.json", plan)
    show_plan(plan, simulated=offline)
    print(f"Routing-Protokoll: {root}", flush=True)
    if plan["status"] != "ready":
        return 2
    if args.command == "plan":
        print("Nur Einstufung; keine Ausführung und keine Änderung am Projekt.")
        return 0
    if not plan["read_only_task"] and not args.write:
        raise RouterError("Der Auftrag benötigt Änderungen. Für die Ausführung --write ausdrücklich hinzufügen.")
    prompt_file = prepare_execution(plan, task, project, root)
    command = main_command(prefix, plan, project, root, prompt_file, images, args.write)
    write_json(root / "requested_launch.json", {"argv": command,
               "note": "Angeforderte Startparameter, kein Nachweis abgeschlossener Arbeit oder gespawnter Agenten."})
    print("Starte eine neue interaktive Codex-Sitzung. Freigaben bleiben aktiv.", flush=True)
    code = subprocess.call(command, cwd=project)
    write_json(root / "exit.json", {"exit_code": code,
               "note": "Exit 0 ist kein fachlicher Qualitätsnachweis. Prüfungen im Codex-Bericht kontrollieren."})
    return code


if __name__ == "__main__":
    try:
        raise SystemExit(cli())
    except KeyboardInterrupt:
        print("\nAbgebrochen.", file=sys.stderr)
        raise SystemExit(130)
    except (RouterError, OSError, UnicodeError) as exc:
        print(f"FEHLER: {exc}", file=sys.stderr)
        raise SystemExit(2)
