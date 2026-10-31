#pragma once
// GAL fuse buffer size; kept in a header rather than the .ino because
// PlatformIO's Arduino prototype generator otherwise reports spurious macro
// redefinition warnings when it scans both preprocessor branches.
// Large-RAM targets store the full ATF750C map; Uno uses sparse storage.
#ifdef RAM_BIG
#define MAXFUSES 1813
#else
#define MAXFUSES 1332
#define USE_SPARSE_FUSEMAP
#endif
