# Afterburner – odstranění ESP32-S2 pgmspace warning (oprava Q)

## Co řeší

Při `pio run -e esp32s2_saola1` se Arduino-ESP32 `pgmspace.h`
předefinuje oproti dřívějšímu `aftb_platform_compat.h`:
`pgm_read_byte`, `pgm_read_word`, `memcpy_P`.

Nový `aftb_platform_compat.h` nechává definice těchto maker
u ESP32 Arduino core a zachovává vlastní náhrady pouze pro platformy
mimo AVR a ESP32 (např. Uno R4 Renesas). To je cílená změna pouze
kompatibilitní hlavičky; nezasahuje do firmware, mapování pinů ani VPP.

## Instalace

Rozbal do kořene `C:\Dokumenty\GitHub\afterburner`, přepiš
`aftb_platform_compat.h` a spusť:

```powershell
pio run -e esp32s2_saola1 -e uno_r4_minima -e uno_r4_wifi
```

Pokud používáš jiný ESP32 framework, ověř dostupnost nativního
`pgmspace.h`; podpora se týká Arduino-ESP32 použitých v uvedeném logu.

## Ověření

Proběhla syntaktická kontrola hlavičky a testy větvení preprocesoru
pro AVR, ARM/Renesas a ESP32 makra. Skutečný PlatformIO build této
opravy v tomto prostředí nebyl proveden.

**Důležité:** Úspěšný build ESP32-S2 nepotvrzuje elektrickou kompatibilitu
s 5V Afterburner shieldem. ESP32-S2 GPIO není 5V tolerantní.
