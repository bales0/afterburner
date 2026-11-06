"""Static sanity checks for the Afterburner Studio startup hotfix.

Does not replace a .NET 10/Avalonia build or a Windows application launch.
"""
from pathlib import Path
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parent.parent
app = (repo / 'AfterburnerStudio' / 'App.axaml.cs').read_text()
project = (repo / 'AfterburnerStudio' / 'AfterburnerStudio.csproj').read_text()
assert 'AvaloniaXamlLoader.Load(this)' not in app
assert 'RequestedThemeVariant = ThemeVariant.Light' in app
assert 'Styles.Add(new FluentTheme())' in app
assert 'desktop.MainWindow = new MainWindow()' in app
assert '<TargetFramework>net10.0</TargetFramework>' in project
root = ET.fromstring(project)
assert root.tag == 'Project'
# The startup repair explicitly replaces the two default root XAML items.
# Keep exactly one copy of each input, with compilation enabled.
items = root.findall('.//AvaloniaXaml')
assert [item.get('Remove') for item in items if item.get('Remove')] == ['App.axaml;MainWindow.axaml']
assert [item.get('Include') for item in items if item.get('Include')] == ['App.axaml;MainWindow.axaml']
assert root.findtext('.//EnableAvaloniaXamlCompilation') == 'true'
assert root.find(".//Target[@Name='VerifyAndCompileAfterburnerXaml']").get('DependsOnTargets') == 'CompileAvaloniaXaml'
print('OK: App initialization and explicit compiled AXAML inputs are consistent')
