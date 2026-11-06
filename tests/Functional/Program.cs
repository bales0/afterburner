using System.Text;
using AfterburnerStudio;
using AfterburnerStudio.Models;
using AfterburnerStudio.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    private static readonly ChipProfile Atf = ChipProfile.All.Single(c => c.Name == "ATF16V8B");

    [STAThread]
    public static int Main()
    {
        try
        {
            TestData();
            TestProtocol();
            TestUi();
            Console.WriteLine($"PASS: {_checks} checks (JEDEC, pins, serial emulator, UI state, button styles).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void TestData()
    {
        var valid = JedecValidator.ConditioningJedec(Atf.FuseCount);
        Check(JedecValidator.ValidateText(valid, Atf).Valid, "Valid JEDEC rejected");
        Check(!JedecValidator.ValidateText(valid.Replace("C0000", "C0001"), Atf).Valid, "Bad checksum accepted");
        Check(!JedecValidator.ValidateText(valid.Replace("2194", "5892"), Atf).Valid, "Wrong QF accepted");
        Check(!JedecValidator.ValidateText(valid.Replace("F0", "F0*L2194 11"), Atf).Valid, "Out-of-range L accepted");
        Check(!JedecValidator.ValidateText(valid.Replace("F0", "F0*G1"), Atf).Valid, "G1 accepted");
        Check(JedecValidator.ValidateText(valid.Replace("2194", "2195"), Atf).Valid, "APD fuse rejected");
        foreach (int pins in new[] {20,24})
        {
            var actual = new List<int>();
            for (int col=0; col<12; col++)
            {
                var top = SocketLayout.ChipPin(pins,col,true);
                var bottom = SocketLayout.ChipPin(pins,col,false);
                if (top.HasValue) actual.Add(top.Value);
                if (bottom.HasValue) actual.Add(bottom.Value);
            }
            Check(actual.Order().SequenceEqual(Enumerable.Range(1,pins)), $"DIP{pins} numbering has omissions or duplicates");
            Check(SocketLayout.ChipPin(pins,11,true) == 1, "Pin 1 must be upper right");
        }
        Check(SocketLayout.TopContact(0)==12 && SocketLayout.TopContact(11)==1, "ZIF top row mirrored");
        Check(SocketLayout.BottomContact(0)==13 && SocketLayout.BottomContact(11)==24, "ZIF bottom row mirrored");
        Check(SocketLayout.ChipPin(20,0,true)==null && SocketLayout.ChipPin(20,1,false)==null, "DIP20 unused contacts wrong");
        Check(SocketLayout.ChipPin(20,2,false)==11, "DIP20 pin 11 must occupy ZIF15");
        Check(NativeAfterburner.XsvfSucceeded("Q-0,OK") && !NativeAfterburner.XsvfSucceeded("Q-3,Fail"), "XSVF result parsing");
    }

    private static NativeResult Run(Emulator port, ChipProfile chip, string mode, CancellationToken token = default, byte[]? bits = null) =>
        new NativeAfterburner(_ => port).RunAsync("COM_TEST", chip, mode, bits ?? new byte[chip.FuseCount],
            new byte[]{1,2,0}, new byte[]{1,2,0}, 0, new Progress<string>(), TimeSpan.FromSeconds(3), token).GetAwaiter().GetResult();

    private static void TestProtocol()
    {
        var port = new Emulator();
        Check(Run(port,Atf,"wv").Succeeded, "Write+verify failed");
        Check(port.Commands.Contains("#t ; ATF16V8B") && port.Commands.Contains("#c 0000"), "Wrong GAL ID or checksum");
        Check(port.Commands.TakeLast(2).SequenceEqual(new[]{"w","v"}), "Write/verify order wrong");
        Check(port.Closed && port.Disposed, "Port leak");
        // VPP must use Studio's native serial protocol, not the CLI YES prompt.
        var voltagePort = new Emulator();
        var voltageEvents = new ProgressCollector<ProgrammerProgress>();
        var voltageResult = new NativeAfterburner(_ => voltagePort).RunAsync("COM_TEST", Atf, "m",
            null, null, null, 0, new Progress<string>(), TimeSpan.FromSeconds(3),
            default, voltageEvents).GetAwaiter().GetResult();
        Check(voltageResult.Succeeded && voltagePort.Commands.Contains("m") &&
            !voltagePort.Commands.Any(c => c.Contains("YES", StringComparison.OrdinalIgnoreCase)),
            "Studio VPP measurement incorrectly depends on CLI confirmation");
        Check(voltageEvents.Events.Count(e => e.Kind == ProgrammerProgressKind.Voltage) == 6,
            "Missing structured voltage events");
        Check(voltageResult.VoltageReadings?.Count == 6,
            "Structured final VPP readings were not preserved");
        var orderedVppEvents = voltageEvents.Events.Where(e =>
            e.Kind is ProgrammerProgressKind.Voltage or ProgrammerProgressKind.VoltageStep).ToList();
        Check(orderedVppEvents.Count == 12 &&
            Enumerable.Range(0, 6).All(i =>
                orderedVppEvents[i * 2].Kind == ProgrammerProgressKind.VoltageStep &&
                orderedVppEvents[i * 2].Current == i + 1 &&
                orderedVppEvents[i * 2 + 1].Kind == ProgrammerProgressKind.Voltage &&
                orderedVppEvents[i * 2 + 1].Current == i + 1),
            "VPP steps must be announced and measured incrementally in order");
        Check(voltageEvents.Events.Any(e => e.Kind == ProgrammerProgressKind.Voltage && e.VoltageTarget == "9.0V" && e.Voltage == 9.03),
            "Incorrect VPP event parsing");
        Check(NativeAfterburner.VppStepNumber("4.2 - 5.0V") == 1 &&
            NativeAfterburner.VppStepNumber("16.0V") == 6 &&
            NativeAfterburner.VppStepNumber("not-a-target") == 0,
            "VPP step prefix recognition changed unexpectedly");
        var missingVoltagePort = new Emulator { IncompleteVpp = true };
        var incompleteResult = Run(missingVoltagePort, Atf, "m");
        Check(!incompleteResult.Succeeded && incompleteResult.Error?.Contains("Incomplete VPP measurement") == true,
            "Incomplete VPP measurement must be reported as an error");
        // Device information is a structured UI result, not text scraped from the diagnostic log.
        var pesPort = new Emulator();
        var pesEvents = new ProgressCollector<ProgrammerProgress>();
        var pesResult = new NativeAfterburner(_ => pesPort).RunAsync("COM_TEST", Atf, "i",
            null, null, null, 0, new Progress<string>(), TimeSpan.FromSeconds(3),
            default, pesEvents).GetAwaiter().GetResult();
        Check(pesResult.Succeeded && pesResult.Details?.Any(s => s.Contains("PES info:")) == true,
            "Device identification was not returned as structured UI details");
        var noChip = new Emulator { PesResponse = "PES info: 5V Unknown GAL." };
        var missingChipResult = Run(noChip, Atf, "i");
        Check(!missingChipResult.Succeeded &&
            missingChipResult.Error?.Contains("Chip not detected") == true &&
            missingChipResult.Details?.Any(d => d.Contains("Chip not detected")) == true,
            "An unknown PES must display chip-not-detected instead of success");
        Check(!Run(new Emulator { PesResponse = "PES info: " }, Atf, "i").Succeeded,
            "Empty PES must not be identified as a detected chip");
        var mismatch = Run(new Emulator {
            PesResponse = "PES info: 5V Lattice GAL22V10 VPP=12.00 Timing: prog=100 erase=25" }, Atf, "i");
        Check(!mismatch.Succeeded && mismatch.Error?.Contains("Detected GAL22V10") == true,
            "A different GAL type must not be shown as successfully identified");
        Check(NativeAfterburner.ValidatePesIdentification(
            "PES info: Atmel ATF16V8C VPP=11.00 Timing: prog=20 erase=25", "ATF16V8B") is null,
            "Supported ATF16V8C alias must remain accepted");

        Check(pesResult.ConnectionSummary?.Contains("AFTerburner v.") == true,
            "Final result must contain connection information independently of queued UI progress events");
        Check(pesEvents.Events.Any(e => e.Kind == ProgrammerProgressKind.Connection) &&
            pesEvents.Events.Any(e => e.Kind == ProgrammerProgressKind.Detail),
            "Programmer connection and PES information must be UI events");
        var verifyPort = new Emulator();
        var verifyUi = new ProgressCollector<ProgrammerProgress>();
        var verifyResult = new NativeAfterburner(_ => verifyPort).RunAsync("COM_TEST", Atf, "wv",
            new byte[Atf.FuseCount], null, null, 0, new Progress<string>(), TimeSpan.FromSeconds(3),
            default, verifyUi).GetAwaiter().GetResult();
        Check(verifyResult.Succeeded && verifyResult.Details?.Any(d => d.StartsWith("Verification:")) == true,
            "Program/verification results must not exist only in the diagnostic log");
        var noCalibration = new Emulator { VppCalibrationStatus = "VPP-CAL-MISSING" };
        Check(!Run(noCalibration, Atf, "wv").Succeeded && !noCalibration.Commands.Contains("w"),
            "Missing or invalid VPP calibration must block programming");
        // Only diagnostics: RAM presence must not influence programming decisions.
        foreach (var (banner, expected) in new[] {
            ("SeRAM:23LC512", "23LC512 detected"),
            ("SeRAM:23LC1024", "23LC1024 detected"),
            ("SeRAM:None", "Not detected"),
            ("SeRAM:NotChecked", "Detection unavailable"),
            ("", "Not reported by firmware") })
        {
            var probePort = new Emulator { ExternalRamBanner = banner };
            var programmer = new NativeAfterburner(_ => probePort);
            var result = programmer.RunAsync("COM_TEST", Atf, "m", null, null, null, 0,
                new Progress<string>(), TimeSpan.FromSeconds(3), default).GetAwaiter().GetResult();
            Check(result.Succeeded && programmer.ExternalRamStatus.Contains(expected, StringComparison.Ordinal),
                "Bad external SRAM diagnostic: " + banner);
        }
        var legacyNoStatus = new Emulator { VppCalibrationStatus = "" };
        Check(!Run(legacyNoStatus, Atf, "wv").Succeeded && !legacyNoStatus.Commands.Contains("w"),
            "Legacy firmware without VPP calibration status must not be treated as verified");
        var bits = new byte[Atf.FuseCount]; bits[0]=1; bits[9]=1;
        port = new Emulator();
        Check(Run(port,Atf,"wv",bits:bits).Succeeded && port.Commands.Contains("#f 0000 01020000") &&
            port.Commands.Contains("#c 0003"), "Fuse bit packing / checksum is wrong");
        port = new Emulator { ErrorCommand="v" };
        Check(!Run(port,Atf,"wv").Succeeded, "Firmware verify error ignored");
        port = new Emulator { ErrorCommand="#c 0000" };
        Check(!Run(port,Atf,"wv").Succeeded && !port.Commands.Contains("w"), "Write continued after checksum rejection");
        var jtag = ChipProfile.All.Single(c=>c.Name=="ATF1502AS");
        port = new Emulator();
        Check(Run(port,jtag,"ew").Succeeded, "XSVF erase+write failed");
        Check(port.Commands.Where(x=>x.StartsWith('j')).SequenceEqual(new[]{"j1","j0"}), "JTAG order wrong");
        Check(port.BinaryChunks==2 && !port.UnreadBeforeCommand, "Next XSVF started before cleanup prompt");
        foreach (string reply in new[] { "ER Unknown command: j1", "Error: JTAG unavailable", ">" })
        {
            port = new Emulator { JtagStartResponse = reply + "\r\n>\r\n" };
            var rejected = Run(port, jtag, "ew");
            Check(!rejected.Succeeded && rejected.Error?.Contains("JTAG") == true &&
                !rejected.Error.Contains("timed out", StringComparison.OrdinalIgnoreCase),
                "JTAG rejection must fail immediately with a useful error: " + reply);
            Check(port.BinaryChunks == 0 && !port.Commands.Contains("j0") && port.Closed && port.Disposed,
                "Rejected JTAG operation continued or leaked the port");
        }
        port = new Emulator { JtagResult="Q-3,Fail" };
        Check(!Run(port,jtag,"ew").Succeeded && !port.Commands.Contains("j0"), "JTAG failure ignored");
        port = new Emulator();
        Check(Run(port,jtag,"m").Succeeded && port.Commands.Contains("m") && !port.Commands.Any(x=>x.StartsWith('j')), "Calibration depends on selected JTAG profile");
        using var cts = new CancellationTokenSource();
        port = new Emulator { CancelOnWrite=cts };
        bool cancelled=false;
        try { Run(port,Atf,"wv",cts.Token); } catch(OperationCanceledException) { cancelled=true; }
        Check(cancelled && port.Disposed, "Cancellation failed to close port");
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void TestUi()
    {
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var window = new MainWindow();
        Check(window.FindControl<ComboBox>("PortList") is not null,
            "Compiled main-window XAML was not loaded");
        T Find<T>(string name) where T:Control => window.FindControl<T>(name) ?? throw new Exception("Missing "+name);
        window.Show(); Pump();
        string directory=Path.Combine(Path.GetTempPath(),"AfterburnerTests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var ports=Find<ComboBox>("PortList");
            ports.ItemsSource=new[]{"COM3","COM12","COM_TEST"}; ports.SelectedItem="COM12"; Pump();
            Check(Find<Button>("IdentifyButton").IsEnabled,"COM selection does not enable identification");
            Check(window.FindControl<TextBox>("PortBox") is null,"Obsolete manual port field remains");
            Check(!Find<TextBlock>("HardwareSummary").IsVisible,
                "Hardware details must stay hidden until the programmer responds");
            var tabs = Find<TabControl>("Tabs");
            tabs.SelectedIndex = 1; Pump();
            // Connection and PCB selection must remain visible on the VPP page too.
            var connectionPanel = Find<Border>("ConnectionInputs");
            var board = Find<ComboBox>("BoardBox");
            Check(!tabs.GetVisualDescendants().Contains(connectionPanel) &&
                  connectionPanel.IsVisible && board.IsVisible,
                "Port and programmer PCB revision must be shared across both tabs");
            foreach (var name in new[] {"Vpp5Text","Vpp9Text","Vpp10Text","Vpp12Text","Vpp14Text","Vpp16Text"})
                Check(Find<TextBlock>(name).IsVisible, "Missing VPP live text: " + name);
            foreach (var name in new[] {"Vpp5Bar","Vpp9Bar","Vpp10Bar","Vpp12Bar","Vpp14Bar","Vpp16Bar"})
                Check(window.FindControl<ProgressBar>(name) is null, "Misleading voltage bars still present: " + name);
            var liveValue = typeof(MainWindow).GetMethod("SetVoltageReading", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Check((bool)liveValue.Invoke(window, new object?[] { "9.0V", 9.03 })! &&
                Find<TextBlock>("Vpp9Text").Text!.Contains("9.03") &&
                Find<TextBlock>("Vpp10Text").Text == "Waiting",
                "Single VPP point must update its own text immediately");
            var vppSafe = Find<CheckBox>("NoChipCheck");
            Check(window.FindControl<CheckBox>("OffCheck") is null,
                "Repeated insertion-confirmation checkbox should be removed");
            vppSafe.IsChecked = true; Pump();
            ports.SelectedItem = "COM_TEST"; Pump();
            Check(Find<Button>("VppMeasureButton").IsEnabled, "VPP start button disabled despite safety confirmation");
            Find<Button>("VppMeasureButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var vppConfirmation = window.OwnedWindows.Single();
            Check(vppConfirmation.Title?.Contains("socket protection", StringComparison.OrdinalIgnoreCase) == true,
                "Missing graphical safety confirmation for VPP measurement");
            vppConfirmation.Close(false); Pump();
            Check(!Find<Button>("CancelButton").IsEnabled,
                "Cancelled confirmation must not start the programmer");
            tabs.SelectedIndex = 0; Pump();
            Check(Find<TextBlock>("ProgrammingResultTitle").IsVisible &&
                Find<TextBlock>("ProgrammingResultDetails").IsVisible,
                "Programming results must be on the Programming page, not only on the diagnostic tab");
            var file=Find<TextBox>("JedPath");
            var chip=Find<ComboBox>("ChipBox");
            var write=Find<Button>("WriteButton");
            Check(!write.IsEnabled,"Write enabled without a file");
            string jed=Path.Combine(directory,"test.jed");
            File.WriteAllText(jed,JedecValidator.ConditioningJedec(Atf.FuseCount),Encoding.Latin1);
            file.Text=jed; Pump();
            Check(write.IsEnabled,"Valid file and port should permit write confirmation without another checkbox");
            ports.SelectedItem=null; Pump(); Check(!write.IsEnabled,"Write enabled without a port");
            ports.SelectedItem="COM_TEST"; Pump(); Check(write.IsEnabled,"Selected port not accepted");
            file.Text=Path.Combine(directory,"missing.jed"); Pump(); Check(!write.IsEnabled,"Missing file accepted");
            file.Text=jed; Pump();
            // Open confirmation, then cancel. Never open a real serial port in UI tests.
            write.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(!Find<StackPanel>("ProgrammingInputs").IsEnabled,"Inputs editable during confirmation");
            var confirm=window.OwnedWindows.Single();
            Check(confirm.Title=="Write device","Confirmation is not English");
            confirm.Close(false); Pump();
            Check(write.IsEnabled,"Cancel did not restore inputs");
            chip.SelectedIndex=16; Pump();
            Check(Find<StackPanel>("JtagErasePanel").IsVisible,"JTAG XSVF explanation did not appear");
            Check(!Find<Button>("ReadButton").IsEnabled,"JTAG read enabled");
            string xsvf=Path.Combine(directory,"design.xsvf"); File.WriteAllBytes(xsvf,new byte[]{0});
            string erase=Path.Combine(directory,"erase_ATF1502AS.xsvf"); File.WriteAllBytes(erase,new byte[]{0});
            file.Text=xsvf; Find<TextBox>("EraseXsvfPath").Text=erase;
            Pump();
            Check(write.IsEnabled,"Valid XSVF setup does not enable write");
            Find<TextBox>("EraseXsvfPath").Text=Path.Combine(directory,"missing.xsvf"); Pump();
            Check(!write.IsEnabled,"Missing explicit erase file silently ignored");

            // Reuse window styles in an isolated button so pointer tests cannot program hardware.
            var button=new Button {Content="WRITE AND VERIFY",Width=240,Height=48};
            button.Classes.Add("mainaction"); window.Content=button; Pump(); window.UpdateLayout();
            var presenter=button.GetVisualDescendants().OfType<ContentPresenter>().First();
            void White(string state) => Check(presenter.Foreground is ISolidColorBrush brush && brush.Color==Colors.White,"Invisible text in "+state);
            White("normal");
            var center=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),window)!.Value;
            window.MouseMove(center); Pump(); White("hover");
            window.MouseDown(center,MouseButton.Left); Pump(); White("pressed");
            window.MouseUp(center,MouseButton.Left); Pump();
            button.IsEnabled=false; Pump();
            Check(presenter.Foreground is ISolidColorBrush disabled && disabled.Color!=Colors.White,"Disabled text lacks contrast");
        }
        finally { window.Close(); Directory.Delete(directory,true); }
    }
}

internal sealed class Emulator : ISerialTransport
{
    private readonly Queue<byte> _receive=new();
    public List<string> Commands {get;}=new();
    public string? ErrorCommand {get;init;}
    public string JtagResult {get;init;}="Q-0,OK";
    public string JtagStartResponse {get;init;}="\r\nRXSVF\r\n$003\r\n";
    public bool IncompleteVpp {get;init;} = false;
    public string VppCalibrationStatus {get;init;}="VPP-CAL-OK";
    public string ExternalRamBanner {get;init;}="SeRAM:None";
    public string PesResponse {get;init;}="PES info: 5V Atmel ATF16V8B VPP=11.00 Timing: prog=100 erase=25";
    public CancellationTokenSource? CancelOnWrite {get;init;}
    public bool Closed {get;private set;}
    public bool Disposed {get;private set;}
    public bool UnreadBeforeCommand {get;private set;}
    public int BinaryChunks {get;private set;}
    private void Reply(string text) { foreach(var b in Encoding.Latin1.GetBytes(text)) _receive.Enqueue(b); }
    public void Open() {}
    public void Close()=>Closed=true;
    public void Dispose()=>Disposed=true;
    public void DiscardInBuffer()=>_receive.Clear();
    public int ReadByte() { if(_receive.Count==0) throw new TimeoutException(); return _receive.Dequeue(); }
    public void Write(string text)
    {
        if(_receive.Count>0) UnreadBeforeCommand=true;
        string cmd=text.TrimEnd('\r'); Commands.Add(cmd);
        if(CancelOnWrite is not null) {CancelOnWrite.Cancel(); return;}
        if(cmd=="*") Reply("AFTerburner v.0.6.3 varVpp " + VppCalibrationStatus + " " + ExternalRamBanner + "\r\n>\r\n");
        else if(cmd=="m") {
            Reply("VPP calib. offset: -22\r\nVPP: 4.2 - 5.0V : 4.68\r\nVPP: 9.0V : 9.03\r\n" +
                "VPP: 10.0V : 10.02\r\nVPP: 12.0V : 12.00\r\n" +
                "VPP: 14.0V : 14.08\r\n" +
                (IncompleteVpp ? "" : "VPP: 16.0V : 16.05\r\n") + ">\r\n");
        }
        else if(cmd=="p") Reply(PesResponse + "\r\n>\r\n");
        else if(cmd.StartsWith('j')) Reply(JtagStartResponse);
        else if(cmd==ErrorCommand) Reply("ER simulated failure\r\n>\r\n");
        else if(cmd.StartsWith("#c ",StringComparison.Ordinal)) Reply("OK checksum matches\r\n>\r\n");
        else if(cmd=="#e") Reply("OK upload finished\r\n>\r\n");
        else Reply("OK\r\n>\r\n");
    }
    public void Write(byte[] data,int offset,int count)
    {
        if(count!=3 || data[offset+count-1]!=0) throw new Exception("Bad binary XSVF chunk");
        BinaryChunks++;
        Reply(JtagResult+"\r\n>\r\n");
    }
}

internal sealed class ProgressCollector<T> : IProgress<T>
{
    public List<T> Events { get; } = new();
    public void Report(T value) => Events.Add(value);
}
