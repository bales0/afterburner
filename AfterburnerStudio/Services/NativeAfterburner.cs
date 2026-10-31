using System.Diagnostics;
using System.Globalization;
using System.Text;
using AfterburnerStudio.Models;

namespace AfterburnerStudio.Services;

public sealed record NativeResult(bool Succeeded, string Output, string? Error = null);

/// <summary>
/// Native host implementation of the original afterburner.ino text/serial wire protocol.
/// Does not spawn binaries, shells, external CLI programs or use a serial terminal.
/// All serial operations run on a worker thread; one open port is held per operation.
/// Firmware reference: bales0/afterburner @ 7636cc7, afterburner.ino,
/// src_pc/afterburner.c (used only as protocol documentation).
/// </summary>
public sealed class NativeAfterburner
{
    private readonly StringBuilder _output = new();
    private IProgress<string>? _log;
    private CancellationToken _token;
    private Stopwatch _clock = new();
    private TimeSpan _limit;
    private ISerialTransport? _port;
    private readonly Func<string, ISerialTransport> _transportFactory;
    public NativeAfterburner(Func<string, ISerialTransport>? transportFactory = null) =>
        _transportFactory = transportFactory ?? (name => new SerialTransport(name));
    private bool _varVpp;
    private bool _calibrationMissing;
    private const int ResponseMax = 320_000;

    public Task<NativeResult> RunAsync(string port, ChipProfile chip, string action, byte[]? fuseBits,
        byte[]? xsvf, byte[]? eraseXsvf, int offset, IProgress<string> log, TimeSpan timeout, CancellationToken token)
    {
        // All state belongs to one operation; the UI serializes calls.
        return Task.Run(() => RunSync(port, chip, action, fuseBits, xsvf, eraseXsvf, offset, log, timeout, token), token);
    }

    private NativeResult RunSync(string port, ChipProfile chip, string action, byte[]? fuseBits,
        byte[]? xsvf, byte[]? eraseXsvf, int offset, IProgress<string> log, TimeSpan timeout, CancellationToken token)
    {
        _token = token;
        _log = log;
        _limit = timeout;
        _clock = Stopwatch.StartNew();
        _output.Clear();
        _varVpp = false;
        _calibrationMissing = false;
        try
        {
            if (string.IsNullOrWhiteSpace(port)) throw new InvalidOperationException("No serial port selected.");
            _port = _transportFactory(port);
            _port.Open();
            Connect();
            if (_calibrationMissing && action is not ("s" or "b" or "m"))
                throw new InvalidOperationException("Variable VPP has no calibration in EEPROM. Calibrate and verify the voltage before programming.");
            if (action is "s" or "b" or "m")
            {
                RunCalibration(action, offset);
            }
            else if (chip.IsJtag)
            {
                RunJtag(action, xsvf, eraseXsvf);
            }
            else
            {
                // Enable PES/type protection before selecting a new GAL.
                Command("f", show: false);
                int id = ChipProfile.All.ToList().FindIndex(c => c.Name == chip.Name) + 1;
                if (id <= 0 || id > 16) throw new InvalidOperationException("The GAL type has no supported firmware ID.");
                Command("g" + (char)('0' + id), show: false);
                if (action is "r" or "e") SetUploadType(chip);
                switch (action)
                {
                    case "i": Command("p"); break;
                    case "r": Command("r", show: true, largeResponse: true); break;
                    case "e": Command("c"); break; // Normal erase. Never erase PES.
                    case "w":
                    case "v":
                    case "wv":
                        if (fuseBits is null) throw new InvalidOperationException("No validated JEDEC fusemap supplied.");
                        UploadFuseMap(chip, fuseBits);
                        if (action.Contains('w')) Command("w");
                        if (action.Contains('v')) Command("v");
                        break;
                    default: throw new NotSupportedException("Unsupported operation: " + action);
                }
            }
            return new NativeResult(true, _output.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.Report("ERROR: " + ex.Message);
            return new NativeResult(false, _output.ToString(), ex.Message);
        }
        finally
        {
            // The firmware must finish its own timed VPP action. Closing serial
            // DOES NOT guarantee immediate hardware power-off on older firmware.
            try { _port?.Close(); } catch { }
            _port?.Dispose();
            _port = null;
        }
    }

    private void Check()
    {
        _token.ThrowIfCancellationRequested();
        if (_clock.Elapsed >= _limit) throw new TimeoutException("Operation timed out (" + _limit.TotalSeconds + " s). Check VPP on the programmer.");
    }

    private int ReadByte()
    {
        while (true)
        {
            Check();
            try { return _port!.ReadByte(); }
            catch (TimeoutException) { /* cancellation polling */ }
        }
    }

    private void Write(string s)
    {
        Check();
        _port!.Write(s + "\r");
    }

    private void Connect()
    {
        // Initial USB serial open may reset UNO. Probe until firmware is ready,
        // ignore boot text/partial responses, never send programming commands
        // until a valid banner and command prompt are both observed.
        var deadline = TimeSpan.FromSeconds(9);
        var remaining = _limit - _clock.Elapsed;
        if (remaining < deadline) deadline = remaining;
        while (_clock.Elapsed < deadline)
        {
            Check();
            _port!.DiscardInBuffer();
            Write("*");
            try
            {
                string response = ReadPrompt(TimeSpan.FromMilliseconds(950), 8192, false);
                if (response.Contains("AFTerburner v.", StringComparison.Ordinal) && response.EndsWith(">\r\n", StringComparison.Ordinal))
                {
                    _varVpp = response.Contains("varVpp", StringComparison.OrdinalIgnoreCase);
                    _calibrationMissing = response.Contains("VPP-CAL-MISSING", StringComparison.OrdinalIgnoreCase);
                    Log("Connected firmware: " + response.Split('\n').FirstOrDefault(x => x.Contains("AFTerburner v."))?.Trim());
                    Log(_varVpp ? "Variable VPP board detected." : "Variable VPP was not detected; calibration is unavailable.");
                    return;
                }
            }
            catch (TimeoutException) { }
            Thread.Sleep(170);
        }
        throw new IOException("No AFTerburner banner and prompt received. Check the port, firmware and board reset.");
    }

    private string ReadPrompt(TimeSpan stageTimeout, int maxChars, bool stream)
    {
        var start = _clock.Elapsed;
        var sb = new StringBuilder();
        var line = new StringBuilder();
        while (true)
        {
            Check();
            if (_clock.Elapsed - start > stageTimeout) throw new TimeoutException("The firmware did not respond in time.");
            int x;
            try { x = _port!.ReadByte(); }
            catch (TimeoutException) { continue; }
            if (sb.Length >= maxChars) throw new IOException("Serial response is too long.");
            char ch = (char)x;
            sb.Append(ch);
            if (ch == '\n')
            {
                var info = line.ToString().Trim();
                if (stream && info.Length > 0 && info != ">" && !info.StartsWith("OK ")) Log(info);
                line.Clear();
            }
            else if (ch != '\r') line.Append(ch);
            if (sb.Length >= 3 && sb[sb.Length - 3] == '>' && sb[sb.Length - 2] == '\r' && sb[sb.Length - 1] == '\n')
                return sb.ToString();
        }
    }

    private void Log(string message)
    {
        _log?.Report(message);
        if (_output.Length + message.Length < 1_200_000) _output.AppendLine(message);
    }

    private string Command(string command, bool show = true, bool largeResponse = false, int seconds = 24)
    {
        Check();
        Write(command);
        string body = ReadPrompt(TimeSpan.FromSeconds(seconds), largeResponse ? ResponseMax : 16_384, show && !largeResponse);
        string[] lines = body.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l != ">" && l != command).ToArray();
        // Arduino reports error lines starting ER or "ER ". Do not accept a prompt as success if firmware refused an operation.
        string? error = lines.FirstOrDefault(l =>
            l.StartsWith("ER", StringComparison.Ordinal) ||
            l.StartsWith("Error:", StringComparison.OrdinalIgnoreCase));
        if (error is not null) throw new IOException("Firmware rejected command " + command + ": " + error);
        if (show)
        {
            if (largeResponse) Log(body); // preserve complete JEDEC readback
            // Other command lines were emitted live by ReadPrompt().
        }
        return body;
    }

    private void SetUploadType(ChipProfile chip)
    {
        // Matches legacy CLI: type selection in upload mode for read/erase.
        int id = ChipProfile.All.ToList().FindIndex(c => c.Name == chip.Name) + 1;
        Command("u", show: false);
        Command("#t " + (char)('0' + id), show: false);
        Command("#e", show: false);
    }

    private void UploadFuseMap(ChipProfile chip, byte[] bits)
    {
        int total = chip.FuseCount;
        bool canApd = chip.Name is "ATF16V8B" or "ATF22V10C";
        if (bits.Length < total || bits.Length > total + (canApd ? 1 : 0))
            throw new InvalidDataException("Invalid fusemap length.");
        bool apd = canApd && bits.Length > total && bits[total] == 1;
        Command(apd ? "z" : "Z", show: false);
        if (apd) total++;
        int id = ChipProfile.All.ToList().FindIndex(c => c.Name == chip.Name) + 1;
        Command("u", show: false);
        Command("#t " + (char)('0' + id) + " " + chip.Name, show: false);
        uint checksum = 0;
        for (int bitStart = 0; bitStart < total; bitStart += 32)
        {
            var hex = new StringBuilder();
            bool nonzero = false;
            for (int byteAt = 0; byteAt < 4 && bitStart + 8 * byteAt < total; byteAt++)
            {
                byte value = 0;
                for (int bit = 0; bit < 8 && bitStart + byteAt * 8 + bit < total; bit++)
                    if (bits[bitStart + byteAt * 8 + bit] != 0) value |= (byte)(1 << bit);
                checksum += value;
                nonzero |= value != 0;
                hex.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
            if (nonzero) Command("#f " + bitStart.ToString("D4", CultureInfo.InvariantCulture) + " " + hex, show: false);
            if (bitStart % 512 == 0) _log?.Report($"Uploading fusemap: {Math.Min(bitStart + 32, total)}/{total} bits");
        }
        string checksumReply = Command("#c " + ((ushort)checksum).ToString("X4", CultureInfo.InvariantCulture), show: false);
        if (!checksumReply.Contains("OK checksum matches", StringComparison.Ordinal))
            throw new IOException("Firmware did not confirm the uploaded fusemap checksum.");
        string uploadReply = Command("#e", show: false);
        if (!uploadReply.Contains("OK upload finished", StringComparison.Ordinal))
            throw new IOException("Firmware did not confirm completion of the fusemap upload.");
        Log($"Fusemap uploaded: {total} bits; checksum {(ushort)checksum:X4}");
    }

    private void RunCalibration(string action, int offset)
    {
        if (action != "s" && !_varVpp) throw new InvalidOperationException("The firmware does not support variable VPP; calibration and measurement are unavailable.");
        switch (action)
        {
            case "s": Command("t", seconds: 29); break;
            case "m": Command("m", seconds: 43); break;
            case "b":
                if (offset < -32 || offset > 32) throw new ArgumentOutOfRangeException(nameof(offset));
                Command("B" + (char)('0' + offset + 32), show: false);
                string result = Command("b", seconds: 40);
                if (!result.Contains("Calibration OK", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The firmware did not confirm successful calibration. Check measurements and EEPROM.");
                break;
        }
    }

    private void RunJtag(string action, byte[]? image, byte[]? erase)
    {
        if (action == "i") throw new NotSupportedException("JTAG identification requires a dedicated ID XSVF script.");
        if (action == "r" || action == "v") throw new NotSupportedException("Standalone ATF150x readback and verification are not supported by the firmware.");
        if (action is not ("e" or "ew" or "w")) throw new NotSupportedException("Unsupported JTAG operation: " + action);
        if (action == "e" || action == "ew")
        {
            if (erase is null || erase.Length == 0)
                throw new InvalidOperationException("ATF150x erase requires the matching erase_*.xsvf file from Afterburner.");
            PlayXsvf(erase, vpp: true);
        }
        if (action is "ew" or "w")
        {
            if (image is null || image.Length == 0) throw new InvalidOperationException("No design XSVF file supplied.");
            PlayXsvf(image, vpp: false);
        }
    }

    private void PlayXsvf(byte[] data, bool vpp)
    {
        if (data.Length > 256 * 1024) throw new InvalidDataException("XSVF exceeds the 256 KiB compatibility limit.");
        int sent = 0;
        bool ready = false;
        Write(vpp ? "j1" : "j0");
        var line = new StringBuilder();
        while (true)
        {
            int b = ReadByte();
            if (b == '$')
            {
                // Original MCU sends '$ddd\r\n' to request a specified amount of binary data.
                byte[] request = new byte[5];
                for (int i = 0; i < request.Length; i++) request[i] = (byte)ReadByte();
                string digits = Encoding.ASCII.GetString(request, 0, 3);
                if (!int.TryParse(digits, out int count) || count is < 1 or > 999 || request[3] != '\r' || request[4] != '\n')
                    throw new IOException("Invalid JTAG data request.");
                if (!ready) throw new IOException("JTAG requested data before announcing RXSVF.");
                int len = Math.Min(count, data.Length - sent);
                if (len > 0)
                {
                    _port!.Write(data, sent, len);
                    sent += len;
                    if (sent == data.Length || sent % 1024 < len) _log?.Report($"XSVF sent {sent}/{data.Length} B");
                }
                continue;
            }
            if (b == '\r') continue;
            if (b != '\n')
            {
                if (line.Length > 8192) throw new IOException("JTAG message is too long.");
                line.Append((char)b);
                continue;
            }
            string message = line.ToString().Trim();
            line.Clear();
            if (message == "RXSVF") { ready = true; Log("JTAG is ready for XSVF data."); }
            else if (message.StartsWith("Q", StringComparison.Ordinal))
            {
                if (XsvfSucceeded(message))
                {
                    if (sent != data.Length)
                        throw new IOException($"XSVF completed after {sent}/{data.Length} bytes; input may be truncated or contain trailing data.");
                    // Q is emitted before VPP cleanup/readGarbage and the terminal prompt.
                    // Do not send the next j0 until that cleanup has completed.
                    ReadPrompt(TimeSpan.FromSeconds(5), 8192, false);
                    Log("XSVF complete.");
                    return;
                }
                throw new IOException("JTAG player error: " + message);
            }
            else if (message.StartsWith("!", StringComparison.Ordinal) || message.StartsWith("D", StringComparison.Ordinal)) Log(message);
        }
    }

    public static bool XsvfSucceeded(string message) => message is "Q-0,OK" or "Q0";
}

