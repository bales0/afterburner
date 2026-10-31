using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AfterburnerStudio.Models;

namespace AfterburnerStudio.Services;

public sealed record VoltageReading(string Target, double Measured);
public sealed record NativeResult(bool Succeeded, string Output, string? Error = null,
    IReadOnlyList<VoltageReading>? VoltageReadings = null,
    IReadOnlyList<string>? Details = null, string? ConnectionSummary = null);

// Structured UI events: the diagnostic log is NOT the source of UI state.
public enum ProgrammerProgressKind { Stage, Progress, Voltage, VoltageStep, Message, Error, Connection, Detail }
public sealed record ProgrammerProgress(ProgrammerProgressKind Kind, string Message,
    int Current = 0, int Total = 0, string? VoltageTarget = null, double? Voltage = null);

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
    private IProgress<ProgrammerProgress>? _uiProgress;
    private string _activeAction = "";
    private int _vppPointCount;
    private int _lastAnnouncedVppStep;
    private readonly List<VoltageReading> _voltageReadings = new();
    private readonly List<string> _details = new();
    private string? _connectionSummary;
    private static readonly Regex VppPointPattern = new(
        @"^VPP:\s*(.+?)\s*:\s*(-?\d+(?:\.\d+)?)\s*(?:V)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex VppStepPrefixPattern = new(
        @"^VPP:\s*(.+?)\s*:\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private CancellationToken _token;
    private Stopwatch _clock = new();
    private TimeSpan _limit;
    private ISerialTransport? _port;
    private readonly Func<string, ISerialTransport> _transportFactory;
    public NativeAfterburner(Func<string, ISerialTransport>? transportFactory = null) =>
        _transportFactory = transportFactory ?? (name => new SerialTransport(name));
    private bool _varVpp;
    // Set after a successful programmer handshake; purely informational.
    public string ExternalRamStatus { get; private set; } = "Not queried";
    private bool _calibrationMissing;
    private bool _calibrationVerified;
    private const int ResponseMax = 320_000;

    public Task<NativeResult> RunAsync(string port, ChipProfile chip, string action, byte[]? fuseBits,
        byte[]? xsvf, byte[]? eraseXsvf, int offset, IProgress<string> log, TimeSpan timeout, CancellationToken token,
        IProgress<ProgrammerProgress>? uiProgress = null)
    {
        // All state belongs to one operation; the UI serializes calls.
        return Task.Run(() => RunSync(port, chip, action, fuseBits, xsvf, eraseXsvf, offset, log, timeout, token, uiProgress), token);
    }

    private NativeResult RunSync(string port, ChipProfile chip, string action, byte[]? fuseBits,
        byte[]? xsvf, byte[]? eraseXsvf, int offset, IProgress<string> log, TimeSpan timeout, CancellationToken token,
        IProgress<ProgrammerProgress>? uiProgress)
    {
        _token = token;
        _log = log;
        _uiProgress = uiProgress;
        _activeAction = action;
        _vppPointCount = 0;
        _lastAnnouncedVppStep = 0;
        _voltageReadings.Clear();
        _details.Clear();
        _connectionSummary = null;
        _limit = timeout;
        _clock = Stopwatch.StartNew();
        _output.Clear();
        _varVpp = false;
        ExternalRamStatus = "Not queried";
        _calibrationMissing = false;
        _calibrationVerified = false;
        try
        {
            if (string.IsNullOrWhiteSpace(port)) throw new InvalidOperationException("No serial port selected.");
            Emit(ProgrammerProgressKind.Stage, "Connecting to programmer on " + port + "…");
            _port = _transportFactory(port);
            _port.Open();
            Connect();
            Emit(ProgrammerProgressKind.Stage, "Connected to programmer on " + port + ".");
            if (_varVpp && !_calibrationVerified && action is not ("s" or "b" or "m"))
                throw new InvalidOperationException(_calibrationMissing
                    ? "Variable VPP failed its startup voltage measurement. Verify the hardware and recalibrate with an empty socket."
                    : "Firmware has not confirmed a measured VPP startup check. Update the firmware before programming.");
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
                Emit(ProgrammerProgressKind.Stage, action switch {
                    "i" => "Reading device identification…",
                    "r" => "Reading fusemap…",
                    "e" => "Erasing device…",
                    "w" => "Programming device…",
                    "v" => "Verifying device…",
                    "wv" => "Preparing device programming and verification…",
                    _ => "Communicating with programmer…"
                });
                // Enable PES/type protection before selecting a new GAL.
                Command("f", show: false);
                int id = ChipProfile.All.ToList().FindIndex(c => c.Name == chip.Name) + 1;
                if (id <= 0 || id > 16) throw new InvalidOperationException("The GAL type has no supported firmware ID.");
                Command("g" + (char)('0' + id), show: false);
                if (action is "r" or "e") SetUploadType(chip);
                switch (action)
                {
                    case "i":
                        AddDeviceInfo(Command("p"), chip);
                        break;
                    case "r":
                        Command("r", show: true, largeResponse: true);
                        AddDetail("Fusemap received from the programmer; validating and saving the JEDEC file.");
                        break;
                    case "e":
                        AddCommandInfo("Erase", Command("c")); // Normal erase. Never erase PES.
                        break;
                    case "w":
                    case "v":
                    case "wv":
                        if (fuseBits is null) throw new InvalidOperationException("No validated JEDEC fusemap supplied.");
                        UploadFuseMap(chip, fuseBits);
                        if (action.Contains('w')) {
                            Emit(ProgrammerProgressKind.Stage, "Programming device…");
                            AddCommandInfo("Programming", Command("w"));
                        }
                        if (action.Contains('v')) {
                            Emit(ProgrammerProgressKind.Stage, "Verifying programmed data…");
                            AddCommandInfo("Verification", Command("v"));
                        }
                        break;
                    default: throw new NotSupportedException("Unsupported operation: " + action);
                }
            }
            Emit(ProgrammerProgressKind.Stage, "Programmer operation completed.");
            return new NativeResult(true, _output.ToString(), VoltageReadings: _voltageReadings.ToArray(),
                Details: _details.ToArray(), ConnectionSummary: _connectionSummary);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Emit(ProgrammerProgressKind.Error, ex.Message);
            _log?.Report("ERROR: " + ex.Message);
            return new NativeResult(false, _output.ToString(), ex.Message, _voltageReadings.ToArray(),
                _details.ToArray(), _connectionSummary);
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

    private void Emit(ProgrammerProgressKind kind, string message,
        int current = 0, int total = 0, string? voltageTarget = null, double? voltage = null) =>
        _uiProgress?.Report(new ProgrammerProgress(kind, message, current, total, voltageTarget, voltage));

    // Structured, bounded results for the active GUI page. The diagnostic log
    // is never parsed by MainWindow to decide whether an operation succeeded.
    private void AddDetail(string message)
    {
        message = message.Trim();
        if (message.Length == 0) return;
        if (message.Length > 240) message = message[..240] + "…";
        if (_details.Count >= 10) _details.RemoveAt(0);
        _details.Add(message);
        Emit(ProgrammerProgressKind.Detail, message);
    }

    // A serial prompt means only that the firmware finished the command.
    // It does NOT mean that a chip was found. Unknown/empty PES is a failure.
    private static readonly Regex PesTypePattern = new(
        @"\b(GAL16V8|GAL18V10|GAL20V8|GAL20RA10|GAL20XV10|GAL22V10|GAL26CV12|GAL26V12|GAL6001|GAL6002|ATF16V8B|ATF16V8C|ATF20V8B|ATF22V10B|ATF22V10C|ATF750C|ATF750LVC)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static string? ValidatePesIdentification(string reply, string selectedType)
    {
        var pes = reply.Replace("\r", "").Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("PES info:", StringComparison.OrdinalIgnoreCase));
        if (pes is null || pes.Contains("Unknown GAL", StringComparison.OrdinalIgnoreCase) ||
            !pes.Contains("VPP=", StringComparison.OrdinalIgnoreCase))
            return "Chip not detected or PES identification invalid. Check chip insertion, socket power and contacts.";
        var detected = PesTypePattern.Match(pes);
        if (!detected.Success)
            return "Chip not detected: firmware did not identify a supported GAL/ATF device.";
        string model = detected.Value.ToUpperInvariant();
        bool matches = model == selectedType.ToUpperInvariant() ||
            (selectedType == "ATF16V8B" && model == "ATF16V8C") ||
            (selectedType == "ATF750C" && model == "ATF750LVC");
        return matches ? null : $"Detected {model}, but {selectedType} is selected. Check the selected chip type.";
    }

    private void AddDeviceInfo(string reply, ChipProfile chip)
    {
        string? error = ValidatePesIdentification(reply, chip.Name);
        if (error is not null)
        {
            AddDetail(error);
            throw new InvalidOperationException(error);
        }
        foreach (var line in reply.Replace("\r", "").Split('\n'))
        {
            string value = line.Trim();
            if (value.StartsWith("PES info:", StringComparison.OrdinalIgnoreCase))
                AddDetail("Device detected — " + value);
        }
    }

    private void AddCommandInfo(string action, string reply)
    {
        bool reported = false;
        foreach (var line in reply.Replace("\r", "").Split('\n'))
        {
            string value = line.Trim();
            if (value.Length == 0 || value == ">" || value.Equals("OK", StringComparison.OrdinalIgnoreCase))
                continue;
            // Do not show raw serial data, but retain firmware feedback for users.
            if (value.StartsWith("OK ", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("VPP", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("verify", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                AddDetail(action + ": " + value);
                reported = true;
            }
        }
        if (!reported)
            AddDetail(action + ": firmware command finished without a reported error.");
    }

    private void HandleFirmwareLine(string line)
    {
        if (_activeAction is not ("m" or "b" or "s")) return;
        var point = VppPointPattern.Match(line);
        if (point.Success && double.TryParse(point.Groups[2].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double measured))
        {
            _vppPointCount++;
            _voltageReadings.Add(new VoltageReading(point.Groups[1].Value.Trim(), measured));
            Emit(ProgrammerProgressKind.Voltage, line, _vppPointCount,
                _activeAction == "m" ? 6 : 0, point.Groups[1].Value.Trim(), measured);
            // Do not announce the next measurement here. The firmware delays
            // between points, so announcing the next step immediately is misleading.
            // ReadPrompt announces each step from its live, unfinished VPP header.
        }
        else if (line.StartsWith("VPP calib.", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("Calibration", StringComparison.OrdinalIgnoreCase) ||
                 line.StartsWith("*Index", StringComparison.OrdinalIgnoreCase))
            Emit(ProgrammerProgressKind.Message, line);
    }

    // The firmware sends "VPP: <target> : " before it sends the measured
    // voltage and line ending. Detect that prefix to show the active step
    // immediately, rather than waiting for the entire response or guessing
    // that the next step started during the firmware's inter-step delay.
    internal static int VppStepNumber(string target) =>
        target.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "4.2-5.0V" => 1,
            "9.0V" => 2,
            "10.0V" => 3,
            "12.0V" => 4,
            "14.0V" => 5,
            "16.0V" => 6,
            _ => 0
        };

    private void HandlePartialVppHeader(string text)
    {
        if (_activeAction != "m") return;
        var match = VppStepPrefixPattern.Match(text);
        if (!match.Success) return;
        string target = match.Groups[1].Value.Trim();
        int step = VppStepNumber(target);
        if (step == 0 || step == _lastAnnouncedVppStep) return;
        _lastAnnouncedVppStep = step;
        Emit(ProgrammerProgressKind.VoltageStep,
            $"Measuring {target} — step {step}/6…", step, 6);
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
                    _calibrationVerified = _varVpp && !_calibrationMissing &&
                        response.Contains("VPP-CAL-OK", StringComparison.OrdinalIgnoreCase);
                    Log("Connected firmware: " + response.Split('\n').FirstOrDefault(x => x.Contains("AFTerburner v."))?.Trim());
                    Log(_varVpp ? "Variable VPP board detected." : "Variable VPP was not detected; calibration is unavailable.");
                    ExternalRamStatus = response.Contains("SeRAM:23LC512", StringComparison.Ordinal)
                        ? "23LC512 detected (diagnostics only)"
                        : response.Contains("SeRAM:23LC1024", StringComparison.Ordinal)
                        ? "23LC1024 detected (diagnostics only)"
                        : response.Contains("SeRAM:None", StringComparison.Ordinal)
                        ? "Not detected"
                        : response.Contains("SeRAM:NotChecked", StringComparison.Ordinal)
                        ? "Detection unavailable on this board"
                        : "Not reported by firmware";
                    Log("External serial SRAM: " + ExternalRamStatus);
                    string fw = response.Split('\n').FirstOrDefault(x => x.Contains("AFTerburner v."))?.Trim() ?? "AFTerburner";
                    _connectionSummary = $"Firmware: {fw}   |   Variable VPP: {(_varVpp ? (_calibrationVerified ? "calibrated" : "check required") : "not detected")}   |   SRAM: {ExternalRamStatus}";
                    Emit(ProgrammerProgressKind.Connection, _connectionSummary);
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
                if (stream && info.Length > 0 && info != ">" && !info.StartsWith("OK ")) {
                    HandleFirmwareLine(info); // independently of diagnostic log presentation
                    Log(info);
                }
                line.Clear();
            }
            else if (ch != '\r')
            {
                line.Append(ch);
                // The second ':' in "VPP: 9.0V :" arrives before the value.
                // Updating the GUI here makes each physical measurement step
                // visible even if the voltage value arrives later.
                if (stream && ch == ':' && line.Length > 5)
                    HandlePartialVppHeader(line.ToString());
            }
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
            if (bitStart % 512 == 0) {
                int sent = Math.Min(bitStart + 32, total);
                _log?.Report($"Uploading fusemap: {sent}/{total} bits");
                Emit(ProgrammerProgressKind.Progress, "Transferring fusemap…", sent, total);
            }
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
        Emit(ProgrammerProgressKind.Stage, action switch {
            "s" => "Adjusting VPP — chip removed or VPP physically isolated from socket.",
            "b" => "Calibrating VPP — chip removed or VPP physically isolated from socket.",
            _ => "Measuring VPP — chip removed or VPP physically isolated from socket."
        });
        switch (action)
        {
            // Studio speaks directly to the firmware; CLI's interactive YES prompt
            // is not part of the serial wire protocol and must never be sent here.
            case "s": Command("t", seconds: 29); break;
            case "m":
                Emit(ProgrammerProgressKind.VoltageStep,
                    "Measuring 4.2–5.0 V — step 1/6…", 1, 6);
                Command("m", seconds: 43);
                if (_vppPointCount != 6)
                    throw new IOException($"Incomplete VPP measurement: {_vppPointCount}/6 voltage points received.");
                break;
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
            Emit(ProgrammerProgressKind.Stage, "Erasing JTAG device…");
            if (erase is null || erase.Length == 0)
                throw new InvalidOperationException("ATF150x erase requires the matching erase_*.xsvf file from Afterburner.");
            PlayXsvf(erase, vpp: true);
            AddDetail("JTAG erase XSVF completed without a reported player error.");
        }
        if (action is "ew" or "w")
        {
            Emit(ProgrammerProgressKind.Stage, "Programming JTAG/XSVF…");
            if (image is null || image.Length == 0) throw new InvalidOperationException("No design XSVF file supplied.");
            PlayXsvf(image, vpp: false);
            AddDetail("JTAG design XSVF completed without a reported player error. Verify according to the XSVF script.");
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
                    if (sent == data.Length || sent % 1024 < len) {
                        _log?.Report($"XSVF sent {sent}/{data.Length} B");
                        Emit(ProgrammerProgressKind.Progress, "Transferring XSVF…", sent, data.Length);
                    }
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

