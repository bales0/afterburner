#!/usr/bin/env python3
"""Afterburner v0.6.3: fix disabled GAL600x on 32U4 without replacing firmware.

Applies an exact, idempotent, fail-closed source update to the user's current
src/afterburner.ino, preserving unrelated local edits. No dependencies beyond
Python stdlib. Run from any directory.
"""
from pathlib import Path
import argparse
import difflib
import shutil
import sys

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / 'src' / 'afterburner.ino'
MARKER = 'static bool isGalTypeEnabledInBuild(GALTYPE type) {'


def replace_once(src: str, old: str, new: str, label: str) -> str:
    count = src.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected exactly 1 anchor, found {count}; no files modified')
    return src.replace(old, new, 1)


def apply_changes(original: str) -> str:
    # Require the expected source version and built-in type enum.
    if '#define VERSION "0.6.3"' not in original:
        raise RuntimeError('Firmware version mismatch (expected 0.6.3); no files modified')
    if MARKER in original:
        return original

    updated = original
    # Function implementation is already inside #if CFG_USE_GAL600X.
    # The caller in readOrVerifyGal was not, causing an undefined symbol.
    old = '''    case GAL6001:
    case GAL6002:
        cfgArray = (gal == GAL6001) ? (unsigned char*) cfg6001 : (unsigned char*) cfg6002;
        i = readOrVerifyGalFuseMap600(cfgArray, verify);
        break;'''
    new = '''#if CFG_USE_GAL600X
    case GAL6001:
    case GAL6002:
        cfgArray = (gal == GAL6001) ? (unsigned char*) cfg6001 : (unsigned char*) cfg6002;
        i = readOrVerifyGalFuseMap600(cfgArray, verify);
        break;
#endif /* CFG_USE_GAL600X */'''
    updated = replace_once(updated, old, new, 'GAL600x read/verify switch')

    # Do not remove enum members or galInfoList rows. Their numeric indices
    # are part of the existing Studio/CLI serial protocol.
    helper = '''// Optional chip families must be rejected, not silently processed using a
// generic read/write path when the smaller 32U4 build excludes their algorithms.
static bool isGalTypeEnabledInBuild(GALTYPE type) {
    if (type <= UNKNOWN || type >= LAST_GAL_TYPE) return false;
#if !CFG_USE_GAL600X
    if (type == GAL6001 || type == GAL6002) return false;
#endif
#if !CFG_USE_PEEL
    if (type == PEEL18CV8) return false;
#endif
    return true;
}

'''
    # This insertion is after includes/configuration, before normal functions.
    anchor = '// print some help on the serial console\n'
    updated = replace_once(updated, anchor, helper + anchor, 'chip-type guard helper')

    # Reject disabled types during the modern #t data upload, before any data
    # are accepted. One-character firmware chip IDs remain unchanged.
    old = '''      gal = (GALTYPE)type;
      copyGalInfo();
      sparseSetup(1);
      uploadTypeSelected = 1;'''
    new = '''      if (!isGalTypeEnabledInBuild((GALTYPE)type)) {
        uploadError = 1;
        uploadChecksumOk = 0;
        mapUploaded = 0;
        Serial.println(F("ER chip type unavailable in this firmware build"));
        break;
      }
      gal = (GALTYPE)type;
      copyGalInfo();
      sparseSetup(1);
      uploadTypeSelected = 1;'''
    updated = replace_once(updated, old, new, 'modern #t chip selection')

    # Reject unsupported chips via the legacy g command too.
    old = '''        if (type >= 1 && type < LAST_GAL_TYPE) {
          // ensure PEEL IC operations run on a new type of Aftburner PCB with a shift register IC'''
    new = '''        if (type >= 1 && type < LAST_GAL_TYPE) {
          if (!isGalTypeEnabledInBuild((GALTYPE)type)) {
            Serial.println(F("ER chip type unavailable in this firmware build"));
            break;
          }
          // ensure PEEL IC operations run on a new type of Aftburner PCB with a shift register IC'''
    updated = replace_once(updated, old, new, 'legacy g chip selection')

    # A stale GAL selection can survive reset via .noinit; fail closed before
    # raising VPP or controlling the ZIF socket.
    old = '''static char doTypeCheck(void) {
  
  // Never apply high voltage if the variable-VPP board has no EEPROM map.'''
    new = '''static char doTypeCheck(void) {
  if (!isGalTypeEnabledInBuild(gal)) {
    Serial.println(F("ER chip type unavailable in this firmware build"));
    return 0;
  }
  // Never apply high voltage if the variable-VPP board has no EEPROM map.'''
    updated = replace_once(updated, old, new, 'runtime type safety')

    # Read/write PES can bypass doTypeCheck(). Reject an old .noinit chip
    # selection instead of using its disabled programming family.
    old = '''      case COMMAND_READ_PES : {
        char type;
        if (varVppExists && !varVppCalibrationOk) {'''
    new = '''      case COMMAND_READ_PES : {
        char type;
        if (!isGalTypeEnabledInBuild(gal)) {
            Serial.println(F("ER chip type unavailable in this firmware build"));
        } else if (varVppExists && !varVppCalibrationOk) {'''
    updated = replace_once(updated, old, new, 'read PES safety')

    old = '''      case COMMAND_WRITE_PES : {
        char type;
        if (varVppExists && !varVppCalibrationOk) {'''
    new = '''      case COMMAND_WRITE_PES : {
        char type;
        if (!isGalTypeEnabledInBuild(gal)) {
            Serial.println(F("ER chip type unavailable in this firmware build"));
        } else if (varVppExists && !varVppCalibrationOk) {'''
    updated = replace_once(updated, old, new, 'write PES safety')
    return updated


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='validate only; do not write')
    parser.add_argument('--diff', action='store_true', help='print unified diff')
    args = parser.parse_args()
    if not SRC.is_file():
        print(f'ERROR: missing {SRC}', file=sys.stderr)
        return 2
    raw = SRC.read_bytes()
    bom = b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b''
    clean = raw[len(bom):].decode('utf-8')
    newline = '\r\n' if '\r\n' in clean else '\n'
    lf = clean.replace('\r\n', '\n')
    try:
        result = apply_changes(lf)
    except RuntimeError as ex:
        print(f'ERROR: {ex}', file=sys.stderr)
        return 3
    if args.diff:
        print(''.join(difflib.unified_diff(lf.splitlines(keepends=True), result.splitlines(keepends=True),
                                            fromfile='src/afterburner.ino (old)', tofile='src/afterburner.ino (fixed)')))
    if result == lf:
        print('Already fixed; no file changes.')
        return 0
    if args.check or args.diff:
        print('Patch is applicable; source is not yet modified.')
        return 0
    backup = SRC.with_suffix('.ino.pre-32u4-fix.bak')
    if not backup.exists():
        shutil.copy2(SRC, backup)
    encoded = bom + result.replace('\n', newline).encode('utf-8')
    SRC.write_bytes(encoded)
    print(f'Updated: {SRC}')
    print(f'Backup:  {backup}')
    print('32U4 GAL600x conditional compilation and unsupported-chip checks applied.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
