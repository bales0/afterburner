"""Regression for the patcher using the tracked pre-fix firmware fixture."""
from pathlib import Path
import importlib.util
import tempfile
import subprocess
import shutil
import sys

here = Path(__file__).resolve().parents[1]
script = here / 'scripts' / 'fix_32u4_gal600x.py'
spec = importlib.util.spec_from_file_location('fix_32u4', script)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
old = (here / 'src/afterburner.ino.pre-32u4-fix.bak').read_text(encoding='utf-8')
a = m.apply_changes(old)
assert a != old
assert m.apply_changes(a) == a
assert a.count('static bool isGalTypeEnabledInBuild(GALTYPE type) {') == 1
assert a.count('ER chip type unavailable in this firmware build') >= 5
assert '#if CFG_USE_GAL600X\n    case GAL6001:\n    case GAL6002:' in a
assert 'if (!isGalTypeEnabledInBuild(gal)) {' in a
assert m.apply_changes((here / 'src/afterburner.ino').read_text(encoding='utf-8')) == (here / 'src/afterburner.ino').read_text(encoding='utf-8')
with tempfile.TemporaryDirectory() as d:
    root = Path(d)
    (root / 'src').mkdir()
    (root / 'scripts').mkdir()
    shutil.copy(script, root / 'scripts/fix_32u4_gal600x.py')
    source = root / 'src/afterburner.ino'
    source.write_text(old, encoding='utf-8')
    original_bytes = source.read_bytes()
    first = subprocess.run([sys.executable, str(root / 'scripts/fix_32u4_gal600x.py'), '--diff'], capture_output=True, text=True)
    assert first.returncode == 0 and source.read_bytes() == original_bytes, first.stderr
    for _ in range(2):
        proc = subprocess.run([sys.executable, str(root / 'scripts/fix_32u4_gal600x.py')], capture_output=True, text=True)
        assert proc.returncode == 0, proc.stderr
    assert source.read_text(encoding='utf-8') == a
    assert (root / 'src/afterburner.ino.pre-32u4-fix.bak').read_bytes() == original_bytes
print('PASS: 32U4 patch, dry run, idempotence and backup preservation')
