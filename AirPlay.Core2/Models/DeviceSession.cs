using AirPlay.Core2.Controllers;
using AirPlay.Core2.Crypto;
using AirPlay.Core2.Models.Messages;
using AirPlay.Core2.Models.Messages.Audio;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AirPlay.Core2.Models;

public class DeviceSession(byte[] aesIv, byte[] ecdhShared, ushort timingPort, ILogger? logger = null) : IDisposable
{
    private readonly byte[] _decryptedAesKey = new byte[16];

    public event EventHandler? AudioControllerCreated;
    public event EventHandler? AudioControllerClosed;

    public required string DeviceDisplayName { get; init; }

    public required string DeviceMacAddress { get; init; }

    public required string DacpId { get; init; }

    public required string ActiveRemote {  get; init; }

    public string? DeviceModel { get; init; }

    public bool IsMirrorSession { get; init; }

    public IPEndPoint? DacpServiceEndPoint { get; private set; }

    public AudioController? AudioController { get; private set; }

    public event EventHandler<MediaProgressInfo>? MediaProgressInfoReceived;
    public event EventHandler<MediaWorkInfo>? MediaWorkInfoReceived;
    public event EventHandler<byte[]>? MediaCoverReceived;
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
    }

    internal void RemoteSetProgress(MediaProgressInfo progressInfo) => MediaProgressInfoReceived?.Invoke(this, progressInfo);

    internal void RemoteSetWorkInfo(MediaWorkInfo mediaWorkInfo) => MediaWorkInfoReceived?.Invoke(this, mediaWorkInfo);

    internal void RemoteSetCover(byte[] mediaCover) => MediaCoverReceived?.Invoke(this, mediaCover);

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