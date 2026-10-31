namespace AfterburnerStudio.Models;

public enum PackageKind { Dip20, Dip24, SpecialAdapter, Jtag }

public sealed record ChipProfile(
    string Name,
    int FuseCount,
    PackageKind Package,
    string? Adapter,
    string MinimumBoard,
    string Note,
    bool Conditioning = false)
{
    public bool IsJtag => Package == PackageKind.Jtag;
    public bool NeedsAdapter => Adapter is not null;
    public int DipPins => Package switch
    {
        PackageKind.Dip20 => 20,
        PackageKind.Dip24 => 24,
        _ => 0
    };

    public static IReadOnlyList<ChipProfile> All { get; } = new List<ChipProfile>
    {
        new("GAL16V8", 2194, PackageKind.Dip20, null, "3.0+", "DIP20. Pin 1 at upper right, next to the lever; USB on the left."),
        new("GAL18V10", 3540, PackageKind.Dip20, null, "3.1+", "Requires PCB 3.1 or a verified modification of an older board."),
        new("GAL20V8", 2706, PackageKind.Dip24, null, "3.0+", "Direct placement in the ZIF24 socket."),
        new("GAL20RA10", 3274, PackageKind.Dip24, null, "3.0+", "Check the PCB revision and device variant before use."),
        new("GAL20XV10", 1671, PackageKind.Dip24, null, "3.0+", "Check the PCB revision and device variant before use."),
        new("GAL22V10", 5892, PackageKind.Dip24, null, "3.0+", "Direct placement in the ZIF24 socket."),
        new("GAL26CV12", 6432, PackageKind.SpecialAdapter, "Dedicated GAL26CV12 adapter (PLCC28)", "adapter dependent", "Use the dedicated adapter schematic; a generic PLCC28 to DIP24 adapter is unsuitable."),
        new("GAL26V12", 7912, PackageKind.SpecialAdapter, "GAL26V12 adapter", "adapter dependent", "Requires an adapter. Check wiring and placement against its PCB documentation."),
        new("GAL6001", 8294, PackageKind.Dip24, null, "3.0+", "Direct DIP24 placement; check the device variant and hardware limitations."),
        new("GAL6002", 8330, PackageKind.Dip24, null, "3.0+", "Direct DIP24 placement; check the device variant and hardware limitations."),
        new("ATF16V8B", 2194, PackageKind.Dip20, null, "3.0+", "Also supports an identified ATF16V8C variant. Initial conditioning is optional.", true),
        new("ATF20V8B", 2706, PackageKind.Dip24, null, "3.0+", "Optional initial conditioning for a new ATF device.", true),
        new("ATF22V10B", 5892, PackageKind.Dip24, null, "3.0+", "Optional initial conditioning for a new ATF device.", true),
        new("ATF22V10C", 5892, PackageKind.Dip24, null, "3.0+", "Optional initial conditioning for a new ATF device.", true),
        new("ATF750C", 14499, PackageKind.Dip24, null, "3.0+", "Large fusemap; operation on UNO R3 requires sparse mode."),
        new("PEEL18CV8", 2696, PackageKind.SpecialAdapter, "PEEL18CV8 programming adapter", "3.2+", "Requires the dedicated programming adapter and board 3.2 or a modified older board."),
        new("ATF1502AS", 0, PackageKind.Jtag, "PLCC44 adapter or JTAG cable", "wiring dependent", "XSVF over JTAG only. Fusemap readback is not supported."),
        new("ATF1504AS", 0, PackageKind.Jtag, "PLCC44 adapter or JTAG cable", "wiring dependent", "XSVF over JTAG only. Fusemap readback is not supported.")
    };
}


