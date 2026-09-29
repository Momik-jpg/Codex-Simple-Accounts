# Auto-Aufgabe: Modell und Denkaufwand automatisch auswählen

Die Windows-App bietet im Kontenfenster den zusätzlichen Button **Neue Auto-Aufgabe**.
Der bisherige Kontowechsel, DPAPI-Speicher, `CodexBackend` und die Legacy-Bereinigung werden nicht verändert.
Der Router startet eine **neue Codex-CLI-Sitzung**. Er verändert keine laufenden Desktop-Chats und überwacht deren Prompts nicht.

## Wer entscheidet?

1. **Codex `model/list`** liefert den sichtbaren Modellkatalog der aktuellen CLI-Anmeldung. Er entscheidet nicht, welches Modell für die Aufgabe am besten ist.
2. **Ein kurzer Klassifikationsaufruf** bewertet ausschliesslich Auftrag und ausdrücklich beigefügte Bilder und schlägt 1–5 konkrete, überprüfbare Arbeitsschritte vor. Präferenz: ein verfügbares Sol-Modell aus `classifier_models` mit `low`. Fehlt es, werden `fast`, die übrigen `balanced`-Modelle und zuletzt `deep` geprüft. Das ist eine lokale Kostenpräferenz, keine verifizierte Preisrangliste. Der tatsächliche Klassifikator und jeder Fallback stehen in `intake.json`.
3. **Deterministischer lokaler Code** (`decide` in `router.py`) wendet die Gewichte, Schwellen und Qualitätsregeln an. Er wählt Hauptmodell, angebotene Denkstufe und 0–2 geeignete Nebenrollen. Das Modell kann diese Regeln oder die Schreibfreigabe nicht ändern.
4. **Der Starter** übergibt die Auswahl an einen neuen Codex-Prozess. Der Hauptagent entscheidet innerhalb der konfigurierten Obergrenze, ob/wann er die vorgesehenen Nebenrollen tatsächlich startet.
5. **Der Nutzer** bestimmt Auftrag, optionales Qualitätsprofil, Live-Modell und Denkaufwand, eigene Regeldatei, Agentenobergrenze und ausdrückliche Schreibfreigabe. `Automatisch` bleibt die empfohlene Voreinstellung. Subagenten sind in dieser Version nur lesend.

Die Bewertung ist eine Startheuristik, kein trainierter oder empirisch validierter Qualitätsmesser. Die Einstufung kann falsch sein. `XS` bis `XL` beschreibt Arbeitsumfang, keine Stunden-, Kosten- oder Tokenprognose. Eine Versionsnummer und lange Laufzeit sind kein Beweis für entsprechend viele gescheiterte Versuche.

## Voraussetzungen und Bedienung

- Bestehende .NET-/Windows-Voraussetzungen der App.
- Python **3.10+** als `py.exe`, `python.exe` oder `python3.exe` im PATH. Keine Python-Pakete nötig.
- Aktuelle, angemeldete Codex CLI mit den verwendeten Runtime-/Config-Schaltern. Ältere Clients sollen mit einem sichtbaren Fehler abbrechen, nicht still ohne Regeln starten.
- Mindestens ein für die Einstufung freigegebenes Live-Modell. Modellnamen in der JSON-Datei sind Präferenzen, keine Verfügbarkeitsgarantie. Beim hier geprüften Konto bot `model/list` nur `gpt-5.5` mit `low` bis `xhigh`; andere Konten können abweichen. Dieses Modell ist im Standardprofil `balanced`, nicht als ungeprüfter Ersatz für `deep` freigegeben.

Gewünschtes Konto zuerst im Kontowechsler aktivieren und **Auto-Swap vor dem Router-Lauf ausschalten**. Bei aktivem Auto-Swap oder laufendem Kontowechsel lässt die Oberfläche keinen Router-Lauf starten. Sobald der Router die Anmeldung reserviert hat, sperrt dieselbe atomare Laufwache manuelle Kontowechsel, Abmeldungen und automatische Rotation bis zum Prozessende. Die Tray-Oberfläche zeigt die Sperre an; verspätete oder konkurrierende Wechsel werden zusätzlich im Prozessmanager abgewiesen. Externe Programme und direkte Änderungen an `auth.json` kann die App nicht sperren.

Die Integration schreibt selbst weder `auth.json` noch Kontoeinstellungen; sie setzt `CODEX_HOME` für ihren Kindprozess auf das aktive Codex-Home des Kontowechslers. Eigene Provider-/API-Key-Konfigurationen der CLI bleiben zu beachten.

Die Auto-Aufgabe lässt sich im Kontenfenster und direkt im Tray-Menü öffnen. Im Dialog Projektordner, Auftrag und optional Referenzbilder auswählen. Der zuletzt erfolgreich validierte Projektordner sowie Qualitätsprofil, Modell, Denkaufwand und Agentenobergrenze werden für den nächsten Aufruf wieder eingesetzt; die Schreibfreigabe bleibt nach jedem Öffnen sicherheitshalber aus. Projekt, optionale Regeldatei und Referenzbilder werden bereits während der Eingabe geprüft; ungültige Pfade halten die Startaktionen deaktiviert. Die Agentenobergrenze darf 0, 1 oder 2 sein; sie erzwingt keine Delegation. Schreibzugriff ist standardmässig aus und wird bei Aktivierung deutlich hervorgehoben. Ein Umsetzungsauftrag startet ohne explizite Schreibfreigabe nicht.

**Analyse & Plan** zeigt Ziel, Einstufung, Modellentscheidung, einen vorläufigen KI-Arbeitsplan und noch ungeprüfte Abschlusskriterien, ohne den Projektauftrag auszuführen. Die Schritte beruhen nur auf dem Eingangstext: Es sind weder inspizierte Projektbefunde noch Freigaben. Der ausführende Agent muss sie nach Sichtung des Projekts bestätigen, ändern oder verwerfen. Danach wird der Hauptbutton zu **Entscheidung starten**: Die bereits erzeugte strukturierte Einschätzung wird wiederverwendet, während Modellkatalog, Regeln, Profil, Modell, Denkaufwand, Projekt, Bilder und aktive Anmeldung vor dem Start erneut geprüft werden. So benötigt Vorschau plus Start keinen zweiten Klassifikationsaufruf. Ändern sich Auftrag, Bilder oder aktive Anmeldung, verfällt die zwischengespeicherte Einschätzung automatisch. Bei geänderter Auswahl muss die Entscheidung erst erneut angezeigt werden. **Mit Codex starten** stuft ohne Vorschau ein und öffnet direkt danach eine interaktive Codex-Konsole. Rückfragen zu Aktionen werden dort beantwortet.

Während Einstufung oder Ausführung sind die Eingaben gesperrt, eine Laufanzeige nennt Zustand und Dauer, und ein mehrfacher Abbruch wird verhindert. **Abbrechen** beendet den von diesem Dialog gestarteten Prozessbaum, nicht beliebige ChatGPT-/Codex-Prozesse. **Schliessen** beendet nur den inaktiven Dialog; `Esc` bricht einen laufenden Router-Prozess ab und schliesst sonst das Fenster. `Strg+Enter` stuft nur ein, `Strg+Umschalt+Enter` startet automatisch. Vor und nach dem Lauf zeigt der Dialog die angeforderten Startparameter wie Projekt, Modus, Schreibfreigabe, Nebenrollen, Bilder und Regelquelle an; das ist keine Behauptung über bereits ausgeführte Arbeit. Bereits ausgeführte Änderungen werden nicht rückgängig gemacht. Das vorhandene **Notfall AUS** bleibt der Schalter für den Kontowechsel; für Router-Aufgaben den Abbrechen-Button beziehungsweise das Router-Terminal verwenden.

Anmelden, neu anmelden, Konto hinzufügen, abmelden, manuell wechseln und Auto-Swap benutzen dieselbe Aktivitätssperre. Solange eine Auto-Aufgabe oder Kontoaktion läuft, bleiben konkurrierende Kontofunktionen deaktiviert und werden zusätzlich in der Service-Schicht abgewiesen. Ein sichtbarer Aktivitätsstatus zeigt Anmeldung, Abmeldung, Kontowechsel oder Router-Sperre an; Buttons besitzen klare Hover-, Fokus-, Tastatur- und Deaktiviert-Zustände.

Die Buttons wechseln ihren Hover-Zustand sanft in rund 160 ms. Wenn Windows Animationen im Clientbereich deaktiviert, erfolgt der Zustandswechsel sofort; der Tastaturfokus bleibt sichtbar.

## Anpassbare Regeln

Standarddatei: `tools/task-router/routing_policy.json`, im installierten Programm unter `TaskRouter/`.
Für eigene Einstellungen eine Kopie ausserhalb von Program Files anlegen und im Dialog unter **Regeln** wählen. Nicht die globale Codex-Konfiguration überschreiben.

- `models`: geordnete Modell-IDs für `fast`, `balanced`, `deep`.
- `classifier_models`: optionale geordnete primäre Einstufungsmodelle aus `balanced`; danach gelten `fast`, übrige `balanced` und `deep`. Fehlt der Schlüssel in einer älteren eigenen Regeldatei, gilt deren vollständige `balanced`-Liste als primär.
- `weights`, `thresholds`: Gewichtung der sechs 0–3-Bewertungen.
- `max_subagents`: absolute Obergrenze, höchstens 2.
- `max_stagnant_attempts`: Arbeitsregel zur Neubewertung der Methode; kein externer Laufzeitwächter.

Ein benötigtes tiefes Profil wird nicht still auf ein Routineprofil heruntergestuft. Bildaufgaben benötigen eine ausdrücklich vom Live-Katalog gemeldete Bildmodalität; fehlende Angaben zählen nicht als Bildfreigabe. Manuell gewählte Modelle müssen einer Qualitätsstufe in der Regeldatei zugeordnet sein und dürfen die Sicherheitsuntergrenze nicht unterschreiten. Manuell gewählte, nicht unterstützte Denkstufen blockieren den Start mit einem Hinweis; automatische Ersatzstufen werden begründet. `max`/`ultra` sind kein automatischer Ersatz.

Im Dialog stehen vier Qualitätsprofile zur Verfügung: **Automatisch**, **Schnell**, **Ausgewogen** und **Gründlich**. Eine manuelle Wahl beeinflusst die lokale Regelentscheidung, kann aber die Sicherheitsgrenze für anspruchsvolle Rekonstruktionen, hohe Fehlerfolgen oder belegte methodische Stagnation nicht unterschreiten. Der Denkaufwand kann separat von **Minimal** bis **Ultra** gewählt werden. `Maximum` und `Ultra` werden ausschliesslich nach ausdrücklicher Auswahl angefordert und nur verwendet, wenn das live gewählte Modell die Stufe tatsächlich anbietet. **Auto zurücksetzen** stellt Profil und Denkaufwand auf automatisch, die Agentenobergrenze auf 2 und den Schreibzugriff auf aus.

## Protokolle und Datenschutz

GUI-Läufe: `%LOCALAPPDATA%/CodexAccountTray/RouterRuns/<Datum-ID>/`.
Darin liegen Auftrag, Modellkatalog, Einstufung, angewandte Regeln, Entscheidung und angeforderte Startparameter. `intake.json` enthält das tatsächliche Einstufungsmodell und gegebenenfalls gemeldeten Verbrauch. `error.log` hält Startfehler fest, auch wenn die Konsole schliesst.

Die Dateien können vertrauliche Auftrags- und Projektinformationen enthalten. Nicht ins Repository committen oder ungeprüft teilen. Der Router liest keine anderen Unterhaltungen. Der Einstufungsauftrag und ausdrücklich angehängte Bilder gehen über die bestehende Codex-Anmeldung an deren Anbieter; das ist kein rein lokaler Klassifikator. Diese Aufrufe verbrauchen Kontingent beziehungsweise API-Nutzung entsprechend der CLI-Konfiguration.

`plan.json`, `prelaunch_check.json` und `requested_launch.json` belegen Entscheidungen, erneute Prüfung und angeforderte Parameter, nicht tatsächlich gestartete Subagenten oder bestandene fachliche Tests. `exit.json` nennt Exit-Code und ob `auth.json` während des Laufs geändert wurde; auch eine Token-Erneuerung kann dafür verantwortlich sein. Ein Exit-Code 0 ist kein Qualitätsnachweis.

`plan_steps` in `plan.json` sind Vorschläge aus der Eingangsprüfung. Die JSON-Prüfung begrenzt Länge, Anzahl und Steuerzeichen. Ein Plantext darf keine Berechtigungen oder Sicherheitsregeln verändern.

## Grenzen

Kein automatischer Modellwechsel mitten im laufenden Turn, keine Hintergrundüberwachung, keine automatische Kontorotation für diese CLI-Aufträge, kein Tokenbudget-Wächter, keine Leistungs- oder Spargarantie. Ein Modellkatalog belegt angebotene Optionen, nicht ausreichendes Restkontingent für den gesamten Auftrag.

## Prüfung

```powershell
dotnet test .\CodexAccountTray.sln -c Release
python -m unittest discover -s tools/task-router/tests -v
python tools/task-router/router.py demo umr
```

Die Python-Tests verwenden synthetische Kataloge und einen lokalen Fake-Codex, keine bezahlten Modellaufrufe. Die .NET-Tests prüfen Eingaben, Argumenttrennung, Schreibfreigabe, `CODEX_HOME`, Ergebnisdarstellung und die gegenseitige Sperre von Router-Lauf und Kontowechsel. CI prüft zusätzlich die Router-Dateien im Publish-Ausgabeverzeichnis. Ein erfolgreicher CI-Lauf ersetzt keinen visuellen Windows-Test und keinen echten Lauf mit einem angemeldeten Codex-Konto.

## Technische Quellen

Stand der Integrationsprüfung: 27. September 2026. Für die installierte Version immer den zurückgegebenen Katalog und tatsächliche Fehlermeldungen beachten.

- [App Server / model discovery](https://developers.openai.com/codex/app-server)
- [CLI reference](https://developers.openai.com/codex/cli/reference)
- [Configuration reference](https://developers.openai.com/codex/config-reference)
- [Subagents](https://developers.openai.com/codex/subagents)
- [Strukturierte Ausgaben und unterstützte JSON-Schema-Felder](https://developers.openai.com/api/docs/guides/structured-outputs)
- [Windows-Einstellung für Clientbereich-Animationen](https://learn.microsoft.com/en-us/windows/win32/winauto/client-area-animation)
