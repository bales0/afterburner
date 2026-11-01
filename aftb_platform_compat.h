#pragma once
// Flash-table compatibility for non-AVR Arduino cores.
// This header is force-included only for the firmware source by PlatformIO.
// ESP32 Arduino already supplies PROGMEM/pgm_read_*/memcpy_P in pgmspace.h:
// do not redefine those macros before the core headers are included.
#if !defined(__AVR__) && !defined(ARDUINO_ARCH_ESP32) && !defined(ESP32)
#include <stdint.h>
#include <string.h>
#ifndef PROGMEM
#define PROGMEM
#endif
#ifndef pgm_read_byte
#define pgm_read_byte(addr) (*(const uint8_t *)(addr))
#endif
#ifndef pgm_read_word
#define pgm_read_word(addr) (*(const uint16_t *)(addr))
#endif
#ifndef memcpy_P
#define memcpy_P(dst,src,len) memcpy((dst),(src),(len))
#endif
#endif
