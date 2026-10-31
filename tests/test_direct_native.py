"""Static protocol guards; physical Arduino and .NET build need real host testing."""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
code='\n'.join(p.read_text() for p in (root/'AfterburnerStudio').rglob('*.cs'))
backend=(root/'AfterburnerStudio/Services/NativeAfterburner.cs').read_text()
view=(root/'AfterburnerStudio/MainWindow.axaml').read_text()
assert 'CliRunner' not in code and 'Process.Start(' not in code
assert 'CliPath' not in view
for command in ['"*"', '"#t "', '"#f "', '"#c "', '"#e"', '"j1"', '"j0"', '"RXSVF"', '"Q0"']:
    assert command in backend, command
assert 'SerialPort' in backend and '57600' in backend
assert 'JtagErasePanel' in view and 'Controls' not in view[:10]
print('PASS: no CLI, direct serial protocol, XSVF feeder, JTAG selection in GUI')
