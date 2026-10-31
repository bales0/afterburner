# Pin placement verification

Scope: the Afterburner 3.x ZIF24 board shown in the local reference photograph. The illustration uses a **top view with USB on the left and the lever on the right**. It does not claim that an arbitrary modified/older board or adapter has identical wiring.

## Primary evidence

- Local `design/afterburner/afterburner_fab.pcb`: socket contact rows are at y=850 mil and y=1150 mil, x=1400 through 2500 mil. The square pad at **(2500,850 mil)** marks the upper-right pin-1 location. The square marker and the matching device notch in the board photograph establish the orientation used in this application.
- Local `img/afterburner_new_design.jpg`: USB connector at the left, ZIF lever and the device notch at the right; the DIP20 example leaves two positions unused on each row at the left.
- Local `src/afterburner.ino`: `PINOUT_16V8` uses the ZIF24 pin names for the 20-pin placement; the 20-pin part is not centered in the socket.
- Upstream [Afterburner README](https://github.com/ole00/afterburner/blob/master/README.md) says that 16V8 devices go near the ZIF lever and refers to the title photograph. [Board photograph](https://github.com/ole00/afterburner/blob/master/img/afterburner_new_design.jpg).

The old normalized drawing put pin 1 at the upper left while also putting the notch on the left. That mirrored the standard top-view DIP numbering. It is no longer included in this package.

## Mapping used by the application

Rows below are **left to right**:

| Row | ZIF contacts | DIP24 pins | DIP20 pins |
| --- | --- | --- | --- |
| Upper | 12,11,10,9,8,7,6,5,4,3,2,1 | 12,11,10,9,8,7,6,5,4,3,2,1 | unused,unused,10,9,8,7,6,5,4,3,2,1 |
| Lower | 13,14,15,16,17,18,19,20,21,22,23,24 | 13,14,15,16,17,18,19,20,21,22,23,24 | unused,unused,11,12,13,14,15,16,17,18,19,20 |

`Models/SocketLayout.cs` supplies the runtime drawing. The headless regression tests check each device pin appears exactly once and that DIP20 pin 11 occupies ZIF15. The SVG generator uses the same formulas. Adapter profiles show no device-pin placement or invented adapter wiring.

## Local evidence hashes (SHA-256)

- `afterburner_fab.pcb`: `C63BD226F5C29ACD8439F2D81B612A151703D86D336C638EB83F04A712E3A3DE`
- `afterburner_new_design.jpg`: `4B62585825044513FE0608AA99AA3A8E9C092C84644D2D2098BE6CA25E9CA520`
- `src/afterburner.ino`: `7F1066F0D52DE16AA66BE68AE97569AFDD17B378B7A5AFB47277FCCFACC46C42`

Date checked: 2026-10-31. This is source/photo verification, not an electrical continuity measurement of the user's physical programmer.
