# Auto-Aufgabe: Modell und Denkaufwand automatisch auswählen

Die Windows-App bietet im Kontenfenster den zusätzlichen Button **Auto-Aufgabe …**.
Der bisherige Kontowechsel, DPAPI-Speicher, `CodexBackend` und die Legacy-Bereinigung werden nicht verändert.
Der Router startet eine **neue Codex-CLI-Sitzung**. Er verändert keine laufenden Desktop-Chats und überwacht deren Prompts nicht.

## Wer entscheidet?

1. **Codex `model/list`** liefert den sichtbaren Modellkatalog der aktuellen CLI-Anmeldung. Er entscheidet nicht, welches Modell für die Aufgabe am besten ist.
2. **Ein kurzer Klassifikationsaufruf** bewertet ausschliesslich Auftrag und ausdrücklich beigefügte Bilder. Standard: ein verfügbares Modell aus dem `balanced`-Profil (Sol), gewünschte Stufe `low`. Fehlt das Profil, darf ein freigegebenes höheres Profil verwendet werden. Der tatsächliche Klassifikator steht in `intake.json`.
3. **Deterministischer lokaler Code** (`decide` in `router.py`) wendet die Gewichte, Schwellen und Qualitätsregeln an. Er wählt Hauptmodell, angebotene Denkstufe und 0–2 geeignete Nebenrollen. Das Modell kann diese Regeln oder die Schreibfreigabe nicht ändern.
4. **Der Starter** übergibt die Auswahl an einen neuen Codex-Prozess. Der Hauptagent entscheidet innerhalb der konfigurierten Obergrenze, ob/wann er die vorgesehenen Nebenrollen tatsächlich startet.
5. **Der Nutzer** bestimmt Auftrag, optionale eigene Regeldatei, Agentenobergrenze und ausdrückliche Schreibfreigabe. Subagenten sind in dieser Version nur lesend.

Die Bewertung ist eine Startheuristik, kein trainierter oder empirisch validierter Qualitätsmesser. Die Einstufung kann falsch sein. `XS` bis `XL` beschreibt Arbeitsumfang, keine Stunden-, Kosten- oder Tokenprognose. Eine Versionsnummer und lange Laufzeit sind kein Beweis für entsprechend viele gescheiterte Versuche.

## Voraussetzungen und Bedienung

- Bestehende .NET-/Windows-Voraussetzungen der App.
- Python **3.10+** als `py.exe`, `python.exe` oder `python3.exe` im PATH. Keine Python-Pakete nötig.
- Aktuelle, angemeldete Codex CLI mit den verwendeten Runtime-/Config-Schaltern. Ältere Clients sollen mit einem sichtbaren Fehler abbrechen, nicht still ohne Regeln starten.
- Ein Modell aus dem konfigurierten Standardprofil für die Einstufung. Modellnamen in der JSON-Datei sind Präferenzen, keine Verfügbarkeitsgarantie.

Gewünschtes Konto zuerst im Kontowechsler aktivieren. **Auto-Swap während Router-Läufen ausschalten und kein Konto wechseln.** Bei aktivem Auto-Swap oder laufendem Kontowechsel lässt die Oberfläche keinen Router-Lauf starten. Die Integration schreibt weder `auth.json` noch Kontoeinstellungen; sie setzt `CODEX_HOME` für ihren Kindprozess auf das aktive Codex-Home des Kontowechslers. Eigene Provider-/API-Key-Konfigurationen der CLI bleiben zu beachten.

Im Dialog Projektordner, Auftrag und optional Referenzbilder auswählen. Die Agentenobergrenze darf 0, 1 oder 2 sein; sie erzwingt keine Delegation. Schreibzugriff ist standardmässig aus. Ein Umsetzungsauftrag startet ohne explizite Schreibfreigabe nicht.

**Nur einstufen** zeigt die Entscheidung, ohne den Projektauftrag auszuführen. **Automatisch starten** liest einen frischen Katalog, stuft neu ein und öffnet eine interaktive Codex-Konsole. Eine zuvor angesehene Vorschau wird bewusst nicht als aktuelle Freigabe wiederverwendet. Daher verursachen Vorschau plus Start zwei Einstufungsaufrufe. Rückfragen zu Aktionen werden in der Codex-Konsole beantwortet.

**Abbrechen** beendet den von diesem Dialog gestarteten Prozessbaum, nicht beliebige ChatGPT-/Codex-Prozesse. Bereits ausgeführte Änderungen werden nicht rückgängig gemacht. Das vorhandene **Notfall AUS** bleibt der Schalter für den Kontowechsel; für Router-Aufgaben den Abbrechen-Button beziehungsweise das Router-Terminal verwenden.

## Anpassbare Regeln

Standarddatei: `tools/task-router/routing_policy.json`, im installierten Programm unter `TaskRouter/`.
Für eigene Einstellungen eine Kopie ausserhalb von Program Files anlegen und im Dialog unter **Regeln** wählen. Nicht die globale Codex-Konfiguration überschreiben.

- `models`: geordnete Modell-IDs für `fast`, `balanced`, `deep`.
- `weights`, `thresholds`: Gewichtung der sechs 0–3-Bewertungen.
- `max_subagents`: absolute Obergrenze, höchstens 2.
- `max_stagnant_attempts`: Arbeitsregel zur Neubewertung der Methode; kein externer Laufzeitwächter.

Ein benötigtes tiefes Profil wird nicht still auf ein Routineprofil heruntergestuft. Bildaufgaben prüfen die angebotenen Modalitäten. Nicht angebotene Denkstufen werden gekennzeichnet auf eine bestätigte niedrigere/geeignete Stufe angepasst; `max`/`ultra` sind kein automatischer Ersatz.

## Protokolle und Datenschutz

GUI-Läufe: `%LOCALAPPDATA%/CodexAccountTray/RouterRuns/<Datum-ID>/`.
Darin liegen Auftrag, Modellkatalog, Einstufung, angewandte Regeln, Entscheidung und angeforderte Startparameter. `intake.json` enthält das tatsächliche Einstufungsmodell und gegebenenfalls gemeldeten Verbrauch. `error.log` hält Startfehler fest, auch wenn die Konsole schliesst.

Die Dateien können vertrauliche Auftrags- und Projektinformationen enthalten. Nicht ins Repository committen oder ungeprüft teilen. Der Router liest keine anderen Unterhaltungen. Der Einstufungsauftrag und ausdrücklich angehängte Bilder gehen über die bestehende Codex-Anmeldung an deren Anbieter; das ist kein rein lokaler Klassifikator. Diese Aufrufe verbrauchen Kontingent beziehungsweise API-Nutzung entsprechend der CLI-Konfiguration.

`plan.json` und `requested_launch.json` belegen Entscheidungen und angeforderte Parameter, nicht tatsächlich gestartete Subagenten oder bestandene fachliche Tests. Ein Exit-Code 0 ist kein Qualitätsnachweis.

## Grenzen

Kein automatischer Modellwechsel mitten im laufenden Turn, keine Hintergrundüberwachung, keine automatische Kontorotation für diese CLI-Aufträge, kein Tokenbudget-Wächter, keine Leistungs- oder Spargarantie. Ein Modellkatalog belegt angebotene Optionen, nicht ausreichendes Restkontingent für den gesamten Auftrag.

## Prüfung

```powershell
dotnet test .\CodexAccountTray.sln -c Release
python -m unittest discover -s tools/task-router/tests -v
python tools/task-router/router.py demo umr
```

Die Python-Tests verwenden synthetische Kataloge und einen lokalen Fake-Codex, keine bezahlten Modellaufrufe. Die .NET-Tests prüfen Eingaben, Argumenttrennung, Schreibfreigabe, `CODEX_HOME` und Ergebnisdarstellung. CI prüft zusätzlich die Router-Dateien im Publish-Ausgabeverzeichnis. Ein erfolgreicher CI-Lauf ersetzt keinen visuellen Windows-Test und keinen echten Lauf mit einem angemeldeten Codex-Konto.

## Technische Quellen

Stand der Integrationsprüfung: 27. September 2026. Für die installierte Version immer den zurückgegebenen Katalog und tatsächliche Fehlermeldungen beachten.

- [App Server / model discovery](https://developers.openai.com/codex/app-server)
- [CLI reference](https://developers.openai.com/codex/cli/reference)
- [Configuration reference](https://developers.openai.com/codex/config-reference)
- [Subagents](https://developers.openai.com/codex/subagents)
