# Afterburner – stabilita příjmu a informace o sériové RAM

Připraveno pro aktuální `bales0/afterburner`, bez změny verze firmwaru **0.6.3**, CLI **v.0.6.3** a identifikačního řetězce `AFTerburner v.`.

## Co je změněno

1. **Přetečený sériový řádek:** příjem se přepne do stavu `discardUntilEol`. Zbytek řádku se zahodí i při příchodu v několika USB dávkách; nemůže se vykonat jako nový příkaz. U uploadu se rozpracovaná data zneplatní. Sériový prompt/protokol zůstává zachován.
2. **Přísná kontrola uploadu:** `#t` vyžaduje platný jednopísmenný ID kód (včetně ASCII hodnot pro ID > 9), volitelný textový název GAL; `#f` vyžaduje 4- nebo 5místnou desetinnou adresu a 1 až 4 úplné hexadecimální bajty; `#c` přesně 4 hexadecimální číslice; `#e` žádné další znaky. Po chybném řádku zůstane přenos chybový až do ukončení. `#p` PES navíc vyžaduje přesně 8 dvojic hexadecimálních znaků oddělených `:`. Platné řídké nahrávání (vynechané nulové bloky), typ-only `u/#t/#e` a kontrola vyplňujících nul za poslední pojistkou zůstaly zachovány.
3. **Regresní testy:** `tests/test_firmware_serial_safety.py` extrahuje skutečné funkce z `.ino`, kompiluje jejich těla s náhradami hardwaru a testuje poškozené rámce, checksum, ztrátu ukončení, přesycení bufferu, pětičíslicové adresy, typ-only upload, sparse řádky a kompatibilní `#p`. Rozšířená sada `tests/Functional/Program.cs` má emulované varianty banneru SRAM; ostatní existující testy zůstávají zachovány.
4. **Pouze detekce externí RAM:** pro desky s variable VPP se při bootu provede stávající `seRamInit()` i na Uno. Do identifikačního banneru přibyl přesný diagnostický token:
   - `SeRAM:23LC512` – detekovaný režim 16bit adresování
   - `SeRAM:23LC1024` – detekovaný režim 24bit adresování
   - `SeRAM:None` – RAM při testu nenalezena
   - `SeRAM:NotChecked` – deska bez podporované sdílené sběrnice; detekce se neprovádí

CLI token čte jen v režimu `-v`. Studio po prvním skutečném připojení zobrazuje stav v části **Connection and device** a zároveň v logu. Starší firmware bez tokenu hlásí **Not reported by firmware** (nikoli chybně Not detected).

**Není implementována** žádná externí fusemapa ani přesměrování uložených dat do 23LC512/23LC1024. `RAM_BIG`, sparse komprese, komunikace při zápisu, kalibrační postupy a adresování pojistek zůstávají beze změny.

## Offline kontrola

```bash
python tests/test_firmware_serial_safety.py
python tests/test_vpp_startup.py
python tests/test_signed_semantics.py
python tests/test_manifest.py
python tests/test_direct_native.py
python tests/test_warning_cleanup.py
```

Pro Studio navíc, je-li k dispozici .NET 8:

```bash
dotnet run --project tests/Functional/Functional.csproj
```

Pro Uno po aktualizaci firmware:

```bash
pio run -e uno
```

## Nezbytné zkoušky s hardwarem

- **Uno bez externí SRAM:** start bez změny VPP, připojení CLI `-v` a Studia; očekává se `SeRAM:None` pro desku variable VPP. Na starší desce `SeRAM:NotChecked`.
- **Uno + 23LC512 a Uno + 23LC1024:** zkontrolovat přesnost identifikace, proběhnutí kalibrace VPP po startu a návrat VPP do 5V neprogramovacího stavu. Identifikace čipu není čtením výrobcem definovaného ID, jde o odvození podle režimu adresování.
- **VPP bez osazeného GAL:** ověřit, že výstupy a shift registr zůstávají po detekci SRAM v klidovém stavu a že kalibrace VPP pokračuje stejně jako před úpravou.
- **Běžné GAL16V8/22V10 a ATF750C:** úplný cyklus identifikace, uploadu, čtení, zápisu a verifikace; testovat s pamětí i bez paměti. Pro destruktivní testy používat pouze zkušební obvody.
- **Chyby komunikace:** chybné `#f`, přerušený upload, příliš dlouhý řádek, špatný `#c`, invalidní `#e` -> operace musí skončit chybou a příkaz `w` nesmí mít platnou fusemapu.
- **JTAG/XSVF:** ověřit start, reset a předání bufferu; přidaný detekční test SRAM nesmí zasahovat do JTAG hodinových signálů.

## Omezení provedeného ověření

Host test kompiluje pouze výňatky firmware s náhradami I/O, nikoliv celý AVR firmware. Není náhradou PlatformIO buildu, měření obsazení flash/SRAM, dotnet sestavení ani měření fyzických signálů/VPP. Před nasazením na skutečný programátor je nutné absolvovat uvedené hardwarové testy.
