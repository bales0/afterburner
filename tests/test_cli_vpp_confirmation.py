#!/usr/bin/env python3
"""Unix offline regression for interactive VPP consent and CLI result messages.
No programmer, GAL, or real serial port required.
Run from repository root: python3 tests/test_cli_vpp_confirmation.py
"""
from pathlib import Path
import os
import pty
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
assert shutil.which('gcc'), 'gcc required for regression'
with tempfile.TemporaryDirectory() as tmp:
    exe = str(Path(tmp) / 'afterburner_cli')
    subprocess.run(['gcc', '-std=gnu11', '-O2', '-o', exe,
                    str(root/'src_pc/afterburner.c'),
                    str(root/'src_pc/exerciser.c')], check=True)
    missing_port = '/dev/afterburner-no-such-device'
    args = [exe, 'm', '-v', '-d', missing_port]
    # Redirected stdin must not allow bypassing the interactive physical check.
    p = subprocess.run(args, input='YES\n', capture_output=True, text=True, timeout=5)
    combined = p.stdout + p.stderr
    assert p.returncode == 2, (p.returncode, combined)
    assert 'requires interactive confirmation' in combined, combined
    assert 'opening serial' not in combined and 'sending' not in combined, combined
    assert 'result=' not in combined and 'gal=0' not in combined, combined
    assert 'Operation cancelled:' in combined

    def pty_case(reply: bytes):
        master, slave = pty.openpty()
        try:
            p = subprocess.Popen(args, stdin=slave, stdout=subprocess.PIPE,
                                 stderr=subprocess.STDOUT)
            os.close(slave)
            slave = -1
            os.write(master, reply)
            stdout, _ = p.communicate(timeout=5)
            return p.returncode, stdout.decode('utf-8', errors='replace')
        finally:
            os.close(master)
            if slave >= 0:
                os.close(slave)

    for reply in (b'no\n', b'y\n', b'\n', b'YESPLEASE\n'):
        code, out = pty_case(reply)
        assert code == 2, (reply,code,out)
        assert 'opening serial' not in out, out
        assert 'Operation cancelled:' in out, out
    code, out = pty_case(b'YES\n')
    assert code != 0 and code != 2, (code,out)
    assert 'opening serial' in out, out
    assert 'Operation failed.' in out, out
    assert 'result=' not in out and 'gal=0' not in out, out
    assert 'Cannot connect' in out or 'unable to open serial port' in out

    # All three voltage-producing operation letters use the same safety gate.
    for mode in ('s', 'b'):
        p = subprocess.run([exe, mode, '-v', '-d', missing_port],
                           input='YES\n', capture_output=True, text=True, timeout=5)
        combined = p.stdout + p.stderr
        assert p.returncode == 2, (mode, p.returncode, combined)
        assert 'SAFETY CHECK' in combined and 'opening serial' not in combined

    # Failure in argument validation must not open the programmer or ask for confirmation.
    p = subprocess.run([exe,'i','m','-v'],capture_output=True,text=True,timeout=5)
    combined = p.stdout+p.stderr
    assert p.returncode != 0 and 'missing GAL type' in combined
    assert 'SAFETY CHECK' not in combined and 'opening serial' not in combined

print('PASS: VPP console confirmation, cancellation, redirected input and readable result')
