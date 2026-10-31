"""Static sanity checks for the Afterburner Studio startup hotfix.

Does not replace a .NET 10/Avalonia build or a Windows application launch.
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parent.parent
app = (repo / 'AfterburnerStudio' / 'App.axaml.cs').read_text()
project = (repo / 'AfterburnerStudio' / 'AfterburnerStudio.csproj').read_text()
assert 'AvaloniaXamlLoader.Load(this)' not in app
assert 'RequestedThemeVariant = ThemeVariant.Light' in app
assert 'Styles.Add(new FluentTheme())' in app
assert 'desktop.MainWindow = new MainWindow()' in app
assert '<TargetFramework>net10.0</TargetFramework>' in project
assert not re.search(r'<AvaloniaXaml\s+(Remove|Include)', project), 'Do not override Avalonia default XAML items'
root = ET.fromstring(project)
assert root.tag == 'Project'
print('OK: App initialization does not depend on its compiled AXAML; project uses default Avalonia resources')
