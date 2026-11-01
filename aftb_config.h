#pragma once
// Board-dependent feature defaults. -D overrides are allowed when explicitly
// validating a custom board/build. Firmware programming protocol stays v0.6.3.

#if defined(__AVR_ATmega32U4__) || defined(ARDUINO_AVR_LEONARDO) || \
    defined(ARDUINO_AVR_MICRO) || defined(ARDUINO_AVR_YUN)
  // The 32U4 has only ~28 KiB usable flash and 2.5 KiB RAM. Default to
  // core GAL/PEEL functionality; the JTAG player/exerciser are too large to
  // presume safe without real board-specific size tests.
  #ifndef CFG_USE_GAL600X
    #define CFG_USE_GAL600X 0
  #endif
  #ifndef CFG_USE_AFT150X
    #define CFG_USE_AFT150X 0
  #endif
  #ifndef CFG_USE_PEEL
    #define CFG_USE_PEEL 1
  #endif
  #ifndef CFG_USE_EXERCISER
    #define CFG_USE_EXERCISER 0
  #endif
#else
  #ifndef CFG_USE_GAL600X
    #define CFG_USE_GAL600X 1
  #endif
  #ifndef CFG_USE_AFT150X
    #define CFG_USE_AFT150X 1
  #endif
  #ifndef CFG_USE_PEEL
    #define CFG_USE_PEEL 1
  #endif
  #ifndef CFG_USE_EXERCISER
    #define CFG_USE_EXERCISER 1
  #endif
#endif
