# Afterburner – následná kontrola varování Arduino Uno (31. 10. 2026)

## Vstup

Uživatelský log PlatformIO pro předchozí opravený firmware 0.6.4:

- **SUCCESS**, bez kompilátorové chyby.
- **SRAM 1777/2048 B (86,8 %)**, tedy 271 B pro zásobník a další dynamická data.
- **Flash 27294/32256 B (84,6 %)**.

Údaje jsou **z předchozího buildu**, nikoli z nově vygenerovaného firmware. Po tomto úklidu je třeba spustit PlatformIO znovu; zde není nainstalován AVR toolchain ani .NET SDK.

## Změny v této revizi

- `src/afterburner.ino` + `aftb_fusemap_config.h`: přemístění alternativních definic `MAXFUSES` z `.ino` do hlavičky; 1813 B pro `RAM_BIG`, 1332 B + `USE_SPARSE_FUSEMAP` pro malé SRAM. Hodnoty nezměněny. Cílem je vyloučit falešnou redefinici při skenování `.ino` prostředím PlatformIO.
- `src/afterburner.ino`: index `lineIndex` je `uint8_t`, protože buffer `line` má 32 B; o 1 B nižší statické nároky na AVR, kontrola rozsahu bez signedness warningu.
- `src/afterburner.ino`: pole struktury `galinfo_t`, která mají pouze nezáporné hodnoty, používají odpovídající neznaménkové datové typy (`uint8_t`, `uint16_t`) při **nezměněné velikosti jednotlivých polí**. Současně jsou sjednocena porovnání a přetypování počtů bitů. Pořadí polí, počet položek a protokol GAL se nemění.
- `src/afterburner.ino`: doplnění výchozích větví `switch` s dosavadním chováním „bez akce“ pro nepodporované typy; explicitní inicializace čítače při neplatném typu; odstranění nepoužívané deklarace `cfgAddr`, `i`, `unused`, `start` a nepoužívané pomocné funkce.
- `aftb_vpp.h`: proměnná pro kontrolu digitálního potenciometru se vytváří jen při `VPP_PARANOID`; výpočet `r2` jen při `SAMPLE_SHIFT` nebo explicitní diagnostice `VPP_ADC_DEBUG_SKEW`; `v` pouze uvnitř vypnuté diagnostické větve. Algoritmus ADC pro AVR se tím nemění.
- `aftb_vpp.h`: diagnostický ramp test se kompiluje jen se zapnutým `VPP_TEST_RAMP`; jeho původně nekonečný cyklus s `uint8_t i < 256` je opraven na `uint16_t`.
- `aftb_sparse.h`: diagnostické výpisy jen se zapnutým `AFTB_SPARSE_STATS`. Sparse kodér a ochrana přetečení dat nebyly v této revizi funkčně měněny.
- `tests/test_warning_cleanup.py`: statické kontroly této revize.

## Zpětná kompatibilita

Beze změny: sériový protokol 57600 8N1, příkazy `*`, `g`, `f`, `u`, `#t`, `#f`, `#c`, `#e`, `w`, `v`, `c`, `j0/j1`, obsah JEDEC, výpočet checksumu, profil GAL, korekce ADC AVR, výběr programovacího napětí, zdroje původního CLI a Studio.

## Provedené ověření

- Původní PC CLI: build GCC v GNU C režimu **PASS**.
- JEDEC CLI parser: 6 regresních případů **PASS** (AddressSanitizer/UndefinedBehaviorSanitizer).
- Sparse fusemap: testy kompakce, duplicitních bitů a odmítnutí přetečení **PASS** (AddressSanitizer/UndefinedBehaviorSanitizer).
- Statické strukturové kontroly GUI a protokolu **PASS**.
- Kontrola archivu a všech zdrojových cest proběhne při vytvoření ZIP.

**Neověřeno:** nový překlad PlatformIO/AVR, přeložení .NET Studia, měření skutečné zásobníkové rezervy a hardwarové zkoušky.

## Postup zkoušky

1. Rozbalte kompletní ZIP, ve VS Code otevřete `afterburner-master`.
2. `pio run -e uno` a zkontrolujte varování, SRAM a Flash.
3. `dotnet build AfterburnerStudio.sln -c Release` a spusťte testy Studia.
4. Pro ověření programování použijte nejprve prázdnou ZIF patici při kalibraci VPP a následně obětní GAL.

S hodnotou zbývajících 271 B RAM **nelze** pouze podle linkovacího výpisu potvrdit bezpečný chod všech operací; zásobníková rezerva vyžaduje kontrolu za běhu.
