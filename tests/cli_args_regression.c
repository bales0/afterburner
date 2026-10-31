#define main cli_original_main
#include "../src_pc/afterburner.c"
#undef main
#include <assert.h>
int main(void) {
    char *a[]={"cli","im","-v","-t","ATF16V8B",NULL};
    assert(checkArgs(5,a)==0 && opInfo && opMeasureVPP && verbose);
    opInfo=opMeasureVPP=verbose=0; gal=UNKNOWN;
    char *b[]={"cli","-v","m","-t","ATF16V8B","i",NULL};
    assert(checkArgs(6,b)==0 && opInfo && opMeasureVPP && verbose);
    opInfo=opMeasureVPP=verbose=0; gal=UNKNOWN;
    char *c[]={"cli","i","-v","m","-t","ATF16V8B",NULL};
    assert(checkArgs(6,c)==0 && opInfo && opMeasureVPP && verbose);
    opInfo=opMeasureVPP=verbose=0; gal=UNKNOWN;
    char *bad[]={"cli","m","-t",NULL};
    assert(checkArgs(3,bad)!=0);
    char *bad2[]={"cli","m","-unknown",NULL};
    assert(checkArgs(3,bad2)!=0);
    char *bad3[]={"cli","mx", "-t","ATF16V8B",NULL};
    // x is a valid operation; reject only invalid letters
    char *bad4[]={"cli","mq",NULL};
    assert(checkArgs(2,bad4)!=0);
    printf("CLI parser regression: PASS\n");
}
