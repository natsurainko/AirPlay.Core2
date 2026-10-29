using AirPlay.Core2.Models;
using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;

namespace AirPlay.Core2.Services;

public class DacpDiscoveryService(MulticastService mdns, SessionManager sessionManager,
    ILogger<DacpDiscoveryService>? logger = null) : BackgroundService
{
    private readonly MulticastService _mdns = mdns ?? throw new ArgumentNullException(nameof(mdns));
    private readonly ConcurrentDictionary<string, (DomainName Name, IPEndPoint EndPoint)> _dacpServices
        = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<IPEndPoint>? OnDacpServiceShutdown;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var discovery = new ServiceDiscovery(_mdns);
        discovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
        discovery.ServiceInstanceShutdown += OnServiceInstanceShutdown;
        sessionManager.SessionCreated += OnSessionCreated;

        try
        {
            // AirPlayPublisher starts the shared mDNS transport later in host startup.
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            do
            {
                try
                {
                    discovery.QueryServiceInstances("_dacp._tcp");
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger?.LogWarning(ex, "Failed to query DACP services; discovery will retry.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            sessionManager.SessionCreated -= OnSessionCreated;
            discovery.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
            discovery.ServiceInstanceShutdown -= OnServiceInstanceShutdown;
            _dacpServices.Clear();
            // The shared MulticastService is managed by the publisher and DI container.
        }
    }

    private void OnSessionCreated(object? sender, DeviceSession session)
    {
        if (_dacpServices.TryGetValue(session.DacpId, out var service))
            session.SetDacpServiceEndPoint(service.EndPoint);
    }

    private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        var records = e.Message.Answers.Concat(e.Message.AdditionalRecords).ToArray();

        foreach (var srv in records.OfType<SRVRecord>())
        {
            if (srv.Name != e.ServiceInstanceName) continue;

            const string prefix = "iTunes_Ctrl_";
            string instance = srv.Name.Labels[0];
            if (!instance.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || instance.Length == prefix.Length)
                continue;

            var address = records.OfType<AddressRecord>()
                .FirstOrDefault(a => a.Name == srv.Target && a.Type == DnsType.A);
            if (address == null) continue;

            string dacpId = instance[prefix.Length..];
            var endPoint = new IPEndPoint(address.Address, srv.Port);
            _dacpServices[dacpId] = (srv.Name, endPoint);

            // Discovery may complete after SessionCreated, so update existing sessions too.
            sessionManager.SetDacpServiceEndPoint(dacpId, endPoint);
        }
    }

    private void OnServiceInstanceShutdown(object? sender, ServiceInstanceShutdownEventArgs e)
    {
        foreach (var service in _dacpServices)
        {
            if (service.Value.Name != e.ServiceInstanceName) continue;

            if (_dacpServices.TryRemove(service.Key, out var removed))
            {
                sessionManager.SetDacpServiceEndPoint(service.Key, null);
                OnDacpServiceShutdown?.Invoke(this, removed.EndPoint);
            }
        }
    }
}
