# Weitere Ausbaustufen

Die folgende Liste beschreibt mögliche Erweiterungen; sie sind in Version 1.3 nicht bereits implementiert. Für die aktuelle Version stehen Live-Regler, Tray-Steuerung, Profile und Rückkehrschutz bereits zur Verfügung.

## Besonders nützlich für fünf Monitore

1. **Programmabhängige Profile:** Ein Spiel aktiviert Hauptmonitor + passenden Hz-Modus, ein Schnittprogramm lädt den kompletten Arbeitsplatz.
2. **Fensterzonen pro Monitor:** Eigene Raster, feste Apps je Zone, Tastenkürzel zum Verschieben und Tauschen.
3. **Profile mit einem eigenen Tastenkürzel:** Beispielsweise Schnitt, Lernen, Gaming, Musik und Präsentation direkt aufrufen.
4. **Zeitlich begrenzter Fokus:** Nach 15, 30 oder 60 Minuten automatisch zum gesicherten Arbeitsplatz zurückkehren.
5. **Monitorgruppen:** Etwa zwei Displays für Programmieren, drei für Produktion, alle für Recherche; gemeinsame Regler je Gruppe.
6. **Drag-and-drop-Anordnung:** Monitorrechtecke verschieben, Kanten automatisch einrasten und Wege zwischen unterschiedlich großen Displays verbessern.

## Farbe und Komfort

7. **Geführter visueller Abgleich:** Referenzmonitor auswählen, Weiß/Grau/Schwarz nebeneinander beurteilen und pro Monitor schrittweise anpassen.
8. **ICC-Profilzuordnung je Arbeitsprofil:** Bestehende Farbprofile gezielt zuordnen, vorherigen Zustand sichern und Windows-Farbverwaltung berücksichtigen.
9. **Messgeräte-Integration:** Colorimeter-Messwerte, Delta-E-Vergleich und Kalibrierungsverlauf. Erfordert echte unterstützte Messhardware.
10. **Umgebungslichtregelung:** Helligkeit über einen Lichtsensor anpassen, mit getrennten Kurven pro Monitor.
11. **Weiche Helligkeitsübergänge:** Zeitplanänderungen über mehrere Minuten statt sofort ausführen.
12. **Referenzhelligkeit in cd/m²:** Gemessene Helligkeitskurven statt identischer Prozentwerte verwenden.
13. **Zusätzliche Testbilder:** Gamma, Banding, Schärfe, Pixelfehler und Schwarz-Clipping mit erklärten Schritten.

## Leistung und Automatik

14. **Reale Vorher-/Nachher-Messung:** GPU-Leistung, Displaytakte und Last vor und nach Fokusmodus protokollieren; Hardwarezugriff/Herstellerunterstützung klären.
15. **Eco-Profil:** Reduzierte Bildwiederholrate, bestimmte Monitore aus, passende Helligkeit, danach komplette Rückkehr.
16. **Dock-/Kabel-Ereignisse:** Bekannte Monitorgruppe beim Anschließen erkennen und das passende Profil anbieten.
17. **Leerlaufregeln pro Monitor:** Ungenutzte Zusatzbildschirme nach Inaktivität abdunkeln oder deaktivieren, mit zuverlässigem Wiederaufwecken.
18. **Akkubetrieb-/Netzbetrieb-Profile:** Für den späteren Einsatz am Notebook.

## Erweiterte Bedienung

19. **Stream-Deck-Unterstützung:** Monitorprofile, Helligkeit und Fokusstatus auf physische Tasten legen.
20. **Monitor-Eingänge umschalten:** HDMI/DisplayPort über DDC/CI mit gerätespezifischer Freigabe und Rückkehrplan.
21. **Monitorlautstärke:** Lautsprecherpegel und Mute, soweit per DDC/CI tatsächlich angeboten.
22. **Mehrere Wiederherstellungspunkte:** Verlauf mit lesbarer Vorschau und Vergleich vor dem Zurücksetzen.
23. **Profil duplizieren und gezielt bearbeiten:** Einzelwerte im Profil ändern, ohne erst den kompletten realen Arbeitsplatz umzuschalten.
24. **App-Neustart nach Windows-Neustart:** Gewählte Programme neu öffnen und Fenster anhand gespeicherter Regeln zuordnen; Dokumentwiederherstellung nur nach Unterstützung der jeweiligen App.
25. **Signierte Updates:** Codesignatur und ein Updatekanal mit Integritätsprüfung und einfacher Rückkehr. Das öffentliche Repository und automatisierte Release-Pakete sind bereits eingerichtet.
