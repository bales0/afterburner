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
        T Find<T>(string name) where T:Control => window.FindControl<T>(name) ?? throw new Exception("Missing "+name);
        window.Show(); Pump();
        string directory=Path.Combine(Path.GetTempPath(),"AfterburnerTests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var port=Find<TextBox>("PortBox");
            var ports=Find<ComboBox>("PortList");
            ports.ItemsSource=new[]{"COM3","COM12"}; ports.SelectedItem="COM12"; Pump();
            Check(port.Text=="COM12","COM selection does not update port");
            port.Text="COM99"; Pump();
            Check(ports.SelectedItem==null,"Manual port leaves stale list selection");
            var file=Find<TextBox>("JedPath");
            var chip=Find<ComboBox>("ChipBox");
            var off=Find<CheckBox>("OffCheck");
            var write=Find<Button>("WriteButton");
            Check(!write.IsEnabled,"Write enabled without a file");
            string jed=Path.Combine(directory,"test.jed");
            File.WriteAllText(jed,JedecValidator.ConditioningJedec(Atf.FuseCount),Encoding.Latin1);
            file.Text=jed; Pump();
            Check(!write.IsEnabled,"Write enabled without insertion confirmation");
            off.IsChecked=true; Pump();
            Check(write.IsEnabled,"Valid setup does not enable write");
            port.Text=""; Pump(); Check(!write.IsEnabled,"Write enabled without a port");
            port.Text="COM_TEST"; Pump(); Check(write.IsEnabled,"Manual port not accepted");
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
            Check(off.IsChecked==false && Find<StackPanel>("JtagErasePanel").IsVisible,"Device change did not reset confirmation / show XSVF controls");
            Check(!Find<Button>("ReadButton").IsEnabled,"JTAG read enabled");
            string xsvf=Path.Combine(directory,"design.xsvf"); File.WriteAllBytes(xsvf,new byte[]{0});
            string erase=Path.Combine(directory,"erase_ATF1502AS.xsvf"); File.WriteAllBytes(erase,new byte[]{0});
            file.Text=xsvf; Find<TextBox>("EraseXsvfPath").Text=erase; off.IsChecked=true;
            Find<CheckBox>("AdapterCheck").IsChecked=true; Pump();
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
        if(cmd=="*") Reply("AFTerburner v.0.6.2 varVpp\r\n>\r\n");
        else if(cmd.StartsWith('j')) Reply("\r\nRXSVF\r\n$003\r\n");
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
