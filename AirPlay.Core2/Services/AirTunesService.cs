using AirPlay.Core2.Connections;
using AirPlay.Core2.Models.Configs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace AirPlay.Core2.Services;

public class AirTunesService(SessionManager sessionManager,
    ILoggerFactory loggerFactory, IOptions<AirTunesConfig> options) : BackgroundService
{
    private readonly ILogger<AirTunesService> _logger = loggerFactory.CreateLogger<AirTunesService>();

    private readonly TcpListener _tcpListener = new(IPAddress.Any, options.Value.Port);
    private readonly ConcurrentDictionary<IPEndPoint, RtspConnection> _rtspConnections = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _tcpListener.Start();

        while (!stoppingToken.IsCancellationRequested)
        {
            var client = await _tcpListener.AcceptTcpClientAsync(stoppingToken);
            _logger.RtspClientAccpeted(client.Client.RemoteEndPoint);

            if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint)
            {
                client.Close();
                continue;
            }

            var connection = new RtspConnection(client, options, loggerFactory);
            connection.BeginMessageLoopWorker(stoppingToken);
            connection.SessionPaired += (_, session) =>
            {
                sessionManager.TryAddSession(remoteEndPoint, session);
                _logger.DevicePaired(session.DeviceDisplayName, session.DeviceModel);
            };
            connection.ConnectionClosed += (_, _) =>
            {
                _rtspConnections.TryRemove(remoteEndPoint, out var rtspConnections);
                sessionManager.TryRemoveSession(remoteEndPoint, out var deviceSession);

                if (deviceSession != null)
                    _logger.DeviceDisconnected(deviceSession.DeviceDisplayName, deviceSession.DeviceModel);

                deviceSession?.Dispose();
                rtspConnections?.Dispose();
                client?.Dispose();
            };

            _rtspConnections.TryAdd(remoteEndPoint, connection);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _tcpListener.Stop();
        _tcpListener.Dispose();

        return base.StopAsync(cancellationToken);
    }
}

internal static partial class AirTunesServiceLoggers
{
    [LoggerMessage(LogLevel.Information, "Client from [{endPoint}] accepted, creating RtspConnection..")]
    public static partial void RtspClientAccpeted(this ILogger logger, EndPoint? endPoint);

    [LoggerMessage(LogLevel.Information, "Device [\"{model}\": \"{name}\"] Session Paired")]
    public static partial void DevicePaired(this ILogger logger, string name, string? model);

    [LoggerMessage(LogLevel.Information, "Device [\"{model}\": \"{name}\"] Session Disconnected")]
    public static partial void DeviceDisconnected(this ILogger logger, string name, string? model);
}