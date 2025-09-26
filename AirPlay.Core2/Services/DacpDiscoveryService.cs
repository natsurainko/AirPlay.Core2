using AirPlay.Core2.Models;
using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

namespace AirPlay.Core2.Services;

public class DacpDiscoveryService(MulticastService mdns, SessionManager sessionManager) : IHostedService
{
    private readonly MulticastService _mdns = mdns ?? throw new ArgumentNullException(nameof(mdns));
    private ServiceDiscovery? _serviceDiscovery;

    private readonly ConcurrentDictionary<string, (DomainName, IPEndPoint)> _dacpServices = [];

    public event EventHandler<IPEndPoint>? OnDacpServiceShutdown;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _serviceDiscovery = new ServiceDiscovery(_mdns);
        _serviceDiscovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
        _serviceDiscovery.ServiceInstanceShutdown += OnServiceInstanceShutdown;

        sessionManager.SessionCreated += OnSessionCreated;
        return Task.CompletedTask;
    }

    private void OnSessionCreated(object? sender, DeviceSession e)
    {
        if (_dacpServices.TryGetValue(e.DacpId, out var dacpService))
            e.SetDacpServiceEndPoint(dacpService.Item2);
    }

    private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        if (e.Message.Answers.Any(a => a is SRVRecord && a.CanonicalName.StartsWith($"iTunes_Ctrl_", StringComparison.OrdinalIgnoreCase)))
        {
            SRVRecord sRVRecord = e.Message.Answers.OfType<SRVRecord>()
                .First(a => a is not null);

            AddressRecord? addressRecord = e.Message.AdditionalRecords.OfType<AddressRecord>()
                .Concat(e.Message.Answers.OfType<AddressRecord>())
                .FirstOrDefault(a => a.Name == sRVRecord.Target && a.Type == DnsType.A);

            if (addressRecord == null) return;

            string dacpId = sRVRecord.Name.Labels[0].Replace("iTunes_Ctrl_", string.Empty);
            IPEndPoint iPEndPoint = new(addressRecord.Address, sRVRecord.Port);

            _dacpServices.AddOrUpdate(dacpId, (e.ServiceInstanceName, iPEndPoint), 
                (key, oldValue) => (e.ServiceInstanceName, iPEndPoint));
        }
    }

    private void OnServiceInstanceShutdown(object? sender, ServiceInstanceShutdownEventArgs e)
    {
        var dacpId = _dacpServices.FirstOrDefault(kv => kv.Value.Item1 == e.ServiceInstanceName);

        if (_dacpServices.TryRemove(dacpId.Key, out var kvp))
            OnDacpServiceShutdown?.Invoke(this, kvp.Item2);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _serviceDiscovery?.Dispose();
        _mdns.Stop();
        _mdns.Dispose();

        return Task.CompletedTask;
    }
}
