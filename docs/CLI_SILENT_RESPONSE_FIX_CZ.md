# Oprava tichych odpovedi CLI (31. 10. 2026)

Log `sending 'f' command; read: 5 ''; result=-1` ukazuje chybne
vyhodnoceni odpovedi, nikoli nutne chybu firmwaru.

Firmware na konfiguracni prikazy `f`, `F` a `g<typ>` nevypisuje text.
Odpoved pouze `\r\n>\r\n` ma 5 bajtu. `sendLine()` spravne
rozpoznalo prompt, ale `sendGenericCommand()` chybne odmital prazdny
obsah. Nyni akceptuje prazdny obsah vyhradne u techto prikazu a
stale vyzaduje kompletny prompt. Jine prikazy (napr. zapis, overeni,
mazani) prazdnou odpoved nadale odmitaji.

Puvodni cisla verzi nejsou zmenena.

## Overeni

- CLI GCC compile: provedeno v lokalnim prostredi.
- Regressni test `tests/cli_serial_regression.c`: provedeno vcetne f/F/g a ochrany w.
- Fyzicka komunikace s programatorem na COM5: neprovedena.

Poznamka: log uvadi firmware `v.0.6.3`; archiv obsahuje firmware
`v.0.6.4`, ktere je nutne samostatne nahrat do Arduina, ma-li byt
pouzita nova ochrana VPP. Tato oprava tichych odpovedi je na strane CLI
a funguje i s firmwarem 0.6.3.
