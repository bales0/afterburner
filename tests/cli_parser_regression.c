/* Offline regression tests for the legacy CLI JEDEC parser.
 * Build (from repo root):
 * gcc -std=gnu11 -DGCOM='"test"' -o /tmp/afterburner_cli_parser_test tests/cli_parser_regression.c src_pc/exerciser.c
 * /tmp/afterburner_cli_parser_test
 */
#define main original_afterburner_main
#include "../src_pc/afterburner.c"
#undef main
#include <assert.h>

static void check(const char* text, int expected_ok)
{
    char input[256];
    assert(strlen(text) < sizeof(input));
    strcpy(input, text);
    gal = ATF16V8B;
    memset(fusemap, 0, sizeof(fusemap));
    int rc = parseFuseMap(input);
    if ((rc == 0) != expected_ok) {
        fprintf(stderr, "JEDEC test failed: %s (result %d)\n", text, rc);
        exit(1);
    }
}

int main(void)
{
    check("\002*QF2194*F0*C0000*\003", 1);
    check("\002*QF2194*F0*L0000 1*C0001*\003", 1);
    check("\002*QF2194*F0*L0000 1*C0000*\003", 0); /* incorrect checksum */
    check("\002*QF2194*F0*L29999 11*C0000*\003", 0); /* buffer overrun */
    check("\002*QF2194*F0*L999999999999 1*C0000*\003", 0); /* int overflow */
    check("\002*QF2194*F3*C0000*\003", 0); /* syntax */
    puts("PASS: 6 CLI JEDEC parser regression cases");
    return 0;
}
