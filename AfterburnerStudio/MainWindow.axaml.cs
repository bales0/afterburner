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
    private ChipProfile Current => ChipProfile.All[Math.Max(0, ChipBox.SelectedIndex)];

    public MainWindow()
    {
        InitializeComponent();
        ChipBox.ItemsSource = ChipProfile.All.Select(x => x.Name).ToList();
        ChipBox.SelectedIndex = 10; // ATF16V8B
        BoardBox.SelectedIndex = 2;
        SetChip(Current);
        RefreshPorts();
        _ready = true;
        UpdateOperationState();
        Log("Afterburner Studio. Serial connection: 57600 8N1.");
    }

    private void SetChip(ChipProfile chip)
    {
        Diagram.Chip = chip;
        OffCheck.IsChecked = false;
        AdapterCheck.IsVisible = chip.NeedsAdapter;
        AdapterCheck.IsChecked = false;
        JtagErasePanel.IsVisible = chip.IsJtag;
        ConditionButton.IsEnabled = chip.Conditioning;
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

    private void RefreshPorts()
    {
        try
        {
            string selected = PortBox.Text?.Trim() ?? "";
            string[] ports = SerialPort.GetPortNames().OrderBy(x => x.Length).ThenBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            PortList.ItemsSource = ports;
            PortList.SelectedItem = ports.FirstOrDefault(p => p.Equals(selected, StringComparison.OrdinalIgnoreCase));
            PortBox.Text = selected;
            if (selected.Length == 0 && ports.Length == 1) PortList.SelectedItem = ports[0];
            Log("Detected ports: " + (ports.Length > 0 ? string.Join(", ", ports) : "none (manual entry available)"));
        }
        catch (Exception ex) { Log("Ports: " + ex.Message + " — enter the port manually."); }
        UpdateOperationState();
    }
    private void OnRefreshPorts(object? sender, RoutedEventArgs e) => RefreshPorts();

    private void OnPortSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (PortList.SelectedItem is string port) PortBox.Text = port;
        UpdateOperationState();
    }
    private void OnInputChanged(object? sender, RoutedEventArgs e)
    {
        if (_ready && ReferenceEquals(sender, PortBox) && PortList.SelectedItem is string listed &&
            !listed.Equals(PortBox.Text?.Trim(), StringComparison.OrdinalIgnoreCase)) PortList.SelectedItem = null;
        UpdateOperationState();
    }
    private void OnSetupChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        OffCheck.IsChecked = false;
        AdapterCheck.IsChecked = false;
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
        if (string.IsNullOrWhiteSpace(PortBox.Text)) return "Select a serial port or enter it manually.";
        if (OffCheck.IsChecked != true) return "Confirm that the device was inserted with socket power off.";
        if (Current.NeedsAdapter && AdapterCheck.IsChecked != true) return "Confirm the adapter / JTAG wiring check.";
        if (Current.MinimumBoard == "3.1+" && BoardBox.SelectedIndex == 0) return "This device requires board 3.1+ or a verified modification.";
        if (Current.MinimumBoard == "3.2+" && BoardBox.SelectedIndex < 2) return "This device requires board 3.2+ or a verified modification.";
        return null;
    }

    private void UpdateOperationState()
    {
        if (!_ready) return;
        bool idle = !_interactionBusy && _running is null;
        ProgrammingInputs.IsEnabled = idle;
        CalibrationInputs.IsEnabled = idle;
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
        ConditionButton.IsEnabled = idle && setup && Current.Conditioning;
        WriteButton.Content = Current.IsJtag ? "ERASE AND WRITE XSVF" : "WRITE AND VERIFY";
        WriteReadiness.Text = !idle ? "An operation is in progress…" : SetupIssue() ??
            (!report.Valid ? "Select a valid file: " + report.Message : eraseIssue ?? "Ready to write. Switch socket power on.");
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
        StatusText.Text = text;
        Log(text);
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
        if (string.IsNullOrWhiteSpace(PortBox.Text)) { SetStatus("Select a serial port."); return false; }
        if (!requireChip) return true;
        if (OffCheck.IsChecked != true)
        {
            SetStatus("Confirm that the device was inserted with socket power off before continuing.");
            return false;
        }
        if (Current.NeedsAdapter && AdapterCheck.IsChecked != true)
        {
            SetStatus("Check the adapter wiring and orientation against its PCB documentation before continuing.");
            return false;
        }
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
        _running = new CancellationTokenSource();
        BusyBar.IsIndeterminate = true;
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
            var progress = new Progress<string>(Log);
            var result = await _programmer.RunAsync(PortBox.Text ?? "", Current, mode, bits, xsvf, erase,
                offset ?? 0, progress, timeout, _running.Token);
            SetStatus(result.Succeeded ? "The programmer confirmed completion." : "Operation failed: " + result.Error);
            if (mode is "m" or "b" or "s") CalibrationResult.Text = SummarizeVpp(result.Output);
            return result;
        }
        catch (OperationCanceledException) { SetStatus("Operation cancelled. Socket voltage may still be active; check the programmer."); return null; }
        catch (Exception ex) { SetStatus("Error: " + ex.Message); return null; }
        finally { _running.Dispose(); _running = null; BusyBar.IsIndeterminate = false; CancelButton.IsEnabled = false; UpdateOperationState(); }
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
        }
        catch (Exception ex) { SetStatus("Backup was not saved: " + ex.Message); }
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
        if (!Current.Conditioning || !SetupOk()) return;
        if (!await ConfirmAsync("Condition new ATF", $"This procedure destroys the contents of {Current.Name}.\n\n1. Write an all-zero fusemap twice.\n2. Erase the device.\n\nUse only for supported new ATF devices, following manufacturer guidance.")) return;
        string path = Path.Combine(Path.GetTempPath(), "afterburner-conditioning-" + Guid.NewGuid().ToString("N") + ".jed");
        try
        {
            await File.WriteAllTextAsync(path, JedecValidator.ConditioningJedec(Current.FuseCount), Encoding.Latin1);
            for (int pass = 1; pass <= 2; pass++)
            {
                SetStatus($"Conditioning: write {pass}/2");
                var outcome = await ExecuteAsync("w", TimeSpan.FromSeconds(50), path);
                if (outcome?.Succeeded != true) { SetStatus("Conditioning stopped: write failed."); return; }
            }
            SetStatus("Conditioning: final erase");
            var erased = await ExecuteAsync("e", TimeSpan.FromSeconds(18));
            if (erased?.Succeeded == true) SetStatus("Conditioning complete. You can now write your JEDEC file.");
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

    private void OnCancel(object? sender, RoutedEventArgs e) => _running?.Cancel();

    private bool VppSafe() { if (NoChipCheck.IsChecked == true) return true; SetStatus("Calibration is blocked: remove all devices from the socket."); return false; }
    private async Task VppSetAsync()
    {
        if (!VppSafe() || !SetupOk(requireChip: false)) return;
        if (await ConfirmAsync("Adjust VPP", "Ensure the socket is empty. The programmer will temporarily generate high voltage; measure it with a multimeter."))
            await ExecuteAsync("s", TimeSpan.FromSeconds(31));
    }
    private async Task VppCalibrateAsync()
    {
        if (!VppSafe() || !SetupOk(requireChip: false)) return;
        if (await ConfirmAsync("VPP calibration", "Calibration will overwrite EEPROM calibration values. Check the 16.5 V setting with a multimeter and keep the socket empty."))
            await ExecuteAsync("b", TimeSpan.FromSeconds(55), offset: (int)(OffsetBox.Value ?? 0));
    }
    private async Task VppMeasureAsync()
    {
        if (!VppSafe() || !SetupOk(requireChip: false)) return;
        await ExecuteAsync("m", TimeSpan.FromSeconds(53));
    }
    private async Task SaveLogAsync()
    {
        var item = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save diagnostic log", SuggestedFileName = "afterburner-studio.log", DefaultExtension = "log" });
        if (item?.TryGetLocalPath() is string path) { await File.WriteAllTextAsync(path, LogBox.Text ?? ""); SetStatus("Log saved: " + path); }
    }
    private void OnClearLog(object? sender, RoutedEventArgs e) => LogBox.Text = "";
}


