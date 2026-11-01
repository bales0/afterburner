#pragma once
// Determine RAM mode from the MCU, not from existence of analog pin A11.
// Some ATmega32U4 boards have A11 but only 2.5 KiB SRAM; forcing a
// 1813-byte fusemap leaves too little headroom for the USB stack.
#if defined(__AVR_ATmega32U4__) || defined(ARDUINO_AVR_LEONARDO) || \
    defined(ARDUINO_AVR_MICRO) || defined(ARDUINO_AVR_YUN)
  #ifdef RAM_BIG
    #undef RAM_BIG
  #endif
#elif defined(__AVR_ATmega2560__) || defined(__AVR_ATmega1280__) || \
      defined(_RENESAS_RA_) || defined(ARDUINO_ARCH_RENESAS) || \
      (defined(CONFIG_IDF_TARGET_ESP32S2) && CONFIG_IDF_TARGET_ESP32S2)
  #ifndef RAM_BIG
    #define RAM_BIG
  #endif
#endif

#ifdef RAM_BIG
  // Uncompressed ATF750C fusemap for MCUs with sufficient SRAM.
  #define MAXFUSES 1813
#else
  // Small SRAM variant uses sparse ATF750C map; refuse too-dense maps.
  #define MAXFUSES 1332
  #define USE_SPARSE_FUSEMAP
#endif
