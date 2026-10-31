# Kontrola a opravy bez změny verze (31. 10. 2026)

## Podklad

Aktuální veřejný HEAD `bales0/afterburner`: `1c6ba1ab7988343d89811d93fa8a107ecad4c300`.
Archiv obsahuje celé zdrojové stromy firmwaru, původního PC CLI a Afterburner Studia se zachovanými adresáři. Repo na GitHubu nebylo změněno.

**Čísla verzí se nezvyšovala:** firmware `0.6.4`; CLI `v.0.6.2`; projektová metadata Studia jsou beze změny.

## Společné funkční opravy

### Automatické skutečné měření VPP při každém startu

`aftb_vpp.h` – `varVppCheckCalibration()`:

1. Přečte kalibrační tabulku EEPROM, ověří nulové a sestupné záznamy.
2. Jestliže je tabulka platná, nastaví **skutečný výstup 9 V**, počká na ustálení a provede **dvě ADC měření** se vzájemnou kontrolou.
3. Přijme rozsah **8,80–9,20 V** a rozdíl dvou měření nejvýše 0,12 V; při odchylce ohlásí selhání.
4. Po měření a také při každé chybové cestě vždy vypne digitální potenciometr (`VPP_5V0`, klidová poloha). Nezvyšuje napětí pro kalibraci bez platných dat.
5. Nesplnění znamená `VPP-CAL-MISSING`, zákaz programovacích operací a možnost znovu kalibrovat.
6. Po příkazu kalibrace `b` se tabulka **okamžitě znovu ověří skutečným měřením** ještě před `Calibration OK`.

**Bezpečnostní upozornění:** Při automatickém testu firmware krátce generuje přibližně 9 V. Před zapnutím nebo resetem programátoru musí být napájení ZIF patice vypnuté. Pro první uvedení do provozu i nadále změřte skutečnou hodnotu VPP multimetrem. Dvě ADC měření neodhalí nesprávnou analogovou referenci nebo poruchu samotného měřicího děliče.

`src/afterburner.ino` nyní odpojí CS posuvného registru **ještě před** měřením VPP. V režimu s variabilním VPP blokuje bez platného měření také čtení/zápis PES, zápis/mazání/ověření GAL a JTAG.

`NativeAfterburner.cs` vyžaduje pro variabilní VPP explicitní `VPP-CAL-OK`. Starší firmware bez tohoto stavu nepovažuje za změřený; programování se zablokuje, diagnostika a kalibrace zůstávají dostupné. U starých desek s pevným VPP zůstává kompatibilita zachována.

### Původní CLI a protokol

- `src_pc/afterburner.c`: časový limit je chyba, pokud chybí konečný `>\r\n`, i když přišla částečná odpověď; nula bajtů při zápisu není úspěch; kontrolují se všechna chybová hlášení `ER` / `Error:`.
- POSIX `EAGAIN`/`EWOULDBLOCK`/`EINTR` neznamenají chybu portu; čtení je zkusí znovu, skutečné I/O chyby zůstávají chybami.
- JTAG požadavek `$ddd\r\n` lze nyní přečíst po jednotlivých bajtech. Neplatný/rozbitý požadavek, timeout, neznámé `Q` nebo předčasný úspěšný konec XSVF vracejí chybu.
- Uživatelům Windows se automaticky vybere WinAPI větev `src_pc/serial_port.h` namísto `<termios.h>`; opraven COM10+ prefix a uvolňování Win32 handle při selhání nastavení portu.
- Firmware rozlišuje `OK gal type selected` pro přenos pouze `u / #t / #e` potřebný k čtení/mazání, a `OK upload finished` jen pro potvrzený checksum nahrané mapy. Neplatný datový blok nevrací zároveň `ER` a následné zavádějící `OK`.

## Audit původních znaménkových proměnných

Porovnány původní deklarace a hodnoty v tabulce `galInfoList` proti verzím předchozího cleanupu:

| Původní typ | Současný typ | Rozhodnutí | Zdůvodnění |
|---|---|---|---|
| `short fuses`, `short uesfuse` | `uint16_t` | Ponechat | Počty/adresy 0 až 14499, žádné záporné hodnoty ani sentinel `-1` |
| `char pins`, `char rows`, `char uesrow`, `char uesbytes`, `char eraserow`, `char eraseallrow`, `char pesrow`, `char pesbytes`, `char cfgrow` | `uint8_t` | Ponechat | Všech 17 položek tabulky má nezáporné hodnoty v 8bitovém rozsahu; v přístupech se netestují negativní stavy |
| `short lineIndex` | `uint8_t` | Ponechat | Index do `line[32]`, nabývá jen 0 až 31; proti původnímu ušetří 1 B SRAM AVR |
| `char` indexy parseru | `uint8_t` | Ponechat | Jsou to výhradně nezáporné indexy do 32bajtového řádku; doplněny mezní kontroly |
| `unsigned short parse2hex` s `return -1` | **`int16_t parse2hex`** | Změnit | `-1` je skutečně nutný terminátor seznamu `#f`; původní typ jej vytvářel nepřímo přes unsigned→signed konverzi |
| `unsigned short parse4hex` s `return -1` | **`int32_t parse4hex`** | Změnit | `0xFFFF` může být platný checksum; záporný sentinel nesmí s platným číslem kolidovat |
| `int8_t calOffset`, `int8_t inc`, `int16_t d1/d2/minDif`, `SAMPLE_SHIFT -45` | Znaménkové | Zachovat | Záporná korekce ADC, offset a rozdíly jsou legitimní |
| `int8_t i` v `aftb_mcp4131.h` | Znaménkové | Zachovat | Smyčka musí po bitu 0 dojít na `-1`, jinak hrozí nekonečná smyčka |
| `int16_t dataPos` v `aftb_sparse.h` | Znaménkové | Zachovat | Stávající kontrola `dataPos >= 0` má smysl jako ochrana před neplatnou pozicí |

Typy v `galinfo_t` mají na AVR shodné velikosti jako před změnou (8/16 bitů). Velikost struktur a počet položek zůstávají stejné. Ověření v místním souboru `tests/test_signed_semantics.py` kontroluje nezáporné hodnoty tabulky a existenci důležitých znaménkových sentinelů.

## Provedené testy

- GCC Linux CLI: úspěšný překlad (nikoli Windows cross-build).
- `tests/cli_parser_regression.c`: šest regresních případů.
- `tests/cli_serial_regression.c`: timeout, kompletní prompt, chyba před `OK`, fragmentovaný a poškozený JTAG požadavek.
- `tests/sparse_regression.cpp`: zápis, opakované bity, kompakce, ochrana kapacity.
- `tests/test_vpp_startup.py`: kontrola **skutečné funkce** `varVppCheckCalibration()` s imitovaným ADC (úspěch, chybná hodnota, nestabilita, poškozená tabulka, vypnutí napětí po každé cestě).
- `tests/test_signed_semantics.py`: audit znaménkovosti a GAL tabulky.

**Neprovedeno:** současný build `pio run -e uno`, `dotnet build`, headless Avalonia a fyzické měření VPP multimetrem. V prostředí chybí AVR toolchain, .NET SDK a hardware. U Arduino Uno je nadále třeba měřit rezervu zásobníku; poslední známý build měl 271 B volné statické SRAM ještě před těmito změnami.

## Postup po rozbalení

```text
pio run -e uno
dotnet build AfterburnerStudio.sln -c Release
dotnet run --project tests/Functional/Functional.csproj -c Release
```

Při prvním zapnutí zkontrolujte `I: VPP 9V startup measurement OK` a `VPP-CAL-OK`, s vypnutým napájením ZIF a s multimetrem na VPP. Pokud test selže, **neprogramujte obvody**, ověřte elektroniku, analogovou referenci a opakujte kalibraci.
