using System.IO.Ports;
using System.Text;

namespace AfterburnerStudio.Services;

public interface ISerialTransport : IDisposable
{
    void Open();
    void Close();
    void DiscardInBuffer();
    int ReadByte();
    void Write(string text);
    void Write(byte[] data, int offset, int count);
}

internal sealed class SerialTransport(string name) : ISerialTransport
{
    private readonly SerialPort _port = new(name.Trim(), 57600, Parity.None, 8, StopBits.One)
    {
        ReadTimeout = 120, WriteTimeout = 2500, Handshake = Handshake.None,
        DtrEnable = false, RtsEnable = false, Encoding = Encoding.Latin1
    };
    public void Open() => _port.Open();
    public void Close() => _port.Close();
    public void DiscardInBuffer() => _port.DiscardInBuffer();
    public int ReadByte() => _port.ReadByte();
    public void Write(string text) => _port.Write(text);
    public void Write(byte[] data, int offset, int count) => _port.Write(data, offset, count);
    public void Dispose() => _port.Dispose();
}

