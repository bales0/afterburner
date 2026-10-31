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

The local firmware source still contains `if (0 == flagBits & FLAG_BIT_TYPE_CHECK)` and `xsvf->sirsize_bits <= 8` in XSIR2 handling, matching the defects discussed in the referenced chat. The desktop archive does not modify the firmware. In particular, host-side XSVF response handling cannot fix the XSIR2 firmware instruction bug. Check the firmware actually flashed on the programmer before testing affected XSVF files.
