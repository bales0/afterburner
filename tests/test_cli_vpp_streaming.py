#!/usr/bin/env python3
"""Test streamed VPP output is not duplicated, including split firmware prompt."""
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
assert shutil.which('gcc'), 'gcc required for regression'
harness=r'''
#define main existing_afterburner_main
#include "../src_pc/afterburner.c"
#undef main
#include <sys/socket.h>
#include <sys/wait.h>
#include <assert.h>

int main(void) {
    int fds[2];
    assert(socketpair(AF_UNIX,SOCK_STREAM,0,fds)==0);
    serialF = fds[0];
    assert(fcntl(serialF,F_SETFL,O_NONBLOCK)==0);
    pid_t pid=fork(); assert(pid>=0);
    if(pid==0){
        close(fds[0]);
        const char *part1="\r\nVPP: 9.0V : 9.03\r\nVPP: 10.0V : 10.02\r\n";
        assert(write(fds[1],part1,strlen(part1))==(ssize_t)strlen(part1));
        usleep(15000);
        assert(write(fds[1],"\r\n>",3)==3);
        usleep(15000);
        assert(write(fds[1],"\r",1)==1);
        usleep(15000);
        assert(write(fds[1],"\n",1)==1);
        close(fds[1]);_exit(0);
    }
    close(fds[1]);
    verbose=1;
    printSerialWhileWaiting=1;
    assert(sendGenericCommand("m\r","VPP test failed",1000,1)==0);
    printSerialWhileWaiting=0;
    close(serialF);serialF=INVALID_HANDLE;
    int status;
    assert(waitpid(pid,&status,0)==pid && WIFEXITED(status) && WEXITSTATUS(status)==0);
    return 0;
}
'''
with tempfile.TemporaryDirectory() as tmp:
    t=Path(tmp)
    (t/'tests').mkdir(); (t/'src_pc').mkdir()
    (t/'tests/stream_harness.c').write_text(harness)
    for f in ('afterburner.c','serial_port.h','exerciser.h'):
        shutil.copy(root/'src_pc'/f,t/'src_pc'/f)
    exe=t/'stream_test'
    subprocess.run(['gcc','-std=gnu11','-O2','-o',str(exe),str(t/'tests/stream_harness.c'),str(root/'src_pc/exerciser.c')], check=True)
    p=subprocess.run([str(exe)],capture_output=True,text=True,timeout=5)
    combined=p.stdout+p.stderr
    assert p.returncode==0,(p.returncode,combined)
    assert combined.count('VPP: 9.0V : 9.03')==1,combined
    assert combined.count('VPP: 10.0V : 10.02')==1,combined
    assert 'read:' not in combined,combined
    assert '>' not in combined,combined
print('PASS: VPP streamed values printed exactly once and serial prompt suppressed')
