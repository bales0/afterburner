#!/usr/bin/env python3
"""Safely update the current aftb_vpp.h, preserving other local fixes.

Runs with Python 3 and only writes aftb_vpp.h after checking expected anchors.
A .multiboard-backup copy is created once; subsequent runs are idempotent.
"""
from pathlib import Path
import sys

repo = Path(__file__).resolve().parent.parent
file = repo / "aftb_vpp.h"
if not file.is_file():
    sys.exit("ERROR: aftb_vpp.h not found. Run from root of Afterburner repo.")
s = file.read_text(encoding="utf-8")
original = s

# Replace only original ESP32-S2 IDF ADC driver usage, not Arduino analogRead.
s = s.replace('#include "driver/adc.h"\n#define ADC_PIN ADC2_CHANNEL_3\n',
              '// Use Arduino ADC API; no dependency on legacy ESP-IDF ADC2 driver.\n')
s = s.replace('  adc2_config_channel_atten(ADC_PIN, ADC_ATTEN_DB_11); // AREF 2.5V\n',
              '  (void)ref; // Analog reference is controlled by the Arduino ESP32 core.\n')
if 'adc2_config_channel_atten(' in s:
    sys.exit('ERROR: unsupported ADC2 initialization form; no changes written')

# Existing upstream actively applies 9V at EVERY startup. We allow a live
# check only with an explicit opt-in macro; the default is a PASSIVE table check.
if '#ifndef AFTB_VPP_ACTIVE_9V_AT_STARTUP' not in s:
    target = '    varVppSet(VPP_9V0);\n    delay(250);'
    if target not in s:
        # Older firmware had disabled ADC test; do not try to enable it.
        if '#if 0' not in s[s.find('static int8_t varVppCheckCalibration(void)'):]:
            sys.exit('ERROR: unexpected VPP calibration body; no changes written')
    else:
        start = s.index('static int8_t varVppCheckCalibration(void)')
        block_start = s.index('    varVppSet(VPP_9V0);\n    delay(250);', start)
        block_end = s.index('    return OK;', block_start)
        # Keep active test intact, including safety cleanup / error checks.
        s = (s[:block_start] + '#if AFTB_VPP_ACTIVE_9V_AT_STARTUP\n' +
             s[block_start:block_end] + '#else\n'
             '    // No 9V is applied on boot; EEPROM table validity only.\n'
             '    Serial.println(F("I: VPP calibration table valid (voltage not tested at startup)"));\n'
             '#endif\n' + s[block_end:])
        insertion = '#ifndef AFTB_VPP_ACTIVE_9V_AT_STARTUP\n#define AFTB_VPP_ACTIVE_9V_AT_STARTUP 0\n#endif\n\n'
        guard = '//return 1 on success (variable VPP functionality present)'
        if guard not in s:
            sys.exit('ERROR: cannot insert VPP guard; no changes written')
        s = s.replace(guard, insertion+guard, 1)

if s == original:
    print('aftb_vpp.h: already compatible, no changes')
else:
    backup = file.with_suffix(file.suffix+'.multiboard-backup')
    if not backup.exists():
        backup.write_text(original, encoding='utf-8')
    file.write_text(s, encoding='utf-8')
    print('aftb_vpp.h updated; backup:', backup.name)
