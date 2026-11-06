#!/usr/bin/env python3
"""Host-side regression tests: compile the actual firmware serial/upload functions.

No Arduino or programmer is required. The test extracts unmodified C++ function
bodies from src/afterburner.ino and compiles them with small deterministic stubs.
For real VPP/CS behavior, see docs/STABILITA_A_SRAM_TEST_CZ.md.
"""
from pathlib import Path
import subprocess
import tempfile
import shutil

source = (Path(__file__).resolve().parents[1] / 'src/afterburner.ino').read_text()
def section(start, finish):
    return source[source.index(start):source.index(finish, source.index(start))]
functions = section('static bool isGalTypeEnabledInBuild(', '// print some help on the serial console')
functions += section('void readGarbage() {', '// Parses decimal integer number typed')
functions += section('unsigned short parse45dec(', '// Parses a line fed by the serial connection')
functions += section('static uint8_t uploadLineLength(void)', '// *********************************************************')
header = r'''
#include <cstdint>
#include <cstring>
#include <string>
#include <sstream>
#include <deque>
#include <iostream>
#include <stdexcept>
#include <algorithm>
#define F(x) x
#define DEC 10
#define HEX 16
#define INPUT 0
#define FLAG_BIT_EXERCISE 1
#define FLAG_BIT_APD 2
#define COMMAND_NONE 0
#define COMMAND_UNKNOWN 1
#define COMMAND_SET_GAL_TYPE 'g'
#define COMMAND_CALIBRATION_OFFSET 'B'
#define COMMAND_JTAG_PLAYER 'j'
#define COMMAND_EXERCISE 'X'
#define COMMAND_EXERCISE_SET_PINS 'x'
#define UNKNOWN 0
#define GAL6001 9
#define GAL6002 10
#define PEEL18CV8 16
#define LAST_GAL_TYPE 17
using GALTYPE = int;
struct FakeSerial {
    std::deque<char> q;
    std::string out;
    int available() { return (int)q.size(); }
    char read() { char c=q.front();q.pop_front(); return c; }
    template <typename T> void print(T v, int base=10) {
        std::ostringstream s;
        if(base==16) s<<std::hex;
        s << v; out += s.str();
    }
    void println() { out += "\n"; }
    template <typename T> void println(T v, int base=10) { print(v,base);out += "\n"; }
} Serial;
char line[32] = {};
uint8_t lineIndex = 0;
char endOfLine=0,isUploading=0,discardUntilEol=0,uploadTypeSelected=0;
char uploadError=0,uploadChecksumOk=0,mapUploaded=0,uploadDataReceived=0,fuseMapOverflow=0;
char echoEnabled=0,flagBits=0;
short progtime=100;
uint8_t pes[12] = {};
uint8_t fusemap[2000] = {};
GALTYPE gal=1;
struct { uint16_t fuses; } galinfo={100};
int dangerousCommands = 0;
bool forceFusemapOverflow = false; // simulate a full compressed fusemap
void setFlagBit(uint8_t f,uint8_t v) {(void)f;(void)v;}
void setupGpios(int) {}
void copyGalInfo() {}
void sparseSetup(int) {}
void setFuseBit(unsigned short bit) {
    if (forceFusemapOverflow || bit>=sizeof(fusemap)*8) {fuseMapOverflow=1;return;}
    fusemap[bit/8] |= (1<<(bit%8));
}
unsigned short checkSum(unsigned short bitCount) {
    unsigned short result=0;
    for(unsigned short i=0;i<(bitCount+7)/8;i++) result+=fusemap[i];
    return result;
}
'''
header += '#include "' + (Path(__file__).resolve().parents[1] / 'aftb_config.h').as_posix() + '"\n'
footer = r'''
static void require(bool p, const std::string& message) { if (!p) throw std::runtime_error(message); }
static void drain() {
    int counter=0;
    while (Serial.available() > 0 && ++counter < 10000) {
        char cmd=handleTerminalCommands();
        if (isUploading && cmd != '#' && cmd != 0) {isUploading=0;mapUploaded=0;uploadError=1;}
        if (cmd=='u') {
            memset(fusemap,0,sizeof(fusemap));isUploading=1;uploadTypeSelected=0;
            uploadError=0;uploadDataReceived=0;uploadChecksumOk=0;mapUploaded=0;fuseMapOverflow=0;
        } else if (cmd=='#') {
            if(isUploading) parseUploadLine();
        } else if (cmd=='w') { if(mapUploaded) dangerousCommands++; }
    }
    require(counter < 10000, "serial loop did not terminate");
}
static void feed(std::string s) {for(char c:s) Serial.q.push_back(c);drain();}
static void reset() {
    Serial.q.clear();Serial.out.clear();lineIndex=0;endOfLine=0;isUploading=0;
    discardUntilEol=0;uploadTypeSelected=0;uploadError=0;uploadChecksumOk=0;
    uploadDataReceived=0;mapUploaded=0;fuseMapOverflow=0;echoEnabled=0;
    dangerousCommands=0;forceFusemapOverflow=false;galinfo.fuses=100;
    memset(fusemap,0,sizeof(fusemap));
}
static void rejectRow(std::string row) {
    reset();feed("u\r#t 1 ATF16V8B\r");feed(row+"\r");
    require(uploadError, "malformed input was accepted: "+row);
    feed("#c 0000\r#e\r");
    require(!mapUploaded, "malformed upload became valid after checksum/end: "+row);
}
int main() {
#if !CFG_USE_GAL600X
    reset();feed("u\r#t 9 GAL6001\r#c 0000\r#e\rw\r");
    require(uploadError && !mapUploaded && dangerousCommands==0,
            "disabled GAL6001 upload was accepted");
    require(Serial.out.find("ER chip type unavailable") != std::string::npos,
            "missing disabled-chip diagnostic");
#endif
    reset();feed("u\r#t 1 ATF16V8B\r#f 0000 01\r#f 0032 02\r#c 0003\r#e\r");
    require(mapUploaded && !uploadError && fusemap[0]==1 && fusemap[4]==2, "valid sparse upload failed");
    reset();feed("u\r#t 1\r#e\r");
    require(!uploadError && !mapUploaded && !isUploading, "valid type-only upload failed");
    reset();feed("u\r#t @ PEEL18CV8\r#e\r");
    require(!uploadError && gal==16, "non-digit firmware type ID was rejected");
    reset();galinfo.fuses=14500;feed("u\r#t 1\r#f 10000 01\r");
    require(!uploadError && fusemap[1250]==1, "5-digit offset was rejected");
    rejectRow("#f 00A0 01");
    rejectRow("#f 0000 0");
    rejectRow("#f 0000 01Z");
    rejectRow("#f 0000 01 ");
    rejectRow("#f 0000 01XYZ");
    rejectRow("#f 0000");
    rejectRow("#t invalid");
    rejectRow("#c 1ZZZ");
    reset();feed("u\r#t 1\r#c 0001\r#e\r");
    require(uploadError && !mapUploaded, "checksum mismatch was accepted");
    reset();feed("u\r#t 1\r#f 0099 80\r#c 0080\r#e\r");
    require(uploadError && !mapUploaded, "nonzero out-of-range final bit accepted");
    // SRAM capacity failure must report a specific error and invalidate the
    // transaction irreversibly, even if checksum/end and a write follow.
    reset();forceFusemapOverflow=true;
    feed("u\r#t 1\r#f 0000 01\r");
    require(fuseMapOverflow && uploadError && !mapUploaded, "overflow did not reject upload");
    require(Serial.out.find("ER fusemap overflow: 2000 bytes capacity") != std::string::npos,
            "overflow capacity diagnostic missing");
    feed("#c 0000\r#e\rw\r");
    require(!mapUploaded && dangerousCommands==0, "invalid fusemap was allowed to program");
    reset();feed("u\r#t 1\r#p 00:11:22:33:44:55:66:77\r#e\r");
    require(!uploadError && pes[7]==0x77, "valid PES upload was rejected");
    reset();feed("u\r#t 1\r#p 00:11:22:33:44:55:GG:77\r#e\r");
    require(uploadError && !mapUploaded, "invalid PES upload accepted");
    reset();feed("u\r#t 1\r");
    feed("#f 0000 00" + std::string(70, 'A'));
    require(discardUntilEol && !isUploading && !mapUploaded, "overflow did not abort upload");
    feed("w\r");
    require(!discardUntilEol && dangerousCommands==0 && !mapUploaded, "overflow tail executed");
    reset();feed("u\r#t 1\r#f 00");
    require(isUploading && lineIndex>0, "partial line did not buffer");
    feed("00 01\r#c 0001\r#e\r");
    require(mapUploaded && !uploadError, "fragmented valid row failed");
    reset();feed("u\r#t 1\r#e EXTRA\r");
    require(uploadError && !mapUploaded, "bad upload end accepted");
    std::cout << "PASS: firmware serial/upload regression suite (real source functions)\n";
}
'''
if shutil.which('g++') is None:
    raise SystemExit('g++ required for host regression test')
with tempfile.TemporaryDirectory() as td:
    src = Path(td)/'firmware_serial.cpp'
    exe = Path(td)/'firmware_serial_test'
    src.write_text(header+functions+footer)
    for flags in ([], ['-D__AVR_ATmega32U4__']):
        subprocess.run(['g++','-std=c++17','-Wall','-Wextra','-Werror','-Wno-error=sign-compare','-O1',*flags,str(src),'-o',str(exe)],check=True)
        subprocess.run([str(exe)],check=True)
