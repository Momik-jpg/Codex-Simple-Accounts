# Prüfbericht

- Version: 1.5.4
- Build und automatische Tests: 51 von 51 bestanden
- Installer: erfolgreich erstellt und installiert
- Installierte EXE: SHA-256 identisch mit dem geprüften Publish-Build
- Windows `Installierte Apps`: `Codex-Konten`, Version `1.5.4`, Herausgeber `Andrin`
- Deinstallation: Eintrag und `unins000.exe` vorhanden
- Autostart: Benutzer-Eintrag mit `--background` vorhanden
- Symbol: gemeinsames blaues `C` für EXE, Fenster, Installer und Windows-App-Eintrag
- Oberfläche: echtes Desktop-Fenster geprüft, keine abgeschnittenen Bedienelemente
- Design: dunkle abgerundete Kontokarten, blaue aktive Schaltflächen, klare deaktivierte Zustände
- Zugangsdaten: lokal mit Windows-DPAPI geschützt
- Sicherheitsprüfung: Gitleaks fand in `src`, `installer`, `scripts` und `tools` keine Secrets
- Start: öffnet die grafische ChatGPT-App, nicht das Terminal
- Erster Kontowechsel: fordert ChatGPT normal zum Schliessen auf, aktiviert das Konto und startet ChatGPT neu
- Vollständiger Neustart: verbleibende ChatGPT-Hintergrundprozesse werden nach drei Sekunden einzeln beendet
- Weitere Kontowechsel: beenden ChatGPT vollständig und starten die Windows-App normal neu
- Auto-Swap: 5-Stunden-Limit bei 1 % frei, Wochenlimit bei 0 % frei
- Zielkonto: Konten mit 0 % Wochenlimit werden übersprungen
- Limitabfrage: geschützte Probe-Caches werden wiederverwendet; temporäre `auth.json` wird nach jeder Abfrage entfernt
- Echte Limitabfrage: alle vier gespeicherten Konten erfolgreich aktualisiert
- Kontonamen: echte lokale ChatGPT-Profilnamen werden angezeigt
- Kontenanzahl: dynamisch, weitere Konten können über `Konto hinzufügen` ergänzt werden
- Aktives Konto: wird über die Konto-ID aus `.codex/auth.json` erkannt und blau markiert
- Prüfintervall: automatische Limitprüfung alle 30 Sekunden
- Kontowechsel: setzt die ausgewählte Anmeldung atomar ein und bestätigt sie erst nach erfolgreichem Neustart
- Prozesswechsel: schliesst das ChatGPT-Fenster, beendet danach verbleibende ChatGPT-Hintergrundprozesse und startet mit dem gewählten Konto neu
- Benutzerpfade: werden über die Windows-SID bestimmt und nicht aus geerbten Sandbox-Umgebungsvariablen übernommen
- Oberfläche: Projektordner entfernt; die App ist ausschliesslich für Kontoverwaltung und Kontowechsel
- Kartenlayout: alle Schaltflächen bleiben mit festem rechten Abstand vollständig innerhalb der Karte
- Prozessschutz: beendet alle `ChatGPT.exe`-Instanzen einzeln, aber nicht deren vollständigen Prozessbaum; Codex-Konten wird beim Wechsel nicht mehr mitbeendet
- Installation: genau ein Windows-App-Eintrag, ein Programmordner und ein Autostartwert vorhanden
- Notfall AUS: bricht einen laufenden Wechsel ab und deaktiviert Auto-Swap; gespeicherte Konten bleiben erhalten
- App-Server: kein eigener Proxy, kein Port `47831` und keine gesetzte `CODEX_APP_SERVER_WS_URL`
- Fehlerbehebung: `mcp_servers.codex_app: invalid transport` kann durch Codex-Konten nicht mehr erzeugt werden
- Bestehende Chats und Angeheftet: bleiben im gemeinsamen Codex-Benutzerordner

Nicht live ausgeführt: echter Kontowechsel während dieser laufenden Codex-Aufgabe. Der Ablauf ist automatisiert getestet.
