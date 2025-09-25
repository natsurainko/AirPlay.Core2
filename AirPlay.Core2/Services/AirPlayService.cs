using AirPlay.Core2.Extensions;
using AirPlay.Core2.Models.Configs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.NetworkInformation;

namespace AirPlay.Core2.Services;

public class AirPlayService(ILogger<AirPlayService> logger, IOptions<AirPlayConfig> options) : BackgroundService
{
    private readonly HttpListener _httpListener = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => !nic.Description.Contains("Virtual") && nic.OperationalStatus == OperationalStatus.Up && nic.Supports(NetworkInterfaceComponent.IPv4))
            .Select(nic => nic.GetIPProperties())
            .Select(p => p.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address)
            .OfType<IPAddress>()
            .Where(a => !IPAddress.IsLoopback(a));

        foreach (var address in addresses)
            _httpListener.Prefixes.Add($"http://{address}:{options.Value.Port}/");

        if (!addresses.Any())
            _httpListener.Prefixes.Add($"http://+:{options.Value.Port}/");

        _httpListener.Start();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var httpContext = await _httpListener.GetContextAsync();
                if (!httpContext.Request.IsLocal)
                {
                    httpContext.Response.StatusCode = 403;
                    httpContext.Response.Close();
                    continue;
                }

                Task.Run(async () => await HandleHttpContextAsync(httpContext, stoppingToken), stoppingToken)
                    .ContinueWith(t => httpContext.Response.Close(), stoppingToken).Forget();
            }
            catch (Exception)
            {

            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _httpListener.Stop();
        _httpListener.Close();

        return base.StopAsync(cancellationToken);
    }

    private async Task HandleHttpContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        logger.HttpRequestMessageReceived(context.Request.RemoteEndPoint!, context.Request.HttpMethod, context.Request.Url!.ToString());
    }
}

internal static partial class AirPlayServiceLoggers
{
    [LoggerMessage(LogLevel.Information, "HttpRequestMessage from [{iPEndPoint}] Received: [{method}] \"{requestPath}\"")]
    public static partial void HttpRequestMessageReceived(this ILogger logger, IPEndPoint iPEndPoint, string method, string requestPath);
}