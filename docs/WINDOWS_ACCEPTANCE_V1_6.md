# Windows-Abnahme v1.6.0 – Zwischenstand, nicht freigegeben

Stand: 29. September 2026. Diese Datei ist ein lokales Prüfprotokoll; die Checkliste in [Issue #6](https://github.com/Momik-jpg/Codex-Simple-Accounts/issues/6) bleibt offen. Der belegte Zwischenstand wurde [im Issue kommentiert](https://github.com/Momik-jpg/Codex-Simple-Accounts/issues/6#issuecomment-5886424726), ohne manuelle Prüfungen als erledigt zu markieren.

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
- 95 .NET-Tests und 75 Python-Tests bestanden. Diese simulieren kritische Router-, Start-, Konto- und Fehlerpfade sowie Planvalidierung, ersetzen aber keine echte Anmeldung oder Desktop-Abnahme.
- Release-Build und Inno-Installer wurden lokal kompiliert; Produkt-/Dateiversion `1.6.0`. Der Installer wurde **nicht** ausgeführt.
- Lokaler Installationskandidat `Codex-Konten-Installer.exe`: `34'543'001` Bytes, SHA-256 `FC68D7D1A9E1B3888CCF2F05B2A027CFF3BB5F7EC7E686F5335026252EB4EBEE`. Der Hash ist noch nicht mit einem hochgeladenen Release-Asset verglichen.
- Der Build kopierte Projekt-MIT-Lizenz, .NET-Runtime-Lizenz, deren Drittanbieterhinweise und WindowsDesktop-Lizenz versionsgenau und unverändert in die Publish-Ausgabe. Das Inno-Buildprotokoll bestätigt alle fünf Dateien im Installer und die Lizenzseite; der Installer selbst wurde nicht installiert.
- `Get-AuthenticodeSignature` meldete `NotSigned`; eine Windows-Herausgeber-/SmartScreen-Warnung ist möglich und wird nicht automatisch umgangen.
- Python-Syntax, Router-JSON, Projekt-XML, Versionsabgleich und Publish-Payload wurden lokal geprüft.
- Quelltextsuche ergab keinen neuen Listener für Port `47831`; bei der Stichprobe war auf diesem Port kein lokaler TCP-Listener sichtbar. Der bestehende `CodexBackend` kann einen kurzlebigen freien Loopback-Port verwenden.

## Offen und nicht als bestanden behauptet

- Öffnen aus Kontenfenster und Tray; Fokus, Tastatur, Hover-Übergänge mit Windows-Animationen ein/aus, Fehler-/Ladezustände; Layout bei 100 %, 125 %, 150 % und 200 %; Screenshots.
- Echte Einstufung, Start, schreibgeschützter und ausdrücklich freigegebener Schreibmodus mit separatem Testkonto und Wegwerfprojekt.
- Login, Logout, Re-Login, Auto-Swap und Kontowechsel an einer ungefährlichen Testanmeldung; keine Beschädigung von Desktop-Chats oder Kontospeicher.
- Richtlinienentscheid: Die bestehende Regel in `LimitMonitor` wählt bei erschöpftem Limit ein anderes Konto. Die [OpenAI-Nutzungsbedingungen für die Schweiz](https://openai.com/policies/eu-terms-of-use/) verbieten die Umgehung von Rate-Limits. Bis zum Entscheid über das Abschalten dieser Automatik darf der Release nicht als regelkonform gelten. Die erlaubte Nutzung mehrerer eigener Konten ist damit nicht pauschal beurteilt.
- Lizenz-/Markenprüfung: Der lokal installierte Inno-Setup-Compiler meldet „Non-commercial use only“; kommerzieller Einsatzzweck und gegebenenfalls eine passende Inno-Lizenz sind nicht geklärt. Die Herkunft/Nutzungsrechte des App-Icons sind nicht unabhängig belegt. Der Unabhängigkeits-Hinweis im README ersetzt keine Markenprüfung.
- Clean-Install, Upgrade von v1.5.5, Deinstallation und Neuinstallation. Installation auf dem produktiven Benutzerprofil wäre ohne Testumgebung nicht verantwortbar.
- [PR #7](https://github.com/Momik-jpg/Codex-Simple-Accounts/pull/7) bleibt ein Entwurf. Die CI für die neue Lizenzänderung steht noch aus; Review, Merge und Füllen des Release-Entwurfs mit einem final verifizierten Installer bleiben offen. Die lokale GitHub-CLI-Anmeldung meldet ein ungültiges Token; PR und Issue-Kommentar wurden über die bereits funktionierende Git-Anmeldung erstellt.

Bis diese Punkte wirklich geprüft und dokumentiert sind, ist v1.6.0 **nicht veröffentlichungsbereit**. Der bestehende Release-Entwurf bleibt unveröffentlicht.
