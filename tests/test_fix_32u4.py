"""Offline smoke/regression test for surgical firmware patching."""
from pathlib import Path
import importlib.util
import tempfile
import subprocess
import shutil
from zipfile import ZipFile

here = Path(__file__).resolve().parents[1]
script = here / 'scripts' / 'fix_32u4_gal600x.py'
spec = importlib.util.spec_from_file_location('fix_32u4', script)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

# Legacy 0.6.3 fixture as held in the user's original working files; add the
# #t modern upload fragment that the current GitHub firmware has.
archive = Path('/mnt/data/afterburner-master.zip')
if archive.is_file():
    with ZipFile(archive) as z:
        old = z.read('afterburner-master/src/afterburner.ino').decode('utf-8')
    current_fragment = '''\nvoid test_modern_chip_selection_fixture() {
      gal = (GALTYPE)type;
      copyGalInfo();
      sparseSetup(1);
      uploadTypeSelected = 1;
}
'''
    old = old.replace('// print some help on the serial console', current_fragment+'\n// print some help on the serial console')
    old = old.replace('static char doTypeCheck(void) {\n  \n  if (0 ==',
                      'static char doTypeCheck(void) {\n  \n  // Never apply high voltage if the variable-VPP board has no EEPROM map.\n  if (0 ==')
    for command in ['READ_PES', 'WRITE_PES']:
        old = old.replace(f'      case COMMAND_{command} : {{\n        char type;\n        if (gal == PEEL18CV8)',
                          f'      case COMMAND_{command} : {{\n        char type;\n        if (varVppExists && !varVppCalibrationOk) {{\n            Serial.println(F("ER VPP calibration missing"));\n        }} else if (gal == PEEL18CV8)')
    a = m.apply_changes(old)
    assert a != old
    assert m.apply_changes(a) == a
    assert a.count('static bool isGalTypeEnabledInBuild(GALTYPE type) {') == 1
    assert a.count('ER chip type unavailable in this firmware build') >= 5
    assert '#if CFG_USE_GAL600X\n    case GAL6001:\n    case GAL6002:' in a
    assert 'if (!isGalTypeEnabledInBuild(gal)) {' in a
    with tempfile.TemporaryDirectory() as d:
        root = Path(d)
        (root / 'src').mkdir(); (root / 'scripts').mkdir()
        shutil.copy(script, root / 'scripts/fix_32u4_gal600x.py')
        source = root / 'src/afterburner.ino'
        source.write_text(old, encoding='utf-8')
        first = subprocess.run(['python', str(root / 'scripts/fix_32u4_gal600x.py'), '--diff'], capture_output=True, text=True)
        assert first.returncode == 0 and source.read_text() == old
        for _ in range(2):
            proc = subprocess.run(['python', str(root / 'scripts/fix_32u4_gal600x.py')], capture_output=True, text=True)
            assert proc.returncode == 0, proc.stderr
        assert source.read_text() == a
        assert (root / 'src/afterburner.ino.pre-32u4-fix.bak').read_text() == old
else:
    print('Skipped mounted old-source fixture; archive unavailable')
print('32U4 patch tests OK')
