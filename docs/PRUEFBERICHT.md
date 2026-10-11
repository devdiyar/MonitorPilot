# MonitorPilot — Prüfbericht

Stand: 11.10.2026. Zielversion: **1.3.0**. Prüfplatz: Windows 11 x64, fünf externe Monitore, Intel UHD 770 und NVIDIA RTX 4070 SUPER. Keine Zertifizierung für jede Windows-/Monitorkombination.

## Aktuelle Prüfungen

| Prüfung | Nachweis |
| --- | --- |
| Release | Selbständige Windows-x64-EXE erfolgreich erstellt. |
| Selbsttests | **47 bestanden**: native Strukturgrößen, Snapshot-Rundreise, Fokusauswahl, Prozentabbildung, Adapter-Zugriff, Cache, begrenzte Wiederholungen, Live-Queue, Zeitplan, Profilvalidierung, beschädigte Dateien und Guardian-Startkommando. |
| WPF-Integration | **14 bestanden**: echte App-/Tray-Regler gegen simulierte Hardware, Live-Übernahme, keine Bildregler-Übernahmebuttons, App-/Tray-Abgleich, gemeinsamer Regler, RGB nach Weißpunkt, Dimmfenster-Wiederverwendung, Drain vor Arbeitsplatzaktionen, schmale Seiten/Fenster. |
| Datenerhalt beim lokalen Update | Einstellungen, Profile, originale Hardwarewerte, letzter Arbeitsplatz und Fenstersicherung per Hash unverändert. |
| Paketgleichheit | Setup-, portable und installierte EXE per SHA-256 identisch beim lokalen Update. CI erstellt eigene Pakete. |
| Laufende App | Version 1.3.0 und Live-Hinweis in der tatsächlich gestarteten Installation bestätigt. |

Hardwareunabhängige Ergebnisse unter [validation](validation) enthalten Testnamen/Resultate, keine privaten Profile, Fensterlisten oder Monitorpfade. CI führt dieselben sicheren Tests auf Windows aus; ein Runner bestätigt keine physische DDC-Unterstützung.

## Hardware und Leistung

Am Prüfplatz wurden Helligkeit und Rot-Gain aller fünf Displays geändert, zurückgelesen und exakt zurückgestellt: **10 von 10 Kontrollen bestanden**. Der Live-Stresstest bestätigte die letzten Zielwerte aller fünf Displays, ließ Kontrast/RGB unverändert und stellte die vollständigen Ausgangswerte wieder her. Ein Lauf brauchte 7 Transaktionen und 1,33 Sekunden; ein früherer Lauf 2,33 Sekunden und verfehlte das 2-Sekunden-Budget. [Performance](PERFORMANCE.md) nennt beide Werte und Messgrenzen.

Geräte: AOC Q32G2WG3, 27G15N, 27G1G4, LG W2243 und Dell P2419H. Dies bestätigt keine anderen Firmware-/Bildmodusvarianten.

## Frühere Nachweise

Die ursprüngliche Version bestand eine physische Fokus-Rückkehr: ein aktiver Monitor im Fokus, danach fünf, Anordnung und Hz exakt zurück. **9 von 9** damaligen Fensterpositionen kamen zurück. Ohne Bestätigung stellte der Guardian nach rund 34,6 Sekunden einschließlich Rückkehrzeit wieder her. Diese Umschalttests sind frühere Nachweise und wurden nicht für jede spätere UI-Änderung erneut ausgeführt.

Frühere Theme-Prüfungen öffneten Auflösungslisten in Hell/Dunkel sowie Tray-Menü und Einstellungen. Geprüfte Text-/Button-Farbpaare lagen über 4,5:1; kein vollständiges Barrierefreiheitsaudit. Aktuelle Bilder zeigen echte WPF-Komponenten mit anonymen Daten.

## Offen / nicht verifiziert

- Physische Windows-10-Prüfung und weitere GPUs, Firmware, Docks/DisplayLink, TVs, Notebookpanels, ARM64.
- Langzeitbetrieb, alle Hotplug-/DPI-/HDR-Kombinationen, Clone-/Remote-Modi, umfassende Barrierefreiheit.
- Gemessene CPU-/GPU-/Energieersparnis und Colorimeter-Kalibrierung.
- Codesignatur und automatischer Updatekanal.

Unverfügbare DDC-Regler sind deaktiviert, fehlgeschlagene Rücklesewerte werden gemeldet und defekte Startdateien vor dem Ersetzen gesichert. [Kompatibilität](COMPATIBILITY.md) beschreibt Grenzen. Raw-QA mit lokalen Identitäten/Fenstern bleibt außerhalb des öffentlichen Repositories.
