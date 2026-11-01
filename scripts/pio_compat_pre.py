# PlatformIO/SCons pre-build hook; do not rely on __file__ (SConscript).
# Keep the VPP portability and 32U4 GAL600x fix idempotent.
from pathlib import Path
import subprocess
import sys

Import('env')
repo = Path(env.subst('$PROJECT_DIR')).resolve()
for script in ('apply_vpp_compat.py', 'fix_32u4_gal600x.py'):
    result = subprocess.run([sys.executable, str(repo / 'scripts' / script)], check=False)
    if result.returncode:
        raise RuntimeError(f'Afterburner source fix {script} failed; see output above')
