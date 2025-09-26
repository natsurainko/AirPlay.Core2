using AirPlay.Core2.Connections.Audio;
using AirPlay.Core2.Models.Messages.Audio;
using AirPlay.Core2.Utils;

using AesSecret = (byte[] DecryptedAesKey, byte[] AesIv, byte[] EcdhShared);

namespace AirPlay.Core2.Controllers;

public class AudioController : IDisposable
{
    public const int RAOP_PACKET_LENGTH = 50000;

    private readonly AudioDataConnection _dataConnection;
    private readonly AudioControlConnection _controlConnection;

    public ushort ControlPort { get; }
    public ushort TimingPort { get; }
    public ushort DataPort { get; }

    public required ushort RemoteControlPort { get; init; }
    public required ushort RemoteTimingPort { get; init; }
    public AudioFormat AudioFormat { get; }

    public int? LatencyMin { get; init; }
    public int? LatencyMax { get; init; }

    public double Volume { get; private set; } = 0;

    public event EventHandler<PcmAudioData>? AudioDataReceived
    {
        add => _dataConnection?.DataReceived += value;
        remove => _dataConnection?.DataReceived -= value;
    }
    public event EventHandler<double>? RemoteSetVolumeRequest;

    public AudioController(AudioFormat audioFormat, AesSecret aesSecret)
    {
        ushort[] ports = [.. PortUtils.GetAvalivableUdpPorts(7050, 3)];
        ControlPort = ports[0];
        TimingPort = ports[1];
        DataPort = ports[2];

        AudioFormat = audioFormat;

        _dataConnection = new AudioDataConnection
        (
            DataPort, 
            audioFormat, 
            aesSecret
        );
        _controlConnection = new AudioControlConnection(RemoteControlPort);

        _controlConnection.SyncDataReceived += (_, data) => _dataConnection.HandleSyncData(data);
        _controlConnection.ResentDataReceived += (_, data) => _dataConnection.HandleResendBuffer(data);
        _dataConnection.ResendRequested += (_, r) => _controlConnection.HandleResendPacket(r);
    }

    public void BeginConnectionWorkers()
    {
        _controlConnection?.BeginControlMessageLoopWorker();
        _dataConnection?.BeginDataMessageLoopWorker();
    }

    public void EndConnectionWorkers()
    {
        _controlConnection?.EndControlMessageLoopWorker();
        _dataConnection?.EndDataMessageLoopWorker();
    }

    public void Flush(int nextSeq) => _dataConnection?.Flush(nextSeq);

    public void SetVolume(double volume) => Volume = volume;

    public void Dispose()
    {
        _dataConnection.Dispose();
        _controlConnection.Dispose();
    }

    internal void RemoteSetVolume(double volume)
    {
        Volume = volume;
        RemoteSetVolumeRequest?.Invoke(this, volume);
    }
}
