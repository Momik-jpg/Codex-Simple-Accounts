# Windows-Abnahme v1.6.0 – Zwischenstand, nicht freigegeben

Stand: 29. September 2026. Diese Datei ist ein lokales Prüfprotokoll; die Checkliste in [Issue #6](https://github.com/Momik-jpg/Codex-Simple-Accounts/issues/6) bleibt offen und wurde nicht als erledigt markiert.

## Beobachtete Umgebung

| Merkmal | Beobachtung |
|---|---|
| Windows | Build `10.0.26200.9457`, DisplayVersion `25H2`; der Registry-Produktname meldet widersprüchlich „Windows 10 Pro“, daher Windows-11-Edition noch nicht unabhängig bestätigt |
| Architektur | `AMD64` |
| Skalierung | `AppliedDPI=144` (150 %); keine visuelle Abnahme daraus ableitbar |
| Python | 3.11.9 |
| Codex CLI | 0.140.0 |
| Inno Setup | 6.7.3 |
| Konto | Vorhandene CLI-Anmeldung ausschliesslich für den lesenden `model/list`-Abruf verwendet; kein separates Testkonto bereitgestellt |
| Testprojekt | Temporäre Unit-Test-Fixtures, noch kein manueller wegwerfbarer UI-Testordner |
| Screenshots | Keine – native UI wurde noch nicht visuell geprüft |

## Automatisch lokal geprüft

- `model/list` lieferte für die aktive CLI-Anmeldung nur `gpt-5.5` mit `low`, `medium`, `high`, `xhigh`. Kein Modell wurde aus der Präferenzliste als verfügbar vorausgesetzt.
- 94 .NET-Tests und 72 Python-Tests bestanden. Diese simulieren kritische Router-, Start-, Konto- und Fehlerpfade, ersetzen aber keine echte Anmeldung oder Desktop-Abnahme.
- Release-Build und Inno-Installer wurden lokal kompiliert; Produkt-/Dateiversion `1.6.0`. Der Installer wurde **nicht** ausgeführt.
- Lokaler Installationskandidat `Codex-Konten-Installer.exe`: `34'535'920` Bytes, SHA-256 `0F1EAAF473B200D67D87487D1105FB86B5415C7C54141D24A08F0CBE61449218`. Der Hash ist noch nicht mit einem hochgeladenen Release-Asset verglichen.
- Python-Syntax, Router-JSON, Projekt-XML, Versionsabgleich und Publish-Payload wurden lokal geprüft.
- Quelltextsuche ergab keinen neuen Listener für Port `47831`; bei der Stichprobe war auf diesem Port kein lokaler TCP-Listener sichtbar. Der bestehende `CodexBackend` kann einen kurzlebigen freien Loopback-Port verwenden.

## Offen und nicht als bestanden behauptet

- Öffnen aus Kontenfenster und Tray; Fokus, Tastatur, Hover, Fehler-/Ladezustände; Layout bei 100 %, 125 %, 150 % und 200 %; Screenshots.
- Echte Einstufung, Start, schreibgeschützter und ausdrücklich freigegebener Schreibmodus mit separatem Testkonto und Wegwerfprojekt.
- Login, Logout, Re-Login, Auto-Swap und Kontowechsel an einer ungefährlichen Testanmeldung; keine Beschädigung von Desktop-Chats oder Kontospeicher.
- Clean-Install, Upgrade von v1.5.5, Deinstallation und Neuinstallation. Installation auf dem produktiven Benutzerprofil wäre ohne Testumgebung nicht verantwortbar.
- [PR #7](https://github.com/Momik-jpg/Codex-Simple-Accounts/pull/7) wurde als Entwurf erstellt. Seine [CI](https://github.com/Momik-jpg/Codex-Simple-Accounts/actions/runs/36541430452) ist noch zu prüfen. GitHub-Issue #6 aktualisieren, Merge und Release-Entwurf mit finalem Installer füllen. Die lokale GitHub-CLI-Anmeldung meldet ein ungültiges Token; der PR wurde über die bereits funktionierende Git-Anmeldung erstellt.

Bis diese Punkte wirklich geprüft und dokumentiert sind, ist v1.6.0 **nicht veröffentlichungsbereit**. Der bestehende Release-Entwurf bleibt unveröffentlicht.
