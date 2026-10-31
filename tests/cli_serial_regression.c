/* Test nonblocking reads, missing prompt and fragmented JTAG requests.
 * Linux/POSIX offline test (not a real programmer).
 * gcc -std=gnu11 -O1 -o /tmp/afterburner_cli_serial_test tests/cli_serial_regression.c src_pc/exerciser.c
 */
#define main afterburner_existing_main
#include "../src_pc/afterburner.c"
#undef main
#include <assert.h>
#include <sys/socket.h>
#include <sys/types.h>
#include <sys/wait.h>

static int peer;
static void start_serial(void) {
    int fds[2];
    assert(socketpair(AF_UNIX, SOCK_STREAM, 0, fds) == 0);
    serialF = fds[0]; peer = fds[1];
    assert(fcntl(serialF, F_SETFL, O_NONBLOCK) == 0);
}
static void close_serial(void) { close(serialF); close(peer); serialF = INVALID_HANDLE; }

int main(void) {
    char buffer[256];
    start_serial();
    assert(write(peer, "OK one\r\n>\r\n", 11) == 11);
    assert(waitForSerialPrompt(buffer, sizeof(buffer), 300) == 11);
    assert(strstr(buffer, "OK one") != NULL);
    close_serial();

    start_serial();
    assert(write(peer, "OK but not finished\r\n", 21) == 21);
    assert(waitForSerialPrompt(buffer, sizeof(buffer), 40) == -1);
    close_serial();

    // Firmware intentionally returns only a prompt for f/F/g commands.
    for (const char **cmd = (const char*[]){ "f\r", "F\r", "g1\r", NULL }; *cmd; ++cmd) {
        start_serial();
        assert(write(peer, "\r\n>\r\n", 5) == 5);
        assert(sendGenericCommand(*cmd, "unexpected failure", 300, 0) == 0);
        close_serial();
    }

    // An empty response must not be enough to claim a successful program/erase.
    start_serial();
    assert(write(peer, "\r\n>\r\n", 5) == 5);
    assert(sendGenericCommand("w\r", "expected refusal", 300, 0) == -1);
    close_serial();

    start_serial();
    assert(write(peer, "ER first\r\nOK misleading\r\n>\r\n", 29) == 29);
    assert(sendGenericCommand("g1\r", "failed", 300, 0) == -1);
    close_serial();

    start_serial();
    pid_t p = fork(); assert(p >= 0);
    if (p == 0) {
        const char data[] = "$123\r\n";
        for (int i = 0; i < 6; ++i) { assert(write(peer, &data[i], 1) == 1); usleep(3000); }
        _exit(0);
    }
    int request = 0;
    assert(readJtagSerialLine(buffer, sizeof(buffer), 3000, &request) >= 0);
    assert(request == 123);
    waitpid(p, NULL, 0);
    close_serial();

    start_serial();
    assert(write(peer, "$12X\r\n", 6) == 6);
    assert(readJtagSerialLine(buffer, sizeof(buffer), 300, &request) == -1);
    close_serial();
    puts("PASS: serial prompt, timeout, ER precedence, fragmented JTAG, malformed request");
    return 0;
}
