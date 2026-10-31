#!/usr/bin/env python3
"""Static structure checks for current unified SocketLayout model and AXAML."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
GUI = ROOT / 'AfterburnerStudio'
VIEW = GUI / 'MainWindow.axaml'
CODE = GUI / 'MainWindow.axaml.cs'
MODEL = GUI / 'Models/ChipProfile.cs'
LAYOUT = GUI / 'Models/SocketLayout.cs'
DRAW = GUI / 'Controls/SocketDiagram.cs'

for path in [VIEW, CODE, MODEL, LAYOUT, DRAW, GUI / 'App.axaml', GUI / 'AfterburnerStudio.csproj']:
    assert path.exists(), f'Missing: {path}'
for path in GUI.rglob('*.axaml'):
    ET.parse(path)
for path in GUI.rglob('*.svg'):
    ET.parse(path)
root = ET.parse(VIEW).getroot()
names = {element.get('Name') for element in root.iter() if element.get('Name')}
cs = CODE.read_text('utf-8')
for name in names:
    assert re.search(r'\b' + re.escape(name) + r'\b', cs) or name == 'Tabs', f'Undefined control: {name}'
for handler in re.findall(r'(?:Click|SelectionChanged)="([A-Za-z][A-Za-z\d_]*)"', VIEW.read_text('utf-8')):
    assert re.search(r'\b' + re.escape(handler) + r'\s*\(', cs), f'Missing handler: {handler}'
model = MODEL.read_text('utf-8')
chips = re.findall(r'new\("([A-Z\d]+)"', model)
assert len(chips) == len(set(chips)) == 18, chips
assert 'PEEL18CV8' in chips and 'ATF1504AS' in chips
layout = LAYOUT.read_text('utf-8')
assert '12 - column' in layout and '13 + column' in layout
assert 'dipPins - 11 + column' in layout
assert 'SocketLayout.TopContact(column)' in DRAW.read_text('utf-8')
assert 'SocketLayout.ChipPin(chip.DipPins' in DRAW.read_text('utf-8')
assert 'UNVERIFIED' in ''.join(path.name for path in (GUI/'Assets/Diagrams').glob('*.svg'))
