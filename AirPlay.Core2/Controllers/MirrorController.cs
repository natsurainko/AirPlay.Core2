using AirPlay.Core2.Connections.Mirror;
using AirPlay.Core2.Models.Messages.Mirror;
using AirPlay.Core2.Utils;
using System.Drawing;

using AesSecret = (byte[] DecryptedAesKey, byte[] AesIv, byte[] EcdhShared);

namespace AirPlay.Core2.Controllers;

public class MirrorController : IDisposable
{
    private readonly MirrorDataConnection _dataConnection;

    public ushort DataPort { get; }

    public ushort TimingPort { get; } = 7010;

    public Size? FrameSize => _dataConnection.FrameSize;

    public event EventHandler<Size>? FrameSizeChanged
    {
        add => _dataConnection?.FrameSizeChanged += value;
        remove => _dataConnection?.FrameSizeChanged -= value;
    }
    public event EventHandler<H264Data>? H264DataReceived
    {
        add => _dataConnection?.DataReceived += value;
        remove => _dataConnection?.DataReceived -= value;
    }

    public MirrorController(string streamConnectionId, AesSecret aesSecret)
    {
        ushort[] ports = [.. PortUtils.GetAvalivableTcpPorts(7050, 1)];
        DataPort = ports[0];

        _dataConnection = new MirrorDataConnection
        (
            DataPort,
            streamConnectionId,
            aesSecret
        );
    }

    public void BeginConnectionWorkers()
    {
        _dataConnection?.BeginDataMessageLoopWorker();
    }

    public void EndConnectionWorkers()
    {
        _dataConnection?.EndDataMessageLoopWorker();
    }

    public void Dispose()
    {
        _dataConnection.Dispose();
    }
}
