"""Compile the actual VPP startup-check function against a mocked ADC.
This tests control flow, not physical calibration precision or hardware safety.
Run: python tests/test_vpp_startup.py
"""
from pathlib import Path
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = (root / 'aftb_vpp.h').read_text()
start = source.index('static int8_t varVppCheckCalibration(void) {')
opening = source.index('{', start)
depth = 1
end = opening + 1
while depth:
    if source[end] == '{': depth += 1
    if source[end] == '}': depth -= 1
    end += 1
function = source[start:end]
assert '#if 0' not in function
assert 'varVppMeasureVpp(0)' in function
assert 'varVppSet(VPP_5V0)' in function
assert '#define VERSION "0.6.4"' in (root / 'src/afterburner.ino').read_text()

program = r'''
#include <cstdint>
#include <cassert>
#include <cstdio>
#define F(s) s
#define FAIL 0
#define OK 1
#define MAX_WIPER 16
#define VPP_9V0 0
#define VPP_5V0 255
uint8_t vppWiper[MAX_WIPER];
int16_t values[2];
int reads, sets;
uint8_t last, first;
struct SerialMock {
 void println(const char*) {}
 void print(const char*) {}
 void print(char) {}
 void print(int16_t) {}
 void println(int16_t) {}
} Serial;
void delay(int) {}
void varVppReadCalib() {}
void varVppSet(uint8_t v) { if (!sets) first=v; ++sets; last=v; }
int16_t varVppMeasureVpp(int) { assert(reads<2); return values[reads++]; }
'''+function+r'''
void prepare(int a, int b) {
 for (int i=0;i<MAX_WIPER;i++) vppWiper[i]=(uint8_t)(10+i*8);
 values[0]=a;values[1]=b;
 reads=sets=0;first=last=0;
}
int main() {
 prepare(900,902);assert(varVppCheckCalibration()==OK);assert(reads==2);assert(first==VPP_9V0 && last==VPP_5V0);
 prepare(930,931);assert(varVppCheckCalibration()==FAIL);assert(reads==2);assert(last==VPP_5V0);
 prepare(880,920);assert(varVppCheckCalibration()==FAIL);assert(reads==2);assert(last==VPP_5V0);
 prepare(900,900);vppWiper[2]=0;assert(varVppCheckCalibration()==FAIL);assert(reads==0);assert(last==VPP_5V0);
 prepare(900,900);vppWiper[7]=vppWiper[6]-1;assert(varVppCheckCalibration()==FAIL);assert(reads==0);assert(last==VPP_5V0);
 puts("PASS: measured VPP boot check and idle state on all exits");
}
'''
with tempfile.TemporaryDirectory() as tmp:
    cpp = Path(tmp) / 'vpp_test.cpp'
    binary = Path(tmp) / 'vpp_test'
    cpp.write_text(program)
    subprocess.run(['g++', '-std=c++17', '-Wall', '-Wextra', str(cpp), '-o', str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
