using AirPlay.Core2.Models;
using AirPlay.Core2.Services;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace AirPlay.App.Services;

internal class MirrorService(SessionManager sessionManager) : IHostedService
{
    //private readonly ConcurrentDictionary<DeviceSession, FileStream> _mirroringSession = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.SessionCreated += (_, session) =>
        {
            session.MirrorControllerCreated += (_, _) =>
            {
                //var fileStream = File.Create($"outputs\\{session.ActiveRemote}.h264");
                //_mirroringSession.TryAdd(session, fileStream);

                //session.MirrorController!.H264DataReceived += (_, d) => Task.Run(() => fileStream.WriteAsync(d.Data));
            };

            session.MirrorControllerClosed += (_, _) =>
            {
                //_mirroringSession.TryRemove(session, out var fileStream);
                //fileStream?.Close();
            };
        };

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
