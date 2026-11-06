# Verification report — 2026-11-02

## Repairs verified

- The functional project now targets .NET 10, matching Studio. Internal protocol helpers are visible to the Functional test assembly.
- JTAG firmware errors (`ER` / `Error:`) and a terminal prompt without a player result fail immediately. Tests check that no binary data or subsequent stream is sent and the serial port is disposed.
- VPP measurement announces each of its six steps once. Calibration errors describe invalid/missing data without claiming an ADC measurement at every boot.
- Static and UI checks follow the current single COM selector and explicit compiled AXAML inputs. Generated bin/obj sources are excluded from static checks.
- Host serial/upload tests compile the real chip-availability helper for ordinary AVR and 32U4 defaults; disabled GAL6001 uploads are rejected.
- VPP startup tests cover default passive validation (no high voltage) and opt-in active 9 V measurement.
- The 32U4 patch test uses the tracked pre-fix firmware backup. Dry run, idempotence and byte-for-byte backup preservation run without an external ZIP.
- Explicit guarded declarations avoid Arduino-generated unused static-function warnings on small-board builds.

## Completed on Windows

| Check | Result |
| --- | --- |
| `dotnet build AfterburnerStudio.sln -c Release` | PASS — 0 warnings, 0 errors; AXAML compiled |
| `dotnet run --project tests/Functional/Functional.csproj -c Release` | PASS — 95 checks, including headless UI and serial emulator |
| `pwsh -NoProfile -File tests/verify-source.ps1` | PASS — 54 static checks |
| Twelve Windows-compatible `tests/test_*.py` scripts, invoked individually | PASS; CLI safety test supplied the newly compiled executable |
| Windows C CLI build with GCC, `-D_USE_WIN_API_ -DNO_CLOSE` | PASS |
| `cli_parser_regression.c`, `cli_args_regression.c`, `sparse_regression.cpp` | PASS — 6 JEDEC cases, CLI arguments, sparse capacity/compaction/canaries |
| `pio run` | PASS — uno, nano, mega2560, leonardo, micro, yun |
| `pio run -e uno_r4_minima -e uno_r4_wifi -e esp32s2_saola1` | PASS — all 3 experimental configurations |

Firmware builds do not upload to hardware. Uno/Nano static SRAM use is 1779/2048 bytes (269 bytes remain for stack and other runtime use); this is not a runtime stack-margin measurement. The host serial test still reports two existing signed/unsigned comparison warnings per configuration on the 64-bit host. AVR builds completed without those warnings.

## Limits

- Physical GAL/ATF/PEEL/JTAG programming, VPP accuracy, adapters, real COM reconnects and native file-picker behavior were not tested.
- R4/ESP32-S2 compilation does not establish electrical compatibility. Those configurations remain experimental.
- Unix-only `test_cli_vpp_confirmation.py`, `test_cli_vpp_streaming.py` and POSIX `cli_serial_regression.c` were not run on Windows.
- The current VPP boot default validates the EEPROM table; active 9 V measurement requires `AFTB_VPP_ACTIVE_9V_AT_STARTUP=1`. This repair preserves that default.

## Historical verification — 2026-10-31

The following records describe earlier environments and do not override the results above.

### Completed

`tests/verify-source.ps1`: **PASS — 52 static checks**.

Checks cover XML parsing for AXAML/project/SVG files; registered event-handler names; unique named controls; editable source-path fallback; template styles for hover/pressed/disabled states; English application source; absence of CLI process launch; the 18-device catalog; corrected ZIF/DIP numbering; XSVF success/cleanup handling; interaction locks; bundled erase data.

This does **not** compile AXAML or execute C#.

### Build and runtime tests: blocked

Attempted:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build AfterburnerStudio.sln -c Release
```

Windows denied starting `dotnet.exe` before it produced compiler output. Attempts to run with elevated permissions also failed before execution: Windows error **740**, operation requires elevation. No .NET build result, headless test result or physical programming result is available. No claim is made that these checks passed.

Run on the development machine:

```powershell
dotnet build AfterburnerStudio.sln -c Release
dotnet run --project tests/Functional/Functional.csproj -c Release
```

The functional suite tests the real JEDEC parser and host protocol through an injected transport, plus UI state and Fluent button pointer states. Cases include a rejected checksum, wrong QF, out-of-range L data, G1 rejection, APD support, write→verify order, firmware error propagation, port disposal, XSVF erase→write ordering and terminal cleanup, cancellation, JTAG-selected calibration, port selection/manual override, missing files, insertion confirmations, cancelling a write confirmation and missing explicit erase data.

### Manual checks still required

- Open a JEDEC and XSVF using the native file picker, then cancel the picker and check that the previous selection remains.
- Use the actual COM list with multiple devices, refresh after unplugging/replugging and confirm the intended port before writing.
- Check the complete layout at the minimum window size and display scaling used on the target machine.
- On a disposable supported test device, check identification, readback/save, erase, write, verification failure, timeout and cancellation with the installed firmware. No serial programming was attempted during preparation.
- Confirm adapter orientation against the actual adapter PCB; no adapter electrical map is supplied by the illustration.

### Existing firmware observations

The previous upstream defects (`if (0 == flagBits & FLAG_BIT_TYPE_CHECK)` and `xsvf->sirsize_bits <= 8`) have both been corrected in the firmware sources supplied in this ZIP. Installed programmers will still run their previously flashed firmware until explicitly updated. Hardware behavior remains unverified.

### Offline repair verification (2026-10-31)

- `gcc -O2 -std=gnu11 -DGCOM=\"test\" -o afterburner src_pc/afterburner.c src_pc/exerciser.c`: **PASS** (Linux-host CLI build).
- `tests/cli_parser_regression.c`: **PASS**, 6 valid/invalid JEDEC cases.
- `tests/sparse_regression.cpp`: **PASS**, compiled with `-fsanitize=address,undefined` and exercised sparse insertion, duplicate bits, compaction, and capacity overflow.
- `python tests/test_direct_native.py` and `python tests/test_manifest.py`: **PASS**, source-level assertions and XML parsing.
- **NOT RUN**: AVR/PlatformIO firmware compile, dotnet build, Avalonia headless tests, physical VPP/GAL/JTAG programming. Neither AVR toolchain nor dotnet SDK was available in the repair environment.
- Original 52 PowerShell static checks in the earlier section are historical, not a claim that the modified tree was executed with PowerShell here.

See `docs/FIXES_CZ.md` for hardware safety and upgrade notes.

### 2026-10-31 – Uno compiler warning cleanup

The supplied PlatformIO log showed SUCCESS (1777/2048 SRAM and 27294/32256 flash) **before** this cleanup. The subsequent source-only warning cleanup was tested with GCC for the legacy CLI, the JEDEC parser regression, the sparse fusemap regression and static checks. No AVR or .NET rebuild is claimed. See `WARNING_CLEANUP_CZ.md`.
