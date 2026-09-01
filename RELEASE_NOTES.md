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
