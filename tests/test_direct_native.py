"""Source-level protocol guards (these do not replace .NET/hardware tests)."""
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
code = '\n'.join(p.read_text() for p in (ROOT / 'AfterburnerStudio').rglob('*.cs'))
backend = (ROOT / 'AfterburnerStudio/Services/NativeAfterburner.cs').read_text()
transport = (ROOT / 'AfterburnerStudio/Services/SerialTransport.cs').read_text()
firmware = (ROOT / 'src/afterburner.ino').read_text()
view = (ROOT / 'AfterburnerStudio/MainWindow.axaml').read_text()
assert 'CliRunner' not in code and 'Process.Start(' not in code
assert 'CliPath' not in view
for command in ['"*"', '"#t "', '"#f "', '"#c "', '"#e"', '"j1"', '"j0"', '"RXSVF"', '"Q0"']:
    assert command in backend, command
assert 'SerialPort' in transport and '57600' in transport
assert 'OK checksum matches' in backend and 'OK upload finished' in backend
assert 'VPP-CAL-MISSING' in backend and 'VPP-CAL-MISSING' in firmware
assert 'uploadChecksumOk' in firmware and 'fuseMapOverflow' in firmware
assert 'JtagErasePanel' in view
