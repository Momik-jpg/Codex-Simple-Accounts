---
name: task-router
description: >-
  Rate a new substantial task or a stalled task and select an available model,
  reasoning effort and zero to two non-writing subagents. Use when the user asks
  for automatic task routing, resource selection or effort control, including
  reference-based Minecraft/Blender modeling. Do not add routing overhead to
  greetings or repeat routing when a launcher already supplied a valid plan.
---

# Task Router

## Aufgabe und Grenze
Ordne benötigte Fähigkeiten zu, nicht einfach Textlänge oder Laufzeit. Qualität hat
Vorrang; wähle die einfachste plausible Konfiguration, die die Abschlusskriterien
verlässlich erfüllen kann. Deine Bewertung ist eine überprüfbare Startheuristik,
kein wissenschaftlicher Leistungsscore und keine Stunden-/Tokenprognose.

Eine im Auftrag enthaltene externe Startkonfiguration gilt zunächst. Starte dafür
keinen weiteren Routerlauf. Sonst lies `references/policy.md` und bewerte den Auftrag
kurz. Bei einer trivialen Anfrage antworte direkt statt einen Routingbericht zu erzeugen.

## Verfügbarkeit vor Modellnamen
Verwende nur vom Client bestätigte Modell-IDs, Denkstufen und Agentenwerkzeuge.
Wenn `model/list` angeboten wird, nutze die echten Werte. Ohne Modellsteuerung bleibt
die Einstufung eine Empfehlung. Ein Rollenwechsel im Text ist kein Modellwechsel.
Ändere nicht eigenmächtig globale Einstellungen, Berechtigungen oder den Codex-Prozess.

Ein externer Launcher kann eine neue Sitzung mit ausgewähltem Modell starten. Er
verändert keine bereits laufende ChatGPT-/Codex-Sitzung. Zeige den Unterschied zwischen
angeforderter Konfiguration, bestätigtem Lauf und fachlich geprüftem Ergebnis.

## Bevor die eigentliche Arbeit beginnt
Prüfe die relevantesten Dateien, Referenzen, Werkzeugzugriffe und Abschlusskriterien.
Übernimm keine Behauptung der Eingangsprüfung als bereits untersuchten Projektbefund.
Fehlende Inputs/Berechtigungen, Renderwartezeit und fehlendes Blender sind keine
Beweise für mangelnde Modellleistung. Beschaffe erlaubte fehlende Inputs oder benenne
die konkrete Blockade. Verwendbare unabhängige Arbeit darf weitergehen.

Fasse die Zuordnung in einer Zeile zusammen:
`Routing: [Modell/Denkstufe tatsächlich oder empfohlen] | Aufwand [XS–XL] | [0–2] Subagenten`.
Erkläre nur wesentliche Unsicherheit oder Abweichungen. Dann führe den Auftrag aus.

## Delegation
Standard: keine Subagenten. Nutze höchstens die im Routingplan freigegebene Zahl.
Ein Subagent benötigt eine unabhängige Frage, klaren Zusatznutzen, konkrete Inputs,
überprüfbare Rückgabe und ein bestätigtes Modell samt Denkstufe. Keine doppelte
Gesamtrecherche und keine simulierten Expertenrollen als echte Agenten ausgeben.

Die erste Paketversion erlaubt nur nicht schreibende Nebenrollen. Es gibt genau
einen schreibenden Hauptagenten. Verwende die vom Launcher angelegten Rollen mit
dem Präfix `router_`, sofern vorhanden. Spawn diese Rollen statt allgemeiner Arbeiter,
damit Modell, Denkstufe, Read-only-Sandbox und das Verbot weiterer Delegation gelten.
Fehlt ein vorgesehenes Werkzeug, bearbeite die Nebenaufgabe selbst und melde die Abweichung.
Keine verschachtelte Delegation, keine neuen Codex-/Router-Prozesse in Subagenten.

`after_checkpoint` bedeutet: erst einen gespeicherten stabilen Stand herstellen,
dann den Prüfer starten. In einer gemeinsamen Blender-Szene keine parallelen Schreiber.
Ein Prüfer darf nicht unbemerkt einen beweglichen Zwischenstand bewerten. Der Hauptagent
prüft wesentliche Befunde anhand von Quellen/Tests und integriert die Ergebnisse.

## Fortschritt und Stagnation
Definiere einen sichtbaren oder testbaren Erfolg je Arbeitsschritt und erhalte den
besten bekannten Stand. Nach jeweils einem sinnvollen Änderungspaket: betroffene
Prüfung, Vergleich mit dem letzten stabilen Stand und Regressionen kontrollieren.

Nach drei belegten erfolglosen Versuchen am selben Problem ändere die Diagnose/Methode.
Nicht bloss erneut generieren oder die Versionsnummer erhöhen. Unterscheide:
- Input/Werkzeug/Berechtigung/Compute: tatsächlichen Engpass beheben.
- Fehlerhafte Prüfmethode/Kamera/Repräsentation: diese zuerst korrigieren.
- Unzureichende Schlussfolgerung: höheres Fähigkeitsprofil für den nächsten Lauf empfehlen.

Keine pauschale Max-/Ultra-Einstellung und kein unendlicher Verbesserungsloop.
Eine höhere Denkstufe macht aus einem kleineren Modell nicht ein anderes Modell.
Diese Fortschrittsregel ist eine Arbeitsanweisung, kein externer Zeit-/Tokenwächter.
Ist ein Hauptmodellwechsel nötig, sichere den Stand und liefere einen konkreten
Übergabestand. Ohne echte Umschaltfunktion nicht behaupten, bereits gewechselt zu haben.

## Speziell bei Referenzbild → UMR-/Blender-Modell
Die räumliche Rekonstruktion und strenge Referenztreue gehören ins tiefe Profil.
Ein blosses Dateiinventar, ein fertiger Exportbefehl oder das Sammeln von Logs nicht.
Laufzeit und Versionsnummer belegen allein weder die Zahl der Fehlversuche noch deren Ursache.

Nutze die tatsächliche Referenz und gespeicherte Renders, nicht nur Bildschirmfotos.
Halte die Vergleichskamera stabil. Prüfe Geometrie und Textur getrennt, Front/Seite
und weitere relevante Ansichten auf Regressionen. Kennzeichne nicht sichtbare
Geometrie als ergänzte Annahme statt überprüfte 1:1-Rekonstruktion. Eine Exportprüfung
ist kein Beweis visueller Ähnlichkeit. Rig/Animationen bei betroffenen Änderungen prüfen.

## Abschluss und Freigaben
Ein Analyseauftrag erlaubt keine unbeauftragte Umsetzung. Vor Löschen, Senden,
Veröffentlichen, Käufen, Merges und irreversiblen Eingriffen konkrete Freigabe einholen,
sofern sie nicht bereits vorliegt. Bestehende übergeordnete Regeln bleiben erhalten.

Liefere Ergebnis, tatsächlich ausgeführte Prüfungen und verbleibende Einschränkungen.
Kein Erfolg allein aufgrund eines Exit-Codes, Selbsturteils oder einer Agentenmehrheit.
