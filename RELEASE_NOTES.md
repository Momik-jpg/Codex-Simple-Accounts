# v1.6.0 – lokaler Kandidat, noch nicht veröffentlichungsbereit

Der automatische Aufgabenrouter wurde um manuelle Live-Modellwahl, strengere Denkstufen-/Regelvalidierung, erneute Prüfung direkt vor dem CLI-Start und klarere Fallback-/Sicherheitsmeldungen erweitert. Klassifikation, Arbeitsumfang, Modellentscheidung und Schreibfreigabe bleiben getrennt. Die Windows-Oberfläche zeigt die gewählte Kombination und speichert nur sichere Voreinstellungen; Schreibzugriff startet jedes Mal ausgeschaltet.

Kontowechsel stellen bei fehlgeschlagenem Start die vorherige Anmeldung wieder her; Auto-Swap wartet bei laufendem Codex Desktop auf dessen Schliessen und nutzt keine veralteten Limitdaten. Der Installer enthält keine Python-Tests. Release-Version und Metadaten sind `1.6.0`.

Lokaler Buildkandidat, **nicht hochgeladen oder installiert**:

- Datei: `Codex-Konten-Installer.exe`
- Grösse: `34'535'920` Bytes
- SHA-256: `0F1EAAF473B200D67D87487D1105FB86B5415C7C54141D24A08F0CBE61449218`
- Automatische Prüfungen: 94 .NET-Tests, 72 Python-Tests, Syntax/JSON/XML/Version/Payload sowie Release-Build und Inno-Kompilierung bestanden. NuGet-Auditdaten waren aus der Sandbox nicht erreichbar (`NU1900`); der erforderliche Runtime-Restore gelang gezielt.
- Voraussetzungen für den Router: Windows, Python 3.10+, aktuelle angemeldete Codex-CLI, Auto-Swap aus. Modellangebote hängen vom aktiven Konto ab.

**Offen:** Windows-UI-Abnahme bei verschiedenen DPI-Werten, separates Testkonto/Wegwerfprojekt, Clean-Install, Upgrade von v1.5.5, Deinstallation/Neuinstallation, Review und Merge von [PR #7](https://github.com/Momik-jpg/Codex-Simple-Accounts/pull/7), Abschluss von Issue #6 und Upload zum bestehenden Release-Entwurf. Die PR-CI für den geprüften Code war grün. Details und Unsicherheiten: [Windows-Abnahme](docs/WINDOWS_ACCEPTANCE_V1_6.md). Dieser Kandidat darf nicht veröffentlicht werden.

# v1.5.5

Behebt den Startfehler am Ende der Installation.

## Behoben

- Das Setup startet `Codex-Konten.exe` jetzt direkt statt über den nicht vorhandenen Pfad `C:\Windows\System32\explorer.exe`.
- Der Abschluss der Installation zeigt dadurch keinen Fehlercode 2 mehr an.

## Enthalten

- Mehrere lokale Codex-Konten verwalten
- Reale Konto- und Wochenlimits anzeigen
- Manueller und automatischer Kontowechsel
- Vollständiger ChatGPT-Neustart beim Wechsel
- Dynamische Kontenanzahl und echte Profilnamen
- Mit Windows-DPAPI geschützte Kontodaten
- Autostart, Tray-Menü und Notfall-Schalter

## Sicherheit und Prüfung

- 52 automatische Tests bestanden
- Kein eigener WebSocket-Proxy und kein offener Port `47831`
- Keine dauerhafte Variable `CODEX_APP_SERVER_WS_URL`
- SHA-256 des Installers: `B37BA984CC360B5B98DD1C99B9AD465A96E471CAACF8DCDA6AE0FE7698B9EAFB`
