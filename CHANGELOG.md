# Változásnapló

Formátum: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), verziózás: [SemVer](https://semver.org/).

## [1.0.0] - 2026-10-19

### Added
- Első nyilvános portfólió-kiadás.
- Tartalom alapú átnevezés: heurisztika (PDF legnagyobb betűs sora, DOCX első bekezdése, XLSX első cellája), helyi Ollama LLM és zárt listás típus-osztályozás, szigorú címvalidátorral; az eredeti név zárójelben megmarad.
- Előnézet mód alapból, write-ahead CSV-napló, visszavonás a legutóbbi vagy minden futásra.
- Egyfájlos gyors mód fogd és vidd módszerrel, Enterrel jóváhagyva.
- Mappa-index: egységenként DOCX-kártya, Markdown-fájl vagy egyetlen CSV-táblázat, LLM-mel megállapított típussal, fájlonkénti leírással, opcionális webes hivatkozással és folytatható gyorsítótárral.
- Szövegkinyerés PDF, DOCX/DOCM, DOC (Word COM, csak olvasás), XLSX/XLSM/XLS, TXT/CSV és RTF fájlokból.
- Hibatűrő kötegfeldolgozás, Ollama-ellenőrzés induláskor, időtúllépés-korlát.
- Hardverpanel (CPU, GPU, Ollama-állapot), sötét téma, `psz // v1.0.0` névjegy.
- xUnit-tesztek, köztük képernyőn kívüli GUI-tesztek, és a kitalált demó-archívum generátora.
