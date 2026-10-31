#!/usr/bin/env python3
"""Offline CLI regression: real sendUploadLineChecked rejects firmware errors.

Run from project root with: python3 tests/test_cli_upload_errors.py
This test does not require a serial port, Arduino, or GAL.
"""
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = (root / "src_pc/afterburner.c").read_text(encoding="utf-8")
begin = source.index("static int sendUploadLineChecked(char* buf, int maxDelay) {")
end = source.index("// Upload fusemap in byte format", begin)
function = source[begin:end]

harness = r'''
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define MAX_LINE (16*1024)
static const char* mockReply;
static int failTransport;
static int sendLine(char* buf, int capacity, int delay) {
    (void)delay;
    if (failTransport) return -1;
    if (strlen(mockReply) >= (size_t)capacity) return -1;
    strcpy(buf, mockReply);
    return (int)strlen(buf);
}
static char* stripPrompt(char* buf) { return buf; }
'''
footer = r'''
static void require(int pass, const char* msg) {
    if (!pass) { fprintf(stderr,"FAIL: %s\n",msg);exit(1); }
}
int main(void) {
    char line[MAX_LINE];
    mockReply="\r\nOK 32\r\n";
    require(sendUploadLineChecked(line,100)==0,"valid upload row rejected");
    mockReply="\r\nER fusemap overflow: 1332/1332 bytes capacity\r\n";
    require(sendUploadLineChecked(line,100)<0,"firmware overflow accepted");
    mockReply="OK row\r\nER invalid upload data\r\n";
    require(sendUploadLineChecked(line,100)<0,"late error line was ignored");
    mockReply="\r\nError: bad value\r\n";
    require(sendUploadLineChecked(line,100)<0,"Error: response was ignored");
    mockReply="OK contains ER as ordinary text\r\n";
    require(sendUploadLineChecked(line,100)==0,"non-error containing ER rejected");
    failTransport=1;
    require(sendUploadLineChecked(line,100)<0,"serial failure accepted");
    puts("PASS: CLI upload rejects firmware overflow and preserves detailed error");
    return 0;
}
'''
if not shutil.which("gcc"):
    raise SystemExit("gcc required for CLI host test")
with tempfile.TemporaryDirectory() as td:
    src = Path(td) / "upload_errors.c"
    exe = Path(td) / "upload_errors"
    src.write_text(harness + function + footer)
    subprocess.run(["gcc", "-std=c11", "-O2", "-Wall", "-Wextra", "-Werror", str(src), "-o", str(exe)], check=True)
    subprocess.run([str(exe)], check=True)
