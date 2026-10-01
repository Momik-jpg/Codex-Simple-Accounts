# Codex Simple Accounts

[![CI](https://github.com/Momik-jpg/Codex-Simple-Accounts/actions/workflows/ci.yml/badge.svg)](https://github.com/Momik-jpg/Codex-Simple-Accounts/actions/workflows/ci.yml) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Eine lokale Windows-App für Codex-Konten, Limits und Aufgabenbewertung direkt im bestehenden Codex-Chat.

## Funktionen

- Beliebig viele Konten hinzufügen und lokal verwalten
- Konto- und Wochenlimits regelmässig anzeigen
- Automatischer Kontowechsel bei tiefem Restlimit
- Die zu Codex gehörenden Desktop-Prozesse beim Kontowechsel beenden und mit dem gewählten Konto neu starten
- Kontonamen aus der lokalen Anmeldung anzeigen
- Kontodaten mit Windows-DPAPI verschlüsseln
- Notfall-Schalter zum Abbrechen eines Wechsels und Ausschalten von Auto-Swap
- Aufgabenrouter mit automatischer oder manueller Qualitäts-, Modell- und Denkstufenwahl aus dem Live-Modellkatalog
- Lokale Sicherheitsuntergrenzen, sichtbare Fallbacks und erneute Katalog-/Regelprüfung unmittelbar vor dem Start
- Wiederverwendbare Einstufung für einen schnelleren Start ohne doppelten Klassifikationsaufruf
- Vorschau eines auftragsbezogenen KI-Arbeitsplans mit Prüfschritten und ungeprüften Abschlusskriterien; nach Projektinspektion zu bestätigen
- Aufgaben direkt in Codex über gg bewerten und ausführen; bisheriger separater Starter nur unter „Erweitert“

## Aufgaben direkt in Codex: gg

Nach einmaliger [Chat-Einrichtung](docs/AUTO_TASK_ROUTER.md#gg-app-bewertung-im-bestehenden-codex-chat-optional) im vertrauten Projekt die Aufgabe schreiben und **gg im Slash-Skill-Menü wählen**, alternativ `$gg`:

```text
$gg Behebe den Fehler im Login und prüfe die betroffenen Tests.
```

Die App-Bewertung läuft im Hintergrund. Codex zeigt Plan, tatsächlichen Bewerter und Auswahlgründe, übernimmt die Umsetzung und delegiert geeignete Teilaufgaben über verfügbare Unteragenten-Funktionen. Der bestehende Chat bleibt Hauptchat. Kein Aufgabenformular und kein neuer Hauptchat für diesen Ablauf. Verfügbarkeit und konkrete Modellwahl der Unteragenten hängen vom Client ab; das Hauptmodell wird nicht heimlich gewechselt.

Die Chat-Anbindung braucht Python 3.11+, eine angemeldete Codex CLI und die dauerhaft vorhandenen TaskRouter-Dateien aus diesem PR. Sie wird nicht durch blosses Tippen von `/gg` installiert. Ein lokaler Bewerter auf einem ausgeschalteten Laptop ist mobil nicht erreichbar. Der frühere Dialog ist ein optionaler separater Starter unter **Tray → Erweitert**; er ist nicht der gg-Ablauf.

## Sicherer Wechselablauf

Die App hält Kontodaten lokal verschlüsselt und führt den Wechsel in einem klaren Ablauf aus.

```mermaid
flowchart LR
    A["Konto auswählen"] --> B["Codex Desktop beenden"]
    B --> C["auth.json atomar ersetzen"]
    C --> D["Codex Desktop neu starten"]
    E["Lokale Kontodaten<br/>Windows DPAPI"] --> C
```

## Installation

1. Unter [Releases](https://github.com/Momik-jpg/Codex-Simple-Accounts/releases) den neusten `Codex-Konten-Installer.exe` herunterladen.
2. Installer ausführen.
3. `Codex-Konten` öffnen und Konten hinzufügen.

Die Kontoverwaltung benötigt Windows 10 oder 11 und die installierte Codex-Desktop-App. Für neue Auto-Aufgaben sind zusätzlich Python 3.10+ und eine angemeldete aktuelle Codex-CLI erforderlich. Der Router kann Konto-Kontingent verbrauchen; eine Einstufung ist kein kostenloser Offline-Vorgang. Details: [Auto-Aufgabe](docs/AUTO_TASK_ROUTER.md).

## Sicherheit

- Kontodaten bleiben lokal und werden mit Windows-DPAPI für den aktuellen Benutzer verschlüsselt.
- Die App startet keinen dauerhaften WebSocket-Proxy und keinen Listener auf Port `47831`; die bestehende Codex-Protokollprüfung kann kurzzeitig einen freien lokalen Port verwenden.
- Beim Wechsel wird `~/.codex/auth.json` atomar durch die ausgewählte lokale Anmeldung ersetzt.
- Bei fehlgeschlagenem Start wird die vorherige Anmeldung wiederhergestellt; ein Wechsel wartet, bis Codex Desktop geschlossen ist.
- Der Aufgabenrouter startet standardmässig ohne Schreibrecht, sperrt parallele Kontoaktionen und protokolliert keine Authentifizierungsdateien.
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
Ein erfolgreicher Build ersetzt keine Clean-Install-, Upgrade- oder Kontoabnahme.

## Tests

```powershell
dotnet test .\CodexAccountTray.sln -c Release
python -m unittest discover -s tools/task-router/tests -v
python tools/verify_metadata.py
```

## Hinweis

Dieses Projekt ist ein unabhängiges Hilfsprogramm und nicht mit OpenAI verbunden oder von OpenAI unterstützt.
Auto-Swap bleibt auf Wunsch des Projektinhabers erhalten. Die
[OpenAI-Hilfe zum Kontowechsel](https://help.openai.com/en/articles/20001068-use-multiple-accounts-with-account-switching)
beschreibt mehrere getrennte Konten und den manuellen Wechsel im Web, nicht
jedoch Auto-Swap in Codex Desktop. Die
[Nutzungsbedingungen für die Schweiz](https://openai.com/policies/eu-terms-of-use/)
untersagen die Umgehung von Rate-Limits. Ob ein automatischer Wechsel zwischen
eigenen, separat bezahlten Konten zulässig ist, wird hier nicht als geklärt
behauptet; vor Veröffentlichung ist diese Frage unabhängig zu prüfen.


## Lizenz

MIT – siehe [LICENSE](LICENSE). Hinweise zu mitgelieferten .NET-Komponenten,
externen Werkzeugen und Drittanbietern stehen unter [Lizenz- und Drittanbieterhinweise](docs/THIRD_PARTY_LICENSES.md).
