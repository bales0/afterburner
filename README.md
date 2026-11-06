# Afterburner Studio — Direct Serial

Complete VS Code application project, based on the original DirectSerial ZIP and checked against the identical application sources in `C:\Dokumenty\GitHub\afterburner` on 2026-10-31. The interface, application messages and diagrams are in English.

## Changes

- Removed project goals and promotional header/footer text. Operations are at the top of the right column; the file picker remains in the left column.
- Added a serial-port list and refresh. The previous selection is preserved while the port remains available; otherwise the first detected port is selected. Check the selected port before starting an operation.
- File browsing filters JEDEC or XSVF according to the selected device. Paths are editable as a fallback. Validation updates when the path or device changes.
- Write availability follows the selected port, valid input and board revision. Review device placement and adapter connections in the operation confirmation. VPP operations require the empty-socket or disconnected-VPP confirmation.
- Locked input changes while a file picker, confirmation or operation is active. Exceptions from file selection, snapshots and operations are shown in the status area.
- Fixed normal, hover, pressed and disabled button foreground/background combinations in the Fluent content template. The application uses a light theme.
- Corrected the socket illustration: USB left, lever right, pin 1 upper right. DIP20 leaves ZIF 11–14 unused at the left. The old mirrored SVGs/previews were replaced. Adapter pin maps are not inferred; use the specific adapter schematic. See `docs/PIN_VERIFICATION.md`.
- Preserved native 57600 8N1 communication. The application does not launch a CLI. XSVF now accepts the actual firmware success response `Q-0,OK` and waits for terminal cleanup before the next stream. Calibration works independently of the selected chip profile.
- Included the existing repository erase XSVF data and configured copying into build/publish output.

## Open and run

Open this folder in VS Code. Install .NET 10 SDK and the C# extension.

```powershell
dotnet restore AfterburnerStudio/AfterburnerStudio.csproj
dotnet build AfterburnerStudio.sln -c Release
dotnet run --project AfterburnerStudio/AfterburnerStudio.csproj
```

This repository also contains the Arduino firmware source (`src/afterburner.ino`) and the original C CLI (`src_pc/afterburner.c`). The Studio uses the installed firmware through the serial port; changing firmware sources requires a separate firmware build and flash. See `docs/FIXES_CZ.md` for the 2026-10-31 source fixes, compatibility notes and verification limits.

## Use

1. Select a serial port, device and board revision.
2. Browse for a `.jed` or `.xsvf` file, or paste its local path.
3. With socket power off, insert the device using the correct orientation. Check device orientation and, when applicable, the adapter/JTAG connections.
4. Turn socket power on. Choose **WRITE AND VERIFY**, or **ERASE AND WRITE XSVF** for JTAG. Review the device confirmation.
5. Check the operation result in **Operation result** / **Diagnostic log**. Turn socket power off before removing the device.

For JTAG, verification must be part of the design XSVF. An erase XSVF does not verify the design. Identification, readback and standalone verification are unavailable for the JTAG profiles.

## Verification

```powershell
pwsh -NoProfile -File tests/verify-source.ps1
dotnet run --project tests/Functional/Functional.csproj -c Release
```

The first command checks source structure, XML, handlers, language, style guards, pin mapping and bundled files. The second compiles the application/XAML and runs C# tests for JEDEC validation, pin numbering, emulated serial operations, UI enablement, confirmation cancellation and button pointer states. UI tests use Avalonia.Headless; they do not program a real device.

**Verification (2026-11-02):** Release Studio/AXAML build passed with no warnings or errors; 95 functional checks and 54 static source checks passed. The Windows C CLI, offline parser/sparse-map regressions and all nine firmware configurations passed their applicable checks. Physical programming and VPP measurements remain unverified; R4 and ESP32-S2 remain experimental. See `docs/TESTING.md` for commands and limits.

## Files

- `AfterburnerStudio/`: complete application, project, native protocol services, device catalog and diagrams.
- `AfterburnerStudio/xsvf/`: erase and identification data from the local Afterburner repository.
- `.vscode/`: launch, build and test tasks.
- `tests/Functional/`: executable C# regression tests with a serial emulator and headless UI.
- `tests/verify-source.ps1`: static checks, requiring PowerShell 7.
- `scripts/generate-diagrams.ps1`: regenerates the corrected DIP20/DIP24 SVG diagrams.
- `scripts/publish-self-contained.*`: platform-specific self-contained publish helpers.
- `docs/`: pin verification evidence and test status.
