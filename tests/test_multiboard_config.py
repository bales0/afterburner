from pathlib import Path
p = Path(__file__).resolve().parents[1]
cfg = (p/'aftb_fusemap_config.h').read_text()
assert '#undef RAM_BIG' in cfg
assert 'MAXFUSES 1813' in cfg and 'MAXFUSES 1332' in cfg
vpp = (p/'scripts/apply_vpp_compat.py').read_text()
assert 'AFTB_VPP_ACTIVE_9V_AT_STARTUP' in vpp
ini = (p/'platformio.ini').read_text()
for e in ['uno','nano','mega2560','leonardo','micro','yun','uno_r4_minima','uno_r4_wifi','esp32s2_saola1']:
    assert f'[env:{e}]' in ini
print('Static multi-board configuration checks OK')
