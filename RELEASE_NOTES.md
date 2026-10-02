# v1.6.0 – lokaler Kandidat, noch nicht veröffentlichungsbereit

Der automatische Aufgabenrouter wurde um manuelle Live-Modellwahl, strengere Denkstufen-/Regelvalidierung, erneute Prüfung direkt vor dem CLI-Start und klarere Fallback-/Sicherheitsmeldungen erweitert. Die Analyse zeigt nun einen auftragsbezogenen, vorläufigen KI-Arbeitsplan mit Prüfschritten und noch ungeprüften Abschlusskriterien. Der ausführende Agent muss ihn nach Projektinspektion anpassen. Klassifikation, Arbeitsumfang, Modellentscheidung und Schreibfreigabe bleiben getrennt. Die Windows-Oberfläche zeigt die gewählte Kombination, bietet kurze Hover-Übergänge unter Beachtung der Windows-Animationseinstellung und speichert nur sichere Voreinstellungen; Schreibzugriff startet jedes Mal ausgeschaltet.

Kontowechsel stellen bei fehlgeschlagenem Start die vorherige Anmeldung wieder her; Auto-Swap wartet bei laufendem Codex Desktop auf dessen Schliessen und nutzt keine veralteten Limitdaten. Der Installer enthält keine Python-Tests. Release-Version und Metadaten sind `1.6.0`.

Zusätzliche Kontosicherung: Vor Wechsel und Abmeldung wird die aktive Anmeldung erneut mit `auth.json` abgeglichen. Wechselt sie ausserhalb der App, schützen erneute Identitätsprüfungen vor dem Löschen einer fremden Anmeldung und vor dem Überschreiben des falschen Kontospeichers. Auch die Limitabfrage speichert erneuerte Tokens nur bei nachgewiesener gleicher Kontoidentität; andernfalls gilt die Abfrage als fehlgeschlagen. Externe Änderungen im exakt gleichen Moment lassen sich ohne Kontrolle über Codex Desktop nicht vollständig ausschliessen.

Lokaler Buildkandidat, **nicht hochgeladen oder installiert**:

- Datei: `Codex-Konten-Installer.exe`
- Grösse: `34'544'125` Bytes
- SHA-256: `DB7AE0111A43B58A9E4624D035F1DC8E9F03A27F07AC53DE8BAB3EF6D5D100CC`
- Automatische Prüfungen: 102 .NET-Tests, 75 Python-Tests, `dotnet format`, Release-Build und Inno-Kompilierung bestanden. NuGet-Auditdaten waren aus der Sandbox nicht erreichbar (`NU1900`); ein früherer gezielter Audit fand keine gemeldeten Schwachstellen.
- Voraussetzungen für den Router: Windows, Python 3.10+, aktuelle angemeldete Codex-CLI, Auto-Swap aus. Modellangebote hängen vom aktiven Konto ab.
- Bekannte Einschränkung: Der lokale Installer ist nicht code-signiert. Windows kann deshalb eine Herausgeber-/SmartScreen-Warnung anzeigen; eine solche Warnung darf im Abnahmetest nicht automatisiert umgangen werden.
- Lizenzhinweise: Der Installer zeigt die Projekt-MIT-Lizenz und installiert die Original-Lizenztexte der gebündelten .NET-Laufzeit samt Drittanbieterhinweisen. Details: [Lizenz- und Drittanbieterhinweise](docs/THIRD_PARTY_LICENSES.md).
- Richtlinienprüfung offen: Der Projektinhaber möchte Auto-Swap zwischen eigenen, separat bezahlten Konten beibehalten. Die [OpenAI-Hilfe](https://help.openai.com/en/articles/20001068-use-multiple-accounts-with-account-switching) beschreibt den manuellen Kontowechsel im Web, aber keine Freigabe für einen automatischen limitabhängigen Wechsel in Codex Desktop. Die [Nutzungsbedingungen für die Schweiz](https://openai.com/policies/eu-terms-of-use/) untersagen die Umgehung von Rate-Limits. Eine verbindliche Einordnung dieses konkreten Falls liegt nicht vor; der Release wird nicht als rechtlich freigegeben bezeichnet. Auch kommerzielle Nutzung des lokalen Inno-Setup-Compilers und Rechte am App-Icon sind nicht nachgewiesen.

**Offen:** Windows-UI-Abnahme bei verschiedenen DPI-Werten einschliesslich Animation aus, separates Testkonto/Wegwerfprojekt, Clean-Install, Upgrade von v1.5.5, Deinstallation/Neuinstallation, Richtlinienklärung, Prüfung der CI für den endgültigen Code, Review und Merge von [PR #7](https://github.com/Momik-jpg/Codex-Simple-Accounts/pull/7), Abschluss von Issue #6 und Upload zum bestehenden Release-Entwurf. Details und Unsicherheiten: [Windows-Abnahme](docs/WINDOWS_ACCEPTANCE_V1_6.md). Dieser Kandidat darf nicht veröffentlicht werden.

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
