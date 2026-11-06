#!/usr/bin/env python3
"""Offline regression tests for the legacy CLI; no programmer is needed.
Usage: python tests/test_cli_safety.py /path/to/afterburner_executable
"""
import pathlib
import subprocess
import sys

exe = sys.argv[1] if len(sys.argv) > 1 else None
if not exe:
    raise SystemExit('Pass path to a compiled CLI executable')


def run(*args):
    return subprocess.run([exe, *args], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=8)

# Multiple operation arguments are supported; incompatible VPP/write modes are not.
r = run('w', '-v', 'm', '-t', 'ATF16V8B')
assert r.returncode != 0 and 'VPP functions can not be' in r.stdout, r.stdout
# Identification does not enter the interactive VPP consent path.
r = run('i', '-v', '-t', 'ATF16V8B', '-d', '/definitely/not/a/serial/device')
assert r.returncode != 0 and 'unable to open serial port' in r.stdout, r.stdout
for action in ('m', 'b'):
    r = run(action, '-v', '-d', '/definitely/not/a/serial/device')
    assert r.returncode == 2 and 'requires interactive confirmation' in r.stdout, r.stdout
    assert 'opening serial' not in r.stdout and 'sending' not in r.stdout, r.stdout

source = pathlib.Path(__file__).resolve().parents[1] / 'src_pc' / 'afterburner.c'
s = source.read_text()
assert 'result = operationSecureGal();' in s
assert '(0 == result || noGalCheck)' not in s
assert 'if (opErase && result == 0)' in s
assert 'if (result == 0) {' in s
assert 'if (sendLine(buf, MAX_LINE' in s
print('CLI safety regression: PASS')
