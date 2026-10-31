# Verification report — 2026-10-31

## Completed

`tests/verify-source.ps1`: **PASS — 52 static checks**.

Checks cover XML parsing for AXAML/project/SVG files; registered event-handler names; unique named controls; editable source-path fallback; template styles for hover/pressed/disabled states; English application source; absence of CLI process launch; the 18-device catalog; corrected ZIF/DIP numbering; XSVF success/cleanup handling; interaction locks; bundled erase data.

This does **not** compile AXAML or execute C#.

## Build and runtime tests: blocked

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

## Manual checks still required

- Open a JEDEC and XSVF using the native file picker, then cancel the picker and check that the previous selection remains.
- Use the actual COM list with multiple devices, refresh after unplugging/replugging and confirm the intended port before writing.
- Check the complete layout at the minimum window size and display scaling used on the target machine.
- On a disposable supported test device, check identification, readback/save, erase, write, verification failure, timeout and cancellation with the installed firmware. No serial programming was attempted during preparation.
- Confirm adapter orientation against the actual adapter PCB; no adapter electrical map is supplied by the illustration.

## Existing firmware observations

The previous upstream defects (`if (0 == flagBits & FLAG_BIT_TYPE_CHECK)` and `xsvf->sirsize_bits <= 8`) have both been corrected in the firmware sources supplied in this ZIP. Installed programmers will still run their previously flashed firmware until explicitly updated. Hardware behavior remains unverified.

## Offline repair verification (2026-10-31)

- `gcc -O2 -std=gnu11 -DGCOM=\"test\" -o afterburner src_pc/afterburner.c src_pc/exerciser.c`: **PASS** (Linux-host CLI build).
- `tests/cli_parser_regression.c`: **PASS**, 6 valid/invalid JEDEC cases.
- `tests/sparse_regression.cpp`: **PASS**, compiled with `-fsanitize=address,undefined` and exercised sparse insertion, duplicate bits, compaction, and capacity overflow.
- `python tests/test_direct_native.py` and `python tests/test_manifest.py`: **PASS**, source-level assertions and XML parsing.
- **NOT RUN**: AVR/PlatformIO firmware compile, dotnet build, Avalonia headless tests, physical VPP/GAL/JTAG programming. Neither AVR toolchain nor dotnet SDK was available in the repair environment.
- Original 52 PowerShell static checks in the earlier section are historical, not a claim that the modified tree was executed with PowerShell here.

See `docs/FIXES_CZ.md` for hardware safety and upgrade notes.

## 2026-10-31 – Uno compiler warning cleanup

The supplied PlatformIO log showed SUCCESS (1777/2048 SRAM and 27294/32256 flash) **before** this cleanup. The subsequent source-only warning cleanup was tested with GCC for the legacy CLI, the JEDEC parser regression, the sparse fusemap regression and static checks. No AVR or .NET rebuild is claimed. See `WARNING_CLEANUP_CZ.md`.
