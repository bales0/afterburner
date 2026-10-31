# Afterburner – kompletní zdrojový balík s opravami (31. 10. 2026)

## Obsah

Celý zdrojový strom původního ZIP `afterburner-master.zip`, se zachovanou strukturou projektu, včetně binárních podkladů, návrhů PCB, XSVF, původního C CLI, Arduino firmware a Afterburner Studia. Nejde o patch ani opravný skript.

## Konkrétní změněné soubory

- `src/afterburner.ino`: FW 0.6.4; potvrzení checksumu před povolením zápisu; vynulování stavu při novém uploadu a jeho chybě; kontrola rozsahu fuse adres; ochrana po odmítnutí sparse mapy; korektní reset sparse indexu při `#t`; zakončení uploadových příkazů; blokování programovacích/mazacích operací při chybějící kalibraci proměnného VPP (pevné VPP se nemění); hlášení `VPP-CAL-OK` a `VPP-CAL-MISSING`; typy indexů parseru.
- `aftb_sparse.h`: ochrana před zápisem za 1332B buffer na Uno; pokus o kompakci před odmítnutím, invalidace cache po vložení, rozpoznání implicitních skupin samých jedniček.
- `src_pc/afterburner.c`: ošetření chybného/krátkého sériového banneru bez nedefinované aritmetiky ukazatelů; omezení velikosti vstupního souboru a korektní ukončení NUL; kontrola rozsahů JEDEC L/QF, odmítnutí chybného checksumu, hlášení syntax errors; ukončení portu na chybě v cestě uploadu a čtení.
- `AfterburnerStudio/Services/NativeAfterburner.cs`: validace `OK checksum matches` a `OK upload finished` před zápisem; detekce `Error:` i `ER`; ochrana při `VPP-CAL-MISSING`; ověření, že se při úspěšném XSVF přenosu odečetla celá vstupní data.
- `tests/Functional/Program.cs`: upravený emulátor podle skutečných potvrzení firmwaru.
- `tests/test_direct_native.py`, `tests/test_manifest.py`: obnovené statické kontroly podle nynější architektury aplikace.
- `tests/cli_parser_regression.c`, `tests/sparse_regression.cpp`: nově přidané offline regresní testy.
- `README.md`, `docs/TESTING.md`: aktualizovaná dokumentace.

## Zpětná kompatibilita

Stávající příkazy `*`, `g`, `f`, `u`, `#t`, `#f`, `#c`, `#e`, `w`, `v`, `c`, `p`, `j0/j1` a sériový port **57600 8N1** se nemění. Starý CLI zůstává v `src_pc/`. Pro zápis fusemapy je nyní povinný úspěšný checksum a ukončení `#e`. Režim přenosu typu pro čtení/mazání (`u`, `#t`, `#e`) zůstává dostupný. Po aktualizaci firmwaru je nutné ověřit aktuální kalibraci VPP.

## Provedená kontrola

- **PASS:** kompilace původního PC CLI přes GCC na Linuxu.
- **PASS:** 6 offline regresních případů parseru JEDEC.
- **PASS:** sparse-map regresní test včetně AddressSanitizeru a UndefinedBehaviorSanitizeru.
- **PASS:** statické Python kontroly zdrojů a struktury AXAML/SVG.
- **NEOVĚŘENO:** kompilace Arduino/PlatformIO, kompilace dotnet/Avalonia, fyzické testy GAL, ATF, PEEL, JTAG a měření programovacího napětí. Chybí zde odpovídající toolchain a hardware; nelze proto garantovat okamžitou funkčnost všech obvodů.

## Co před nasazením ověřit

1. `pio run -e uno` (firmware); sledujte FLASH a SRAM, zejména rezervu zásobníku.
2. `dotnet build AfterburnerStudio.sln -c Release` a `dotnet run --project tests/Functional/Functional.csproj -c Release`.
3. S prázdnou ZIF paticí kalibraci proměnného VPP, poté hodnoty porovnat s multimetrem.
4. Na obětním obvodu otestovat identifikaci, zápis, ověření, chybný checksum, readback a mazání.
5. U ATF750C ověřit běžné a velmi husté fusemapy. Hustý obraz, který se na Uno nevejde, musí být explicitně odmítnut; žádný zápis nesmí pokračovat.
6. Před běžným JTAG použitím otestovat XSVF a reset/přepnutí mezi `j1` a `j0`.

Originální GitHub repozitář nebyl při přípravě ZIP změněn.

## Aktualizace po předloženém PlatformIO logu (31. 10. 2026)

Dodatečný úklid varování z úspěšné kompilace Arduino Uno je popsán v `docs/WARNING_CLEANUP_CZ.md`. Základní opravy firmwaru, CLI a Studia zůstaly zachovány. Skutečné sestavení firmwaru s touto novou sadou souborů nebylo v prostředí s AVR toolchainem opakováno.
