namespace AirPlay.Core2.Models.Configs;

public class AirTunesConfig
{
    public string ServiceName { get; set; } = "AirTunes";

    public string MacAddress { get; set; } = "11:22:33:44:55:66";

    public ushort Port { get; set; } = 5000;

    public int MirrorWidth { get; set; } = 2560;

    public int MirrorHeight { get; set; } = 1440;

    public int MirrorMaxFps { get; set; } = Constants.MAX_FPS;
}
