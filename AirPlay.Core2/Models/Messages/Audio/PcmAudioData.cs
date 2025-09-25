namespace AirPlay.Core2.Models.Messages.Audio;

public struct PcmAudioData
{
    public int Length { get; set; }

    public byte[] Data { get; set; }

    public ulong Pts { get; set; }
}
