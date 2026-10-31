namespace AfterburnerStudio.Models;

// Afterburner 3.x top view: USB left, ZIF lever right. See docs/PIN_VERIFICATION.md.
public static class SocketLayout
{
    public static int TopContact(int column) => 12 - column;
    public static int BottomContact(int column) => 13 + column;
    public static int FirstOccupiedColumn(int dipPins) => 12 - dipPins / 2;
    public static int? ChipPin(int dipPins, int column, bool top)
    {
        if (dipPins is not (20 or 24) || column < FirstOccupiedColumn(dipPins) || column > 11) return null;
        return top ? 12 - column : dipPins - 11 + column;
    }
}

