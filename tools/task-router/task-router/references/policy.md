# Startheuristik v1 — nicht empirisch kalibriert

Sechs Bewertungen von 0 (irrelevant/einfach) bis 3 (stark ausgeprägt):
Schlussfolgerung R, Unklarheit U, Kopplung K, visuell-räumliche Schwierigkeit V,
Fehlerfolgen F, Schwierigkeit der Prüfung P.

`Score = 2R + U + K + 2V + F + P` (0–24).

0–5: Routineprofil (Luna-Präferenz), niedriger Denkaufwand.
6–13: Standardprofil (Sol-Präferenz), medium; high bei mehrstufiger Logik.
14–24: tiefes Profil (Astra-Präferenz), high.
Für inverse visuelle Rekonstruktion bzw. Score ab 19: xhigh, sofern bestätigt unterstützt.
Kein automatisches max/ultra. Verwende echte katalogbestätigte Modell-IDs und Denkstufen.

Qualitätsregeln vor der Summe:
- Inverse räumliche Rekonstruktion mit Referenztreue: tiefes Profil.
- R=3 und U>=2: tiefes Profil.
- F=3 und P>=2: tiefes Profil; trotzdem erforderliche fachliche/menschliche Prüfung.
- Mindestens zwei belegte Fehlversuche wegen Schlussfolgerung/Methode: tiefes Profil
  und Methodendiagnose; nicht endlos dieselbe Schleife hochskalieren.
- Unsichere Einstufung oder F>=2: mindestens Standardprofil.
- Reine Ressourcenprobleme erhöhen die Modellklasse nicht von sich aus.

Arbeitsumfang separat: XS direkte Antwort; S wenige bekannte Schritte; M mehrere
zusammenhängende Schritte; L mehrere Komponenten/Prüfzyklen; XL umfangreiche gekoppelte
Arbeit. Daraus werden keine erfundenen Minuten, Franken oder Tokenmengen berechnet.

Parallelität ist unabhängig von Schwierigkeit: standardmässig 0 Nebenagenten. Nur
unabhängige, nicht schreibende Aufgaben mit erkennbarem Nutzen; maximal 2 gleichzeitig.
Für XS/S: 0. Für eine gemeinsame Szene: maximal ein Prüfer nach einem Checkpoint.
Eine schwierige Einzelaufgabe kann tiefes Modell + 0 Nebenagenten benötigen.
Viele einfache unabhängige Extraktionen können leichtes Modell + 2 Nebenagenten benötigen.

Modellnamen und Gewichtungen sind konfigurierbare Präferenzen, keine Naturgesetze.
Der Python-Starter liest routing_policy.json. Dieser Skill beschreibt dessen
mitgelieferte Standardwerte; nach manuellen Policy-Änderungen diese Referenz angleichen.

Technische Grundlagen, geprüft am 27.09.2026:
- https://developers.openai.com/codex/app-server — model/list und Denkstufen je Konto/Client.
- https://developers.openai.com/codex/subagents — Delegation, eigene Modelle, zusätzlicher Aufwand.
- https://developers.openai.com/codex/config-reference — Laufkonfiguration und Agentenlimits.
- https://developers.openai.com/codex/skills — lokale Skills und expliziter/impliziter Aufruf.

Die Quellen belegen die Mechanismen. Sie validieren nicht die obige Punkteskala.
