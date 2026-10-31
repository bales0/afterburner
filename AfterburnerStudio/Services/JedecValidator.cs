using System.Text;
using System.Text.RegularExpressions;
using AfterburnerStudio.Models;

namespace AfterburnerStudio.Services;

public sealed record JedecReport(bool Valid, string Message, int? Qf, int? Qp, ushort? DeclaredChecksum);
public sealed record JedecImage(byte[] FuseBits, int FuseCount, int? PinCount, ushort? Checksum);

/// <summary>
/// Validates AND parses JEDEC without using the original unbounded C parser.
/// The returned bit array is the only data uploaded to the programmer.
/// </summary>
public static class JedecValidator
{
    private static readonly Regex QfRx = new(@"^QF\s*(\d+)\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex QpRx = new(@"^QP\s*(\d+)\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex DefaultRx = new(@"^F\s*([01])\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex LoadRx = new(@"^L\s*(\d+)\s+([01\s]+)$", RegexOptions.IgnoreCase);
    private static readonly Regex CheckRx = new(@"^C\s*([0-9A-F]{4})\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex MetadataRx = new(@"^(G0|QV\d+|X\d+|N.*|U.*|P.*|E.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static JedecReport Validate(string filePath, ChipProfile chip)
    {
        try
        {
            if (chip.IsJtag)
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists || fi.Extension.ToLowerInvariant() != ".xsvf" || fi.Length == 0 || fi.Length > 256 * 1024)
                    return new(false, "JTAG requires a non-empty .xsvf file (maximum 256 KiB).", null, null, null);
                return new(true, "XSVF size and extension checked; the JTAG player validates the instructions.", null, null, null);
            }
            var file = new FileInfo(filePath);
            if (!file.Extension.Equals(".jed", StringComparison.OrdinalIgnoreCase))
                return new(false, "Select a JEDEC (.jed) file for this device.", null, null, null);
            if (!file.Exists || file.Length == 0 || file.Length > 256 * 1024)
                return new(false, "Invalid JEDEC file size (maximum 256 KiB).", null, null, null);
            return ValidateText(File.ReadAllText(filePath, Encoding.Latin1), chip);
        }
        catch (Exception e) { return new(false, "File error: " + e.Message, null, null, null); }
    }

    public static JedecReport ValidateText(string input, ChipProfile chip)
    {
        try
        {
            var image = ParseText(input, chip);
            return new(true, $"JEDEC: QF={image.FuseCount}, QP={(image.PinCount?.ToString() ?? "not specified")}; checksum {(image.Checksum is null ? "missing" : "OK")}",
                image.FuseCount, image.PinCount, image.Checksum);
        }
        catch (Exception e) { return new(false, "JEDEC rejected: " + e.Message, null, null, null); }
    }

    public static JedecImage ParseFile(string filePath, ChipProfile chip)
    {
        if (new FileInfo(filePath).Length is <= 0 or > 262144)
            throw new InvalidDataException("Invalid JEDEC size.");
        return ParseText(File.ReadAllText(filePath, Encoding.Latin1), chip);
    }

    public static JedecImage ParseText(string input, ChipProfile chip)
    {
        if (chip.IsJtag) throw new InvalidDataException("JTAG requires a binary XSVF file.");
        int stx = input.IndexOf('\x02');
        if (stx >= 0) input = input[(stx + 1)..];
        int etx = input.IndexOf('\x03');
        if (etx >= 0) input = input[..etx];
        int baseBits = chip.FuseCount;
        bool apd = chip.Name is "ATF16V8B" or "ATF22V10C";
        int allowed = baseBits + (apd ? 1 : 0);
        byte[] bits = new byte[allowed];
        int? qf = null, qp = null;
        ushort? declared = null;
        bool anyContent = false;
        string[] segments = input.Split('*');
        for (int ix = 0; ix < segments.Length; ix++)
        {
            string cmd = segments[ix].Trim();
            if (cmd.Length == 0 || ix == 0 && cmd.StartsWith("JEDEC file for", StringComparison.OrdinalIgnoreCase)) continue;
            Match m;
            if ((m = QfRx.Match(cmd)).Success)
            {
                qf = int.Parse(m.Groups[1].Value);
                if (qf != baseBits && !(apd && qf == allowed))
                    throw new InvalidDataException($"QF={qf}, expected {baseBits}" + (apd ? $" or {allowed}" : "") + ".");
            }
            else if ((m = QpRx.Match(cmd)).Success)
            {
                qp = int.Parse(m.Groups[1].Value);
                if (qp != (chip.DipPins == 0 ? 28 : chip.DipPins) && chip.DipPins != 0)
                    throw new InvalidDataException($"QP={qp} does not match DIP{chip.DipPins}.");
            }
            else if ((m = DefaultRx.Match(cmd)).Success)
            {
                Array.Fill(bits, m.Groups[1].Value == "1" ? (byte)1 : (byte)0);
                anyContent = true;
            }
            else if ((m = LoadRx.Match(cmd)).Success)
            {
                int start = int.Parse(m.Groups[1].Value);
                string value = new(m.Groups[2].Value.Where(c => c is '0' or '1').ToArray());
                if (start < 0 || start > allowed || value.Length > allowed - start)
                    throw new InvalidDataException("An L record extends beyond the fusemap.");
                for (int j = 0; j < value.Length; j++) bits[start + j] = (byte)(value[j] - '0');
                anyContent = true;
            }
            else if ((m = CheckRx.Match(cmd)).Success)
                declared = Convert.ToUInt16(m.Groups[1].Value, 16);
            else if (MetadataRx.IsMatch(cmd)) { }
            else if (cmd.StartsWith("G1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The JEDEC file requests G1 protection, which this mode does not write.");
            else if (ix == 0 && !cmd.Contains('L') && cmd.Length < 256) { /* header/comment */ }
            else throw new InvalidDataException("Unsupported JEDEC record: " + cmd[..Math.Min(42, cmd.Length)]);
        }
        if (qf is null) throw new InvalidDataException("Missing QF record; the file was rejected.");
        if (!anyContent) throw new InvalidDataException("Missing F/L data.");
        int count = qf.Value;
        if (declared is not null && declared != CalculateChecksum(bits, count))
            throw new InvalidDataException($"Declared checksum {declared:X4} does not match calculated checksum {CalculateChecksum(bits, count):X4}.");
        return new JedecImage(bits[..count], count, qp, declared);
    }

    public static ushort CalculateChecksum(ReadOnlySpan<byte> bits, int length)
    {
        uint sum = 0;
        for (int start = 0; start < length; start += 8)
        {
            byte b = 0;
            for (int i = 0; i < 8 && start + i < length; i++) if (bits[start + i] != 0) b |= (byte)(1 << i);
            sum += b;
        }
        return (ushort)sum;
    }

    public static string ConditioningJedec(int fuseCount) => "\x02\n*QF" + fuseCount + "*F0*C0000*\n\x03";

    public static string ExtractReadback(string output)
    {
        int start = output.IndexOf("*QP", StringComparison.OrdinalIgnoreCase);
        if (start < 0) throw new InvalidDataException("Readback does not contain *QP.");
        int end = output.LastIndexOf('*');
        if (end < start) throw new InvalidDataException("Readback is incomplete.");
        return "\x02\n" + output[start..(end + 1)] + "\n\x03\n";
    }
}

