#!/usr/bin/env python3
"""GUI entry point: preserve startup errors after the console closes. No inference of its own."""
import sys
from pathlib import Path


def main():
    if len(sys.argv) < 3:
        print("Usage: desktop_entry.py ERROR_LOG ROUTER_COMMAND ...", file=sys.stderr)
        return 2
    error_log = Path(sys.argv.pop(1))
    try:
        if sys.version_info < (3, 10):
            raise RuntimeError("Python 3.10 oder neuer erforderlich.")
        import router
        return router.cli()
    except KeyboardInterrupt:
        message, code = "Abgebrochen.", 130
    except Exception as exc:
        message, code = "FEHLER: " + str(exc), 2
    print(message, file=sys.stderr)
    try:
        with error_log.open("x", encoding="utf-8") as stream:
            stream.write(message + "\n")
    except OSError:
        pass  # Never overwrite an existing file or obscure the original failure.
    return code


if __name__ == "__main__":
    raise SystemExit(main())
