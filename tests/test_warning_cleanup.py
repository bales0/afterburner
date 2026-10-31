"""Static checks for the low-risk AVR warning cleanup; no device needed."""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
sketch=(root/'src/afterburner.ino').read_text()
vpp=(root/'aftb_vpp.h').read_text()
assert '#define MAXFUSES' not in sketch
assert '#include "aftb_fusemap_config.h"' in sketch
assert 'uint8_t lineIndex;' in sketch
assert 'unsigned short i = 0; // also defined for invalid/sentinel GAL types' in sketch
assert 'for(bit = 0; bit < (uint16_t)galinfo.uesbytes * 8U;' in sketch
assert 'uint8_t type = UNKNOWN;' in sketch
assert 'unsigned char unused, start;' not in sketch
assert 'addFormatedNumberDec4(' not in sketch
assert 'VPP_ADC_DEBUG_SKEW' in vpp
assert '#ifdef VPP_TEST_RAMP' in vpp
assert 'uint16_t i = 1; // uint8_t would wrap at 255' in vpp
print('PASS: warning cleanup structural checks')
