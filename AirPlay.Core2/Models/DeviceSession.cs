using AirPlay.Core2.Controllers;
using AirPlay.Core2.Crypto;
using AirPlay.Core2.Extensions;
using AirPlay.Core2.Models.Messages;
using AirPlay.Core2.Models.Messages.Audio;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AirPlay.Core2.Models;

public class DeviceSession(byte[] aesIv, byte[] ecdhShared, ushort timingPort, ILogger? logger = null) : IDisposable
{
    private readonly byte[] _decryptedAesKey = new byte[16];
    private Action<double>? _remoteSetVolumeAction;

    public event EventHandler? AudioControllerCreated;
    public event EventHandler? AudioControllerClosed;

    public event EventHandler? MirrorControllerCreated;
    public event EventHandler? MirrorControllerClosed;

    public required string DeviceDisplayName { get; init; }

    public required string DeviceMacAddress { get; init; }

    public required string DacpId { get; init; }

    public required string ActiveRemote {  get; init; }

    public string? DeviceModel { get; init; }

    public bool IsMirrorSession { get; init; }

    public bool RequestedDisconnecet { get; private set; }

    public double Volume { get; private set; } = 100;

    public TimeSpan VolumeDelay => IsMirrorSession ? TimeSpan.Zero : TimeSpan.FromSeconds(1.5);

    public IPEndPoint? DacpServiceEndPoint { get; private set; }

    public AudioController? AudioController { get; private set; }

    public MirrorController? MirrorController { get; private set; }

    public event EventHandler<MediaProgressInfo>? MediaProgressInfoReceived;
    public event EventHandler<MediaWorkInfo>? MediaWorkInfoReceived;
    public event EventHandler<byte[]>? MediaCoverReceived;
    public event EventHandler<double>? RemoteSetVolumeRequest;

    public event EventHandler? DacpServiceFound;

    public void DecrypteAesKey(byte[] keyMsg, byte[] aesKey) => OmgHax.DecryptAesKey(keyMsg, aesKey, _decryptedAesKey);

    public void CreateAudioController(ushort controlPort, AudioFormat audioFormat,
        int? latencyMin = default, int? latencyMax = default)
    {
        AudioController = new AudioController(audioFormat, (_decryptedAesKey, aesIv, ecdhShared))
        {
            RemoteTimingPort = timingPort,
            RemoteControlPort = controlPort,
            LatencyMin = latencyMin,
            LatencyMax = latencyMax
        };

        AudioControllerCreated?.Invoke(this, EventArgs.Empty);
        logger?.ControllerCreated("Audio", DeviceDisplayName);
    }

    public void CloseAudioController()
    {
        if (AudioController != null)
        {
            AudioController.EndConnectionWorkers();
            AudioController.Dispose();
            AudioController = null;

            AudioControllerClosed?.Invoke(this, EventArgs.Empty);
            logger?.ControllerClosed("Audio", DeviceDisplayName);
        }
    }

    public void CreateMirrorController(string streamConnectionId)
    {
        MirrorController = new MirrorController(streamConnectionId, (_decryptedAesKey, aesIv, ecdhShared));
        MirrorControllerCreated?.Invoke(this, EventArgs.Empty);
        logger?.ControllerCreated("Mirror", DeviceDisplayName);
    }

    public void CloseMirrorController()
    {
        if (MirrorController != null)
        {
            MirrorController.EndConnectionWorkers();
            MirrorController.Dispose();
            MirrorController = null;

            MirrorControllerClosed?.Invoke(this, EventArgs.Empty);
            logger?.ControllerClosed("Mirror", DeviceDisplayName);
        }
    }

    public async Task SendMediaControlCommandAsync(HttpClient httpClient, MediaControlCommand command)
    {
        if (DacpServiceEndPoint == null)
            throw new InvalidOperationException("DACP service not found.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://{DacpServiceEndPoint.Address}:{DacpServiceEndPoint.Port}/ctrl-int/1/{command.ToString().ToLower()}");
        request.Headers.Add("Active-Remote", ActiveRemote);

        using var _ = await httpClient.SendAsync(request);
    }

    public void Dispose()
    {
        CloseAudioController();
        CloseMirrorController();
    }
    
    public void Disconnect() => RequestedDisconnecet = true;

    public async Task SetVolumeAsync(double volume, HttpClient httpClient)
    {
        for (int i = 0; i < Math.Abs(Volume - volume) / 6; i++)
            await SendMediaControlCommandAsync(httpClient, volume < Volume ? MediaControlCommand.VolumeDown : MediaControlCommand.VolumeUp);

        Volume = volume;
    }

    internal void RemoteSetProgress(MediaProgressInfo progressInfo) => MediaProgressInfoReceived?.Invoke(this, progressInfo);

    internal void RemoteSetWorkInfo(MediaWorkInfo mediaWorkInfo) => MediaWorkInfoReceived?.Invoke(this, mediaWorkInfo);

    internal void RemoteSetCover(byte[] mediaCover) => MediaCoverReceived?.Invoke(this, mediaCover);

    internal void RemoteSetVolume(double volume)
    {
        if (_remoteSetVolumeAction == null)
        {
            _remoteSetVolumeAction = volume =>
            {
                Volume = (volume + 30) / 30 * 100;
                RemoteSetVolumeRequest?.Invoke(this, Volume);
            };
            _remoteSetVolumeAction = _remoteSetVolumeAction.Debounce(250);
        }

        _remoteSetVolumeAction(volume);
    }

    internal void SetDacpServiceEndPoint(IPEndPoint? endPoint)
    {
        DacpServiceEndPoint = endPoint;

        if (endPoint != null)
        {
            logger?.DacpServiceFound(DeviceDisplayName);
            DacpServiceFound?.Invoke(this, EventArgs.Empty);
        }
    }
}

internal static partial class DeviceSessionLoggers
{
    [LoggerMessage(LogLevel.Information, "[{controllerTypeName}] controller created for device [{deviceSessionName}]")]
    public static partial void ControllerCreated(this ILogger logger, string controllerTypeName, string deviceSessionName);

    [LoggerMessage(LogLevel.Information, "[{controllerTypeName}] controller closed for device [{deviceSessionName}]")]
    public static partial void ControllerClosed(this ILogger logger, string controllerTypeName, string deviceSessionName);

    [LoggerMessage(LogLevel.Information, "Dacp service found for device [{deviceSessionName}]")]
    public static partial void DacpServiceFound(this ILogger logger, string deviceSessionName);
}