using AirPlay.App.Services;
using AirPlay.Core2.Extensions;
using AirPlay.Core2.Models.Configs;
using AirPlay.Example.ConsoleApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.UseAirPlayService();
        services.Configure<AirPlayConfig>(c => c.ServiceName = "AirPlay Example Console");

        services.AddHostedService<AudioPlayService>();
        services.AddHostedService<MirrorService>();

        services.AddSerilog(configure =>
        {
            configure.WriteTo.Logger(l => l.WriteTo
                .Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}][{Level:u3}] <{SourceContext}>: {Message:lj}{NewLine}{Exception}"));
        });
    })
    .Build()
    .Run();