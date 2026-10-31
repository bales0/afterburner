"""Offline static smoke tests for the compact Afterburner Studio interface.

Run from repository root: python tests/test_studio_compact_ui.py
This is NOT a replacement for a .NET build and a GUI/hardware test.
"""
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
xml_text = (ROOT / 'AfterburnerStudio/MainWindow.axaml').read_text(encoding='utf-8')
code = (ROOT / 'AfterburnerStudio/MainWindow.axaml.cs').read_text(encoding='utf-8')
project = (ROOT / 'AfterburnerStudio/AfterburnerStudio.csproj').read_text(encoding='utf-8')
view = ET.fromstring(xml_text)

names = [n.attrib['Name'] for n in view.iter() if 'Name' in n.attrib]
assert len(names) == len(set(names)), 'duplicate XAML control names'
for expected in ('PortList', 'BoardBox', 'ConnectionInputs', 'ProgrammingResultTitle', 'ProgrammingResultDetails'):
    assert names.count(expected) == 1, expected
assert 'PortBox' not in names and 'PortBox' not in code
assert 'Manual port' not in xml_text
assert 'RowDefinitions="Auto,*"' in xml_text
assert 'SelectedPort' in code
assert '_programmer.RunAsync(SelectedPort,' in code
assert 'OperationHeadline.Text = "Failed";' in code
assert 'OperationHeadline.Text = "Completed";' in code
assert 'OperationHeadline.Text = chipNotDetected' not in code
assert '<TargetFramework>net10.0</TargetFramework>' in project
assert 'Loaded += OnWindowLoaded;' in code
print('PASS: compact tab row, single COM choice, no duplicate detailed status, .NET 10')
