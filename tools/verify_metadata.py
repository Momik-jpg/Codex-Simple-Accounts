"""Validate checked-in router JSON and .NET project XML without extra packages."""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
ROUTER = ROOT / "tools" / "task-router"


def main() -> None:
    json_files = [ROUTER / "routing_policy.json", ROUTER / "assessment.schema.json"]
    json_files.extend(sorted((ROUTER / "examples").glob("*.json")))
    projects = sorted(ROOT.glob("src/**/*.csproj")) + sorted(ROOT.glob("tests/**/*.csproj"))
    if not json_files or not projects:
        raise RuntimeError("Router-JSON oder .NET-Projektdateien fehlen.")
    for path in json_files:
        with path.open(encoding="utf-8-sig") as stream:
            json.load(stream)
    for path in projects:
        ET.parse(path)
    app = ET.parse(ROOT / "src" / "CodexAccountTray" / "CodexAccountTray.csproj").getroot()
    version = app.findtext("PropertyGroup/Version")
    if not version or app.findtext("PropertyGroup/AssemblyVersion") != version + ".0" or \
            app.findtext("PropertyGroup/FileVersion") != version + ".0":
        raise RuntimeError("App-/Assembly-/Dateiversion stimmen nicht überein.")
    installer = (ROOT / "installer" / "CodexAccountTray.iss").read_text(encoding="utf-8-sig")
    match = re.search(r'^#define AppVersion "([^"]+)"$', installer, re.MULTILINE)
    if not match or match.group(1) != version or \
            f"VersionInfoVersion={version}.0" not in installer or \
            f"VersionInfoProductVersion={version}" not in installer:
        raise RuntimeError("Installer-Version weicht von der App-Version ab.")
    if not (ROOT / "assets" / "Codex-Konten.ico").is_file():
        raise RuntimeError("Produkt-Icon fehlt.")
    print(f"Metadaten gültig: {len(json_files)} JSON-Dateien, {len(projects)} Projektdateien; Version {version}.")


if __name__ == "__main__":
    main()
