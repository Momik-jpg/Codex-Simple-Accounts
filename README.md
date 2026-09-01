# Codex Simple Accounts

Eine lokale Windows-App zum Verwalten und Wechseln mehrerer Konten in der Codex-Desktop-App.

## Funktionen

- Beliebig viele Konten hinzufügen und lokal verwalten
- Konto- und Wochenlimits regelmässig anzeigen
- Automatischer Kontowechsel bei tiefem Restlimit
- ChatGPT vollständig beenden und mit dem gewählten Konto neu starten
- Kontonamen aus der lokalen Anmeldung anzeigen
- Kontodaten mit Windows-DPAPI verschlüsseln
- Notfall-Schalter zum Abbrechen eines Wechsels und Ausschalten von Auto-Swap

## Installation

1. Unter [Releases](https://github.com/Momik-jpg/Codex-Simple-Accounts/releases) den neusten `Codex-Konten-Installer.exe` herunterladen.
2. Installer ausführen.
3. `Codex-Konten` öffnen und Konten hinzufügen.

Die App benötigt Windows 10 oder 11 und die installierte Codex-Desktop-App.

## Sicherheit

- Kontodaten bleiben lokal und werden mit Windows-DPAPI für den aktuellen Benutzer verschlüsselt.
- Die App verwendet keinen eigenen WebSocket-Proxy und öffnet keinen Netzwerk-Port.
- Beim Wechsel wird `~/.codex/auth.json` atomar durch die ausgewählte lokale Anmeldung ersetzt.
- Der Quellcode enthält keine Kontodaten oder Zugangsschlüssel.

Vor der Installation eines Releases kann die SHA-256-Prüfsumme in den Release Notes verglichen werden.

## Selber bauen

Voraussetzungen:

- .NET 9 SDK
- Inno Setup 6

```powershell
.\build.ps1
```

Der Installer wird unter `artifacts/installer/Codex-Konten-Installer.exe` erstellt.

## Tests

```powershell
dotnet test .\CodexAccountTray.sln -c Release
```

## Hinweis

Dieses Projekt ist ein unabhängiges Hilfsprogramm und nicht mit OpenAI verbunden oder von OpenAI unterstützt.
