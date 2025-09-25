using AirPlay.Core2.Models.Configs;
using AirPlay.Core2.Models.Messages.Rtsp;
using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace AirPlay.Core2;

public partial class AirPlayPublisher(MulticastService multicastService, ILogger<AirPlayPublisher> logger,
    IOptions<AirTunesConfig> airTunesConfig, IOptions<AirPlayConfig> airPlayConfig) : IHostedService
{
    private readonly ServiceDiscovery _serviceDiscovery = new(multicastService);

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        var matches = MacRegex.Match(airTunesConfig.Value.MacAddress);
        if (!matches.Success) throw new ArgumentException("Must be a mac address");

        var deviceIdInstance = string.Join(string.Empty, matches.Groups[2].Captures) + matches.Groups[3].Value;

        #region AirTunes Service

        ServiceProfile airTunesProfile = new
        (
            $"{deviceIdInstance}@{airTunesConfig.Value.ServiceName}",
            AirTunesType,
            airTunesConfig.Value.Port
        );

        //airTunesProfile.AddProperty("ch", "2");
        airTunesProfile.AddProperty("cn", "0,1,2"); // compressionTypes: 0=pcm, 1=alac, 2=aac, 3=aac-eld (not supported here)
        airTunesProfile.AddProperty("da", "true"); // rfc2617DigestAuthKey
        airTunesProfile.AddProperty("et", "0,3,5"); // encryptionTypes: 0=none, 1=rsa (airport express), 3=fairplay, 4=MFiSAP, 5=fairplay SAPv2.5
        airTunesProfile.AddProperty("ft", Constants.FEATURES); // originally "0x5A7FFFF7,0x1E" https://openairplay.github.io/airplay-spec/features.html
        airTunesProfile.AddProperty("sf", "0x4"); //systemFlags
        airTunesProfile.AddProperty("md", "0,1,2"); // metadataTypes 0=text, 1=artwork, 2=progress
        airTunesProfile.AddProperty("am", Constants.DEVICE_MODEL); // deviceModel
        airTunesProfile.AddProperty("pw", "false"); // password
        airTunesProfile.AddProperty("pk", "29fbb183a58b466e05b9ab667b3c429d18a6b785637333d3f0f3a34baa89f45e"); // publicKey
        airTunesProfile.AddProperty("tp", "UDP"); // transportTypes
        airTunesProfile.AddProperty("vn", "65537");
        airTunesProfile.AddProperty("vs", Constants.AIPLAY_SERVICE_VERSION);
        airTunesProfile.AddProperty("ov", "11"); // 	vodkaVersion
        airTunesProfile.AddProperty("vv", "2"); // 	vodkaVersion

        //airTunesProfile.AddProperty("sr", "44100"); // sample rate
        //airTunesProfile.AddProperty("ss", "16"); // bitdepth
        //airTunesProfile.AddProperty("sv", "false"); // unk

        _serviceDiscovery.Advertise(airTunesProfile);
        logger.AirTunesPublished(airTunesConfig.Value.Port);

        #endregion

        #region AirPlay Service

        ServiceProfile airPlayProfile = new
        (
            $"{deviceIdInstance}@{airPlayConfig.Value.ServiceName}",
            AirPlayType,
            airPlayConfig.Value.Port
        );

        airPlayProfile.AddProperty("acl", "0"); // accessControlLevel
        airPlayProfile.AddProperty("deviceid", airTunesConfig.Value.MacAddress);
        airPlayProfile.AddProperty("features", Constants.FEATURES); // originally "0x5A7FFFF7,0x1E" https://openairplay.github.io/airplay-spec/features.html
        airPlayProfile.AddProperty("rsf", "0x0"); // requiredSenderFeatures
        airPlayProfile.AddProperty("flags", "0x4");
        airPlayProfile.AddProperty("model", Constants.DEVICE_MODEL);
        airPlayProfile.AddProperty("protovers", "1.1");
        airPlayProfile.AddProperty("srcvers", Constants.AIPLAY_SERVICE_VERSION);
        airPlayProfile.AddProperty("pi", "1842bdae-8a92-b965-f657-5efd9b909b1a");
        airPlayProfile.AddProperty("gid", "d2e4a324-bfa0-7535-d42a-9048f1ad20ca");
        airPlayProfile.AddProperty("gcgl", "0");
        //airPlayProfile.AddProperty("vv", "2");
        airPlayProfile.AddProperty("pk", "29fbb183a58b466e05b9ab667b3c429d18a6b785637333d3f0f3a34baa89f45e"); // publicKey

        _serviceDiscovery.Advertise(airPlayProfile);
        logger.AirPlayPublished(airPlayConfig.Value.Port);

        #endregion

        multicastService.Start();

        return Task.CompletedTask;
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        _serviceDiscovery.Dispose();
        multicastService.Stop();

        return Task.CompletedTask;
    }
}

partial class AirPlayPublisher
{
    public const string AirPlayType = "_airplay._tcp";
    public const string AirTunesType = "_raop._tcp";

    public static Regex MacRegex = GenMacRegex();

    [GeneratedRegex("^(([0-9a-fA-F][0-9a-fA-F]):){5}([0-9a-fA-F][0-9a-fA-F])$")]
    private static partial Regex GenMacRegex();
}

internal static partial class AirPlayPublisherLoggers
{
    [LoggerMessage(LogLevel.Information, "AirTunes Service [{port}] Published on mDns")]
    public static partial void AirTunesPublished(this ILogger logger, ushort port);

    [LoggerMessage(LogLevel.Information, "AirPlay Service [{port}] Published on mDns")]
    public static partial void AirPlayPublished(this ILogger logger, ushort port);
}