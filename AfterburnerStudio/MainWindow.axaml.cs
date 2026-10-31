using System.Globalization;
using System.IO.Ports;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AfterburnerStudio.Models;
using AfterburnerStudio.Services;

namespace AfterburnerStudio;

public partial class MainWindow : Window
{
    private readonly NativeAfterburner _programmer = new();
    private CancellationTokenSource? _running;
    private bool _ready;
    private bool _interactionBusy;
    private int _vppPointCount;
    private ChipProfile Current => ChipProfile.All[Math.Max(0, ChipBox.SelectedIndex)];

    public MainWindow()
    {
        InitializeComponent();
        ChipBox.ItemsSource = ChipProfile.All.Select(x => x.Name).ToList();
        ChipBox.SelectedIndex = 10; // ATF16V8B
        BoardBox.SelectedIndex = 2;
        Loaded += OnWindowLoaded;
        Log("Afterburner Studio. Serial connection: 57600 8N1.");
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (_ready) return;
        SetChip(Current);
        RefreshPorts();
        _ready = true;
        UpdateOperationState();
    }

    private void SetChip(ChipProfile chip)
    {
        Diagram.Chip = chip;
        if (NoChipCheck is not null) NoChipCheck.IsChecked = false;
        JtagErasePanel.IsVisible = chip.IsJtag;
        ConditionButton.IsEnabled = !chip.IsJtag;
        ChipSummary.Text = $"{chip.Name}  •  " + (chip.IsJtag ? "JTAG/XSVF" : $"{chip.FuseCount} fuse bits") +
            $"\nBoard: {chip.MinimumBoard}\n{chip.Note}";
        AdapterNotice.Text = chip.Adapter is null
            ? "Top view: USB on the left, lever on the right. Device pin 1 and ZIF pin 1 are at the upper right. DIP20 leaves two contacts unused on each row at the left."
            : $"{chip.Adapter}. Check the adapter PCB documentation for its orientation and connections.";
        JedStatus.Text = chip.IsJtag ? "Select an XSVF file." : "Select a JEDEC file.";
        if (!string.IsNullOrWhiteSpace(JedPath.Text)) RefreshJedStatus();
        UpdateOperationState();
    }

    private void OnChipChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ChipBox?.SelectedIndex >= 0 && Diagram != null && ChipSummary != null)
            SetChip(Current);
    }

    private string SelectedPort => PortList?.SelectedItem as string ?? "";

    private void RefreshPorts()
    {
        try
        {
            string previous = SelectedPort;
            string[] ports = SerialPort.GetPortNames()
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            PortList.ItemsSource = ports;
            // Never retain a stale port that disappeared from the operating system.
            PortList.SelectedItem = ports.FirstOrDefault(p => p.Equals(previous, StringComparison.OrdinalIgnoreCase))
                ?? ports.FirstOrDefault();
            SetExternalRamStatus("not queried (diagnostics only)");
            Log("Detected serial ports: " + (ports.Length > 0 ? string.Join(", ", ports) : "none"));
        }
        catch (Exception ex)
        {
            PortList.ItemsSource = Array.Empty<string>();
            PortList.SelectedItem = null;
            SetExternalRamStatus("not queried (diagnostics only)");
            Log("Port scan failed: " + ex.Message);
        }
        UpdateOperationState();
    }
    private void OnRefreshPorts(object? sender, RoutedEventArgs e) => RefreshPorts();

    private void OnPortSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        SetExternalRamStatus("not queried (diagnostics only)");
        UpdateOperationState();
    }

    private void SetExternalRamStatus(string status)
    {
        // Hardware information is shown only after the programmer has responded.
        // SRAM is included in the connection summary, not as a separate empty badge.
        if (HardwareSummary is null) return;
        if (status.Contains("not queried", StringComparison.OrdinalIgnoreCase))
        {
            HardwareSummary.Text = "";
            HardwareSummary.IsVisible = false;
        }
        else if (!string.IsNullOrWhiteSpace(HardwareSummary.Text))
        {
            // Do not duplicate the SRAM portion already supplied by ConnectionSummary.
            HardwareSummary.IsVisible = true;
        }
    }

    private void OnInputChanged(object? sender, RoutedEventArgs e) => UpdateOperationState();
    private void OnSetupChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        NoChipCheck.IsChecked = false;
        UpdateOperationState();
    }
    private void OnSourceChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        RefreshJedStatus();
        UpdateOperationState();
    }

    private string? SetupIssue()
    {
        if (string.IsNullOrWhiteSpace(SelectedPort)) return "Select a detected serial port.";
        if (Current.MinimumBoard == "3.1+" && BoardBox.SelectedIndex == 0) return "This device requires board 3.1+ or a verified modification.";
        if (Current.MinimumBoard == "3.2+" && BoardBox.SelectedIndex < 2) return "This device requires board 3.2+ or a verified modification.";
        return null;
    }

    private void UpdateOperationState()
    {
        if (!_ready) return;
        if (ProgrammingInputs is null || ConnectionInputs is null || CalibrationInputs is null) return;
        bool idle = !_interactionBusy && _running is null;
        ProgrammingInputs.IsEnabled = idle;
        ConnectionInputs.IsEnabled = idle;
        CalibrationInputs.IsEnabled = idle;
        bool vppReady = idle && NoChipCheck.IsChecked == true && !string.IsNullOrWhiteSpace(SelectedPort);
        VppSetButton.IsEnabled = VppCalibrateButton.IsEnabled = VppMeasureButton.IsEnabled = vppReady;
        bool setup = SetupIssue() is null;
        var report = JedecValidator.Validate(JedPath.Text ?? "", Current);
        string? eraseIssue = null;
        if (Current.IsJtag)
        {
            try { FindEraseXsvf(JedPath.Text, log: false); }
            catch (Exception ex) { eraseIssue = ex.Message; }
        }
        IdentifyButton.IsEnabled = ReadButton.IsEnabled = idle && setup && !Current.IsJtag;
        EraseButton.IsEnabled = idle && setup && eraseIssue is null;
        VerifyButton.IsEnabled = idle && setup && report.Valid && !Current.IsJtag;
        WriteButton.IsEnabled = idle && setup && report.Valid && eraseIssue is null;
        ConditionButton.IsEnabled = idle && setup && !Current.IsJtag;
        WriteButton.Content = Current.IsJtag ? "ERASE AND WRITE XSVF" : "WRITE AND VERIFY";
        string fileReadiness;
        if (string.IsNullOrWhiteSpace(JedPath.Text))
            fileReadiness = Current.IsJtag
                ? "Ready for identify, erase or readback setup. Select a design XSVF file to enable erase/write operations."
                : "Ready for identify or read. Select a JEDEC file to enable write and verify.";
        else if (!report.Valid)
            fileReadiness = "Select a valid file: " + report.Message;
        else
            fileReadiness = eraseIssue ?? "Ready. Switch socket power OFF only for chip insertion/removal; use the required power setting (normally ON) for programming.";
        WriteReadiness.Text = !idle ? "An operation is in progress…" : SetupIssue() ?? fileReadiness;
    }

    private async Task WithInteractionAsync(Func<Task> action)
    {
        if (_interactionBusy || _running is not null) return;
        _interactionBusy = true;
        UpdateOperationState();
        try { await action(); }
        catch (Exception ex) { SetStatus("Error: " + ex.Message); }
        finally { _interactionBusy = false; UpdateOperationState(); }
    }

    private async Task BrowseEraseXsvfAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the matching erase XSVF file",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JTAG erase XSVF") { Patterns = new[] { "*.xsvf" } } }
        });
        if (files.Count > 0) EraseXsvfPath.Text = files[0].TryGetLocalPath();
    }

    private async Task BrowseJedAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a programming file",
            AllowMultiple = false,
            FileTypeFilter = Current.IsJtag
                ? new[] { new FilePickerFileType("JTAG XSVF") { Patterns = new[] { "*.xsvf" } } }
                : new[] { new FilePickerFileType("JEDEC fusemap") { Patterns = new[] { "*.jed" } } }
        });
        if (files.Count > 0)
        {
            JedPath.Text = files[0].TryGetLocalPath();
            RefreshJedStatus();
        }
    }
    private JedecReport RefreshJedStatus()
    {
        var report = JedecValidator.Validate(JedPath.Text ?? "", Current);
        JedStatus.Text = (report.Valid ? "✓ " : "⚠ ") + report.Message;
        JedStatus.Foreground = Avalonia.Media.Brush.Parse(report.Valid ? "#13765D" : "#B42318");
        return report;
    }

    private void Log(string text)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {text}";
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Log(text)); return; }
        LogBox.Text = (LogBox.Text ?? "") + entry + Environment.NewLine;
        if (LogBox.Text.Length > 250_000) LogBox.Text = LogBox.Text[^180_000..];
        LogBox.CaretIndex = LogBox.Text.Length;
    }
    private void SetStatus(string text)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => SetStatus(text)); return; }
        // Status is shown inside the Operation result panel on the active page.
        StatusText.Text = text.StartsWith("Operation failed:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            ? "See details below." : text;
        bool error = text.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("refused", StringComparison.OrdinalIgnoreCase);
        // Prevent the same error being printed for a third time in the top status strip.
        // Status belongs to the active UI, not to a hidden diagnostic tab.
        // Serial diagnostics are recorded separately by NativeAfterburner.
        if (error && _running is null)
        {
            OperationHeadline.Text = "Failed";
            ProgressCaption.Text = "See details below";
            if (Tabs?.SelectedIndex == 0 && ProgrammingResultTitle is not null)
            {
                ProgrammingResultTitle.Text = "Action required";
                ProgrammingResultDetails.Text = text;
            }
            else if (Tabs?.SelectedIndex == 1 && CalibrationResult is not null)
                CalibrationResult.Text = text;
        }
    }

    private static string OperationName(string mode) => mode switch
    {
        "m" => "VPP measurement", "b" => "VPP calibration", "s" => "VPP adjustment",
        "i" => "Identify device", "r" => "Read fusemap", "e" => "Erase device",
        "w" => "Program device", "v" => "Verify device", "wv" => "Program and verify",
        "ew" => "Erase and program", _ => "Programmer operation"
    };

    private void BeginOperation(string mode)
    {
        OperationHeadline.Text = OperationName(mode);
        ProgressCaption.Text = "Connecting…";
        HardwareSummary.IsVisible = false;
        HardwareSummary.Text = "";
        if (mode is not ("m" or "b" or "s"))
        {
            ProgrammingResultTitle.Text = OperationName(mode) + " — in progress";
            ProgrammingResultDetails.Text = "Connecting to the programmer…";
        }
        StatusText.Text = "Opening the programmer connection…";
        BusyBar.Value = 0;
        BusyBar.IsIndeterminate = true; // unknown duration, never invent completion percentage
        if (mode is "m" or "b" or "s")
        {
            _vppPointCount = 0;
            ClearVppMeasurements();
        }
    }

    private void ClearVppMeasurements()
    {
        foreach (var label in VppDisplayRows()) label.Text = "Waiting";
        VppOffsetText.Text = "Offset: not measured";
        VppCurrentStep.Text = "Awaiting first measurement…";
        CalibrationResult.Text = "Measurement is not yet complete.";
    }

    private TextBlock[] VppDisplayRows() => new[]
    {
        Vpp5Text, Vpp9Text, Vpp10Text, Vpp12Text, Vpp14Text, Vpp16Text
    };

    private bool SetVoltageReading(string? targetLabel, double measured)
    {
        string target = (targetLabel ?? "").Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        int index = target switch
        {
            "4.2-5.0V" => 0, "9.0V" => 1, "10.0V" => 2,
            "12.0V" => 3, "14.0V" => 4, "16.0V" => 5,
            _ => -1
        };
        if (index < 0) return false;
        VppDisplayRows()[index].Text = "✓ " + measured.ToString("F2", CultureInfo.InvariantCulture) + " V";
        return true;
    }

    private void ShowProgress(ProgrammerProgress progress)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ShowProgress(progress)); return; }
        if (_running is null) return;
        switch (progress.Kind)
        {
            case ProgrammerProgressKind.Connection:
                HardwareSummary.IsVisible = !string.IsNullOrWhiteSpace(progress.Message);
                HardwareSummary.Text = progress.Message;
                break;
            case ProgrammerProgressKind.Detail:
                if (_running is not null && Tabs?.SelectedIndex == 0)
                {
                    if (ProgrammingResultDetails.Text == "Connecting to the programmer…")
                        ProgrammingResultDetails.Text = progress.Message;
                    else
                        ProgrammingResultDetails.Text = (ProgrammingResultDetails.Text + Environment.NewLine + progress.Message).Trim();
                }
                break;
            case ProgrammerProgressKind.Stage:
                StatusText.Text = progress.Message;
                ProgressCaption.Text = "Processing on programmer…";
                BusyBar.IsIndeterminate = true;
                break;
            case ProgrammerProgressKind.Progress:
                StatusText.Text = progress.Message;
                if (progress.Total > 0)
                {
                    int percent = Math.Clamp((int)(100L * progress.Current / progress.Total), 0, 100);
                    ProgressCaption.Text = $"Transfer to programmer: {progress.Current}/{progress.Total} ({percent}%)";
                    BusyBar.IsIndeterminate = false;
                    // This reflects file-transfer progress only, not completion of the entire write/verify algorithm.
                    BusyBar.Value = Math.Min(90, percent * 0.9);
                }
                break;
            case ProgrammerProgressKind.VoltageStep:
                VppCurrentStep.Text = progress.Message;
                // Show the current target immediately, before the value arrives.
                if (progress.Current is >= 1 and <= 6)
                {
                    var targetReading = VppDisplayRows()[progress.Current - 1];
                    if (targetReading.Text == "Waiting") targetReading.Text = "Measuring…";
                }
                ProgressCaption.Text = $"Voltage step {progress.Current}/{progress.Total}";
                StatusText.Text = progress.Message;
                BusyBar.IsIndeterminate = false;
                BusyBar.Value = 100.0 * Math.Max(0, progress.Current - 1) / 6;
                break;
            case ProgrammerProgressKind.Voltage:
                if (progress.Voltage is double measured)
                {
                    // Structured measurement event, not an interpretation of the log control.
                    if (SetVoltageReading(progress.VoltageTarget, measured))
                    {
                        _vppPointCount = Math.Max(_vppPointCount, progress.Current);
                        if (progress.Total > 0)
                        {
                            BusyBar.IsIndeterminate = false;
                            BusyBar.Value = 100.0 * progress.Current / progress.Total;
                            ProgressCaption.Text = $"{progress.Current}/{progress.Total} voltage points";
                        }
                    }
                    VppCurrentStep.Text = $"Step {progress.Current}/6 completed: {progress.Message}";
                    CalibrationResult.Text = $"Received {progress.Current}/6 voltage readings.";
                    StatusText.Text = "Measurement: " + progress.Message;
                }
                break;
            case ProgrammerProgressKind.Message:
                StatusText.Text = progress.Message;
                if (progress.Message.StartsWith("VPP calib.", StringComparison.OrdinalIgnoreCase))
                    VppOffsetText.Text = progress.Message;
                if (Tabs?.SelectedIndex == 1)
                    CalibrationResult.Text = progress.Message;
                break;
            case ProgrammerProgressKind.Error:
                SetStatus("Operation failed: " + progress.Message);
                break;
        }
    }

    private async Task<bool> ConfirmAsync(string title, string details)
    {
        var dialogue = new Window
        {
            Title = title, Width = 560, SizeToContent = SizeToContent.Height, MaxHeight = 700,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(22), Spacing = 15,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold },
                    new TextBlock { Text = details, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Children =
                        {
                            new Button { Content = "Cancel", MinWidth = 95, Name = "cancel" },
                            new Button { Content = "Confirm", MinWidth = 110, Name = "accept" }
                        }
                    }
                }
            }
        };
        var panel = (StackPanel)dialogue.Content!;
        var buttons = (StackPanel)panel.Children[2];
        ((Button)buttons.Children[0]).Click += (_, _) => dialogue.Close(false);
        ((Button)buttons.Children[1]).Click += (_, _) => dialogue.Close(true);
        return await dialogue.ShowDialog<bool>(this);
    }

    private bool SetupOk(bool requireChip = true)
    {
        if (string.IsNullOrWhiteSpace(SelectedPort)) { SetStatus("Select a detected serial port."); return false; }
        if (!requireChip) return true;
        if (Current.MinimumBoard == "3.1+" && BoardBox.SelectedIndex == 0)
        {
            SetStatus("This GAL requires board 3.1+ or a verified modification of an older board.");
            return false;
        }
        if (Current.MinimumBoard == "3.2+" && BoardBox.SelectedIndex < 2)
        {
            SetStatus("PEEL requires PCB 3.2+ or a verified modification of an older board.");
            return false;
        }
        return true;
    }

    private byte[]? FindEraseXsvf(string? sourceFile, bool log = true)
    {
        if (!Current.IsJtag) return null;
        string fileName = "erase_" + Current.Name + ".xsvf";
        var candidates = !string.IsNullOrWhiteSpace(EraseXsvfPath.Text)
            ? new[] { EraseXsvfPath.Text! }
            : new[]
        {
            !string.IsNullOrWhiteSpace(sourceFile) ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourceFile))!, fileName) : "",
            Path.Combine(AppContext.BaseDirectory, "xsvf", fileName),
            Path.Combine(Environment.CurrentDirectory, "xsvf", fileName)
        };
        foreach (var path in candidates.Where(File.Exists))
        {
            if (!Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The erase XSVF must match the device: {fileName}.");
            if (new FileInfo(path).Length is < 1 or > 262144) throw new InvalidDataException("Invalid erase XSVF file size.");
            var data = log ? File.ReadAllBytes(path) : Array.Empty<byte>();
            if (log) Log("Erase file: " + path);
            return data;
        }
        throw new FileNotFoundException($"Missing {fileName}. Select the matching erase file from the Afterburner xsvf directory.");
    }

    private async Task<NativeResult?> ExecuteAsync(string mode, TimeSpan timeout, string? path = null, int? offset = null)
    {
        if (_running is not null) { SetStatus("Another operation is already in progress."); return null; }
        // Any programming/identification action invalidates a previous VPP safety acknowledgement.
        // No repeated checkbox is needed for ordinary non-destructive chip identification.
        if (mode is not ("m" or "b" or "s")) NoChipCheck.IsChecked = false;
        _running = new CancellationTokenSource();
        BeginOperation(mode);
        CancelButton.IsEnabled = true;
        UpdateOperationState();
        SetStatus("Serial operation: " + mode + " " + Current.Name);
        try
        {
            byte[]? bits = null;
            byte[]? xsvf = null;
            byte[]? erase = null;
            if (path is not null)
            {
                if (Current.IsJtag) xsvf = await File.ReadAllBytesAsync(path, _running.Token);
                else bits = JedecValidator.ParseFile(path, Current).FuseBits;
            }
            if (Current.IsJtag && (mode is "e" or "ew")) erase = FindEraseXsvf(JedPath.Text);
            var diagnosticLog = new Progress<string>(Log);
            // Dispatch each structured serial event before the reader processes
            // subsequent lines. Progress<T> posts asynchronously and can leave the
            // VPP bars unchanged until the entire operation has completed.
            var liveUi = new ImmediateUiProgress(ShowProgress);
            var result = await _programmer.RunAsync(SelectedPort, Current, mode, bits, xsvf, erase,
                offset ?? 0, diagnosticLog, timeout, _running.Token, liveUi);
            // Also apply the final connection snapshot: progress callbacks may be queued
            // behind the completed task, and the GUI must never depend on log parsing.
            string hwInfo = result.ConnectionSummary ?? "";
            string sramInfo = _programmer.ExternalRamStatus;
            if (!string.IsNullOrWhiteSpace(sramInfo) &&
                !sramInfo.Contains("not queried", StringComparison.OrdinalIgnoreCase) &&
                !hwInfo.Contains("SRAM", StringComparison.OrdinalIgnoreCase))
                hwInfo = string.IsNullOrWhiteSpace(hwInfo) ? "SRAM: " + sramInfo : hwInfo + "  |  SRAM: " + sramInfo;
            HardwareSummary.Text = hwInfo;
            HardwareSummary.IsVisible = !string.IsNullOrWhiteSpace(hwInfo);
            // Apply the final structured snapshot as well; no last-point race with async UI events.
            if (result.VoltageReadings is { } readings)
            {
                int recognized = 0;
                foreach (var reading in readings)
                    if (SetVoltageReading(reading.Target, reading.Measured)) recognized++;
                _vppPointCount = recognized;
            }
            BusyBar.IsIndeterminate = false;
            if (result.Succeeded)
            {
                BusyBar.Value = 100;
                OperationHeadline.Text = "Completed";
                ProgressCaption.Text = "Finished";
                SetStatus("Operation completed.");
                if (mode == "m")
                {
                    VppCurrentStep.Text = "All six voltage measurements completed.";
                    CalibrationResult.Text = $"VPP measurement completed. {_vppPointCount}/6 readings received.";
                }
                else if (mode is "b" or "s") CalibrationResult.Text = SummarizeVpp(result.Output);
                else
                {
                    ProgrammingResultTitle.Text = OperationName(mode) + " — completed";
                    ProgrammingResultDetails.Text = result.Details is { Count: > 0 }
                        ? string.Join(Environment.NewLine, result.Details)
                        : "The programmer reported no error. Check the operation status above.";
                }
            }
            else
            {
                bool chipNotDetected = mode == "i" &&
                    (result.Error?.Contains("Chip not detected", StringComparison.OrdinalIgnoreCase) == true);
                BusyBar.IsIndeterminate = false;
                BusyBar.Value = 0;
                OperationHeadline.Text = "Failed";
                ProgressCaption.Text = "See details below";
                SetStatus("Operation failed: " + (result.Error ?? "Unknown error"));
                if (mode is "m" or "b" or "s") CalibrationResult.Text = "Measurement/calibration failed: " + (result.Error ?? "Unknown error");
                else
                {
                    ProgrammingResultTitle.Text = chipNotDetected ? "Chip not detected" : OperationName(mode) + " — failed";
                    // Show the actionable reason once, without duplicating the detail list.
                    var reason = result.Error ?? "Unknown error";
                    var details = (result.Details ?? Array.Empty<string>())
                        .Where(d => !string.IsNullOrWhiteSpace(d) &&
                            !d.Contains(reason, StringComparison.OrdinalIgnoreCase) &&
                            !reason.Contains(d, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    ProgrammingResultDetails.Text = details.Length == 0
                        ? reason : reason + Environment.NewLine + string.Join(Environment.NewLine, details);
                }
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            BusyBar.IsIndeterminate = false;
            BusyBar.Value = 0;
            OperationHeadline.Text = "Operation cancelled";
            ProgressCaption.Text = "Cancelled";
            SetStatus("Operation cancelled. Socket voltage may still be active; check the programmer.");
            if (mode is "m" or "b" or "s") CalibrationResult.Text = "Cancelled. Verify that VPP is switched off before handling the socket.";
            else { ProgrammingResultTitle.Text = "Operation cancelled"; ProgrammingResultDetails.Text = "Cancelled. Check the programmer and chip state before continuing."; }
            return null;
        }
        catch (Exception ex)
        {
            BusyBar.IsIndeterminate = false;
            BusyBar.Value = 0;
            OperationHeadline.Text = "Operation failed";
            ProgressCaption.Text = "Failed";
            SetStatus("Error: " + ex.Message);
            if (mode is "m" or "b" or "s") CalibrationResult.Text = "Error: " + ex.Message;
            else { ProgrammingResultTitle.Text = "Operation failed"; ProgrammingResultDetails.Text = "Error: " + ex.Message; }
            return null;
        }
        finally
        {
            _running.Dispose(); _running = null;
            BusyBar.IsIndeterminate = false; CancelButton.IsEnabled = false;
            if (mode is "m" or "b" or "s") NoChipCheck.IsChecked = false; // explicitly reconfirm each high-voltage test
            UpdateOperationState();
        }
    }

    private static string SummarizeVpp(string text)
    {
        var lines = text.Split('\n').Select(x => x.Trim()).Where(x =>
            x.Contains("VPP", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("Calibration", StringComparison.OrdinalIgnoreCase) ||
            x.StartsWith("*Index", StringComparison.OrdinalIgnoreCase));
        return string.Join(Environment.NewLine, lines.Take(25));
    }

    private async Task IdentifyAsync()
    {
        if (!SetupOk() || Current.IsJtag) { if (Current.IsJtag) SetStatus("JTAG identification requires id_ATF150X.xsvf and is not available in this interface."); return; }
        await ExecuteAsync("i", TimeSpan.FromSeconds(18));
    }
    private async Task ReadAsync()
    {
        if (!SetupOk() || Current.IsJtag) { if (Current.IsJtag) SetStatus("Reading ATF150x is not supported."); return; }
        var location = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the JEDEC readback", SuggestedFileName = Current.Name + "_backup.jed",
            DefaultExtension = "jed"
        });
        var path = location?.TryGetLocalPath();
        if (path is null) return;
        var result = await ExecuteAsync("r", TimeSpan.FromSeconds(40));
        if (result?.Succeeded != true) return;
        try
        {
            string content = JedecValidator.ExtractReadback(result.Output);
            var check = JedecValidator.ValidateText(content, Current);
            if (!check.Valid) throw new InvalidDataException("Readback validation failed: " + check.Message);
            await File.WriteAllTextAsync(path, content, Encoding.Latin1);
            SetStatus("Backup saved: " + path);
            ProgrammingResultTitle.Text = "Fusemap read and saved";
            ProgrammingResultDetails.Text = $"Device: {Current.Name}\nFuse bits: {Current.FuseCount}\nFile: {path}\nJEDEC validation: passed.";
        }
        catch (Exception ex)
        {
            SetStatus("Backup was not saved: " + ex.Message);
            ProgrammingResultTitle.Text = "Readback save failed";
            ProgrammingResultDetails.Text = "Error: " + ex.Message;
        }
    }
    private async Task EraseAsync()
    {
        if (!SetupOk()) return;
        if (!await ConfirmAsync("Erase device", $"The contents of {Current.Name} will be erased.\n\nCheck device placement and adapter wiring, then switch socket power on.\n" + (Current.IsJtag ? "JTAG: the matching erase XSVF file will be used." : "Normal erase will be used; PES will be preserved."))) return;
        await ExecuteAsync("e", TimeSpan.FromSeconds(Current.IsJtag ? 180 : 18));
    }
    private async Task<string?> ValidatedFrozenSource()
    {
        var report = RefreshJedStatus();
        if (!report.Valid) { SetStatus("Write refused: " + report.Message); return null; }
        // Snapshot the validated file. Protect against changes during the serial upload.
        string ext = Current.IsJtag ? ".xsvf" : ".jed";
        var frozen = Path.Combine(Path.GetTempPath(), "afterburner-" + Guid.NewGuid().ToString("N") + ext);
        File.Copy(JedPath.Text!, frozen);
        var verify = JedecValidator.Validate(frozen, Current);
        if (!verify.Valid) { File.Delete(frozen); SetStatus("The input snapshot failed validation."); return null; }
        return await Task.FromResult(frozen);
    }
    private async Task WriteVerifyAsync()
    {
        if (!SetupOk()) return;
        string? file = await ValidatedFrozenSource();
        if (file is null) return;
        try
        {
            string operation = Current.IsJtag ? "ew" : "wv";
            var msg = Current.IsJtag
                ? "The programmer will execute the erase XSVF and then the design XSVF. Verification must be included in the XSVF data."
                : "The device will be written and verified. Erase it first if required.";
            if (!await ConfirmAsync("Write device", $"Device: {Current.Name}\n\n{msg}\n\nCheck device orientation and socket power.")) return;
            await ExecuteAsync(operation, TimeSpan.FromSeconds(Current.IsJtag ? 180 : Current.FuseCount > 8000 ? 95 : 65), file);
        }
        finally { try { File.Delete(file); } catch { } }
    }
    private async Task VerifyAsync()
    {
        if (!SetupOk()) return;
        if (Current.IsJtag) { SetStatus("Standalone ATF150x verification is not supported; verification may be included in the XSVF data."); return; }
        string? file = await ValidatedFrozenSource();
        if (file is null) return;
        try { await ExecuteAsync("v", TimeSpan.FromSeconds(42), file); }
        finally { try { File.Delete(file); } catch { } }
    }
    private async Task ConditionAsync()
    {
        if (Current.IsJtag || !SetupOk()) return;
        if (!await ConfirmAsync("Condition device", $"This optional, destructive procedure will overwrite and erase {Current.Name}.\n\n1. Write an all-zero fusemap twice.\n2. Erase the device.\n\nThis is a JEDEC programming sequence, not a non-destructive test. It is primarily intended for compatible ATF GAL devices; for other devices use only when their programming specifications permit it. It cannot guarantee recovery of a defective device.")) return;
        string path = Path.Combine(Path.GetTempPath(), "afterburner-conditioning-" + Guid.NewGuid().ToString("N") + ".jed");
        try
        {
            await File.WriteAllTextAsync(path, JedecValidator.ConditioningJedec(Current.FuseCount), Encoding.Latin1);
            for (int pass = 1; pass <= 2; pass++)
            {
                SetStatus($"Conditioning: write {pass}/2");
                var outcome = await ExecuteAsync("w", TimeSpan.FromSeconds(50), path);
                if (outcome?.Succeeded != true)
                {
                    // ExecuteAsync already displays the actual programmer error. Do not replace it.
                    // A cancellation also returns null; preserve its existing result text.
                    return;
                }
            }
            SetStatus("Conditioning: final erase");
            var erased = await ExecuteAsync("e", TimeSpan.FromSeconds(18));
            if (erased?.Succeeded == true)
            {
                SetStatus("Conditioning complete. The device can now be programmed.");
                ProgrammingResultTitle.Text = "Conditioning complete";
                ProgrammingResultDetails.Text = $"{Current.Name}: two writes and final erase completed without a reported error. This is not an independent verification of the chip contents.";
            }
        }
        finally { try { File.Delete(path); } catch { } }
    }
    private async void OnBrowseJed(object? sender, RoutedEventArgs e) => await WithInteractionAsync(BrowseJedAsync);
    private async void OnBrowseEraseXsvf(object? sender, RoutedEventArgs e) => await WithInteractionAsync(BrowseEraseXsvfAsync);
    private async void OnIdentify(object? sender, RoutedEventArgs e) => await WithInteractionAsync(IdentifyAsync);
    private async void OnRead(object? sender, RoutedEventArgs e) => await WithInteractionAsync(ReadAsync);
    private async void OnErase(object? sender, RoutedEventArgs e) => await WithInteractionAsync(EraseAsync);
    private async void OnWriteVerify(object? sender, RoutedEventArgs e) => await WithInteractionAsync(WriteVerifyAsync);
    private async void OnVerify(object? sender, RoutedEventArgs e) => await WithInteractionAsync(VerifyAsync);
    private async void OnCondition(object? sender, RoutedEventArgs e) => await WithInteractionAsync(ConditionAsync);
    private async void OnVppSet(object? sender, RoutedEventArgs e) => await WithInteractionAsync(VppSetAsync);
    private async void OnVppCalibrate(object? sender, RoutedEventArgs e) => await WithInteractionAsync(VppCalibrateAsync);
    private async void OnVppMeasure(object? sender, RoutedEventArgs e) => await WithInteractionAsync(VppMeasureAsync);
    private async void OnSaveLog(object? sender, RoutedEventArgs e) => await WithInteractionAsync(SaveLogAsync);

    // Make every measured point visible before accepting the next serial line.
    // Do not use fire-and-forget Progress<T> for live voltage updates.
    private sealed class ImmediateUiProgress(Action<ProgrammerProgress> sink) : IProgress<ProgrammerProgress>
    {
        public void Report(ProgrammerProgress progress)
        {
            if (Dispatcher.UIThread.CheckAccess()) sink(progress);
            else Dispatcher.UIThread.InvokeAsync(() => sink(progress), DispatcherPriority.Normal)
                .GetAwaiter().GetResult();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => _running?.Cancel();

    private bool VppSafe() { if (NoChipCheck.IsChecked == true) return true; SetStatus("VPP operation blocked: remove the chip or physically switch OFF VPP to the socket."); return false; }
    private async Task VppSetAsync()
    {
        if (!VppSafe() || !SetupOk(requireChip: false)) return;
        if (await ConfirmAsync("Adjust VPP", "Remove the chip from the socket OR physically switch OFF VPP to the socket. The programmer will generate high voltage; measure it with a multimeter."))
            await ExecuteAsync("s", TimeSpan.FromSeconds(31));
    }
    private async Task VppCalibrateAsync()
    {
        if (!VppSafe() || !SetupOk(requireChip: false)) return;
        if (await ConfirmAsync("VPP calibration", "Calibration will overwrite EEPROM calibration values. Remove the chip OR physically switch OFF VPP to the socket. Check 16.5 V with a multimeter."))
            await ExecuteAsync("b", TimeSpan.FromSeconds(55), offset: (int)(OffsetBox.Value ?? 0));
    }
    private async Task VppMeasureAsync()
    {
        if (!VppSafe() || !SetupOk(requireChip: false)) return;
        // The graphical confirmation replaces CLI's typed YES; NEVER forward YES to firmware.
        if (!await ConfirmAsync("Measure VPP — socket protection required",
            "Remove chip from socket or switch OFF VPP voltage to socket.\n\n" +
            "The programmer will generate programming voltage. " +
            "Confirm that you have physically met one of these conditions before starting."))
        {
            SetStatus("VPP measurement cancelled. No command was sent to the programmer.");
            return;
        }
        await ExecuteAsync("m", TimeSpan.FromSeconds(53));
    }
    private async Task SaveLogAsync()
    {
        var item = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save diagnostic log", SuggestedFileName = "afterburner-studio.log", DefaultExtension = "log" });
        if (item?.TryGetLocalPath() is string path) { await File.WriteAllTextAsync(path, LogBox.Text ?? ""); SetStatus("Log saved: " + path); }
    }
    private void OnClearLog(object? sender, RoutedEventArgs e) => LogBox.Text = "";
}


