using AirPlay.Core2.Connections;
using AirPlay.Core2.Extensions;
using AirPlay.Core2.Models.Configs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace AirPlay.Core2.Services;

public class AirPlayService(ILoggerFactory loggerFactory, IOptions<AirPlayConfig> options) : BackgroundService
{
    private readonly ILogger<AirPlayService> _logger = loggerFactory.CreateLogger<AirPlayService>();

    private readonly TcpListener tcpListener = new(IPAddress.Any, options.Value.Port);
    private readonly ConcurrentDictionary<IPEndPoint, ModifiedHttpConnection> _httpConnections = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        tcpListener.Start();

        while (!stoppingToken.IsCancellationRequested)
        {
            var client = await tcpListener.AcceptTcpClientAsync(stoppingToken);
            _logger.HttpClientAccpeted(client.Client.RemoteEndPoint);

            if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint)
            {
                client.Close();
                continue;
            }
            var connection = new ModifiedHttpConnection(client, options, loggerFactory);
            connection.BeginMessageLoopWorker(stoppingToken);

            _httpConnections.TryAdd(remoteEndPoint, connection);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        return base.StopAsync(cancellationToken);
    }
}

internal static partial class AirPlayServiceLoggers
{
    [LoggerMessage(LogLevel.Information, "Client from [{endPoint}] accepted, creating HttpConnection..")]
    public static partial void HttpClientAccpeted(this ILogger logger, EndPoint? endPoint);
}