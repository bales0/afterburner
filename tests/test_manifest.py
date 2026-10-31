#!/usr/bin/env python3
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
GUI=ROOT/'AfterburnerStudio'
VIEW=GUI/'MainWindow.axaml'
CODE=GUI/'MainWindow.axaml.cs'
MODEL=GUI/'Models'/'ChipProfile.cs'
DRAW=GUI/'Controls'/'SocketDiagram.cs'

for x in [VIEW, CODE, MODEL, DRAW, GUI/'App.axaml', GUI/'AfterburnerStudio.csproj']:
    assert x.exists(), f'Missing: {x}'
for x in GUI.rglob('*.axaml'): ET.parse(x)
for x in GUI.rglob('*.svg'): ET.parse(x)
root=ET.parse(VIEW).getroot()
ref={x.get('Name') for x in root.iter() if x.get('Name')}
ref.discard(None)
cs=CODE.read_text('utf-8')
for name in ref:
    assert re.search(r'\b'+re.escape(name)+r'\b',cs) or name in ('Tabs',), f'Unused/undefined control {name}'
for handler in re.findall(r'(?:Click|SelectionChanged)="([A-Za-z][A-Za-z\d_]*)"', VIEW.read_text('utf-8')):
    assert re.search(r'\b'+re.escape(handler)+r'\s*\(', cs), f'Missing handler: {handler}'
model=MODEL.read_text('utf-8')
chip_names=re.findall(r'new\("([A-Z\d]+)"',model)
assert len(chip_names)==18,chip_names
assert len(set(chip_names))==18
assert 'PEEL18CV8' in chip_names and 'ATF1504AS' in chip_names
assert '24 - i' in DRAW.read_text('utf-8'), 'Socket lower numbering must descend!'
assert 'chip.DipPins - i' in DRAW.read_text('utf-8'), 'DIP lower numbering must descend!'
assert 'UNVERIFIED' in ''.join(f.name for f in (GUI/'Assets/Diagrams').glob('*.svg'))
print(f'PASS: {len(chip_names)} profiles; {len(ref)} XAML named controls; {len(list(GUI.rglob("*.svg")))} SVG assets; XML and handlers verified.')
