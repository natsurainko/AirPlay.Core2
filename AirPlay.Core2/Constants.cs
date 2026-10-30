namespace AirPlay.Core2;

public static class Constants
{
    public const string AIPLAY_SERVICE_VERSION = "220.68";
    public const string AIRTUNES_SERVER_VERSION = "AirTunes/220.68";

    public const string DEVICE_MODEL = "AppleTV5,3";

    // Experimental: bit 42 advertises ScreenMultiCodec, enabling HEVC mirroring.
    // The HEVC path still requires device validation before a production release.
    public const long FEATURES_VALUE = 130367356919L | (1L << 42);

    public static readonly string FEATURES = $"0x{FEATURES_VALUE & uint.MaxValue:X},0x{FEATURES_VALUE >> 32:X}";

    public const int MAX_FPS = 60;
}
