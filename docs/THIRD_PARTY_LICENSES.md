# Lizenz- und Drittanbieterhinweise

Codex Simple Accounts ist ein unabhängiges Projekt von Andrin Maag unter der
MIT-Lizenz. Der vollständige Text liegt im Repository als `LICENSE` und nach
der Installation als `LICENSE.txt` im Programmordner.

Der selbständige Windows-Build bündelt die .NET-Laufzeit und WindowsDesktop.
Beim Release-Build werden die zu den tatsächlich verwendeten Runtime-Paketen
gehörenden Originaltexte unverändert in den Installer übernommen:

- `Licenses/dotnet-runtime-LICENSE.txt` – .NET Runtime, MIT-Lizenz.
- `Licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt` – Drittanbieterhinweise der .NET Runtime.
- `Licenses/windowsdesktop-runtime-LICENSE.txt` – WindowsDesktop Runtime, MIT-Lizenz.

Die konkreten Paketversionen stehen in der Publish-Metadatei; die Paketierung
bricht ab, wenn ein erforderlicher Text fehlt. Die [offiziellen .NET-Quellen](https://github.com/dotnet/runtime)
enthalten die zugehörigen Lizenz- und Hinweistexte.

Der Installer wird mit [Inno Setup](https://jrsoftware.org/files/is/license.txt)
gebaut. Der Compiler wird nicht mit der App verteilt. Wer Inno Setup in einem
kommerziellen Kontext einsetzt, muss dessen [Lizenzhinweise zum kommerziellen Einsatz](https://jrsoftware.org/isorder.php)
prüfen. Python, Codex CLI und Codex Desktop werden nicht mitgeliefert und
unterliegen den jeweiligen Bedingungen ihrer Anbieter. Testpakete wie xUnit
und coverlet sind nicht Teil des Installers.

Die Erwähnung von Codex beschreibt die Kompatibilität; dieses Projekt ist
nicht von OpenAI erstellt, unterstützt oder zertifiziert. Die Nutzung der
OpenAI-Dienste bleibt an deren [Nutzungsbedingungen für die Schweiz](https://openai.com/policies/eu-terms-of-use/)
und [Markenrichtlinien](https://openai.com/brand/) gebunden. Diese Datei ist
eine technische Inventarliste, keine rechtliche Freigabe.
