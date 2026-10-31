"""Static regression guard: preserve negative-value sentinels and offsets.
Run: python tests/test_signed_semantics.py
"""
import re
from pathlib import Path
r = Path(__file__).resolve().parents[1]
firmware = (r / 'src/afterburner.ino').read_text()
vpp = (r / 'aftb_vpp.h').read_text()
mcp = (r / 'aftb_mcp4131.h').read_text()
assert re.search(r'#define VERSION\s+"0\.6\.4"',firmware)
assert 'int16_t parse2hex(uint8_t i)' in firmware
assert 'return -1;' in firmware
assert 'int32_t parse4hex(uint8_t i)' in firmware
assert 'val >= 0 && cs == (uint16_t)val' in firmware
assert 'uint8_t lineIndex' in firmware
assert 'int8_t calOffset' in vpp and 'int8_t inc' in vpp
assert 'int16_t minDif' in vpp and 'int16_t d1,d2' in vpp
assert '#define SAMPLE_SHIFT -45' in vpp
assert re.search(r'\bint8_t\s+i\b',mcp) and re.search(r'while\s*\(i\s*>=\s*0\)',mcp)
# All originally signed GAL profile row/count/address fields are constant and nonnegative;
# no negative sentinel appears in the initialization table.
block = firmware.split('const static galinfo_t galInfoList[] PROGMEM =',1)[1].split('galinfo_t galinfo',1)[0]
entries = re.findall(r'^\s*\{(UNKNOWN|GAL\w+|ATF\w+|PEEL\w+),\s*[^\n]+',block,re.M)
assert len(entries)==17,entries
for line in block.splitlines():
    if not re.match(r'^\s*\{(?:UNKNOWN|GAL|ATF|PEEL)',line): continue
    fields = [x.strip() for x in line.split('//',1)[0].split(',')]
    # Fields 3..14: fuse count, DIP pins, rows, bits, UES row/address/count,
    # erase row/all, PES row/count, config row.
    values = [int(x) for x in fields[3:15]]
    assert len(values)==12 and all(0<=x<=65535 for x in values),line
    for position in [4,5,6,7,9,10,11,12,13,14]:
        assert 0<=int(fields[position])<=255,(position,line)
print('PASS: original negative-value semantics retained and GAL table unsigned fields safe')
