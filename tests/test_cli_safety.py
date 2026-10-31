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

r = run('i', '-v', 'm')
assert r.returncode != 0 and 'multiple operation arguments' in r.stdout, r.stdout
r = run('m', '-v', '-d', '/definitely/not/a/serial/device')
assert r.returncode != 0 and 'failed to open serial' in r.stdout, r.stdout
r = run('b', '-v', '-d', '/definitely/not/a/serial/device')
assert r.returncode != 0, r.stdout

source = pathlib.Path(__file__).resolve().parents[1] / 'src_pc' / 'afterburner.c'
s = source.read_text()
assert 'result = operationSecureGal();' in s
assert '(0 == result || noGalCheck)' not in s
assert 'if (opErase && result == 0)' in s
assert 'if (result == 0) {' in s
assert 'if (sendLine(buf, MAX_LINE' in s
print('CLI safety regression: PASS')
